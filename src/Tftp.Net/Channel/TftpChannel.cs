using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Tftp.Net.Channel.Client;
using Tftp.Net.Commands;
using Tftp.Net.Commands.Parser;
using Tftp.Net.Commands.Properties;
using Tftp.Net.Commands.Serializer;
using Tftp.Net.Commands.Validation;
using Tftp.Net.Transfer;

namespace Tftp.Net.Channel;

/// <summary>
/// Implements the TFTP message exchange over a UDP channel, covering both the server side
/// (accepting incoming requests) and the client side (initiating outgoing requests).
/// </summary>
/// <remarks>A channel instance represents a single logical transfer. It is not thread-safe:
/// all operations for one transfer must run sequentially on the same channel.</remarks>
internal sealed partial class TftpChannel(ILogger<TftpChannel> logger, OptionSetValidator optionSetValidator, IUdpClient client) : ITftpChannel
{
	private const ushort DefaultTimeout = 5;
	private const ushort DefaultBlockSize = 512;

	/// <summary>
	/// The number of blocks kept in flight when no window size was negotiated (RFC 7440 default).
	/// </summary>
	private const ushort DefaultWindowSize = 1;

	/// <summary>
	/// Maximum number of consecutive timeouts (or retries) before a transfer is abandoned.
	/// </summary>
	private const int MaxRetries = 5;

	private readonly AsyncRetryPolicy<TftpCommandResult> _retryPolicy = Policy
		.HandleResult<TftpCommandResult>(x => x is TftpCommandRetryResult)
		.RetryAsync(
			retryCount: MaxRetries,
			onRetry: (result, retryCount, context) =>
			{
				logger.LogRetryingTransmit(retryCount);
			});

	private TftpTransferProgress _transferProgress = new(Guid.NewGuid(), TftpTransferState.Handshake, string.Empty, string.Empty, 0, 0);

	// Options negotiated for the current transfer. They fall back to the protocol defaults
	// whenever option negotiation does not take place (or is declined by the remote endpoint).
	private ushort _timeout = DefaultTimeout;
	private ushort _blockSize = DefaultBlockSize;
	private ulong _transferSize = 0;
	private ushort _windowSize = DefaultWindowSize;

	/// <summary>
	/// Listens for incoming requests on the well-known port and yields one handshake per valid request packet.
	/// </summary>
	/// <remarks>Packets which cannot be parsed as TFTP commands at all are silently ignored, since responding to them
	/// could generate unnecessary traffic on a noisy network, and packets received on the well-known port which are
	/// not read or write requests are likewise ignored. Requests whose mode is undefined or whose option list
	/// violates the protocol (duplicate options) are yielded as <see cref="ErrorHandshake"/>s so that the caller can
	/// respond with an appropriate error. Unusable option values are not fatal: they are declined or clamped into
	/// the protocol range before the handshake is created.</remarks>
	public async IAsyncEnumerable<IServerHandshake> ServerListenAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			IServerHandshake handshake;
			try
			{
				var result = await client.ReceiveAsync(cancellationToken);

				// If we failed to parse any kind of valid TFTP command, it's possible we received a packet that is not intended for our TFTP server (e.g., noise or a different protocol).
				// In this case, we should ignore the packet and wait for the next one instead of responding with an error, since responding could generate unnecessary traffic if the port is noisy.
				if (!CommandParser.TryParse(result.Buffer, out var command, logger))
				{
					LogFailedToParseCommand(result.RemoteEndPoint);
					continue;
				}

				// The spec does not clearly define how packets received on the well-known port that are not RRQ or WRQ should be handled.
				// Responding with an error would be an option as well, however UDP is noisy and this could generate unnecessary traffic.
				// Ignoring the packet seems to be the most reasonable option.
				if (command is not Request requestCommand)
				{
					LogUnexpectedCommandOnListenPort(command.OpCode);
					continue;
				}

				using var _ = logger.BeginScope("Received handshake message: Remote Endpoint = {Endpoint}",
					result.RemoteEndPoint.ToString());

				// Validate the request's semantics (transfer mode and options). If validation fails, yield an error handshake
				// which the caller will translate into a proper error response.
				if (!TryValidateCommand(requestCommand, out var validatedRequest, out var mode, out var options))
				{
					handshake = new ErrorHandshake(result.RemoteEndPoint, Error.IllegalOperation);
				}
				// If validation succeeds, create the appropriate handshake object based on the request type
				else
				{
					handshake = validatedRequest switch
					{
						ReadRequest => new ServerReadRequestHandshake(result.RemoteEndPoint, validatedRequest.Filename, mode, options),
						WriteRequest => new ServerWriteRequestHandshake(result.RemoteEndPoint, validatedRequest.Filename, mode, options),
						_ => throw new InvalidDataException("Unexpected request type after validation")
					};
				}
			}
			catch (ObjectDisposedException ex)
			{
				LogUdpClientDisposed(ex);
				throw;
			}
			catch (SocketException ex)
			{
				LogSocketException(ex);
				continue;
			}
			catch (OperationCanceledException ex)
			{
				LogListeningCanceled(ex);
				yield break;
			}

