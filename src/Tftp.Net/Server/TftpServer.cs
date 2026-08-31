using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Tftp.Net.Channel;
using Tftp.Net.Channel.Client;
using Tftp.Net.Commands;
using Tftp.Net.Commands.Properties;
using Tftp.Net.Commands.Validation;
using Tftp.Net.Configuration;
using Tftp.Net.Transfer;

namespace Tftp.Net.Server;

public sealed partial class TftpServer(ILogger<TftpServer> logger, ITftpConfigurationProvider serverOptions, IUdpClientFactory clientFactory, ITftpChannelFactory channelFactory)
{
	private const int DefaultPort = 69;

	/// <summary>
	/// Tracks remote endpoints which currently have a handshake queued or a transfer in flight. Used to
	/// silently drop duplicate RRQ/WRQ retransmissions from a client that is still waiting on a slow
	/// response, instead of spawning a second concurrent transfer to the same endpoint.
	/// </summary>
	private readonly ConcurrentDictionary<IPEndPoint, byte> _activeEndpoints = new();

	/// <summary>
	/// Occurs when transfer progress is reported.
	/// </summary>
	public event EventHandler<TftpTransferProgress>? TransferProgress;

	/// <summary>
	/// Starts the server asynchronously, listening on the default port and optionally allowing write requests.
	/// </summary>
	/// <remarks>The server will listen on all network interfaces using the default port. Use the cancellation token
	/// to stop the operation if needed.</remarks>
	/// <param name="allowWriteRequests">Specifies whether the server should accept write requests. Set to <see langword="true"/> to enable write
	/// operations; otherwise, only read requests are permitted.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>A task that represents the asynchronous operation of starting the server.</returns>
	public Task RunAsync(CancellationToken cancellationToken = default) =>
		RunAsync(new IPEndPoint(IPAddress.Any, DefaultPort), cancellationToken);

