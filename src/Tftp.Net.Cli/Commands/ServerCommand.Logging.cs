using Microsoft.Extensions.Logging;

namespace Tftp.Net.Cli.Commands;

public sealed partial class ServerCommand
{
	[LoggerMessage(
		EventId = 4000,
		Level = LogLevel.Critical,
		Message = "Server directory does not exist: Path = {Directory}")]
	public partial void LogServerDirectoryDoesNotExist(string directory);

	[LoggerMessage(
		EventId = 4001,
		Level = LogLevel.Critical,
		Message = "Could not resolve bind address: Address = {Address}")]
	public partial void LogFailedToResolveBindAddress(string address);
}
