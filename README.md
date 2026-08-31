# Tftp.Net

A modern .NET library and command line tool for the Trivial File Transfer Protocol (TFTP). It provides a complete TFTP client and server built on current .NET platform features: dependency injection, `Microsoft.Extensions.Logging`, and native async I/O throughout.

The repository contains:

| Project | Description |
| --- | --- |
| `src/Tftp.Net` | The TFTP client/server library (class library, packable). |
| `src/Tftp.Net.Cli` | A fully functional command line client and server executable built on the library. |
| `test/Tftp.Net.Tests` | Unit tests (xUnit v3). |

## Requirements

- .NET 10 SDK (`net10.0` target framework).

## Design

### .NET hosting model: dependency injection and logging

The library is written to be hosted, not to host itself. It has no opinion about where it runs: it can be dropped into an ASP.NET Core application, a worker service, a desktop app, or a plain console program.

Integration starts with a single extension method, `AddTftp()` on `IServiceCollection` (in `Tftp.Net`):

- `TftpServer` is registered as a **singleton** (one long-running server per process).
- `TftpClient` is registered as a **transient** (one instance per transfer).
- The internal plumbing, `IUdpClientFactory` and `ITftpChannelFactory`, is registered as singletons.

`ITftpConfigurationProvider` is deliberately **not** registered by the library. The host application registers its own implementation, which is what lets configuration flow in from whatever source the application already uses: command line options (as `Tftp.Net.Cli` does), `appsettings.json`, environment variables, or a custom record. The library ships `TftpConfigurationProvider`, an immutable reference implementation, for convenience:

```csharp
services.AddSingleton<ITftpConfigurationProvider>(
    new TftpConfigurationProvider(rootDirectory: "./tftp-root", allowWriteRequests: true));
```

Logging follows the same pattern. The library depends only on `Microsoft.Extensions.Logging` and never writes to the console or a file itself. Every component (`TftpServer`, `TftpClient`, `TftpChannel`, the command parser and validator) receives a typed `ILogger<T>` through constructor injection and emits structured messages with scopes (`BeginScope`) per handshake and per transfer, so logs correlate cleanly in any sink. The host chooses the providers, exactly as it would for the rest of its application:

```csharp
services.AddLogging(builder => builder.AddSimpleConsole());
```

A minimal complete host is just a `ServiceCollection` and a service provider; the Generic Host is optional. The CLI executable demonstrates this pattern end to end: it builds a `ServiceCollection`, configures logging, calls `AddTftp()`, and then lets Spectre.Console.Cli resolve its `server` and `client` commands from that same container via a `TypeRegistrar` adapter. The server command composes an `ITftpConfigurationProvider` from the validated command line options before activating `TftpServer` from the container.

### Native async design

There is no blocking I/O anywhere in the protocol path:

- All socket I/O goes through the `IUdpClient` abstraction, which wraps `UdpClient.ReceiveAsync` / `UdpClient.SendAsync` (`ValueTask`-based). There are no synchronous `Send`/`Receive` calls, no `BeginX`/`EndX`, and no sync-over-async.
- `TftpClient.RunAsync` is a single `Task<bool>` call that takes an `IProgress<TftpTransferProgress>` and a `CancellationToken`.
- `TftpServer.RunAsync` is an `async` listen loop. The channel exposes `ServerListenAsync` as an `IAsyncEnumerable<IServerHandshake>`: the server iterates handshakes with `await foreach` and never blocks a thread.
- `CancellationToken` is threaded through every layer: the listen loop, each in-flight transfer, option negotiation, retransmission waits, and UDP sends/receives. Cancelling the token (for example, from a Ctrl+C handler) shuts the server down gracefully and aborts client transfers.
- Concurrency on the server is built with `System.Threading.Channels`. Accepted handshakes are written to a bounded channel, and a pool of worker tasks (bounded by `MaxConcurrentTransfers` in the configuration) consumes it with `ReadAllAsync`. One slow or stalled transfer cannot block the other clients; when the queue is full the server answers with `ServerBusy`.
- Retransmission (timeout handling) is implemented with a Polly `AsyncRetryPolicy` rather than manual sleep loops.
- Progress reporting is event-driven and allocation-light: the client reports through `IProgress<TftpTransferProgress>`, the server exposes a `TransferProgress` event. Both deliver a `TftpTransferProgress` record carrying the transfer `Id`, `State` (`Handshake`, `OptionNegotiation`, `DataTransfer`, `Completed`, `Failed`), endpoint, filename, bytes transferred, and total bytes.

### Protocol support

- RFC 1350: core TFTP (octet mode; `ascii` and `mail` are rejected with `IllegalOperation`)
- RFC 2347: option negotiation
- RFC 2348: `blksize` option
- RFC 2349: `tsize` and `timeout` options
- RFC 7440: `winsize` option (windowed transfers, multiple blocks in flight)
- IPv4 and IPv6 endpoints

