using System.Net;
using Tftp.Net.Commands;
using Tftp.Net.Transfer;

namespace Tftp.Net.Channel;

/// <summary>
/// Implements the TFTP message exchange over a UDP channel, covering both the server side
/// (accepting incoming requests) and the client side (initiating outgoing requests).
/// </summary>
public interface ITftpChannel
{
    /// <summary>
    /// Listens for incoming requests on the well-known port and yields one handshake per valid request packet.
    /// </summary>
    IAsyncEnumerable<IServerHandshake> ServerListenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an error to a remote endpoint before any transfer has been established.
    /// </summary>
    Task SendPreTransferErrorAsync(Error error, IPEndPoint remoteEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an incoming handshake (server side) and performs the associated file transfer.
    /// </summary>
    Task<bool> ProcessHandshakeAsync(IProgress<TftpTransferProgress> progress, RequestHandshake handshake, FileStream fileStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates an outgoing request against a remote server (client side) and performs the associated file transfer.
    /// </summary>
    Task<bool> InitiateRequestAsync(IProgress<TftpTransferProgress> progress, ClientHandshake handshake, FileStream fileStream, CancellationToken cancellationToken = default);
}
