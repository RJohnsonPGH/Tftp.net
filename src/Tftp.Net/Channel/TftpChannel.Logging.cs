using Microsoft.Extensions.Logging;
using System.Net;
using Tftp.Net.Commands.Properties;

namespace Tftp.Net.Channel;

internal static partial class TftpChannelLogging
{
	// Retry policy logs
	[LoggerMessage(
		EventId = 3005,
		Level = LogLevel.Warning,
		Message = "Retrying transmit: Attempt = {RetryCount}")]
	public static partial void LogRetryingTransmit(this ILogger<TftpChannel> logger, int retryCount);
}

internal sealed partial class TftpChannel
{
	// BeginListenAsync logs
	[LoggerMessage(
		EventId = 3000,
		Level = LogLevel.Error,
		Message = "Failed to parse command from {RemoteEndPoint}. Ignoring packet.")]
	public partial void LogFailedToParseCommand(IPEndPoint remoteEndPoint);

	[LoggerMessage(
		EventId = 3001,
		Level = LogLevel.Error,
		Message = "Received non-request command: OpCode = {OpCode}")]
	public partial void LogUnexpectedCommandOnListenPort(OpCode opCode);

	[LoggerMessage(
		EventId = 3002,
		Level = LogLevel.Critical,
		Message = "UdpClient has been disposed.")]
	public partial void LogUdpClientDisposed(Exception ex);

	[LoggerMessage(
		EventId = 3003,
		Level = LogLevel.Error,
		Message = "Socket exception occurred while receiving data.")]
	public partial void LogSocketException(Exception ex);

	[LoggerMessage(
		EventId = 3004,
		Level = LogLevel.Information,
		Message = "Listening operation was canceled.")]
	public partial void LogListeningCanceled(Exception ex);

	// NegotiateOptionsAsync logs
	[LoggerMessage(
		EventId = 3006,
		Level = LogLevel.Information,
		Message = "No options to negotiate.")]
	public partial void LogNoOptionsToNegotiate();

	[LoggerMessage(
		EventId = 3007,
		Level = LogLevel.Error,
		Message = "Failed to send Option Acknowledgement for handshake with {RemoteEndPoint}.")]
	public partial void LogFailedToSendOptionAcknowledgement(IPEndPoint remoteEndPoint);

	// ProcessHandshakeAsync logs
	[LoggerMessage(
		EventId = 3008,
		Level = LogLevel.Error,
		Message = "Failed to negotiate options for handshake with {RemoteEndPoint}.")]
	public partial void LogFailedToNegotiateOptions(IPEndPoint remoteEndPoint);

	[LoggerMessage(
		EventId = 3009,
		Level = LogLevel.Error,
		Message = "Data transfer failed for handshake with {RemoteEndPoint}.")]
	public partial void LogDataTransferFailed(IPEndPoint remoteEndPoint);

	// ReceiveFileAsync logs
	[LoggerMessage(
		EventId = 3010,
		Level = LogLevel.Warning,
		Message = "Invalid response received.")]
	public partial void LogInvalidResponseReceived();

	[LoggerMessage(
		EventId = 3011,
		Level = LogLevel.Error,
		Message = "Unexpected response for current state: Expected = {Expected}, Actual = {Actual}")]
	public partial void LogUnexpectedResponse(OpCode expected, OpCode actual);

	[LoggerMessage(
		EventId = 3012,
		Level = LogLevel.Warning,
		Message = "Unexpected block number in DATA/ACK: Expected = {Expected}, Actual = {Actual}")]
	public partial void LogUnexpectedBlockNumber(ushort expected, ushort actual);

	[LoggerMessage(
		EventId = 3013,
		Level = LogLevel.Trace,
		Message = "Wrote to file: Block Number = {BlockNumber}, Length = {Length}")]
	public partial void LogWroteToFile(ushort blockNumber, int length);

	[LoggerMessage(
		EventId = 3014,
		Level = LogLevel.Information,
		Message = "Received final data packet with block number {BlockNumber}. Transfer is complete.")]
	public partial void LogReceivedFinalDataPacket(ushort blockNumber);

	[LoggerMessage(
		EventId = 3025,
		Level = LogLevel.Error,
		Message = "Aborting file reception after {Retries} consecutive timeouts.")]
	public partial void LogAbortedFileReception(int retries);

