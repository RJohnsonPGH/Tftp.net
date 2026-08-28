using System.Net;
using System.Net.Sockets;

namespace Tftp.Net.Channel.Client;

/// <summary>
/// Provides a wrapper for UDP client operations, enabling sending and receiving datagrams over a network endpoint.
/// </summary>
/// <remarks>This class implements the IUdpClient interface to abstract UDP communication. Use this wrapper to
/// manage UDP connections and perform asynchronous send and receive operations. The client must be bound to a valid
/// local endpoint before use.</remarks>
/// <param name="localEndpoint">The local network endpoint to bind the UDP client to. Specifies the address and port used for communication.</param>
internal class UdpClientWrapper(IPEndPoint localEndpoint) : IUdpClient, IDisposable
{
	// A generous receive buffer prevents kernel-level drops when a remote endpoint bursts a
	// whole window of data packets at once during a windowed transfer (RFC 7440).
	private const int SocketBufferSize = 4 * 1024 * 1024;

	private readonly UdpClient _udpClient = CreateUdpClient(localEndpoint);

	private static UdpClient CreateUdpClient(IPEndPoint localEndpoint)
	{
		var udpClient = new UdpClient(localEndpoint);
		udpClient.Client.ReceiveBufferSize = SocketBufferSize;
		udpClient.Client.SendBufferSize = SocketBufferSize;
		return udpClient;
	}

	public IPEndPoint LocalEndpoint => (IPEndPoint)_udpClient.Client.LocalEndPoint!;

	public void Connect(IPEndPoint remoteEndPoint) => 
		_udpClient.Connect(remoteEndPoint);

    public ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken = default) =>
        _udpClient.ReceiveAsync(cancellationToken);

	public ValueTask<int> SendAsync(byte[] datagram, CancellationToken cancellationToken = default) =>
		_udpClient.SendAsync(datagram, cancellationToken);

	public ValueTask<int> SendAsync(byte[] datagram, IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default) =>
		_udpClient.SendAsync(datagram, remoteEndPoint, cancellationToken);

	public void Dispose() => _udpClient.Dispose();
}