The client proposes the negotiated options (`timeout`, `blksize`, `tsize`, `winsize`); the server clamps them against the limits supplied by the `ITftpConfigurationProvider` (`MaxBlockSize`, `MaxTimeoutSeconds`, `MaxWindowSize`) before the transfer begins.

Server-side hardening:

- Comprehensive checks to prevent unintended path traversal. Requested files can only be served from the configured directory and subfolders.
- Write requests are refused unless enabled by configuration, and existing files are never overwritten (create-new semantics).
- Duplicate RRQ/WRQ retransmissions from an endpoint that already has a handshake queued or a transfer in flight are dropped silently instead of spawning a second transfer.
- Unparseable packets and non-request packets on the listen port are ignored, since responding to noise on UDP could generate unnecessary traffic.

## Command line usage

The `Tftp.Net.Cli` executable ships two commands: `server` and `client`. Run with `--help` for full details.

### Start a server

```text
Tftp.Net.Cli server
Tftp.Net.Cli server -d C:\TftpRoot -a 192.168.1.1 -p 69
Tftp.Net.Cli server --allow-write --max-block-size 1024 --max-timeout 10 --max-window-size 8
```

- Serves files from the current directory by default; `--directory` selects another root.
- Listens on all interfaces by default; `--address` binds a specific IP address.
- Read-only by default; `--allow-write` enables uploads.
- `--max-block-size`, `--max-timeout`, and `--max-window-size` set the upper bounds for option negotiation.
- Press Ctrl+C to stop the server.

### Transfer a file

```text
Tftp.Net.Cli client 192.168.1.1 test.txt .\downloads\test.txt
Tftp.Net.Cli client 192.168.1.1:6900 test.txt .\uploads\test.txt --write --timeout 10 --block-size 1024 --window-size 8
```

- Downloads (read request) by default; `--write` uploads the local file.
- The remote endpoint accepts an IP address or FQDN, optionally followed by `:port` (default port 69). Bracketed IPv6 literals such as `[::1]:6900` are supported.
- `--timeout` (1-255 s), `--block-size` (8-65464 bytes), and `--window-size` (1-65535 blocks) are proposed during option negotiation.
- Both commands render a live progress display (progress bar, speed, remaining time) and return a non-zero exit code on failure.

## Using the library

Add the `Tftp.Net` package (or a project reference) to your application, then compose the services.

### Run a TFTP server

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tftp.Net;
using Tftp.Net.Configuration;
using Tftp.Net.Server;
using Tftp.Net.Transfer;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddSimpleConsole());
services.AddSingleton<ITftpConfigurationProvider>(
    new TftpConfigurationProvider(rootDirectory: "./tftp-root", allowWriteRequests: true));
services.AddTftp();

await using var provider = services.BuildServiceProvider();
var server = provider.GetRequiredService<TftpServer>();

server.TransferProgress += (_, progress) =>
    Console.WriteLine($"{progress.Endpoint} {progress.Filename}: {progress.BytesTransferred}/{progress.TotalBytes} ({progress.State})");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await server.RunAsync(new IPEndPoint(IPAddress.Any, 69), cts.Token);
```

Omit the endpoint argument to use the default (all interfaces, port 69).

### Transfer a file (client)

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tftp.Net;
using Tftp.Net.Client;
using Tftp.Net.Transfer;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddSimpleConsole());
services.AddTftp();

await using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<TftpClient>();

var progress = new Progress<TftpTransferProgress>(p =>
    Console.WriteLine($"{p.Filename}: {p.BytesTransferred:N0}/{p.TotalBytes:N0} bytes"));

var serverEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 69);

var success = await client.RunAsync(
    progress: progress,
    remoteEndpoint: serverEndpoint,
    isWriteRequest: false,            // true to upload
    remoteFilename: "image.bmp",
    filename: "./downloads/image.bmp",
    timeout: 30,
    blockSize: 65464,
    windowSize: 8);
```

### Integrate into an existing application

Because the library only requires an `IServiceCollection`, an `ILoggerFactory`, and an `ITftpConfigurationProvider`, it can be added to an application that already has a host. For example, an ASP.NET Core app can run the TFTP server alongside its web endpoints for the lifetime of the process:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTftp();
builder.Services.AddSingleton<ITftpConfigurationProvider>(
    new TftpConfigurationProvider(rootDirectory: "tftp-root", allowWriteRequests: true));

var app = builder.Build();

_ = app.Services.GetRequiredService<TftpServer>()
    .RunAsync(app.Lifetime.ApplicationStopping);

app.Run();
```

Stopping the host (or cancelling the token) shuts the server down cleanly, including any in-flight transfers.

## Build and test

```text
dotnet build
dotnet test
```

To produce a NuGet package of the library:

```text
dotnet pack src/Tftp.Net
```

## License

This project is licensed under the [Microsoft Public License (MS-PL)](license.md).

## Origin

This library originated as a fork of [tftp.net](https://github.com/Callisto82/tftp.net).
