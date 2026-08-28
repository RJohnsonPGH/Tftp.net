using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Tftp.Net.Client;
using Tftp.Net.Transfer;

namespace Tftp.Net.Cli.Commands;

public sealed partial class ClientCommand(ILogger<ClientCommand> logger, IServiceCollection serviceCollection) : AsyncCommand<ClientCommand.Settings>
{
	private const int DefaultTftpPort = 69;

	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<REMOTE_ENDPOINT>")]
		[Description("The TFTP server to connect to, as an IP address or FQDN, optionally followed by a colon and a port (e.g. 192.168.1.1 or 192.168.1.1:6900). Default port is 69.")]
		public string RemoteEndpoint { get; set; } = null!;

		[CommandArgument(1, "<REMOTE_FILENAME>")]
		[Description("The name of the file as it is known on the remote server.")]
		public string RemoteFilename { get; set; } = null!;

		[CommandArgument(2, "<LOCAL_FILENAME>")]
		[Description("The path of the local file to read from (upload) or write to (download).")]
		public string LocalFilename { get; set; } = null!;

		[CommandOption("-w|--write", false)]
		[Description("Upload the local file to the server (write request). Default is to download (read request).")]
		public bool Write { get; set; }

		[CommandOption("-t|--timeout", false)]
		[Description("The timeout in seconds to propose for option negotiation. Valid values are between 1 and 255. Default is 30 seconds.")]
		public ushort Timeout { get; set; } = 30;

		[CommandOption("-b|--block-size", false)]
		[Description("The block size in bytes to propose for option negotiation. Valid values are between 8 and 65464. Default is 65464 bytes.")]
		public ushort BlockSize { get; set; } = 65464;

		[CommandOption("-s|--window-size", false)]
		[Description("The window size in blocks to propose for option negotiation (RFC 7440). Valid values are between 1 and 65535. Default is 1.")]
		public ushort WindowSize { get; set; } = 1;

