using Tftp.Net.Commands.Properties;

namespace Tftp.Net.Commands;

public interface ICommand
{
	OpCode OpCode { get; }
}

public abstract record Request(string Filename, string Mode, IEnumerable<KeyValuePair<string, string>> Options) : ICommand
{
	public abstract OpCode OpCode { get; }
}

public sealed record ReadRequest(string Filename, string Mode, IEnumerable<KeyValuePair<string, string>> Options) 
	: Request(Filename, Mode, Options), ICommand
{
	public override OpCode OpCode => OpCode.ReadRequest;
}

public sealed record WriteRequest(string Filename, string Mode, IEnumerable<KeyValuePair<string, string>> Options) 
	: Request(Filename, Mode, Options), ICommand
{ 
	public override OpCode OpCode => OpCode.WriteRequest;
}

public sealed record Data(ushort BlockNumber, ReadOnlyMemory<byte> DataBytes) : ICommand 
{
	public OpCode OpCode => OpCode.Data;
}

public sealed record Acknowledgement(ushort BlockNumber) : ICommand
{
	public OpCode OpCode => OpCode.Acknowledgement;
}

public sealed record Error(ushort ErrorCode, string Message) : ICommand
{
	public OpCode OpCode => OpCode.Error;

	// Predefined error codes from RFC 1350
	public static readonly Error FileNotFound = new(1, "File not found");
	public static readonly Error AccessViolation = new(2, "Access violation");
	public static readonly Error DiskFull = new(3, "Disk full or allocation exceeded");
	public static readonly Error IllegalOperation = new(4, "Illegal TFTP operation");
	public static readonly Error UnknownTransferId = new(5, "Unknown transfer ID");
	public static readonly Error FileAlreadyExists = new(6, "File already exists");
	public static readonly Error NoSuchUser = new(7, "No such user");
	public static readonly Error OptionNegotiationFailed = new(8, "Option negotiation failed");

	// Custom error codes
	public static readonly Error UnknownError = new(0, "Unknown error");
	public static readonly Error ServerBusy = new(10, "Server busy");
}

public sealed record OptionAcknowledgement(IEnumerable<KeyValuePair<string, string>> Options) : ICommand
{
	public OpCode OpCode => OpCode.OptionAcknowledgement;
}
