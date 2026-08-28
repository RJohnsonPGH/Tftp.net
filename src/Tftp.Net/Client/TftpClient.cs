using Microsoft.Extensions.Logging;
using System.Net;
using Tftp.Net.Channel;
using Tftp.Net.Channel.Client;
using Tftp.Net.Commands.Properties;
using Tftp.Net.Transfer;

namespace Tftp.Net.Client;

/// <summary>
/// A TFTP client that can transfer files with a TFTP server.
/// </summary>
public class TftpClient(ILogger<TftpClient> logger, IUdpClientFactory clientFactory, ITftpChannelFactory channelFactory)
{

	/// <summary>
	/// Downloads a file from a TFTP server or uploads one to it, depending on <paramref name="isWriteRequest"/>.
	/// </summary>
	/// <param name="progress">Receives progress updates for the transfer.</param>
	/// <param name="remoteEndpoint">The endpoint of the remote server.</param>
	/// <param name="isWriteRequest">True to upload (write request); false to download (read request).</param>
	/// <param name="remoteFilename">The filename as it is known on the remote server.</param>
	/// <param name="filename">The path of the local file to read from or write to.</param>
	/// <param name="timeout">The timeout interval in seconds to propose for option negotiation.</param>
	/// <param name="blockSize">The block size in bytes to propose for option negotiation.</param>
	/// <param name="windowSize">The window size in blocks to propose for option negotiation (RFC 7440). Defaults to 1 (classic lockstep TFTP).</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the transfer operation.</param>
	/// <returns>A task whose result is <see langword="true"/> if the transfer completed successfully; otherwise, <see langword="false"/>.</returns>
	public async Task<bool> RunAsync(IProgress<TftpTransferProgress> progress, IPEndPoint remoteEndpoint, bool isWriteRequest, string remoteFilename, string filename,
		ushort timeout, ushort blockSize, ushort windowSize = 1, CancellationToken cancellationToken = default)
	{
		var localEndpoint = new IPEndPoint(IPAddress.Any, 0);

		// The transfer socket is owned by this transfer and disposed when it completes
		using var client = clientFactory.Create(localEndpoint);
		var channel = channelFactory.Create(client);

		using var _ = logger.BeginScope("Client starting new handshake: Remote Endpoint = {Endpoint}, Local Endpoint = {LocalEndpoint}",
			remoteEndpoint.ToString(), client.LocalEndpoint);

		FileStream fileStream;
		OptionSet proposedOptions;
		ClientHandshake handshake;
		switch (isWriteRequest)
		{
			case true:
				fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
				proposedOptions = new(
					timeout,
					blockSize,
					(ulong)fileStream.Length,
					windowSize);
				handshake = new ClientWriteRequestHandshake(remoteEndpoint, remoteFilename, TransferMode.Octet, proposedOptions);
				break;

			case false:
				fileStream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None);
				proposedOptions = new(
					timeout,
					blockSize,
					0,
					windowSize);
				handshake = new ClientReadRequestHandshake(remoteEndpoint, remoteFilename, TransferMode.Octet, proposedOptions);
				break;
		}

		using (fileStream)
		{
			return await channel.InitiateRequestAsync(progress, handshake, fileStream, cancellationToken);
		}
	}
}