			yield return handshake;
		}
	}

	/// <summary>
	/// Attempts to validate a TFTP request and extract its parameters.
	/// </summary>
	/// <remarks>This method performs semantic validation of an already syntactically parsed request.
	/// The transfer mode must be one of the modes defined by the spec ('netascii', 'octet' or 'mail'),
	/// and the option list must not violate the protocol (duplicate option names). Unusable option
	/// values do not fail validation: they are declined or clamped per RFC 2347. If the request is
	/// invalid, the method returns <see langword="false"/> and outputs <see langword="null"/> values.
	/// Use the output parameters only when the method returns <see langword="true"/>.</remarks>
	/// <param name="command">The TFTP request to validate.</param>
	/// <param name="request">When this method returns <see langword="true"/>, contains the validated request object; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="mode">When this method returns <see langword="true"/>, contains the parsed transfer mode; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="options">When this method returns <see langword="true"/>, contains the validated set of options; otherwise, <see
	/// langword="null"/>.</param>
	/// <returns><see langword="true"/> if the command's mode and options are valid according to the TFTP specification;
	/// otherwise, <see langword="false"/>.</returns>
	private bool TryValidateCommand(Request command, [NotNullWhen(true)] out Request? request, [NotNullWhen(true)] out TransferMode? mode, [NotNullWhen(true)] out OptionSet? options)
	{
		request = null;
		mode = null;
		options = null;

		// A mode which is not one of the modes defined by the spec cannot proceed and is answered
		// with an error (RFC 1350 defines 'netascii', 'octet' and 'mail').
		if (!TransferMode.TryParse(command.Mode, out var validatedMode))
		{
			LogFailedToParseTransferMode(command.Mode);
			return false;
		}

		// Options are adjudicated individually by the validator: unknown names are dropped and
		// unusable values are declined. Parsing therefore fails only when the option
		// list itself violates the protocol (duplicate option names, RFC 2347, etc.).
		if (!optionSetValidator.TryParseRequestOptionSet(command.Options, out var validatedOptions))
		{
			return false;
		}

		// Validation successful, set output parameters and return true
		request = command;
		mode = validatedMode;
		options = validatedOptions;
		return true;
	}

	/// <summary>
	/// Asynchronously sends an error message to the specified remote endpoint before a transfer operation begins.
	/// </summary>
	/// <param name="remoteEndpoint">The remote network endpoint to which the error message will be sent.</param>
	/// <param name="error">The error information to be sent to the remote endpoint. Cannot be null.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
	/// <returns>A task that represents the asynchronous send operation.</returns>
	public async Task SendPreTransferErrorAsync(Error error, IPEndPoint remoteEndpoint, CancellationToken cancellationToken = default)
	{
		var serializedCommand = CommandSerializer.Serialize(error);
		await client.SendAsync(serializedCommand, remoteEndpoint, cancellationToken);
	}

	/// <summary>
	/// Processes a handshake initiated by a remote endpoint (server side) and performs the associated file transfer.
	/// </summary>
	/// <remarks>Negotiates options (if any were requested) by transmitting an option acknowledgement, then performs
	/// the data transfer phase: sending the file for read requests, or receiving it for write requests.</remarks>
	/// <param name="progress">An object that receives progress updates for the transfer operation.</param>
	/// <param name="handshake">The validated incoming request handshake. Its options must already be clamped to locally supported limits.</param>
	/// <param name="fileStream">The file stream serving the transfer. Opened for reading for read requests, or for writing for write requests.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the transfer operation.</param>
	/// <returns>A task whose result is <see langword="true"/> if the transfer completed successfully; otherwise, <see langword="false"/>.</returns>
	public async Task<bool> ProcessHandshakeAsync(IProgress<TftpTransferProgress> progress, RequestHandshake handshake, FileStream fileStream, CancellationToken cancellationToken = default)
	{
		using var _ = logger.BeginScope("Processing handshake: Local Endpoint = {LocalEndpoint}, Remote Endpoint = {RemoteEndpoint}, Filename = {Filename}",
			client.LocalEndpoint.ToString(),
			handshake.RemoteEndpoint.ToString(),
			handshake.Filename);

		client.Connect(handshake.RemoteEndpoint);

		_transferProgress = new TftpTransferProgress(Guid.NewGuid(), TftpTransferState.Handshake,
			handshake.RemoteEndpoint.ToString(), handshake.Filename, 0, 0);
		progress.Report(_transferProgress);

		bool transferSucceeded = handshake switch
		{
			ServerReadRequestHandshake readRequest =>
				await NegotiateOptionsAsServerAsync(readRequest, cancellationToken) &&
				await SendFileAsync(progress, fileStream, cancellationToken),

			ServerWriteRequestHandshake writeRequest =>
				await NegotiateOptionsAsServerAsync(writeRequest, cancellationToken) &&
				await ReceiveFileAsync(progress, fileStream, cancellationToken: cancellationToken),

			_ => throw new InvalidOperationException($"Unexpected server handshake type in handshake processing: {handshake.GetType().FullName}")
		};

		if (!transferSucceeded)
		{
			LogDataTransferFailed(handshake.RemoteEndpoint);
			progress.Report(_transferProgress with { State = TftpTransferState.Failed });
			return false;
		}

		progress.Report(_transferProgress with { State = TftpTransferState.Completed });
		return true;
	}

	/// <summary>
	/// Initiates a transfer against a remote server (client side) and performs the associated file transfer.
	/// </summary>
	/// <remarks>Transmits the read or write request, evaluates the server's response to determine whether option
	/// negotiation took place (option acknowledgement) or was declined (immediate first data packet or plain
	/// acknowledgement), then performs the data transfer phase accordingly.</remarks>
	/// <param name="progress">An object that receives progress updates for the transfer operation.</param>
	/// <param name="handshake">The outgoing request handshake describing the desired transfer.</param>
	/// <param name="fileStream">The file stream serving the transfer. Opened for writing for read requests, or for reading for write requests.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the transfer operation.</param>
	/// <returns>A task whose result is <see langword="true"/> if the transfer completed successfully; otherwise, <see langword="false"/>.</returns>
	public async Task<bool> InitiateRequestAsync(IProgress<TftpTransferProgress> progress, ClientHandshake handshake, FileStream fileStream, CancellationToken cancellationToken = default)
	{
		using var _ = logger.BeginScope("Initiating request: Local Endpoint = {LocalEndpoint}, Remote Endpoint = {RemoteEndpoint}, Filename = {Filename}",
			client.LocalEndpoint,
			handshake.RemoteEndpoint.ToString(),
			handshake.Filename);

		// The socket is intentionally not connected yet. According to RFC 1350 the server answers
		// requests from a fresh ephemeral port rather than the well-known port the request was sent
		// to, so the socket is only connected once the first response has identified the peer which
		// owns the remainder of this transfer session.
		_transferProgress = new TftpTransferProgress(Guid.NewGuid(), TftpTransferState.Handshake,
			handshake.RemoteEndpoint.ToString(), handshake.Filename, 0, 0);
		progress.Report(_transferProgress);
		progress.Report(_transferProgress with { State = TftpTransferState.OptionNegotiation });

		Request request = handshake switch
		{
			ClientReadRequestHandshake => new ReadRequest(handshake.Filename, handshake.Mode, handshake.Options),
			ClientWriteRequestHandshake => new WriteRequest(handshake.Filename, handshake.Mode, handshake.Options),
			_ => throw new InvalidOperationException($"Unexpected client handshake type in request initiation: {handshake.GetType().FullName}")
		};

		// Transmit the request explicitly to the server's well-known endpoint
		var requestResult = await SendCommandAsync(request, handshake.RemoteEndpoint, cancellationToken);
		if (requestResult is not TftpCommandResponseResult requestSuccess)
		{
			LogFailedToSendRequest(handshake.RemoteEndpoint);
			progress.Report(_transferProgress with { State = TftpTransferState.Failed });
			return false;
		}

		// Lock the channel onto the endpoint which answered the request; all further commands of this
		// session are exchanged exclusively with that peer. According to RFC 1350 the server answers
		// from a fresh ephemeral port rather than the well-known port the request was sent to.
		client.Connect(requestSuccess.Responder);

		LogTransferSessionLockedToResponder(requestSuccess.Responder);
		_transferProgress = _transferProgress with { Endpoint = requestSuccess.Responder.ToString() };

		// Determine whether option negotiation took place based on the server's response and adopt the
		// negotiated (or defaulted) options for the upcoming data transfer phase.
		if (!ApplyNegotiatedOptions(requestSuccess.Response))
		{
			LogFailedToNegotiateOptions(handshake.RemoteEndpoint);
			progress.Report(_transferProgress with { State = TftpTransferState.Failed });
			await SendCommandAsync(Error.OptionNegotiationFailed, handshake.RemoteEndpoint, cancellationToken);
			return false;
		}

		// When the server accepted options for a read request, it waits for a zero block acknowledgement
		// before transmitting the first data packet.
		if (requestSuccess.Response is OptionAcknowledgement && handshake is ClientReadRequestHandshake)
		{
			if (await SendCommandAsync(new Acknowledgement(0), cancellationToken: cancellationToken) is not TftpCommandSentResult)
			{
				progress.Report(_transferProgress with { State = TftpTransferState.Failed });
				return false;
			}
		}

		bool transferSucceeded = handshake switch
		{
			ClientWriteRequestHandshake => await SendFileAsync(progress, fileStream, cancellationToken),
			ClientReadRequestHandshake => await ReceiveFileAsync(progress, fileStream, requestSuccess.Response as Data, cancellationToken),
			_ => throw new InvalidOperationException($"Unexpected client handshake type in data transfer: {handshake.GetType().FullName}")
		};

		if (!transferSucceeded)
		{
			LogDataTransferFailed(handshake.RemoteEndpoint);
			progress.Report(_transferProgress with { State = TftpTransferState.Failed });
			return false;
		}

		progress.Report(_transferProgress with { State = TftpTransferState.Completed });
		return true;
	}

	/// <summary>
	/// Adopts the options negotiated with the remote endpoint based on its response to the initial request.
	/// </summary>
	/// <param name="response">The server's response to the read or write request.</param>
	/// <returns><see langword="true"/> if the response indicates a valid negotiation outcome; otherwise, <see langword="false"/>.</returns>
	private bool ApplyNegotiatedOptions(ICommand response)
	{
		switch (response)
		{
			case OptionAcknowledgement optionAcknowledgement:
				// The server accepted (possibly adjusted) our proposed options.
				// At this point provided options MUST be valid, because we have no way to re-negotiate.
				if (!optionSetValidator.TryParseRequestOptionSet(optionAcknowledgement.Options, out var negotiatedOptions))
				{
					return false;
				}

				ApplyOptions(negotiatedOptions);
				return true;

			// A plain acknowledgement (write requests) or an immediate first data packet (read requests)
			// signals that the server declined option negotiation entirely. Fall back to the defaults.
			case Acknowledgement:
			case Data { BlockNumber: 1 }:
				ApplyOptions(OptionSet.Empty);
				return true;

			default:
				return false;
		}
	}

	/// <summary>
	/// Applies the given option set to the per-transfer state used during the data transfer phase.
	/// </summary>
	private void ApplyOptions(OptionSet options)
	{
		_timeout = options.Timeout ?? DefaultTimeout;
		_blockSize = options.BlockSize ?? DefaultBlockSize;
		_transferSize = options.TransferSize ?? 0;
		_windowSize = options.WindowSize ?? DefaultWindowSize;

		LogAppliedNegotiatedOptions(_timeout, _blockSize, _windowSize, _transferSize);
	}

	/// <summary>
	/// Acknowledges the requested options of an incoming server-side request, if any.
	/// </summary>
	/// <remarks>If the request carries no options, no option acknowledgement is transmitted and the transfer proceeds
	/// with the protocol defaults immediately. For read requests the option acknowledgement is awaited as a zero block
	/// acknowledgement before continuing; for write requests the first arriving data packet implicitly confirms the
	/// negotiated options, so nothing is awaited here.</remarks>
	/// <param name="handshake">The incoming server-side handshake. Its options must already be clamped to locally supported limits.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the negotiation.</param>
	/// <returns><see langword="true"/> if negotiation succeeded or was unnecessary; otherwise, <see langword="false"/>.</returns>
	private async Task<bool> NegotiateOptionsAsServerAsync(ServerHandshake handshake, CancellationToken cancellationToken)
	{
		using var _ = logger.BeginScope("Beginning option negotiation");

		// There are no options, do nothing
		if (handshake.Options == OptionSet.Empty)
		{
			LogNoOptionsToNegotiate();
			return true;
		}

		var oackCommand = new OptionAcknowledgement(handshake.Options);

		if (handshake is ServerReadRequestHandshake)
		{
			// A read request awaits its option acknowledgement being confirmed by a zero block
			// acknowledgement before the remote endpoint listens for the first data packet.
			// SendCommandAsync handles waiting, response validation and retrying transparently.
			if (await SendCommandAsync(oackCommand, cancellationToken: cancellationToken) is not TftpCommandResponseResult)
			{
				LogFailedToSendOptionAcknowledgement(handshake.RemoteEndpoint);
				return false;
			}
		}
		else
		{
			// For a write request the first data packet implicitly acknowledges the option
			// acknowledgement, so transmit it without waiting and start receiving immediately.
			var serializedOack = CommandSerializer.Serialize(oackCommand);
			await client.SendAsync(serializedOack, cancellationToken);
		}

		// Option negotiation successful, adopt the acknowledged options for use in the data transfer phase
		ApplyOptions(handshake.Options);
		return true;
	}

	/// <summary>
	/// Transmits a command, waits for its expected response and retries on timeouts or recoverable failures.
	/// </summary>
	/// <remarks>Error and acknowledgement commands are fire and forget and complete successfully right after being
	/// sent. All other commands await a response until the negotiated timeout expires, in which case the transmission
	/// is retried up to <see cref="MaxRetries"/> times.</remarks>
	/// <param name="command">The command to transmit.</param>
	/// <param name="remoteEndpointOverride">An optional explicit destination endpoint. Used before the underlying socket has been
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the transmission.</param>
	/// connected; when omitted, the command is sent to the connected remote endpoint.</param>
	/// <returns>The outcome of the transmit attempt(s). Commands which by design expect no response yield a
	/// <see cref="TftpCommandSentResult"/> once delivered. All other commands yield a
	/// <see cref="TftpCommandResponseResult"/> carrying both the validated response and the endpoint which sent it,
	/// a <see cref="TftpCommandRetryResult"/> if no usable response arrived, or a <see cref="TftpCommandErrorResult"/>
	/// on a fatal failure.</returns>
	private async Task<TftpCommandResult> SendCommandAsync(ICommand command, IPEndPoint? remoteEndpointOverride = null, CancellationToken cancellationToken = default)
	{
		var result = await _retryPolicy.ExecuteAsync(async () =>
		{
			// Create a new cancellation token source for timeout handling
			// Chain this with the provided cancellation token so that if the original token is cancelled, the timeout will be cancelled as well
			using var timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(_timeout));
			using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellationTokenSource.Token);
			var timeoutCancellationToken = timeoutCancellationTokenSource.Token;
			var linkedCancellationToken = linkedCancellationTokenSource.Token;

			try
			{
				var serializedCommand = CommandSerializer.Serialize(command);

				if (remoteEndpointOverride is null)
				{
					await client.SendAsync(serializedCommand, linkedCancellationToken);
				}
				else
				{
					await client.SendAsync(serializedCommand, remoteEndpointOverride, linkedCancellationToken);
				}

				// Errors and Acknowledgements are fire and forget and do not expect an ACK, so we can return immediately after sending without waiting for a response
				if (command is Error ||
					command is Acknowledgement)
				{
					LogCommandDoesNotExpectAck();
					return new TftpCommandSentResult();
				}

				// All other commands expect some kind of response
				var response = await client.ReceiveAsync(linkedCancellationToken);

				// Response is not a valid TFTP packet. It could be corruption or noise. Retry.
				if (!CommandParser.TryParse(response.Buffer, out var commandResponse, logger))
				{
					LogInvalidResponseReceived();
					return new TftpCommandRetryResult();
				}

				// Response is a valid TFTP response, but is an Error. Terminate.
				if (commandResponse is Error errorCommand)
				{
					LogRemoteEndpointReportedError(errorCommand.ErrorCode, errorCommand.Message);
					return new TftpCommandErrorResult(errorCommand);
				}

				// Validate the response type against the expectations for the transmitted command
				if (!ValidateResponse(command, commandResponse))
				{
					// Response is a valid TFTP packet, but not the expected response to the command. Terminate.
					LogUnexpectedResponse(command.OpCode, commandResponse.OpCode);
					await SendCommandAsync(Error.IllegalOperation, cancellationToken: linkedCancellationToken);
					return new TftpCommandErrorResult(Error.IllegalOperation);
				}

				// Acknowledgements carry an expected block number: DATA expects an ACK for its own block,
				// an OACK expects the zero block ACK. A stale ACK (e.g. a duplicate from a previous block,
				// the "sorcerer's apprentice" scenario) means a prior packet was lost: retry instead of failing.
				if (commandResponse is Acknowledgement acknowledgement)
				{
					var expectedAckBlock = command switch
					{
						Data data => data.BlockNumber,
						OptionAcknowledgement => (ushort)0,
						_ => acknowledgement.BlockNumber
					};

					if (acknowledgement.BlockNumber == expectedAckBlock)
					{
						return new TftpCommandResponseResult(commandResponse, response.RemoteEndPoint);
					}

					LogUnexpectedBlockNumber(expectedAckBlock, acknowledgement.BlockNumber);
					return new TftpCommandRetryResult();
				}

				// Command transmitted and response validated successfully
				return new TftpCommandResponseResult(commandResponse, response.RemoteEndPoint);
			}
			// The linked cancellation token will be cancelled if either the original cancellation token is cancelled or the timeout is reached.
			// Only catch if the timeout cancellation token is the one that triggered the cancellation, otherwise propagate the cancellation.
			catch (OperationCanceledException ex) when (timeoutCancellationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
			{
				LogCommandTimedOut(ex);
				return new TftpCommandRetryResult();
			}
			// See the matching comment in ReceiveCommandAsync: a connected UDP socket reports a
			// disappeared peer as a SocketException. This is an expected, common occurrence (client
			// crashed/disconnected) rather than an unexpected fault, so it is logged concisely and
			// terminates the transfer instead of being retried.
			catch (SocketException ex)
			{
				LogRemoteEndpointUnreachable(ex.SocketErrorCode);
				return new TftpCommandErrorResult(Error.UnknownError);
			}
			catch (Exception ex)
			{
				LogFailedToTransmitCommand(ex);
				return new TftpCommandErrorResult(Error.UnknownError);
			}
		});

		if (!result.IsSuccess)
		{
			LogFailedToSendCommandAfterRetries(command.OpCode);
		}

		return result;
	}

	/// <summary>
	/// Validates that the received response is of a type which can legitimately answer the transmitted command.
	/// </summary>
	/// <remarks>Block number correctness is verified separately by <see cref="SendCommandAsync"/> once the response
	/// type itself has been accepted here.</remarks>
	/// <param name="command">The command which was transmitted.</param>
	/// <param name="response">The response which was received.</param>
	/// <returns><see langword="true"/> if the response type matches the command's expectation; otherwise, <see langword="false"/>.</returns>
	private static bool ValidateResponse(ICommand command, ICommand response)
	{
		return command switch
		{
			// DATA expects an acknowledgement carrying a matching block number
			Data => response is Acknowledgement,

			// An option acknowledgement expects the zero block acknowledgement
			OptionAcknowledgement => response is Acknowledgement,

			// Valid responses to a read request are an OACK (options accepted) or the first DATA
			// packet if the server did not accept any of the requested options
			ReadRequest => response is OptionAcknowledgement or Data,

			// Valid responses to a write request are an OACK (options accepted) or a plain ACK
			// with block number zero if the server did not accept any of the requested options
			WriteRequest => response is OptionAcknowledgement or Acknowledgement,

			_ => false
		};
	}

	/// <summary>
	/// Asynchronously receives the contents of a file over TFTP, writing it to the given stream and reporting progress.
	/// </summary>
	/// <remarks>Duplicate data packets (indicating that one of our acknowledgements was lost) are re-acknowledged
	/// without rewriting their payload. Timeouts trigger a retransmission of the most recent acknowledgement to prompt
	/// the remote endpoint to resume. The transfer completes once a data packet shorter than the negotiated block size
	/// arrives.</remarks>
	/// <param name="progress">An object that receives progress updates for the file transfer operation.</param>
	/// <param name="fileStream">The file stream to write the received data into. The stream must be open and writable.</param>
	/// <param name="firstDataPacket">An optional data packet which was already received during request initiation and belongs to this transfer.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the file transfer operation.</param>
	/// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the file was
	/// received successfully; otherwise, <see langword="false"/>.</returns>
	/// <remarks>When a window size greater than one has been negotiated (RFC 7440), an acknowledgement is
	/// withheld until the last block of the current window has been received (or the transfer's final,
	/// short packet arrives), matching the pipelining behaviour implemented by <see cref="SendFileAsync"/>
	/// for the sending side. With the default window size of one, this degenerates to acknowledging every
	/// block, preserving the original RFC 1350 behaviour.</remarks>
	private async Task<bool> ReceiveFileAsync(IProgress<TftpTransferProgress> progress, FileStream fileStream, Data? firstDataPacket = null, CancellationToken cancellationToken = default)
	{
		_transferProgress = _transferProgress with { State = TftpTransferState.DataTransfer, TotalBytes = _transferSize };
		progress.Report(_transferProgress);

		ushort expectedBlockNumber = 1;
		ushort lastSentAckBlockNumber = 0;
		bool anyAckSent = false;
		ushort blocksReceivedSinceLastAck = 0;
		ulong bytesWritten = 0;
		int consecutiveTimeouts = 0;
		Data? pendingDataPacket = firstDataPacket;

		while (true)
		{
			Data dataPacket;

			if (pendingDataPacket is not null)
			{
				dataPacket = pendingDataPacket;
				pendingDataPacket = null;
			}
			else
			{
				var received = await ReceiveCommandAsync(cancellationToken);

				// Receiving failed terminally (retries exhausted or fatal socket error)
				if (received is null)
				{
					return false;
				}

				// Receiving timed out. Prompt the remote endpoint to retransmit by re-acknowledging
				// the most recently acknowledged block, then keep listening.
				if (received is TftpCommandTimeoutResult)
				{
					if (++consecutiveTimeouts > MaxRetries)
					{
						LogAbortedFileReception(MaxRetries);
						return false;
					}

					if (anyAckSent)
					{
						await SendCommandAsync(new Acknowledgement(lastSentAckBlockNumber), cancellationToken: cancellationToken);
					}

					continue;
				}

				var receivedCommand = ((TftpCommandResponseResult)received).Response;

				// Response is not a valid TFTP DATA packet as required in the data transfer phase. Terminate.
				if (receivedCommand is not Data receivedData)
				{
					LogUnexpectedResponse(OpCode.Data, receivedCommand?.OpCode ?? OpCode.Error);
					await SendCommandAsync(Error.IllegalOperation, cancellationToken: cancellationToken);
					return false;
				}

				dataPacket = receivedData;
			}

			consecutiveTimeouts = 0;

			// A signed comparison (accounting for the natural wrap-around at 65535 -> 0) tells us
			// whether the received block precedes the one we are expecting.
			var blockOffset = (short)(dataPacket.BlockNumber - expectedBlockNumber);

			// A block at or before the start of the current window means our most recent acknowledgement
			// was lost (or the whole window was retransmitted after a stalled ACK). Re-acknowledge the
			// last block we actually confirmed, without writing any payload again, and keep listening.
			if (blockOffset < 0)
			{
				LogUnexpectedBlockNumber(expectedBlockNumber, dataPacket.BlockNumber);
				if (anyAckSent)
				{
					await SendCommandAsync(new Acknowledgement(lastSentAckBlockNumber), cancellationToken: cancellationToken);
				}
				continue;
			}

			// Any block ahead of what we are expecting indicates a gap that cannot be recovered from
			// within this session. Terminate.
			if (blockOffset > 0)
			{
				LogUnexpectedBlockNumber(expectedBlockNumber, dataPacket.BlockNumber);
				await SendCommandAsync(Error.IllegalOperation, cancellationToken: cancellationToken);
				return false;
			}

			// Write the data to the filestream
			await fileStream.WriteAsync(dataPacket.DataBytes, cancellationToken);
			bytesWritten += (ulong)dataPacket.DataBytes.Length;
			blocksReceivedSinceLastAck++;

			// Data packet has a data length less than the block size indicating transfer has completed
			bool isFinalPacket = dataPacket.DataBytes.Length < _blockSize;

			// Only acknowledge once a full window has been received (or the transfer's final, possibly
			// short, packet arrives). Acknowledgements for earlier blocks of the window are intentionally
			// withheld so that a compliant sender keeps the window pipelined instead of stalling on every
			// block, realizing the throughput gain of a negotiated window size greater than one.
			if (blocksReceivedSinceLastAck >= _windowSize || isFinalPacket)
			{
				if (await SendCommandAsync(new Acknowledgement(dataPacket.BlockNumber), cancellationToken: cancellationToken) is not TftpCommandSentResult)
				{
					LogFailedToSendDataPacket(dataPacket.BlockNumber);
					return false;
				}

				lastSentAckBlockNumber = dataPacket.BlockNumber;
				anyAckSent = true;
				blocksReceivedSinceLastAck = 0;
			}

			// Report progress and advance (with natural wrap-around at 65535 -> 0) to the next expected block
			LogWroteToFile(dataPacket.BlockNumber, dataPacket.DataBytes.Length);
			_transferProgress = _transferProgress with { BytesTransferred = bytesWritten };
			progress.Report(_transferProgress);
			expectedBlockNumber++;

			if (isFinalPacket)
			{
				LogReceivedFinalDataPacket(dataPacket.BlockNumber);
				break;
			}
		}

		return true;
	}

	/// <summary>
	/// Asynchronously sends the contents of a file over TFTP, reporting progress and supporting cancellation.
	/// </summary>
	/// <remarks>Progress is reported at the start of the transfer and after each confirmed data block is transmitted.
	/// When a window size greater than one has been negotiated (RFC 7440), up to <c>_windowSize</c> data packets are
	/// pipelined before awaiting the acknowledgement of the final block of the window; acknowledgements for earlier
	/// blocks of the same window arriving in the meantime are drained. On timeout the whole window is retransmitted,
	/// following the retransmission recommendation of RFC 7440. The file transfer is considered complete once a
	/// partial (or empty) final data packet has been acknowledged.</remarks>
	/// <param name="progress">An object that receives progress updates for the file transfer operation.</param>
	/// <param name="fileStream">The file stream containing the data to send. The stream must be open, readable and seekable.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the file transfer operation.</param>
	/// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the file was
	/// sent successfully; otherwise, <see langword="false"/>.</returns>
	private async Task<bool> SendFileAsync(IProgress<TftpTransferProgress> progress, FileStream fileStream, CancellationToken cancellationToken = default)
	{
		_transferSize = (ulong)fileStream.Length;
		_transferProgress = _transferProgress with { State = TftpTransferState.DataTransfer, TotalBytes = _transferSize };
		progress.Report(_transferProgress);

		Memory<byte> buffer = new byte[_blockSize];
		ushort windowStartBlock = 1;
		long windowStartOffset = 0;
		ulong bytesTransferred = 0;
		while (true)
		{
			// (Re)transmit one window worth of data, reading sequentially from the start offset of
			// the current window. Re-seeking on every attempt keeps retransmission simple and correct.
			fileStream.Seek(windowStartOffset, SeekOrigin.Begin);

			var blockNumber = windowStartBlock;
			long windowBytes = 0;
			bool finalPacketSent = false;
			while ((ushort)(blockNumber - windowStartBlock) < _windowSize && !finalPacketSent)
			{
				// Read the next block of data from the filestream
				var bytesRead = await fileStream.ReadAsync(buffer, cancellationToken);

				// Create DATA command with the current block number and the data read from the file.
				// Serialization copies the payload into a fresh packet buffer, so reusing the read
				// buffer for subsequent blocks of the pipeline is safe.
				var data = new Data(blockNumber, buffer[..bytesRead]);
				await client.SendAsync(CommandSerializer.Serialize(data), cancellationToken);

				LogTransmittedData(data.BlockNumber, bytesRead);
				blockNumber++;
				windowBytes += bytesRead;

				// Data packet has a data length less than the block size indicating transfer has completed.
				// This also covers the special case of a file whose size is an exact multiple of the block size
				// (or an empty file), which is terminated by a zero-length final data packet.
				if (bytesRead < _blockSize)
				{
					finalPacketSent = true;
				}
			}

			// The last block transmitted concludes the current window
			var windowLastBlock = (ushort)(blockNumber - 1);

			// Await the acknowledgement of the final block of the window. Acknowledgements for
			// earlier blocks of this window may arrive meanwhile and are drained by the waiter.
			var acked = await TryAwaitWindowAckAsync(windowLastBlock, cancellationToken);
			if (acked is null)
			{
				LogFailedToSendDataPacket(windowLastBlock);
				return false;
			}

			if (!acked.Value)
			{
				// The acknowledgement timed out. Retransmitting the entire window from the earliest
				// unacknowledged block follows the recommendation given in RFC 7440.
				if (++_windowRetransmitAttempts >= MaxRetries)
				{
					LogAbortedFileTransmission(MaxRetries);
					return false;
				}

				continue;
			}

			_windowRetransmitAttempts = 0;
			bytesTransferred += (ulong)windowBytes;
			_transferProgress = _transferProgress with { BytesTransferred = bytesTransferred };
			progress.Report(_transferProgress);

			if (finalPacketSent)
			{
				LogTransmittedFinalDataPacket(windowLastBlock);
				break;
			}

			// Advance to the next window (with natural wrap-around at 65535 -> 0)
			windowStartOffset += windowBytes;
			windowStartBlock = blockNumber;
		}

		// File sent successfully
		return true;
	}

	/// <summary>
	/// Awaits the acknowledgement of the final block of the currently transmitted window.
	/// </summary>
	/// <remarks>Acknowledgements belonging to earlier blocks of the window are drained silently, as they are an
	/// expected consequence of pipelining under RFC 7440.</remarks>
	/// <param name="expectedAckBlock">The block number whose acknowledgement confirms the whole window.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the wait operation.</param>
	/// <returns>A task whose result is <see langword="true"/> when the expected acknowledgement arrived within the
	/// negotiated timeout, <see langword="false"/> when it timed out, or <see langword="null"/> when a fatal condition
	/// (remote error or unexpected response) terminated the transfer.</returns>
	private async Task<bool?> TryAwaitWindowAckAsync(ushort expectedAckBlock, CancellationToken cancellationToken)
	{
		using var timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(_timeout));
		using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellationTokenSource.Token);
		var timeoutCancellationToken = timeoutCancellationTokenSource.Token;

		try
		{
			while (true)
			{
				var response = await client.ReceiveAsync(linkedCancellationTokenSource.Token);

				// Response is not a valid TFTP packet. It could be corruption or noise. Keep waiting.
				if (!CommandParser.TryParse(response.Buffer, out var commandResponse, logger))
				{
					LogInvalidResponseReceived();
					continue;
				}

				// A remote error terminates the transfer immediately
				if (commandResponse is Error errorCommand)
				{
					LogRemoteEndpointReportedError(errorCommand.ErrorCode, errorCommand.Message);
					return null;
				}

				// Anything but an acknowledgement is not expected while awaiting a window confirmation
				if (commandResponse is not Acknowledgement acknowledgement)
				{
					LogUnexpectedResponse(OpCode.Acknowledgement, commandResponse.OpCode);
					return null;
				}

				if (acknowledgement.BlockNumber == expectedAckBlock)
				{
					return true;
				}

				// An acknowledgement for an earlier block of this window: drain it and keep waiting
				// for the confirmation of the final block.
				LogUnexpectedBlockNumber(expectedAckBlock, acknowledgement.BlockNumber);
			}
		}
		catch (OperationCanceledException ex) when (timeoutCancellationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			LogCommandTimedOut(ex);
			return false;
		}
		// See the matching comment in ReceiveCommandAsync: a connected UDP socket reports a
		// disappeared peer as a SocketException rather than a timeout. Treat it as a fatal condition
		// for this transfer (same as a remote error or unexpected response) instead of letting it
		// propagate as an unhandled exception.
		catch (SocketException ex)
		{
			LogRemoteEndpointUnreachable(ex.SocketErrorCode);
			return null;
		}
	}

	private int _windowRetransmitAttempts;

	/// <summary>
	/// Receives a single TFTP command, applying the negotiated timeout and retransmission prompting.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the receive operation.</param>
	/// <returns>The successfully received command wrapped in a <see cref="TftpCommandResponseResult"/>, a
	/// <see cref="TftpCommandTimeoutResult"/> if nothing arrived in time, or <see langword="null"/> if receiving
	/// failed terminally.</returns>
	private async Task<TftpCommandResult?> ReceiveCommandAsync(CancellationToken cancellationToken)
	{
		int attempts = 0;
		while (true)
		{
			using var timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(_timeout));
			using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellationTokenSource.Token);
			var timeoutCancellationToken = timeoutCancellationTokenSource.Token;

			try
			{
				var response = await client.ReceiveAsync(linkedCancellationTokenSource.Token);

				// Response is not a valid TFTP packet. It could be corruption or noise. Keep listening.
				if (!CommandParser.TryParse(response.Buffer, out var command, logger))
				{
					LogInvalidResponseReceived();
					continue;
				}

				return new TftpCommandResponseResult(command, response.RemoteEndPoint);
			}
			catch (OperationCanceledException ex) when (timeoutCancellationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
			{
				if (++attempts >= MaxRetries)
				{
					LogReceivingTimedOut(attempts);
					return null;
				}

				LogCommandTimedOut(ex);
				return new TftpCommandTimeoutResult();
			}
			// A connected UDP socket surfaces the remote endpoint's ICMP "port unreachable" as a
			// SocketException (e.g. 10054/ConnectionReset) on the next receive. This is the expected,
			// common signal that the peer disappeared mid-transfer (crashed, was powered off, or the
			// process was killed) rather than an unexpected fault, so it terminates the transfer the
			// same way a timeout does, without the noise of an unhandled-exception stack trace.
			catch (SocketException ex)
			{
				LogRemoteEndpointUnreachable(ex.SocketErrorCode);
				return null;
			}
		}
	}
}
