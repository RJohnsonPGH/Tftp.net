using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using System;
using System.Threading;
using Tftp.Net;
using Tftp.Net.Cli.Commands;
using Tftp.Net.Cli.Internal;

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true;
	cancellationTokenSource.Cancel();
};

var services = new ServiceCollection();

services.AddLogging(builder =>
{
	builder.SetMinimumLevel(LogLevel.Information);
	//builder.AddDebug();
	//builder.AddSimpleConsole(options =>
	//{
	//	options.SingleLine = true;
	//	options.TimestampFormat = "HH:mm:ss ";
	//});
});

// The TFTP library services. Note that the ITftpConfigurationProvider is intentionally not
// registered here: it is composed by the server command from the parsed command line options.
services.AddTftp();

// Expose the service collection so commands can compose additional services (e.g. the
// configuration provider) from their validated settings before activating the TftpServer.
services.AddSingleton<IServiceCollection>(services);

var registrar = new TypeRegistrar(services);

var app = new CommandApp(registrar);

app.Configure(config =>
{
	config.Settings.ApplicationName = "Tftp.Net.Cli";

	config.AddCommand<ServerCommand>("server")
		.WithDescription("Start a TFTP server. Defaults to serving files from the current directory and to listen on all interfaces on port 69.")
		.WithExample("server [--directory <DIRECTORY>] [--address <IP_ADDRESS>] [--port <PORT>] [--allow-write] [--max-block-size <BLOCKSIZE>] [--max-timeout <TIMEOUT>] [--max-window-size <WINDOWSIZE>]")
		.WithExample("server")
		.WithExample("server -d C:\\TftpRoot -a 192.168.1.1 -p 69");

	config.AddCommand<ClientCommand>("client")
		.WithDescription("Transfer a file to or from a TFTP server. Defaults to downloading (read request); use --write to upload.")
		.WithExample("client <REMOTE_ENDPOINT> <REMOTE_FILENAME> <LOCAL_FILENAME> [--write] [--timeout <TIMEOUT>] [--block-size <BLOCKSIZE>] [--window-size <WINDOWSIZE>]")
		.WithExample("client 192.168.1.1 test.txt test.txt")
		.WithExample("client 192.168.1.1:6900 test.txt test.txt --write --timeout 10 --block-size 1024");
});

return await app.RunAsync(args, cancellationTokenSource.Token);
