using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Tftp.Net.Configuration;
using Tftp.Net.Server;
using Tftp.Net.Transfer;

namespace Tftp.Net.Cli.Commands;

public sealed partial class ServerCommand(ILogger<ServerCommand> logger, IServiceCollection serviceCollection) : AsyncCommand<ServerCommand.ServerSettings>
{
	private readonly ConcurrentDictionary<Guid, ProgressTask> _activeTransfers = new();
	private readonly Lock _progressLock = new();
	private readonly Lock _historyLock = new();
	private readonly List<CompletedTransferInfo> _completedTransfers = [];
	private ProgressContext? _progressContext;

	private sealed class CompletedTransferInfo
	{
		public required string Endpoint { get; init; }
		public required string Filename { get; init; }
		public required bool IsSuccess { get; init; }
		public required DateTime CompletedAt { get; init; }
	}

	public sealed class ServerSettings : CommandSettings
	{
		[CommandOption("-d|--directory", false)]
		[Description("The directory to serve files from. Created if it does not exist. Default is the current directory.")]
		public string Directory { get; set; } = Environment.CurrentDirectory;

		[CommandOption("-a|--address", false)]
		[Description("The IP address to bind to the server. Default is all interfaces.")]
		public string? Address { get; set; }

		[CommandOption("-p|--port", false)]
		[Description("The port the server will listen on. Default is 69.")]
		public int Port { get; set; } = 69;

		[CommandOption("-w|--allow-write", false)]
		[Description("Allow clients to upload (write) files to the server. Default is read-only.")]
		public bool AllowWriteRequests { get; set; }

		[CommandOption("-b|--max-block-size", false)]
		[Description("The maximum block size that can be negotiated with clients. Valid values are between 8 and 65464 bytes. Default is 65464 bytes.")]
		public ushort MaxBlockSize { get; set; } = 65464;

		[CommandOption("-t|--max-timeout", false)]
		[Description("The maximum timeout that can be negotiated with clients. Default is 30 seconds.")]
		public ushort MaxTimeout { get; set; } = 30;

		[CommandOption("-s|--max-window-size", false)]
		[Description("The maximum window size that can be negotiated with clients. Valid values are between 1 and 65535. Default is 1.")]
		public ushort MaxWindowSize { get; set; } = 1;

