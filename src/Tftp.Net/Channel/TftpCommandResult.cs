using System.Net;
using Tftp.Net.Commands;

namespace Tftp.Net.Channel;

internal abstract record TftpCommandResult
{
	internal abstract bool IsSuccess { get; }
}

internal record TftpCommandRetryResult : TftpCommandResult
{
	internal override bool IsSuccess => false;
}

internal record TftpCommandErrorResult(Error Error) : TftpCommandResult
{
	internal override bool IsSuccess => false;
}

/// <summary>
/// Signals that nothing was received within the negotiated timeout, without exhausting retries.
/// </summary>
internal record TftpCommandTimeoutResult : TftpCommandResult
{
	internal override bool IsSuccess => false;
}

/// <summary>
/// Signals that the transmitted command by design does not expect any response.
/// </summary>
/// <remarks>Errors and acknowledgements are fire and forget: they complete successfully right after
/// being sent, so this outcome carries neither a response nor a responding endpoint.</remarks>
internal sealed record TftpCommandSentResult : TftpCommandResult
{
	internal override bool IsSuccess => true;
}

/// <summary>
/// Signals that the transmitted command was answered by a validated response from the given endpoint.
/// </summary>
/// <remarks>Both the response and the endpoint which transmitted it are always present: an instance of
/// this type cannot exist unless a datagram was actually received.</remarks>
internal sealed record TftpCommandResponseResult(ICommand Response, IPEndPoint Responder) : TftpCommandResult
{
	internal override bool IsSuccess => true;
}
