using System.Net;

namespace Tftp.Net.Channel.Client;

public class UdpClientWrapperFactory : IUdpClientFactory
{
    public IUdpClient Create(IPEndPoint localEndpoint) => new UdpClientWrapper(localEndpoint);
}