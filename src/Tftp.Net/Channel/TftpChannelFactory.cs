using Microsoft.Extensions.Logging;
using Tftp.Net.Channel.Client;
using Tftp.Net.Commands.Validation;

namespace Tftp.Net.Channel;

/// <summary>
/// Provides a factory for creating TFTP channel instances using the specified logger factory.
/// </summary>
/// <param name="loggerFactory">The logger factory used to create loggers for TFTP channels. Cannot be null.</param>
/// <param name="optionSetValidator">The option set validator used for validating TFTP options. Cannot be null.</param>
internal class TftpChannelFactory(ILoggerFactory loggerFactory, OptionSetValidator optionSetValidator) : ITftpChannelFactory
{
    public ITftpChannel Create(IUdpClient client) => new TftpChannel(loggerFactory.CreateLogger<TftpChannel>(), optionSetValidator, client);
}