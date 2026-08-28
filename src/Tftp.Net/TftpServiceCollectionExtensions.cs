using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tftp.Net.Channel;
using Tftp.Net.Channel.Client;
using Tftp.Net.Client;
using Tftp.Net.Commands.Validation;
using Tftp.Net.Configuration;
using Tftp.Net.Server;

namespace Tftp.Net;

public static class TftpServiceCollectionExtensions
{
	public static IServiceCollection AddTftp(this IServiceCollection services)
	{
		services.AddSingleton<IUdpClientFactory, UdpClientWrapperFactory>();
		services.AddSingleton<ITftpChannelFactory, TftpChannelFactory>();
		services.AddSingleton<OptionSetValidator>();
		services.AddSingleton(sp =>
		{
			var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TftpServer>();
			var configProvider = sp.GetRequiredService<ITftpConfigurationProvider>();
			var clientFactory = sp.GetRequiredService<IUdpClientFactory>();
			var channelFactory = sp.GetRequiredService<ITftpChannelFactory>();
			return new TftpServer(logger, configProvider, clientFactory, channelFactory);
		});
		services.AddTransient(sp =>
		{
			var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TftpClient>();
			var clientFactory = sp.GetRequiredService<IUdpClientFactory>();
			var channelFactory = sp.GetRequiredService<ITftpChannelFactory>();
			return new TftpClient(logger, clientFactory, channelFactory);
		});

		return services;
	}
}
