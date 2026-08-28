using System.Net;

namespace Tftp.Net.Channel.Client;

public interface IUdpClientFactory
{
    IUdpClient Create(IPEndPoint localEndpoint);
}