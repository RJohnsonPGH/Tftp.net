using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using Tftp.Net.Channel;
using Tftp.Net.Channel.Client;
using Tftp.Net.Commands;
using Tftp.Net.Commands.Properties;
using Tftp.Net.Configuration;
using Tftp.Net.Server;
using Tftp.Net.Tests.Serializers;
using Tftp.Net.Transfer;
using Xunit.Sdk;

[assembly: RegisterXunitSerializer(
    typeof(TftpServerHandshakeSerialzier),
    typeof(ServerHandshake)
)]

namespace Tftp.Net.Tests.Server;

public class TftpServerTests : IDisposable
{
	public TftpServerTests(ITestOutputHelper outputHelper)
	{
		_outputHelper = outputHelper;

		_cancellationTokenSource = new CancellationTokenSource();
		var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, TestContext.Current.CancellationToken);
		_cancellationToken = linkedCancellationTokenSource.Token;

		_tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(_tempDirectory);
		var testFilePath = Path.Combine(_tempDirectory, "existing.txt");
		File.WriteAllText(testFilePath, "test content");
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);

		if (Directory.Exists(_tempDirectory))
		{
			Directory.Delete(_tempDirectory, true);
		}
	}

	private readonly ITestOutputHelper _outputHelper;

	private readonly CancellationTokenSource _cancellationTokenSource;
	private readonly CancellationToken _cancellationToken;

	private readonly string _tempDirectory;

	[Theory]
	[ClassData(typeof(TftpServerTestData))]
	internal async Task RunAsync_ShouldHandleHandshakes_AccordingToConfiguration(
		ServerHandshake handshake,
		bool allowWriteRequests,
		Error? expectedError)
	{
		// Arrange
		var channel = Substitute.For<ITftpChannel>();

		channel.ServerListenAsync(Arg.Any<CancellationToken>())
			.Returns(new IServerHandshake[] { handshake }.ToAsyncEnumerable());

		var clientFactory = Substitute.For<IUdpClientFactory>();
		clientFactory.Create(Arg.Any<IPEndPoint>())
			.Returns(Substitute.For<IUdpClient>());

		var channelFactory = Substitute.For<ITftpChannelFactory>();
		channelFactory.Create(Arg.Any<IUdpClient>())
			.Returns(channel);

		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(_outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.AddSingleton(clientFactory)
			.AddSingleton(channelFactory)
			.AddSingleton<ITftpConfigurationProvider>(new TftpConfigurationProvider(_tempDirectory, allowWriteRequests: allowWriteRequests, maxBlockSize: 512, maxTimeoutSeconds: 5))
			.AddSingleton<TftpServer>()
			.BuildServiceProvider();

		var server = serviceProvider.GetRequiredService<TftpServer>();

		// Act
		var serverTask = server.RunAsync(_cancellationToken);
			
		// Give the server a moment to process
		await Task.Delay(500, _cancellationToken);
		_cancellationTokenSource.Cancel();

		try
		{
			await serverTask;
		}
		catch (OperationCanceledException)
		{
			// Expected when cancelling
		}

		// Assert
		if (allowWriteRequests)
		{
			// Should not send error
			await channel.DidNotReceive().SendPreTransferErrorAsync(Arg.Any<Error>(), Arg.Any<IPEndPoint>(), Arg.Any<CancellationToken>());
		}
		else
		{
			// Should send the expected error
			await channel.Received(1).SendPreTransferErrorAsync(expectedError!, handshake.RemoteEndpoint, Arg.Any<CancellationToken>());
		}
	}

	[Fact]
	public async Task RunAsync_ShouldRejectWriteRequest_WhenFileAlreadyExists()
	{
		// Arrange
		var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		try
		{
			// Create existing file
			var existingFilePath = Path.Combine(tempDir, "existing.txt");
			await File.WriteAllTextAsync(existingFilePath, "existing content", TestContext.Current.CancellationToken);

			var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
			var handshake = new ServerWriteRequestHandshake(remoteEndpoint, "existing.txt", TransferMode.Octet, OptionSet.Empty);
			IServerHandshake[] handshakes = [handshake];

			var channel = Substitute.For<ITftpChannel>();

			channel.ServerListenAsync(Arg.Any<CancellationToken>())
				.Returns(handshakes.ToAsyncEnumerable());

			var clientFactory = Substitute.For<IUdpClientFactory>();
			clientFactory.Create(Arg.Any<IPEndPoint>())
				.Returns(Substitute.For<IUdpClient>());

			var channelFactory = Substitute.For<ITftpChannelFactory>();
			channelFactory.Create(Arg.Any<IUdpClient>())
				.Returns(channel);

			using var serviceProvider = new ServiceCollection()
				.AddLogging((builder) =>
				{
					builder.AddXUnit(_outputHelper);
					builder.SetMinimumLevel(LogLevel.Trace);
				})
				.AddSingleton(clientFactory)
				.AddSingleton(channelFactory)
				.AddSingleton<ITftpConfigurationProvider>(new TftpConfigurationProvider(tempDir, allowWriteRequests: true, maxBlockSize: 512, maxTimeoutSeconds: 5))
				.AddSingleton<TftpServer>()
				.BuildServiceProvider();

			var server = serviceProvider.GetRequiredService<TftpServer>();

			// Act
			var serverTask = server.RunAsync(_cancellationToken);
			await Task.Delay(100, _cancellationToken);
			_cancellationTokenSource.Cancel();

			try
			{
				await serverTask;
			}
			catch (OperationCanceledException)
			{
			}

			// Assert
			await channel.Received(1).SendPreTransferErrorAsync(Error.FileAlreadyExists, remoteEndpoint, Arg.Any<CancellationToken>());
		}
		finally
		{
			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir, true);
			}
		}
	}

	[Fact]
	public async Task RunAsync_ShouldRejectReadRequest_WhenFileDoesNotExist()
	{
		// Arrange
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
		var handshake = new ServerReadRequestHandshake(remoteEndpoint, "nonexistent.txt", TransferMode.Octet, OptionSet.Empty);
		IServerHandshake[] handshakes = [handshake];

		var channel = Substitute.For<ITftpChannel>();

		channel.ServerListenAsync(Arg.Any<CancellationToken>())
			.Returns(handshakes.ToAsyncEnumerable());

		var clientFactory = Substitute.For<IUdpClientFactory>();
		clientFactory.Create(Arg.Any<IPEndPoint>())
			.Returns(Substitute.For<IUdpClient>());

		var channelFactory = Substitute.For<ITftpChannelFactory>();
		channelFactory.Create(Arg.Any<IUdpClient>())
			.Returns(channel);

		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(_outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.AddSingleton(clientFactory)
			.AddSingleton(channelFactory)
			.AddSingleton<ITftpConfigurationProvider>(new TftpConfigurationProvider(_tempDirectory, allowWriteRequests: false, maxBlockSize: 512, maxTimeoutSeconds: 5))
			.AddSingleton<TftpServer>()
			.BuildServiceProvider();

		var server = serviceProvider.GetRequiredService<TftpServer>();

		// Act
		var serverTask = server.RunAsync(_cancellationToken);
		await Task.Delay(100, _cancellationToken);
		_cancellationTokenSource.Cancel();

		try
		{
			await serverTask;
		}
		catch (OperationCanceledException)
		{
		}

		// Assert
		await channel.Received(1).SendPreTransferErrorAsync(Error.FileNotFound, remoteEndpoint, Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData(true, "../existing.txt")]
	[InlineData(true, "..\\existing.txt")]
	[InlineData(true, "subdir/../../existing.txt")]
	[InlineData(true, "..")]
	[InlineData(true, "C:\\Windows\\win.ini")]
	[InlineData(true, "/etc/passwd")]
	[InlineData(false, "../existing.txt")]
	[InlineData(false, "/etc/passwd")]
	public async Task RunAsync_ShouldRejectRequest_WhenFilenameEscapesRootDirectory(bool isWriteRequest, string filename)
	{
		// Arrange
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
		IServerHandshake handshake = isWriteRequest
			? new ServerWriteRequestHandshake(remoteEndpoint, filename, TransferMode.Octet, OptionSet.Empty)
			: new ServerReadRequestHandshake(remoteEndpoint, filename, TransferMode.Octet, OptionSet.Empty);
		IServerHandshake[] handshakes = [handshake];

		var channel = Substitute.For<ITftpChannel>();

		channel.ServerListenAsync(Arg.Any<CancellationToken>())
			.Returns(handshakes.ToAsyncEnumerable());

		var clientFactory = Substitute.For<IUdpClientFactory>();
		clientFactory.Create(Arg.Any<IPEndPoint>())
			.Returns(Substitute.For<IUdpClient>());

		var channelFactory = Substitute.For<ITftpChannelFactory>();
		channelFactory.Create(Arg.Any<IUdpClient>())
			.Returns(channel);

		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(_outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.AddSingleton(clientFactory)
			.AddSingleton(channelFactory)
			.AddSingleton<ITftpConfigurationProvider>(new TftpConfigurationProvider(_tempDirectory, allowWriteRequests: true, maxBlockSize: 512, maxTimeoutSeconds: 5))
			.AddSingleton<TftpServer>()
			.BuildServiceProvider();

		var server = serviceProvider.GetRequiredService<TftpServer>();

		// Act
		var serverTask = server.RunAsync(_cancellationToken);
		await Task.Delay(100, _cancellationToken);
		_cancellationTokenSource.Cancel();

		try
		{
			await serverTask;
		}
		catch (OperationCanceledException)
		{
		}

		// Assert - the request must be rejected with AccessViolation and no transfer may be queued
		await channel.Received(1).SendPreTransferErrorAsync(Error.AccessViolation, remoteEndpoint, Arg.Any<CancellationToken>());
		await channel.DidNotReceiveWithAnyArgs().ProcessHandshakeAsync(default!, default!, default!, Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData(false, "subdir/existing.txt")]
	[InlineData(true, "subdir/newfile.txt")]
	public async Task RunAsync_ShouldAcceptRequest_WhenFilenameIsWithinSubfolder(bool isWriteRequest, string filename)
	{
		// Arrange - subfolders of the root directory are legitimate request targets, so a
		// request for one must pass path validation and reach the transfer stage.
		var subdirectory = Path.Combine(_tempDirectory, "subdir");
		Directory.CreateDirectory(subdirectory);
		await File.WriteAllTextAsync(Path.Combine(subdirectory, "existing.txt"), "subfolder content", TestContext.Current.CancellationToken);

		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
		IServerHandshake handshake = isWriteRequest
			? new ServerWriteRequestHandshake(remoteEndpoint, filename, TransferMode.Octet, OptionSet.Empty)
			: new ServerReadRequestHandshake(remoteEndpoint, filename, TransferMode.Octet, OptionSet.Empty);
		IServerHandshake[] handshakes = [handshake];

		var channel = Substitute.For<ITftpChannel>();

		channel.ServerListenAsync(Arg.Any<CancellationToken>())
			.Returns(handshakes.ToAsyncEnumerable());

		var clientFactory = Substitute.For<IUdpClientFactory>();
		clientFactory.Create(Arg.Any<IPEndPoint>())
			.Returns(Substitute.For<IUdpClient>());

		var channelFactory = Substitute.For<ITftpChannelFactory>();
		channelFactory.Create(Arg.Any<IUdpClient>())
			.Returns(channel);

		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(_outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.AddSingleton(clientFactory)
			.AddSingleton(channelFactory)
			.AddSingleton<ITftpConfigurationProvider>(new TftpConfigurationProvider(_tempDirectory, allowWriteRequests: true, maxBlockSize: 512, maxTimeoutSeconds: 5))
			.AddSingleton<TftpServer>()
			.BuildServiceProvider();

		var server = serviceProvider.GetRequiredService<TftpServer>();

		// Act
		var serverTask = server.RunAsync(_cancellationToken);
		await Task.Delay(100, _cancellationToken);
		_cancellationTokenSource.Cancel();

		try
		{
			await serverTask;
		}
		catch (OperationCanceledException)
		{
		}

		// Assert - no pre-transfer error may be sent and the transfer stage must have been reached
		await channel.DidNotReceive().SendPreTransferErrorAsync(Arg.Any<Error>(), Arg.Any<IPEndPoint>(), Arg.Any<CancellationToken>());
		await channel.Received(1).ProcessHandshakeAsync(Arg.Any<IProgress<TftpTransferProgress>>(), Arg.Any<RequestHandshake>(), Arg.Any<FileStream>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task RunAsync_ShouldDisposeSockets_WhenTransferHasBeenProcessed()
	{
		// Arrange
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
		var handshake = new ServerReadRequestHandshake(remoteEndpoint, "existing.txt", TransferMode.Octet, OptionSet.Empty);
		IServerHandshake[] handshakes = [handshake];

		var channel = Substitute.For<ITftpChannel>();

		channel.ServerListenAsync(Arg.Any<CancellationToken>())
			.Returns(handshakes.ToAsyncEnumerable());

		// The factory creates the listen socket first and one transfer socket per request.
		// Transfer sockets are bound to an ephemeral port (0), which distinguishes them.
		var listenClient = Substitute.For<IUdpClient>();
		var transferClient = Substitute.For<IUdpClient>();

		var clientFactory = Substitute.For<IUdpClientFactory>();
		clientFactory.Create(Arg.Is<IPEndPoint>(endpoint => endpoint.Port != 0)).Returns(listenClient);
		clientFactory.Create(Arg.Is<IPEndPoint>(endpoint => endpoint.Port == 0)).Returns(transferClient);

		var channelFactory = Substitute.For<ITftpChannelFactory>();
		channelFactory.Create(listenClient).Returns(channel);
		channelFactory.Create(transferClient).Returns(Substitute.For<ITftpChannel>());

		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(_outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.AddSingleton(clientFactory)
			.AddSingleton(channelFactory)
			.AddSingleton<ITftpConfigurationProvider>(new TftpConfigurationProvider(_tempDirectory, allowWriteRequests: false, maxBlockSize: 512, maxTimeoutSeconds: 5))
			.AddSingleton<TftpServer>()
			.BuildServiceProvider();

		var server = serviceProvider.GetRequiredService<TftpServer>();

		// Act
		var serverTask = server.RunAsync(_cancellationToken);
		await Task.Delay(100, _cancellationToken);
		_cancellationTokenSource.Cancel();

		try
		{
			await serverTask;
		}
		catch (OperationCanceledException)
		{
		}

		// Assert - both the transfer socket and the listen socket must be disposed
		transferClient.Received(1).Dispose();
		listenClient.Received(1).Dispose();
	}
}

internal class TftpServerTestData : TheoryData<ServerHandshake, bool, Error?>
{
	public TftpServerTestData()
	{
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

		// Valid read request with octet mode - should be accepted
		Add(
			new ServerReadRequestHandshake(remoteEndpoint, "existing.txt", TransferMode.Octet, OptionSet.Empty),
			true,
			null
		);

		// Valid write request with octet mode and writes enabled - should be accepted
		Add(
			new ServerWriteRequestHandshake(remoteEndpoint, "newfile.txt", TransferMode.Octet, OptionSet.Empty),
			true,
			null
		);

		// Write request when writes are disabled - should be rejected with AccessViolation
		Add(
			new ServerWriteRequestHandshake(remoteEndpoint, "newfile.txt", TransferMode.Octet, OptionSet.Empty),
			false,
			Error.AccessViolation
		);

		// Read request with NetAscii mode - should be rejected with IllegalOperation
		Add(
			new ServerReadRequestHandshake(remoteEndpoint, "existing.txt", TransferMode.NetAscii, OptionSet.Empty),
			false,
			Error.IllegalOperation
		);

		// Write request with NetAscii mode - should be rejected with IllegalOperation
		Add(
			new ServerWriteRequestHandshake(remoteEndpoint, "newfile.txt", TransferMode.NetAscii, OptionSet.Empty),
			false,
			Error.IllegalOperation
		);

		// Read request with Mail mode - should be rejected with IllegalOperation
		Add(
			new ServerReadRequestHandshake(remoteEndpoint, "existing.txt", TransferMode.Mail, OptionSet.Empty),
			false,
			Error.IllegalOperation
		);

		// Write request with Mail mode - should be rejected with IllegalOperation
		Add(
			new ServerWriteRequestHandshake(remoteEndpoint, "newfile.txt", TransferMode.Mail, OptionSet.Empty),
			false,
			Error.IllegalOperation
		);

		// Read request with options (blocksize, timeout, tsize)
		Add(
			new ServerReadRequestHandshake(remoteEndpoint, "existing.txt", TransferMode.Octet, new(timeout: 10, blockSize: 1024, transferSize: 0)),
			true,
			null
		);

		// Write request with options (blocksize, timeout, tsize)
		Add(
			new ServerWriteRequestHandshake(remoteEndpoint, "newfile.txt", TransferMode.Octet, new(timeout: 10, blockSize: 1024, transferSize: 0)),
			true,
			null
		);
	}
}