	// SendFileAsync logs
	[LoggerMessage(
		EventId = 3015,
		Level = LogLevel.Error,
		Message = "Failed to send data packet with block number {BlockNumber}. Terminating transfer.")]
	public partial void LogFailedToSendDataPacket(ushort blockNumber);

	[LoggerMessage(
		EventId = 3016,
		Level = LogLevel.Trace,
		Message = "Transmitted data: Block Number = {BlockNumber}, Length = {Length}")]
	public partial void LogTransmittedData(ushort blockNumber, int length);

	[LoggerMessage(
		EventId = 3017,
		Level = LogLevel.Information,
		Message = "Transmitted final data packet with block number {BlockNumber}. Transfer is complete.")]
	public partial void LogTransmittedFinalDataPacket(ushort blockNumber);

	[LoggerMessage(
		EventId = 3026,
		Level = LogLevel.Error,
		Message = "Aborting file transmission after {Retries} window retransmission attempts.")]
	public partial void LogAbortedFileTransmission(int retries);

	// SendCommandAsync logs
	[LoggerMessage(
		EventId = 3018,
		Level = LogLevel.Error,
		Message = "Failed to send command after retries: OpCode = {OpCode}")]
	public partial void LogFailedToSendCommandAfterRetries(OpCode opCode);

	[LoggerMessage(
		EventId = 3019,
		Level = LogLevel.Error,
		Message = "Remote endpoint reported an error: ErrorCode = {ErrorCode}, Message = {Message}")]
	public partial void LogRemoteEndpointReportedError(ushort errorCode, string message);

	// RetryableSendCommandAsync logs
	[LoggerMessage(
		EventId = 3020,
		Level = LogLevel.Trace,
		Message = "Command does not expect an ACK. Transmit complete.")]
	public partial void LogCommandDoesNotExpectAck();

	[LoggerMessage(
		EventId = 3021,
		Level = LogLevel.Warning,
		Message = "Command timed out while waiting for ACK.")]
	public partial void LogCommandTimedOut(Exception ex);

	[LoggerMessage(
		EventId = 3022,
		Level = LogLevel.Error,
		Message = "Failed to transmit command.")]
	public partial void LogFailedToTransmitCommand(Exception ex);

	// TryValidateCommand logs
	[LoggerMessage(
		EventId = 3023,
		Level = LogLevel.Warning,
		Message = "Failed to parse transfer mode: Value = {Value}")]
	public partial void LogFailedToParseTransferMode(string value);

	[LoggerMessage(
		EventId = 3024,
		Level = LogLevel.Warning,
		Message = "Failed to parse option value: Options = {Options}")]
	public partial void LogFailedToParseOptions(string options);

	// InitiateRequestAsync logs
	[LoggerMessage(
		EventId = 3027,
		Level = LogLevel.Information,
		Message = "Transfer session locked onto responder endpoint: {Responder}")]
	public partial void LogTransferSessionLockedToResponder(IPEndPoint responder);

	// ApplyOptions logs
	[LoggerMessage(
		EventId = 3028,
		Level = LogLevel.Information,
		Message = "Applied negotiated options: Timeout = {Timeout}s, Block Size = {BlockSize}, Window Size = {WindowSize}, Transfer Size = {TransferSize}")]
	public partial void LogAppliedNegotiatedOptions(ushort timeout, ushort blockSize, ushort windowSize, ulong transferSize);

	// ReceiveCommandAsync logs
	[LoggerMessage(
		EventId = 3029,
		Level = LogLevel.Error,
		Message = "Receiving timed out after {Retries} attempts.")]
	public partial void LogReceivingTimedOut(int retries);

	[LoggerMessage(
		EventId = 3031,
		Level = LogLevel.Error,
		Message = "Failed to send request to {RemoteEndPoint}. Terminating transfer.")]
	public partial void LogFailedToSendRequest(IPEndPoint remoteEndPoint);

	// Peer-unreachable logs (connected UDP socket surfacing an ICMP "port unreachable" as a
	// SocketException). Deliberately logged without the exception object: the error code alone
	// identifies the condition, and this is expected/common enough (client crashed, was powered
	// off, or disconnected) that a full stack trace would just be noise.
	[LoggerMessage(
		EventId = 3030,
		Level = LogLevel.Warning,
		Message = "Remote endpoint became unreachable ({SocketErrorCode}). The peer likely disconnected or crashed. Terminating transfer.")]
	public partial void LogRemoteEndpointUnreachable(System.Net.Sockets.SocketError socketErrorCode);
}
