using Microsoft.Extensions.Logging;
using System.Net;

namespace Tftp.Net.Server;

public sealed partial class TftpServer
{
	// RunAsync logs
	[LoggerMessage(
		EventId = 5000,
		Level = LogLevel.Information,
		Message = "Received non-handshake command. Rejecting handshake.")]
	public partial void LogReceivedNonHandshakeCommand();

	[LoggerMessage(
		EventId = 5001,
		Level = LogLevel.Information,
		Message = "Unsupported transfer mode requested. Rejecting handshake.")]
	public partial void LogUnsupportedTransferModeRequested();

	[LoggerMessage(
		EventId = 5002,
		Level = LogLevel.Information,
		Message = "Write request received but write requests are not allowed. Rejecting handshake.")]
	public partial void LogWriteRequestNotAllowed();

	[LoggerMessage(
		EventId = 5003,
		Level = LogLevel.Information,
		Message = "Requested file already exists. Rejecting handshake.")]
	public partial void LogRequestedFileAlreadyExists();

	[LoggerMessage(
		EventId = 5004,
		Level = LogLevel.Information,
		Message = "Requested file does not exist. Rejecting handshake.")]
	public partial void LogRequestedFileDoesNotExist();

	[LoggerMessage(
		EventId = 5005,
		Level = LogLevel.Information,
		Message = "New transfer request queued.")]
	public partial void LogTransferRequestQueued();

	[LoggerMessage(
		EventId = 5006,
		Level = LogLevel.Warning,
		Message = "Transfer request queue full. Cancelling transfer.")]
	public partial void LogTransferRequestQueueFull();

	// ConsumeHandshakeAsync logs
	[LoggerMessage(
		EventId = 5007,
		Level = LogLevel.Information,
		Message = "Transfer channel bound to local endpoint: {LocalEndpoint}")]
	public partial void LogTransferChannelBoundToEndpoint(IPEndPoint localEndpoint);

	[LoggerMessage(
		EventId = 5008,
		Level = LogLevel.Error,
		Message = "Failed to process handshake.")]
	public partial void LogFailedToProcessHandshake();

	[LoggerMessage(
		EventId = 5009,
		Level = LogLevel.Error,
		Message = "Access to the file is denied. Cancelling transfer.")]
	public partial void LogFileAccessDenied();

	[LoggerMessage(
		EventId = 5010,
		Level = LogLevel.Error,
		Message = "Requested file not found. Cancelling transfer.")]
	public partial void LogRequestedFileNotFound();

	[LoggerMessage(
		EventId = 5011,
		Level = LogLevel.Error,
		Message = "The file is currently in use. Cancelling transfer.")]
	public partial void LogFileInUse();

	[LoggerMessage(
		EventId = 5012,
		Level = LogLevel.Error,
		Message = "Unknown exception when transferring file. Cancelling transfer.")]
	public partial void LogUnknownTransferException(Exception ex);

	[LoggerMessage(
		EventId = 5013,
		Level = LogLevel.Error,
		Message = "Requested filename '{Filename}' is invalid or resolves outside the root directory. Rejecting handshake.")]
	public partial void LogUnsafeFilenameRejected(string filename);

	[LoggerMessage(
		EventId = 5016,
		Level = LogLevel.Error,
		Message = "Failed to resolve requested filename '{Filename}' against root directory '{RootDirectory}'. The malformed input was rejected; this may indicate a denial-of-service attempt or a coding error.")]
	public partial void LogFailedToResolveRequestedPath(string filename, string rootDirectory, Exception ex);

	[LoggerMessage(
		EventId = 5014,
		Level = LogLevel.Information,
		Message = "Duplicate handshake from {RemoteEndpoint} ignored; a transfer for this endpoint is already queued or in progress.")]
	public partial void LogDuplicateHandshakeIgnored(IPEndPoint remoteEndpoint);

	[LoggerMessage(
		EventId = 5015,
		Level = LogLevel.Warning,
		Message = "Remote endpoint became unreachable ({SocketErrorCode}) while responding to a handshake. The peer likely disconnected or crashed.")]
	public partial void LogRemoteEndpointUnreachable(System.Net.Sockets.SocketError socketErrorCode);
}
