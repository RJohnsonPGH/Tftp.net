using Microsoft.Extensions.Logging;
using System;

namespace Tftp.Net.Cli.Commands;

public sealed partial class ClientCommand
{
	[LoggerMessage(
		EventId = 4100,
		Level = LogLevel.Error,
		Message = "Failed to resolve remote endpoint: Endpoint = {Endpoint}, Error = {Error}")]
	public partial void LogFailedToResolveRemoteEndpoint(string endpoint, string error);

	[LoggerMessage(
		EventId = 4101,
		Level = LogLevel.Critical,
		Message = "Local file does not exist: Path = {Path}")]
	public partial void LogLocalFileDoesNotExist(string path);

	[LoggerMessage(
		EventId = 4102,
		Level = LogLevel.Critical,
		Message = "Directory for local file does not exist: Path = {Path}")]
	public partial void LogLocalDirectoryDoesNotExist(string path);

	[LoggerMessage(
		EventId = 4103,
		Level = LogLevel.Information,
		Message = "Transfer cancelled.")]
	public partial void LogTransferCancelled();

	[LoggerMessage(
		EventId = 4104,
		Level = LogLevel.Information,
		Message = "Transfer completed: Bytes Transferred = {BytesTransferred}, Duration = {Duration}")]
	public partial void LogTransferCompleted(ulong bytesTransferred, TimeSpan duration);

	[LoggerMessage(
		EventId = 4105,
		Level = LogLevel.Critical,
		Message = "Transfer failed.")]
	public partial void LogTransferFailed();
}
