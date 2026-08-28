using System.Net;
using Tftp.Net.Commands;
using Tftp.Net.Commands.Properties;

namespace Tftp.Net.Transfer;

/// <summary>
/// Base contract for all handshake messages exchanged with a remote endpoint.
/// </summary>
public interface IHandshake
{
	/// <summary>
	/// Gets the endpoint of the remote party which sent (or will receive) the handshake.
	/// </summary>
	IPEndPoint RemoteEndpoint { get; }
}

/// <summary>
/// Marker interface for handshakes received or initiated from the server perspective.
/// </summary>
public interface IServerHandshake : IHandshake { }

/// <summary>
/// Marker interface for handshakes received or initiated from the client perspective.
/// </summary>
public interface IClientHandshake : IHandshake { }

/// <summary>
/// Base record for all handshakes carrying only a remote endpoint.
/// </summary>
public abstract record Handshake(IPEndPoint RemoteEndpoint) : IHandshake;

/// <summary>
/// A handshake which signals that an incoming request failed validation and carries the error to report.
/// </summary>
public record ErrorHandshake(IPEndPoint RemoteEndpoint, Error Error) : Handshake(RemoteEndpoint), IServerHandshake, IClientHandshake;

/// <summary>
/// Base record for handshakes representing read or write requests.
/// </summary>
public abstract record RequestHandshake(IPEndPoint RemoteEndpoint, string Filename, TransferMode Mode, OptionSet Options) : Handshake(RemoteEndpoint);

// Server

/// <summary>
/// Base record for server-side request handshakes.
/// </summary>
public abstract record ServerHandshake(IPEndPoint RemoteEndpoint, string Filename, TransferMode Mode, OptionSet Options) :
	RequestHandshake(RemoteEndpoint, Filename, Mode, Options), IServerHandshake;

/// <summary>
/// An incoming write request received by the server.
/// </summary>
public sealed record ServerWriteRequestHandshake(IPEndPoint RemoteEndpoint, string Filename, TransferMode Mode, OptionSet Options) :
	ServerHandshake(RemoteEndpoint, Filename, Mode, Options);

/// <summary>
/// An incoming read request received by the server.
/// </summary>
public sealed record ServerReadRequestHandshake(IPEndPoint RemoteEndpoint, string Filename, TransferMode Mode, OptionSet Options) :
	ServerHandshake(RemoteEndpoint, Filename, Mode, Options);

// Client

/// <summary>
/// Base record for client-side request handshakes.
/// </summary>
public record ClientHandshake(IPEndPoint RemoteEndpoint, string Filename, TransferMode Mode, OptionSet Options) :
	RequestHandshake(RemoteEndpoint, Filename, Mode, Options), IClientHandshake;

/// <summary>
/// An outgoing write request initiated by the client.
/// </summary>
public sealed record ClientWriteRequestHandshake(IPEndPoint RemoteEndpoint, string Filename, TransferMode Mode, OptionSet Options) :
	ClientHandshake(RemoteEndpoint, Filename, Mode, Options);

/// <summary>
/// An outgoing read request initiated by the client.
/// </summary>
public sealed record ClientReadRequestHandshake(IPEndPoint RemoteEndpoint, string Filename, TransferMode Mode, OptionSet Options) :
	ClientHandshake(RemoteEndpoint, Filename, Mode, Options);
