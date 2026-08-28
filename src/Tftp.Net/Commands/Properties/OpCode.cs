namespace Tftp.Net.Commands.Properties;

public enum OpCode : ushort
{
	ReadRequest = 1,
	WriteRequest = 2,
	Data = 3,
	Acknowledgement = 4,
	Error = 5,
	OptionAcknowledgement = 6
}
