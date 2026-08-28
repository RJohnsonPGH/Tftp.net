using System.Net;
using System.Net.Sockets;

namespace Tftp.Net.Channel.Client;

/// <summary>
/// Abstracts UDP datagram communication for a single bound socket.
/// </summary>
/// <remarks>Implementations own an operating system socket which must be released deterministically;
/// consumers are therefore required to dispose every instance they create.</remarks>
public interface IUdpClient : IDisposable
{
	/// <summary>
	/// Gets the local endpoint the underlying socket is bound to.
	/// </summary>
	IPEndPoint LocalEndpoint { get; }

	void Connect(IPEndPoint remoteEndPoint);
	ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken = default);
	ValueTask<int> SendAsync(byte[] datagram, CancellationToken cancellationToken = default);
	ValueTask<int> SendAsync(byte[] datagram, IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default);
}
