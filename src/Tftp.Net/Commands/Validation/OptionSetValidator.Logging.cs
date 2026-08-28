using Microsoft.Extensions.Logging;

namespace Tftp.Net.Commands.Validation;

internal sealed partial class OptionSetValidator
{
	[LoggerMessage(
		EventId = 4000,
		Level = LogLevel.Debug,
		Message = "No options provided, returning empty option set")]
	public partial void LogNoOptionsProvided();

	[LoggerMessage(
		EventId = 4009,
		Level = LogLevel.Error,
		Message = "Duplicate option: Name = {Name}")]
	public partial void LogDuplicateOptionName(string name);

	[LoggerMessage(
		EventId = 4001,
		Level = LogLevel.Trace,
		Message = "Options contain value for timeout: Value = {Value}")]
	public partial void LogTimeoutValue(string value);

	[LoggerMessage(
		EventId = 4002,
		Level = LogLevel.Error,
		Message = "Failed to parse timeout")]
	public partial void LogFailedToParseTimeout();

	[LoggerMessage(
		EventId = 4003,
		Level = LogLevel.Error,
		Message = "Parsed timeout value is out of range: Value = {Value}")]
	public partial void LogTimeoutOutOfRange(ushort value);

	[LoggerMessage(
		EventId = 4004,
		Level = LogLevel.Trace,
		Message = "Options contain value for block size: Value = {Value}")]
	public partial void LogBlockSizeValue(string value);

	[LoggerMessage(
		EventId = 4005,
		Level = LogLevel.Error,
		Message = "Failed to parse block size")]
	public partial void LogFailedToParseBlockSize();

	[LoggerMessage(
		EventId = 4006,
		Level = LogLevel.Error,
		Message = "Parsed block size value is out of range: Value = {Value}")]
	public partial void LogBlockSizeOutOfRange(ushort value);

	[LoggerMessage(
		EventId = 4007,
		Level = LogLevel.Trace,
		Message = "Options contain value for transfer size: Value = {Value}")]
	public partial void LogTransferSizeValue(string value);

	[LoggerMessage(
		EventId = 4008,
		Level = LogLevel.Error,
		Message = "Failed to parse transfer size")]
	public partial void LogFailedToParseTransferSize();

	[LoggerMessage(
		EventId = 4013,
		Level = LogLevel.Trace,
		Message = "Options contain value for window size: Value = {Value}")]
	public partial void LogWindowSizeValue(string value);

	[LoggerMessage(
		EventId = 4014,
		Level = LogLevel.Error,
		Message = "Failed to parse window size")]
	public partial void LogFailedToParseWindowSize();

	[LoggerMessage(
		EventId = 4015,
		Level = LogLevel.Error,
		Message = "Parsed window size value is out of range: Value = {Value}")]
	public partial void LogWindowSizeOutOfRange(ushort value);
}