	/// <summary>
	/// Starts listening for incoming UDP handshake requests on the specified endpoint and processes them asynchronously.
	/// Handles transfer requests according to server configuration and cancellation signals.
	/// </summary>
	/// <remarks>If write requests are not allowed, any incoming write request will be rejected immediately. The
	/// method will also reject unsupported transfer modes and cancel transfers if the request queue is full. The operation
	/// can be cancelled by providing a cancellation token.</remarks>
	/// <param name="localEndpoint">The network endpoint on which the server listens for incoming UDP handshake requests.</param>
	/// <param name="allowWriteRequests">Specifies whether write requests are permitted. If <see langword="false"/>, write requests will be rejected.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the listening and processing operations.</param>
	/// <returns>A task that represents the asynchronous operation of listening for and processing handshake requests.</returns>
	public async Task RunAsync(IPEndPoint localEndpoint, CancellationToken cancellationToken = default)
	{
		// Validate the configuration provider immediately, if the user supplied implementation is broken, we want to fail fast
		serverOptions.Validate();

		using var serverClient = clientFactory.Create(localEndpoint);
		var serverChannel = channelFactory.Create(serverClient);

		// The request queue belongs to this listening session; completing it signals
		// the consumers to shut down once all queued handshakes have been processed.
		var handshakeChannel = System.Threading.Channels.Channel.CreateBounded<ServerHandshake>(10);

		// Multiple workers concurrently pull from the same channel reader (which is safe for
		// multi-consumer use) so that one slow/stalled transfer cannot block every other client.
		// The number of workers is bounded by configuration to avoid unbounded resource usage.
		var requestProcessingTask = Task.WhenAll(Enumerable.Range(0, Math.Max(1, serverOptions.MaxConcurrentTransfers))
			.Select(_ => ConsumeHandshakeAsync(handshakeChannel.Reader, serverChannel, cancellationToken)));

		try
		{
			await foreach (var handshake in serverChannel.ServerListenAsync(cancellationToken))
			{
				using var _ = logger.BeginScope("Server received new handshake: Remote Endopoint = {Endpoint}",
					handshake.RemoteEndpoint.ToString());

				// The handshake was an error, meaning it was a valid TFTP packet but failed validation.
				// Send an error response back to the client with the appropriate error code and message.
				if (handshake is not ServerHandshake serverHandshake)
				{
					LogReceivedNonHandshakeCommand();
					await serverChannel.SendPreTransferErrorAsync(Error.IllegalOperation, handshake.RemoteEndpoint, cancellationToken);
					continue;
				}

				// Reject ascii and mail modes as we do not support them
				if (serverHandshake.Mode != TransferMode.Octet)
				{
					LogUnsupportedTransferModeRequested();
					await serverChannel.SendPreTransferErrorAsync(Error.IllegalOperation, serverHandshake.RemoteEndpoint, cancellationToken);
					continue;
				}

				// Verify the requested file name lives within our configured root directory. If not, reject the request with an access violation error.
				if (!TryResolveRequestedFilePath(serverOptions.RootDirectory, serverHandshake.Filename, out var requestedFilePath))
				{
					LogUnsafeFilenameRejected(serverHandshake.Filename);
					await serverChannel.SendPreTransferErrorAsync(Error.AccessViolation, serverHandshake.RemoteEndpoint, cancellationToken);
					continue;
				}

				if (handshake is ServerWriteRequestHandshake writeRequest)
				{
					// If the server is disallowing writes, reject the request immediately
					if (!serverOptions.AllowWriteRequests)
					{
						LogWriteRequestNotAllowed();
						await serverChannel.SendPreTransferErrorAsync(Error.AccessViolation, serverHandshake.RemoteEndpoint, cancellationToken);
						continue;
					}

					// If the requested file already exists, reject the request to prevent overwriting)
					if (File.Exists(requestedFilePath))
					{
						LogRequestedFileAlreadyExists();
						await serverChannel.SendPreTransferErrorAsync(Error.FileAlreadyExists, serverHandshake.RemoteEndpoint, cancellationToken);
						continue;
					}
				}

				// If the request is a read request and the requested file does not exist, reject the request
				if (serverHandshake is ServerReadRequestHandshake && !File.Exists(requestedFilePath))
				{
					LogRequestedFileDoesNotExist();
					await serverChannel.SendPreTransferErrorAsync(Error.FileNotFound, serverHandshake.RemoteEndpoint, cancellationToken);
					continue;
				}

				// A client that retransmits its RRQ/WRQ (e.g. because the initial response was slow)
				// must not be allowed to spawn a second concurrent transfer to the same endpoint. If a
				// handshake for this endpoint is already queued or being processed, silently drop the
				// duplicate; the original transfer will eventually answer it.
				if (!_activeEndpoints.TryAdd(serverHandshake.RemoteEndpoint, 0))
				{
					LogDuplicateHandshakeIgnored(serverHandshake.RemoteEndpoint);
					continue;
				}

				// Initial checks passed, enqueue the handshake for processing
				if (handshakeChannel.Writer.TryWrite(serverHandshake))
				{
					LogTransferRequestQueued();
				}
				else
				{
					_activeEndpoints.TryRemove(serverHandshake.RemoteEndpoint, out var _2);
					LogTransferRequestQueueFull();
					await serverChannel.SendPreTransferErrorAsync(Error.ServerBusy, serverHandshake.RemoteEndpoint, cancellationToken);
					continue;
				}
			}
		}
		finally
		{
			// Unblock the consumer even when the listen loop exits early (exception or
			// cancellation), so the request processing task cannot hang and the listen
			// socket can be disposed safely.
			handshakeChannel.Writer.TryComplete();

			try
			{
				await requestProcessingTask;
			}
			catch
			{
				// Failures are surfaced through RunAsync's normal completion path; observe and
				// discard any additional fault here to prevent an unobserved task exception.
			}
		}
	}

	private void HandleTransferProgress(TftpTransferProgress progress)
	{
		TransferProgress?.Invoke(this, progress);
	}

	private async Task ConsumeHandshakeAsync(ChannelReader<ServerHandshake> handshakeReader, ITftpChannel serverChannel, CancellationToken cancellationToken)
	{
		await foreach (var handshake in handshakeReader.ReadAllAsync(cancellationToken))
		{
			try
			{
				await ProcessSingleHandshakeAsync(handshake, serverChannel, cancellationToken);
			}
			finally
			{
				// Release the endpoint lock taken by the listen loop so that a subsequent (non-duplicate)
				// request from the same client, such as a follow-up transfer, can be accepted again.
				_activeEndpoints.TryRemove(handshake.RemoteEndpoint, out _);
			}
		}
	}