		public override ValidationResult Validate()
		{
			// Verify that the provided directory exists before starting the server
			if (!System.IO.Directory.Exists(Directory))
			{
				return ValidationResult.Error("Serve directory does not exist.");
			}

			if (Port < 1 || Port > 65535)
			{
				return ValidationResult.Error("Port must be between 1 and 65535.");
			}

			// Accept IPv4/IPv6 literals as well as FQDNs; FQDNs are resolved via DNS before the
			// server binds (see ExecuteAsync). UriHostNameType.Dns covers valid hostnames.
			var hostNameType = Uri.CheckHostName(Address);
			if (!string.IsNullOrEmpty(Address) && // Address is optional; if not provided, server binds to all interfaces
				hostNameType != UriHostNameType.IPv4 && // If provided, must be a valid IPv4/IPv6 literal or FQDN
				hostNameType != UriHostNameType.IPv6 &&
				hostNameType != UriHostNameType.Dns)
			{
				return ValidationResult.Error("Address must be a valid IP address or FQDN.");
			}

			if (MaxBlockSize < 8 || MaxBlockSize > 65464)
			{
				return ValidationResult.Error("Max block size must be between 8 and 65464 bytes.");
			}

			if (MaxTimeout < 1 || MaxTimeout > 255)
			{
				return ValidationResult.Error("Max timeout must be between 1 and 255.");
			}

			if (MaxWindowSize < 1)
			{
				return ValidationResult.Error("Max window size must be between 1 and 65535.");
			}

			return ValidationResult.Success();
		}
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, ServerSettings settings, CancellationToken cancellationToken)
	{
		IPAddress bindAddress;
		if (string.IsNullOrWhiteSpace(settings.Address))
		{
			bindAddress = IPAddress.Any;
		}
		else if (IPAddress.TryParse(settings.Address, out var parsedAddress))
		{
			bindAddress = parsedAddress;
		}
		else
		{
			// Validate() only allows values which are IP literals or syntactically valid FQDNs;
			// resolve the hostname via DNS here, matching ClientCommand's behavior for the remote
			// endpoint, instead of the previous unconditional IPAddress.Parse (which throws for FQDNs).
			try
			{
				bindAddress = (await Dns.GetHostAddressesAsync(settings.Address, cancellationToken))[0];
			}
			catch (SocketException)
			{
				LogFailedToResolveBindAddress(settings.Address);
				AnsiConsole.MarkupLine($"[red]Could not resolve bind address:[/] {Markup.Escape(settings.Address)}");
				return 1;
			}
		}

		var localEndpoint = new IPEndPoint(bindAddress, settings.Port);

		// Compose the server from the validated command line settings before activation
		serviceCollection.AddSingleton<ITftpConfigurationProvider>(
			new TftpConfigurationProvider(settings.Directory, allowWriteRequests: settings.AllowWriteRequests, maxBlockSize: settings.MaxBlockSize, maxTimeoutSeconds: settings.MaxTimeout, maxWindowSize: settings.MaxWindowSize));
		await using var provider = serviceCollection.BuildServiceProvider();
		var server = provider.GetRequiredService<TftpServer>();

		using var _ = logger.BeginScope("Starting server: Directory = {Directory}, Interface = {Address}:{Port}, Allow Write = {AllowWrite}",
			settings.Directory, settings.Address ?? "0.0.0.0", settings.Port, settings.AllowWriteRequests);

		// Subscribe to transfer progress events
		server.TransferProgress += OnTransferProgress;

		// Display server info
		AnsiConsole.MarkupLine($"[green]TFTP Server starting...[/]");
		AnsiConsole.MarkupLine($"[cyan]Directory:[/] {Markup.Escape(settings.Directory)}");
		AnsiConsole.MarkupLine($"[cyan]Writes allowed:[/] {(settings.AllowWriteRequests ? "yes" : "no")}");
		AnsiConsole.MarkupLine($"[cyan]Listening on:[/] {localEndpoint}");
		AnsiConsole.MarkupLine($"[grey]Press Ctrl+C to stop[/]");
		AnsiConsole.WriteLine();

		// Create the progress display using Spectre.Console's Progress API
		await AnsiConsole.Progress()
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
			.UseRenderHook((renderable, tasks) =>
			{
				var footer = CreateCompletedTransfersTable();
				return new Rows(renderable, footer);
			})
			.StartAsync(async ctx =>
			{
				_progressContext = ctx;

				// Run the server
				try
				{
					await server.RunAsync(localEndpoint, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					// Cancellation is expected when user stops the server (Ctrl+C)
					logger.LogInformation("Server shutdown requested.");
					AnsiConsole.MarkupLine("[yellow]Server shutdown requested.[/]");
				}
				finally
				{
					server.TransferProgress -= OnTransferProgress;
					_progressContext = null;
				}
			});

		return 0;
	}

	private void OnTransferProgress(object? sender, TftpTransferProgress progress)
	{
		if (_progressContext is null)
			return;

		// Lock to prevent race condition where addValueFactory is called multiple times concurrently
		lock (_progressLock)
		{
			_activeTransfers.AddOrUpdate(
				progress.Id,
				key =>
				{
					// Create a new progress task for this transfer
					var description = $"[cyan]{progress.Endpoint}[/] [green]{Markup.Escape(progress.Filename)}[/]";
					var task = _progressContext.AddTask(description);
					
					// Set as indeterminate if total size is unknown, otherwise set max value
					if (progress.TotalBytes > 0)
					{
						task.MaxValue = progress.TotalBytes;
						task.Value = progress.BytesTransferred;
					}
					else
					{
						task.IsIndeterminate = true;
					}
					
					return task;
				},
				(key, existingTask) =>
				{
					// Update the existing progress task
					var description = $"[cyan]{progress.Endpoint}[/] [green]{Markup.Escape(progress.Filename)}[/]";
					existingTask.Description = description;
					
					// Update max value and current value
					if (progress.TotalBytes > 0)
					{
						// If we now know the total size, switch from indeterminate to determinate
						if (existingTask.IsIndeterminate)
						{
							existingTask.IsIndeterminate = false;
						}
						existingTask.MaxValue = progress.TotalBytes;
						existingTask.Value = progress.BytesTransferred;
					}
					else
					{
						// Still unknown, keep indeterminate
						existingTask.IsIndeterminate = true;
					}

					// Mark as complete or failed
					if (progress.State == TftpTransferState.Completed)
					{
						existingTask.StopTask();
					}
					else if (progress.State == TftpTransferState.Failed)
					{
						existingTask.StopTask();
					}

					return existingTask;
				});

			// Once a transfer has reached a terminal state, its progress task has been stopped above
			// and is no longer updated. Remove it from the active set so the dictionary does not grow
			// without bound for the lifetime of a long-running server.
			if (progress.State == TftpTransferState.Completed || progress.State == TftpTransferState.Failed)
			{
				_activeTransfers.TryRemove(progress.Id, out _);
			}
		}

		// Add to completed transfers history when transfer finishes
		if (progress.State == TftpTransferState.Completed || progress.State == TftpTransferState.Failed)
		{
			lock (_historyLock)
			{
				// Add to the beginning of the list (most recent first)
				_completedTransfers.Insert(0, new CompletedTransferInfo
				{
					Endpoint = progress.Endpoint ?? "Unknown",
					Filename = progress.Filename ?? "unknown",
					IsSuccess = progress.State == TftpTransferState.Completed,
					CompletedAt = DateTime.UtcNow
				});

				// Keep only the last 10 transfers (circular buffer)
				if (_completedTransfers.Count > 10)
				{
					_completedTransfers.RemoveAt(_completedTransfers.Count - 1);
				}
			}
		}
	}

	private Table CreateCompletedTransfersTable()
	{
		lock (_historyLock)
		{
			var table = new Table()
				.Border(TableBorder.Rounded)
				.BorderColor(Color.Grey)
				.Title("[yellow]Recent Transfers[/]")
				.AddColumn(new TableColumn("[yellow]Result[/]").Centered().Width(8))
				.AddColumn(new TableColumn("[yellow]Client Address[/]"))
				.AddColumn(new TableColumn("[yellow]Filename[/]"))
				.AddColumn(new TableColumn("[yellow]Occurred[/]").RightAligned());

			foreach (var transfer in _completedTransfers)
			{
				var statusText = transfer.IsSuccess ? "[green]Success[/]" : "[red]Failed[/]";
				var timeAgo = DateTime.UtcNow - transfer.CompletedAt;
				var timeText = timeAgo.TotalSeconds < 60
					? $"{(int)timeAgo.TotalSeconds}s ago"
					: timeAgo.TotalMinutes < 60
						? $"{(int)timeAgo.TotalMinutes}m ago"
						: $"{(int)timeAgo.TotalHours}h ago";

				table.AddRow(
					statusText,
					$"[cyan]{transfer.Endpoint}[/]",
					$"[green]{Markup.Escape(transfer.Filename)}[/]",
					$"[grey]{timeText}[/]");
			}

			return table;
		}
	}
}