		public override ValidationResult Validate()
		{
			if (!TrySplitEndpoint(RemoteEndpoint, out _, out _))
			{
				return ValidationResult.Error("Remote endpoint must be an IP address or FQDN, optionally followed by a colon and a port (e.g. 192.168.1.1 or 192.168.1.1:6900).");
			}

			if (string.IsNullOrWhiteSpace(RemoteFilename))
			{
				return ValidationResult.Error("Remote filename is required.");
			}

			if (string.IsNullOrWhiteSpace(LocalFilename))
			{
				return ValidationResult.Error("Local filename is required.");
			}

			if (Timeout < 1 || Timeout > 255)
			{
				return ValidationResult.Error("Timeout must be between 1 and 255.");
			}

			if (BlockSize < 8 || BlockSize > 65464)
			{
				return ValidationResult.Error("Block size must be between 8 and 65464 bytes.");
			}

			if (WindowSize < 1)
			{
				return ValidationResult.Error("Window size must be between 1 and 65535.");
			}

			return ValidationResult.Success();
		}
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
	{
		// Resolve the remote endpoint before starting the transfer
		var (remoteEndpoint, endpointError) = await ResolveRemoteEndpointAsync(settings.RemoteEndpoint, cancellationToken);
		if (remoteEndpoint is null)
		{
			// A null endpoint is always accompanied by a non-null error message
			LogFailedToResolveRemoteEndpoint(settings.RemoteEndpoint, endpointError!);
			AnsiConsole.MarkupLine($"[red]Could not resolve remote endpoint:[/] {Markup.Escape(settings.RemoteEndpoint)}");
			return 1;
		}

		// Verify that the local file (upload) or its target directory (download) exists before transferring
		if (settings.Write)
		{
			if (!File.Exists(settings.LocalFilename))
			{
				LogLocalFileDoesNotExist(settings.LocalFilename);
				AnsiConsole.MarkupLine($"[red]Local file does not exist:[/] {Markup.Escape(settings.LocalFilename)}");
				return 1;
			}
		}
		else
		{
			// GetDirectoryName can only be null when the path is a drive root itself, which is not a valid file target
			var localDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.LocalFilename)) ?? Environment.CurrentDirectory;
			if (!Directory.Exists(localDirectory))
			{
				LogLocalDirectoryDoesNotExist(localDirectory);
				AnsiConsole.MarkupLine($"[red]Directory for local file does not exist:[/] {Markup.Escape(localDirectory)}");
				return 1;
			}
		}

		// Compose the client from the validated command line settings before activation
		using var provider = serviceCollection.BuildServiceProvider();
		var client = provider.GetRequiredService<TftpClient>();

		using var _ = logger.BeginScope("Starting client: Server = {Endpoint}, Mode = {Mode}, Remote Filename = {RemoteFilename}, Local Filename = {LocalFilename}, Block Size = {BlockSize}, Timeout = {Timeout}, Window Size = {WindowSize}",
			remoteEndpoint, settings.Write ? "Write" : "Read", settings.RemoteFilename, settings.LocalFilename, settings.BlockSize, settings.Timeout, settings.WindowSize);

		// Display client info
		AnsiConsole.MarkupLine($"[green]TFTP Client starting...[/]");
		AnsiConsole.MarkupLine($"[cyan]Server:[/] {Markup.Escape(remoteEndpoint.ToString())}");
		AnsiConsole.MarkupLine($"[cyan]Remote file:[/] {Markup.Escape(settings.RemoteFilename)}");
		AnsiConsole.MarkupLine($"[cyan]Local file:[/] {Markup.Escape(settings.LocalFilename)}");
		AnsiConsole.MarkupLine($"[cyan]Mode:[/] {(settings.Write ? "upload (write request)" : "download (read request)")}");
		AnsiConsole.MarkupLine($"[grey]Press Ctrl+C to cancel[/]");
		AnsiConsole.WriteLine();

		// Run the transfer inside a progress display using Spectre.Console's Progress API
		return await AnsiConsole.Progress()
			.AutoRefresh(true)
			.AutoClear(false)
			.HideCompleted(false)
			.Columns(
				new TaskDescriptionColumn(),
				new ProgressBarColumn(),
				new PercentageColumn(),
				new DownloadedColumn(),
				new TransferSpeedColumn(),
				new RemainingTimeColumn(),
				new SpinnerColumn())
			.StartAsync<int>(async ctx =>
			{
				var description = $"[cyan]{Markup.Escape(remoteEndpoint.ToString())}[/] [green]{Markup.Escape(settings.RemoteFilename)}[/]";
				var task = ctx.AddTask(description);

				ulong lastBytes = 0;
				var progress = new Progress<TftpTransferProgress>(transfer =>
				{
					// Set as indeterminate if total size is unknown, otherwise set max value
					if (transfer.TotalBytes > 0)
					{
						task.IsIndeterminate = false;
						task.MaxValue = transfer.TotalBytes;
					}
					else
					{
						task.IsIndeterminate = true;
					}

					// Terminal events are reported from the pre-transfer state and carry a stale
					// byte count, so the progress bar and byte total only ever move forward
					if (transfer.BytesTransferred > lastBytes)
					{
						lastBytes = transfer.BytesTransferred;
						task.Value = lastBytes;
					}
				});

				var stopwatch = Stopwatch.StartNew();
				bool success;
				try
				{
					success = await client.RunAsync(
						progress,
						remoteEndpoint,
						settings.Write,
						settings.RemoteFilename,
						settings.LocalFilename,
						settings.Timeout,
						settings.BlockSize,
						settings.WindowSize,
						cancellationToken);
				}
				catch (OperationCanceledException)
				{
					// Cancellation is expected when the user cancels the transfer (Ctrl+C)
					LogTransferCancelled();
					AnsiConsole.MarkupLine("[yellow]Transfer canceled.[/]");
					return 1;
				}
				finally
				{
					task.StopTask();
					stopwatch.Stop();
				}

				if (success)
				{
					LogTransferCompleted(lastBytes, stopwatch.Elapsed);
					AnsiConsole.MarkupLine($"[green]Transfer completed:[/] {lastBytes:N0} bytes in {stopwatch.Elapsed.TotalSeconds:F1}s");
					return 0;
				}

				LogTransferFailed();
				AnsiConsole.MarkupLine("[red]Transfer failed.[/]");
				if (!settings.Write)
				{
					var partialBytes = new FileInfo(settings.LocalFilename).Length;
					if (partialBytes > 0)
					{
						AnsiConsole.MarkupLine($"[yellow]Partial file kept at:[/] {Markup.Escape(settings.LocalFilename)} ({partialBytes:N0} bytes)");
					}
				}
				return 1;
			});
	}

	/// <summary>
	/// Splits a remote endpoint of the form host, host:port or [ipv6]:port into its host and port parts.
	/// </summary>
	/// <param name="input">The endpoint to split.</param>
	/// <param name="host">When this method returns <see langword="true"/>, contains the host part; otherwise, an empty string.</param>
	/// <param name="port">When this method returns <see langword="true"/>, contains the port; otherwise, <see cref="DefaultTftpPort"/>.</param>
	/// <returns><see langword="true"/> if the input is syntactically valid; otherwise, <see langword="false"/>.</returns>
	private static bool TrySplitEndpoint(string input, out string host, out int port)
	{
		host = string.Empty;
		port = DefaultTftpPort;

		if (input.Length == 0)
		{
			return false;
		}

		// Bracketed IPv6 literal, optionally followed by a port: [::1] or [::1]:6900
		if (input[0] == '[')
		{
			var closing = input.IndexOf(']');
			if (closing < 1)
			{
				return false;
			}

			host = input[1..closing];
			var remainder = input[(closing + 1)..];
			if (remainder.Length > 0)
			{
				if (remainder[0] != ':' ||
					!int.TryParse(remainder[1..], out port) ||
					port < 1 || port > 65535)
				{
					return false;
				}
			}

			return IPAddress.TryParse(host, out _);
		}

		var colonParts = input.Split(':');

		// Two or more colons outside brackets: a bare IPv6 literal without a port
		if (colonParts.Length >= 3)
		{
			return IPAddress.TryParse(input, out _);
		}

		if (colonParts.Length == 2)
		{
			host = colonParts[0];
			if (host.Length == 0 ||
				!int.TryParse(colonParts[1], out port) ||
				port < 1 || port > 65535)
			{
				return false;
			}

			return true;
		}

		host = input;
		return true;
	}

	/// <summary>
	/// Resolves a remote endpoint string (IP literal or FQDN, with an optional port) to an <see cref="IPEndPoint"/>.
	/// </summary>
	/// <param name="input">The endpoint to resolve.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the name resolution.</param>
	/// <returns>A tuple containing the resolved endpoint and <see langword="null"/> on success, or <see langword="null"/> and an
	/// error message describing why the endpoint could not be resolved.</returns>
	private static async Task<(IPEndPoint? Endpoint, string? Error)> ResolveRemoteEndpointAsync(string input, CancellationToken cancellationToken)
	{
		if (!TrySplitEndpoint(input, out var host, out var port))
		{
			return (null, "Remote endpoint must be an IP address or FQDN, optionally followed by a colon and a port (e.g. 192.168.1.1 or 192.168.1.1:6900).");
		}

		IPAddress address;
		if (IPAddress.TryParse(host, out var parsedAddress))
		{
			address = parsedAddress;
		}
		else
		{
			try
			{
				address = (await Dns.GetHostAddressesAsync(host, cancellationToken))[0];
			}
			catch (SocketException)
			{
				return (null, $"Could not resolve host: {host}");
			}
		}

		return (new IPEndPoint(address, port), null);
	}
}
