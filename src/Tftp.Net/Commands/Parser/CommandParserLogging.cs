using Microsoft.Extensions.Logging;
using Tftp.Net.Commands.Properties;


namespace Tftp.Net.Commands;

internal static partial class CommandParserLogging
{
	// Log event IDs follow the following format: ABCD
	// A - Command Parser logs (1)
	// B - OpCode of command parsed (0 for the main TryParse method - OpCode parsing)
	// C - Log level (0 for Trace, 1 for Debug, etc.)
	// D - Specific log message (0 always indicates success)

	// Command parsing logs
	[LoggerMessage(
		EventId = 1010,
		Level = LogLevel.Debug,
		Message = "Command successfully parsed.")]
	public static partial void LogSuccessfullyParsedCommand(this ILogger logger);

	[LoggerMessage(
		EventId = 1041,
		Level = LogLevel.Error,
		Message = "Failed to parse command.")]
	public static partial void LogFailedToParseCommand(this ILogger logger);

	[LoggerMessage(
		EventId = 1042,
		Level = LogLevel.Error,
		Message = "Failed to parse command: Could not read OpCode.")]
	public static partial void LogFailedToReadOpCode(this ILogger logger);

	[LoggerMessage(
		EventId = 1043,
		Level = LogLevel.Error,
		Message = "Failed to parse command: Invalid OpCode.")]
	public static partial void LogInvalidOpCode(this ILogger logger);

	[LoggerMessage(
		EventId = 1046,
		Level = LogLevel.Error,
		Message = "Failed to parse command: Trailing bytes after complete command.")]
	public static partial void LogTrailingBytesAfterCommand(this ILogger logger);


	// OpCode parsing logs
	[LoggerMessage(
		EventId = 1044,
		Level = LogLevel.Error,
		Message = "Failed to read OpCode: Could not read UInt16 from packet.")]
	public static partial void LogFailedToReadUInt16(this ILogger logger);

	[LoggerMessage(
		EventId = 1045,
		Level = LogLevel.Error,
		Message = "Failed to read OpCode: Value {Opcode} is not a valid OpCode.")]
	public static partial void LogInvalidOpCodeValue(this ILogger logger, ushort opcode);

	[LoggerMessage(
		EventId = 1016,
		Level = LogLevel.Debug,
		Message = "Read OpCode: Value = {Value}")]
	public static partial void LogReadOpCode(this ILogger logger, OpCode value);

	// Read request parsing logs - OpCode 1
	[LoggerMessage(
		EventId = 1141,
		Level = LogLevel.Error,
		Message = "Failed to parse read request.")]
	public static partial void LogFailedToParseReadRequest(this ILogger logger);

	// Write request parsing logs - OpCode 2
	[LoggerMessage(
		EventId = 1241,
		Level = LogLevel.Error,
		Message = "Failed to parse write request.")]
	public static partial void LogFailedToParseWriteRequest(this ILogger logger);

	// Common request parsing logs - OpCode 1 and 2
	[LoggerMessage(
		EventId = 1110,
		Level = LogLevel.Debug,
		Message = "Parsed request: Filename = {Filename}, Mode = {Mode}")]
	public static partial void LogParsedRequest(this ILogger logger, string filename, string mode);

	[LoggerMessage(
		EventId = 1142,
		Level = LogLevel.Error,
		Message = "Failed to parse request: Filename could not be parsed.")]
	public static partial void LogFailedToParseString(this ILogger logger);

	[LoggerMessage(
		EventId = 1144,
		Level = LogLevel.Error,
		Message = "Failed to parse request: Options could not be parsed.")]
	public static partial void LogFailedToParseRequestOptions(this ILogger logger);

	[LoggerMessage(
		EventId = 1145,
		Level = LogLevel.Error,
		Message = "Failed to parse options: Option name or value could not be parsed. Ending option parsing.")]
	public static partial void LogFailedToParseOptions(this ILogger logger);

	[LoggerMessage(
		EventId = 1116,
		Level = LogLevel.Debug,
		Message = "Successfully parsed option: Name = {Name}, Value = {Value}")]
	public static partial void LogParsedOption(this ILogger logger, string name, string value);

	[LoggerMessage(
		EventId = 1117,
		Level = LogLevel.Debug,
		Message = "No options to parse")]
	public static partial void LogParsedNoOptions(this ILogger logger);

	// Data parsing logs - OpCode 3
	[LoggerMessage(
		EventId = 1011,
		Level = LogLevel.Error,
		Message = "Failed to parse data: Could not read block number from packet.")]
	public static partial void LogFailedToReadDataBlockNumber(this ILogger logger);

	[LoggerMessage(
		EventId = 1012,
		Level = LogLevel.Trace,
		Message = "Parsed data: Block Number = {BlockNumber}, Data Length = {DataLength}")]
	public static partial void LogParsedData(this ILogger logger, ushort blockNumber, int dataLength);

	// Acknowledgement parsing logs - OpCode 4
	[LoggerMessage(
		EventId = 1400,
		Level = LogLevel.Trace,
		Message = "Parsed acknowledgement for block number: {BlockNumber}")]
	public static partial void LogParsedAcknowledgement(this ILogger logger, ushort blockNumber);

	[LoggerMessage(
		EventId = 1441,
		Level = LogLevel.Error,
		Message = "Failed to parse acknowledgement: Could not read block number from packet.")]
	public static partial void LogFailedToReadBlockNumber(this ILogger logger);

	// Error parsing logs - OpCode 5
	[LoggerMessage(
		EventId = 1510,
		Level = LogLevel.Debug,
		Message = "Parsed error: Error Code = {ErrorCode}, Message = {Message}")]
	public static partial void LogParsedError(this ILogger logger, ushort errorCode, string message);

	[LoggerMessage(
		EventId = 1541,
		Level = LogLevel.Error,
		Message = "Failed to parse error message: Message could not be parsed.")]
	public static partial void LogFailedToParseErrorMessage(this ILogger logger);
}