	private async Task ProcessSingleHandshakeAsync(ServerHandshake handshake, ITftpChannel serverChannel, CancellationToken cancellationToken)
	{
		// Bind the transfer socket to an ephemeral port on any interface matching the address family
		// of the remote endpoint. UDP routing will then pick the appropriate outgoing interface.
		var localEndpoint = new IPEndPoint(
			handshake.RemoteEndpoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ?
				IPAddress.IPv6Any :
				IPAddress.Any,
			0);
		var remoteEndpoint = handshake.RemoteEndpoint;

		using var _ = logger.BeginScope("Transfer initiated: Local Endpoint = {Address}:{Port}, Remote Endpoint = {RemoteAddress}:{RemotePort}",
			localEndpoint.Address, localEndpoint.Port,
			remoteEndpoint.Address, remoteEndpoint.Port);

		// Create a new transfer channel for this transfer and log the endpoint it actually bound to.
		// The transfer socket is owned by this loop iteration and disposed when it ends.
		using var transferClient = clientFactory.Create(localEndpoint);
		LogTransferChannelBoundToEndpoint(transferClient.LocalEndpoint);
		var transferChannel = channelFactory.Create(transferClient);

		if (!TryResolveRequestedFilePath(serverOptions.RootDirectory, handshake.Filename, out var filePath))
		{
			LogUnsafeFilenameRejected(handshake.Filename);
			await serverChannel.SendPreTransferErrorAsync(Error.AccessViolation, handshake.RemoteEndpoint, cancellationToken);
			return;
		}

		try
		{
			FileStream fileStream;
			OptionSet clampedOptions;
			switch (handshake)
			{
				case ServerReadRequestHandshake read:
					fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
					clampedOptions = read.Options.Clamp(
						serverOptions.MaxTimeoutSeconds,
						serverOptions.MaxBlockSize,
						serverOptions.MaxWindowSize,
						(ulong)fileStream.Length);
					break;

				case ServerWriteRequestHandshake write:
					fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
					clampedOptions = write.Options.Clamp(
						serverOptions.MaxTimeoutSeconds,
						serverOptions.MaxBlockSize,
						serverOptions.MaxWindowSize,
						write.Options.TransferSize);
					break;

				default:
					throw new InvalidOperationException("Non differentiated server handshake");
			}

			var clampedHandshake = handshake with { Options = clampedOptions };
			using (fileStream)
			{
				if (!await transferChannel.ProcessHandshakeAsync(new Progress<TftpTransferProgress>(HandleTransferProgress), clampedHandshake, fileStream, cancellationToken))
				{
					LogFailedToProcessHandshake();
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Cancellation (e.g. server shutdown) must propagate as-is. Swallowing it here and then
			// attempting to send an error response with the very same (already cancelled) token would
			// only throw again, masking the original cancellation as an unexpected fault.
			throw;
		}
		catch (UnauthorizedAccessException)
		{
			LogFileAccessDenied();
			await serverChannel.SendPreTransferErrorAsync(Error.AccessViolation, handshake.RemoteEndpoint, cancellationToken);
		}
		catch (FileNotFoundException)
		{
			LogRequestedFileNotFound();
			await serverChannel.SendPreTransferErrorAsync(Error.FileNotFound, handshake.RemoteEndpoint, cancellationToken);
		}
		catch (IOException)
		{
			LogFileInUse();
			await serverChannel.SendPreTransferErrorAsync(Error.AccessViolation, handshake.RemoteEndpoint, cancellationToken);
		}
		// A client disappearing mid-transfer (crash, power-off, disconnect) is a routine occurrence,
		// not an unexpected fault. TftpChannel already handles this internally for the data-transfer
		// phase; this catch is defense-in-depth in case a SocketException still escapes (e.g. from
		// SendPreTransferErrorAsync itself), so it is logged concisely rather than as an "unknown"
		// exception with a full stack trace.
		catch (SocketException ex)
		{
			LogRemoteEndpointUnreachable(ex.SocketErrorCode);
		}
		catch (Exception ex)
		{
			LogUnknownTransferException(ex);
			await serverChannel.SendPreTransferErrorAsync(Error.UnknownError, handshake.RemoteEndpoint, cancellationToken);
		}
	}

	/// <summary>
	/// Resolves a requested filename to a path inside the server's root directory.
	/// </summary>
	/// <param name="rootDirectory">The directory the server is configured to serve.</param>
	/// <param name="filename">The filename as requested by the remote endpoint.</param>
	/// <param name="filePath">When this method returns <see langword="true"/>, contains the resolved absolute file path;
	/// otherwise, <see langword="null"/>.</param>
	/// <returns><see langword="true"/> if the filename resolves inside the root directory;
	/// otherwise, <see langword="false"/>.</returns>
	internal static bool TryResolveRequestedFilePath(string rootDirectory, string filename, [NotNullWhen(true)] out string? filePath)
	{
		filePath = null;

		string root = Path.GetFullPath(rootDirectory);
		string fullPath = Path.GetFullPath(filename, root);

		string relativePath = Path.GetRelativePath(root, fullPath);

		if (Path.IsPathFullyQualified(relativePath))
		{
			return false;
		}

		if (relativePath == ".." ||
			relativePath.StartsWith(".." + Path.DirectorySeparatorChar))
		{
			return false;
		}

		filePath = fullPath;
		return true;
	}
}
