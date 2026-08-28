using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using System.Net.Sockets;
using Tftp.Net.Channel;
using Tftp.Net.Channel.Client;
using Tftp.Net.Commands;
using Tftp.Net.Commands.Properties;
using Tftp.Net.Commands.Serializer;
using Tftp.Net.Commands.Validation;
using Tftp.Net.Transfer;

namespace Tftp.Net.Tests.Channel;

public class TftpChannelTests(ITestOutputHelper outputHelper)
{
	[Fact]
	public async Task BeginListenAsync_ShouldYieldHandshake_WhenValidRequestReceived()
	{
		// Arrange
		var mockUdpClient = Substitute.For<IUdpClient>();
		using var serviceProvider = new ServiceCollection()
			.AddSingleton(mockUdpClient)
			.AddSingleton<OptionSetValidator>()
			.AddSingleton<TftpChannel>()
			.AddLogging(builder =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var channel = serviceProvider.GetRequiredService<TftpChannel>();
		var localEndpoint = new IPEndPoint(IPAddress.Loopback, 69);
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
		
		// Create a valid RRQ command
		var readRequest = new ReadRequest("test.txt", TransferMode.Octet, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		var requestBytes = CommandSerializer.Serialize(readRequest);
		var udpReceiveResult = new UdpReceiveResult(requestBytes, remoteEndpoint);
		
		// Setup the mock to return the request once, then cancel
		var cancellationTokenSource = new CancellationTokenSource();
		mockUdpClient.ReceiveAsync(Arg.Any<CancellationToken>())
			.Returns(udpReceiveResult);
		
		// Act
		var handshakes = new List<IServerHandshake>();
		await foreach (var handshake in channel.ServerListenAsync(cancellationTokenSource.Token))
		{
			handshakes.Add(handshake);
			cancellationTokenSource.Cancel(); // Cancel after receiving first handshake
		}
		
		// Assert
		var receivedHandshake = Assert.Single(handshakes);
		Assert.Equal(remoteEndpoint, receivedHandshake.RemoteEndpoint);
		
		var readRequestHandshake = Assert.IsType<ServerReadRequestHandshake>(receivedHandshake);
		Assert.Equal("test.txt", readRequestHandshake.Filename);
		Assert.Equal(TransferMode.Octet, readRequestHandshake.Mode);
	}
	
	[Fact]
	public async Task BeginListenAsync_ShouldIgnoreNonRequestCommands()
	{
		// Arrange
		var mockUdpClient = Substitute.For<IUdpClient>();
		using var serviceProvider = new ServiceCollection()
			.AddSingleton(mockUdpClient)
			.AddSingleton<OptionSetValidator>()
			.AddSingleton<TftpChannel>()
			.AddLogging(builder =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var channel = serviceProvider.GetRequiredService<TftpChannel>();
		var localEndpoint = new IPEndPoint(IPAddress.Loopback, 69);
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
		
		// Create non-request commands (ACK, DATA, etc.) that should be ignored
		var ackCommand = new Acknowledgement(1);
		var ackBytes = CommandSerializer.Serialize(ackCommand);
		
		var readRequest = new ReadRequest("test.txt", TransferMode.Octet, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		var requestBytes = CommandSerializer.Serialize(readRequest);
		
		var cancellationTokenSource = new CancellationTokenSource();
		mockUdpClient.ReceiveAsync(Arg.Any<CancellationToken>())
			.Returns(
				new UdpReceiveResult(ackBytes, remoteEndpoint), // Should be ignored
				new UdpReceiveResult(requestBytes, remoteEndpoint) // Should be yielded
			);
		
		// Act
		var handshakes = new List<IServerHandshake>();
		await foreach (var handshake in channel.ServerListenAsync(cancellationTokenSource.Token))
		{
			handshakes.Add(handshake);
			cancellationTokenSource.Cancel();
		}
		
		// Assert - Only the read request should be yielded, ACK should be ignored
		var receivedHandshake = Assert.Single(handshakes);
		Assert.IsType<ServerReadRequestHandshake>(receivedHandshake);
	}
	
	[Fact]
	public async Task BeginListenAsync_ShouldHandleInvalidPackets()
	{
		// Arrange
		var mockUdpClient = Substitute.For<IUdpClient>();
		using var serviceProvider = new ServiceCollection()
			.AddSingleton(mockUdpClient)
			.AddSingleton<OptionSetValidator>()
			.AddSingleton<TftpChannel>()
			.AddLogging(builder =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var channel = serviceProvider.GetRequiredService<TftpChannel>();
		var localEndpoint = new IPEndPoint(IPAddress.Loopback, 69);
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
		
		// Create invalid bytes that can't be parsed
		var invalidBytes = new byte[] { 0xFF, 0xFF, 0xFF };
		
		var readRequest = new ReadRequest("test.txt", TransferMode.Octet, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		var requestBytes = CommandSerializer.Serialize(readRequest);
		
		var cancellationTokenSource = new CancellationTokenSource();
		mockUdpClient.ReceiveAsync(Arg.Any<CancellationToken>())
			.Returns(
				new UdpReceiveResult(invalidBytes, remoteEndpoint), // Should be ignored
				new UdpReceiveResult(requestBytes, remoteEndpoint) // Should be yielded
			);
		
		// Act
		var handshakes = new List<IServerHandshake>();
		await foreach (var handshake in channel.ServerListenAsync(cancellationTokenSource.Token))
		{
			handshakes.Add(handshake);
			cancellationTokenSource.Cancel();
		}
		
		// Assert - Only valid request should be yielded
		var receivedHandshake = Assert.Single(handshakes);
		Assert.IsType<ServerReadRequestHandshake>(receivedHandshake);
	}

	[Fact]
	public async Task BeginListenAsync_ShouldYieldErrorHandshake_WhenModeIsUndefined()
	{
		// Arrange
		var mockUdpClient = Substitute.For<IUdpClient>();
		using var serviceProvider = new ServiceCollection()
			.AddSingleton(mockUdpClient)
			.AddSingleton<OptionSetValidator>()
			.AddSingleton<TftpChannel>()
			.AddLogging(builder =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var channel = serviceProvider.GetRequiredService<TftpChannel>();
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

		// A request whose mode is not defined by the spec parses syntactically but is semantically invalid
		var readRequest = new ReadRequest("test.txt", "1ctet", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		var cancellationTokenSource = new CancellationTokenSource();
		mockUdpClient.ReceiveAsync(Arg.Any<CancellationToken>())
			.Returns(new UdpReceiveResult(CommandSerializer.Serialize(readRequest), remoteEndpoint));

		// Act
		var handshakes = new List<IServerHandshake>();
		await foreach (var handshake in channel.ServerListenAsync(cancellationTokenSource.Token))
		{
			handshakes.Add(handshake);
			cancellationTokenSource.Cancel();
		}

		// Assert - the caller must receive an error handshake so it can answer with an error packet
		var errorHandshake = Assert.IsType<ErrorHandshake>(Assert.Single(handshakes));
		Assert.Equal(Error.IllegalOperation, errorHandshake.Error);
	}

	[Fact]
	public async Task BeginListenAsync_ShouldYieldErrorHandshake_WhenOptionsContainDuplicates()
	{
		// Arrange
		var mockUdpClient = Substitute.For<IUdpClient>();
		using var serviceProvider = new ServiceCollection()
			.AddSingleton(mockUdpClient)
			.AddSingleton<OptionSetValidator>()
			.AddSingleton<TftpChannel>()
			.AddLogging(builder =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var channel = serviceProvider.GetRequiredService<TftpChannel>();
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

		// RFC 2347: an option may only be specified once - duplicates reject the whole request
		var readRequest = new ReadRequest("test.txt", TransferMode.Octet,
		[
			new KeyValuePair<string, string>("blksize", "512"),
			new KeyValuePair<string, string>("BLKSIZE", "1024")
		]);
		var cancellationTokenSource = new CancellationTokenSource();
		mockUdpClient.ReceiveAsync(Arg.Any<CancellationToken>())
			.Returns(new UdpReceiveResult(CommandSerializer.Serialize(readRequest), remoteEndpoint));

		// Act
		var handshakes = new List<IServerHandshake>();
		await foreach (var handshake in channel.ServerListenAsync(cancellationTokenSource.Token))
		{
			handshakes.Add(handshake);
			cancellationTokenSource.Cancel();
		}

		// Assert
		var errorHandshake = Assert.IsType<ErrorHandshake>(Assert.Single(handshakes));
		Assert.Equal(Error.IllegalOperation, errorHandshake.Error);
	}

	[Fact]
	public async Task BeginListenAsync_ShouldIgnoreBelowRangeOptionValues()
	{
		// Arrange
		var mockUdpClient = Substitute.For<IUdpClient>();
		using var serviceProvider = new ServiceCollection()
			.AddSingleton(mockUdpClient)
			.AddSingleton<OptionSetValidator>()
			.AddSingleton<TftpChannel>()
			.AddLogging(builder =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var channel = serviceProvider.GetRequiredService<TftpChannel>();
		var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

		// Unusable values are declined, out of range values are clamped - the request stays valid
		var readRequest = new ReadRequest("test.txt", TransferMode.Octet,
		[
			new KeyValuePair<string, string>("blksize", "99999"), // Above the protocol maximum
			new KeyValuePair<string, string>("timeout", "0"),     // Below the protocol minimum
			new KeyValuePair<string, string>("frobnicate", "7")   // Unknown option name
		]);
		var cancellationTokenSource = new CancellationTokenSource();
		mockUdpClient.ReceiveAsync(Arg.Any<CancellationToken>())
			.Returns(new UdpReceiveResult(CommandSerializer.Serialize(readRequest), remoteEndpoint));


		// Act
		var handshakes = new List<IServerHandshake>();
		await foreach (var handshake in channel.ServerListenAsync(cancellationTokenSource.Token))
		{
			handshakes.Add(handshake);
			cancellationTokenSource.Cancel();
		}

		// Assert
		var readRequestHandshake = Assert.IsType<ServerReadRequestHandshake>(Assert.Single(handshakes));
		Assert.Null(readRequestHandshake.Options.BlockSize);
		Assert.Null(readRequestHandshake.Options.Timeout);
		Assert.Null(readRequestHandshake.Options.WindowSize);
	}

	[Fact]
	public async Task InitiateRequestAsync_ShouldLockOntoResponder_WhenWriteRequestIsAnsweredByPlainAcknowledgement()
	{
		// Arrange
		var mockUdpClient = Substitute.For<IUdpClient>();
		using var serviceProvider = new ServiceCollection()
			.AddSingleton(mockUdpClient)
			.AddSingleton<OptionSetValidator>()
			.AddSingleton<TftpChannel>()
			.AddLogging(builder =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var channel = serviceProvider.GetRequiredService<TftpChannel>();
		var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 69);

		// RFC 1350: the server answers a request from a fresh ephemeral port rather than
		// the well-known port. The channel must lock onto whichever endpoint responded.
		var responderEndpoint = new IPEndPoint(IPAddress.Loopback, 54321);


		// The write request is answered by a plain acknowledgement (block zero), which means
		// the server declined option negotiation and expects the data transfer to start.
		mockUdpClient.ReceiveAsync(Arg.Any<CancellationToken>())
			.Returns(
				new UdpReceiveResult(CommandSerializer.Serialize(new Acknowledgement(0)), responderEndpoint),
				new UdpReceiveResult(CommandSerializer.Serialize(new Acknowledgement(1)), responderEndpoint));

		var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		await File.WriteAllTextAsync(filePath, "test content", TestContext.Current.CancellationToken);

		try
		{
			var handshake = new ClientWriteRequestHandshake(serverEndpoint, "test.txt", TransferMode.Octet, OptionSet.Empty);

			// Act
			bool success;
			using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				success = await channel.InitiateRequestAsync(new Progress<TftpTransferProgress>(), handshake, fileStream, TestContext.Current.CancellationToken);
			}

			// Assert - the transfer must succeed and the session must have locked onto the responder
			Assert.True(success);
			mockUdpClient.Received(1).Connect(responderEndpoint);
			await mockUdpClient.Received(1).SendAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
		}
		finally
		{
			File.Delete(filePath);
		}
	}
}
