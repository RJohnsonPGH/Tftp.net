namespace Tftp.Net.Transfer;

public sealed record TftpTransferProgress(Guid Id, TftpTransferState State, string Endpoint, string Filename, ulong BytesTransferred, ulong TotalBytes);

public enum TftpTransferState
{
	Handshake,
	OptionNegotiation,
	DataTransfer,
	Failed,
	Completed
}