using Tftp.Net.Channel.Client;

namespace Tftp.Net.Channel;

/// <summary>
/// Provides a factory for creating TFTP channel instances bound to a UDP client.
/// </summary>
public interface ITftpChannelFactory
{
    /// <summary>
    /// Creates a TFTP channel which sends and receives commands through the given UDP client.
    /// </summary>
    ITftpChannel Create(IUdpClient client);
}
