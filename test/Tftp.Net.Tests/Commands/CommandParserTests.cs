using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tftp.Net.Commands;
using Tftp.Net.Commands.Parser;

namespace Tftp.Net.Tests.Commands;

public class CommandParserTests(ITestOutputHelper outputHelper)
{
	[Theory]
	[ClassData(typeof(CommandSerializerTestData))]
	public void TryParse_ShouldReturnExpectedCommand(ICommand expectedCommand, byte[] bytes)
	{
		// Arrange
		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var logger = serviceProvider.GetRequiredService<ILogger<CommandParserTests>>();

		// Act
		var parseResult = CommandParser.TryParse(bytes, out var actualCommand, logger);

		// Assert
		Assert.True(parseResult);
		Assert.NotNull(actualCommand);
		Assert.Equal(expectedCommand.OpCode, actualCommand.OpCode);

		if (expectedCommand is ReadRequest expectedReadRequestCommand)
		{
			var actualReadRequestCommand = Assert.IsType<ReadRequest>(actualCommand);
			Assert.Equal(expectedReadRequestCommand.Filename, actualReadRequestCommand.Filename);
			Assert.Equal(expectedReadRequestCommand.Mode, actualReadRequestCommand.Mode);
			Assert.Equal(expectedReadRequestCommand.Options, actualReadRequestCommand.Options);
		}
		else if (expectedCommand is WriteRequest expectedWriteRequestCommand)
		{
			var actualWriteRequestCommand = Assert.IsType<WriteRequest>(actualCommand);
			Assert.Equal(expectedWriteRequestCommand.Filename, actualWriteRequestCommand.Filename);
			Assert.Equal(expectedWriteRequestCommand.Mode, actualWriteRequestCommand.Mode);
			Assert.Equal(expectedWriteRequestCommand.Options, actualWriteRequestCommand.Options);
		}
		else if (expectedCommand is Data expectedDataCommand)
		{
			var actualDataCommand = Assert.IsType<Data>(actualCommand);
			Assert.Equal(expectedDataCommand.BlockNumber, actualDataCommand.BlockNumber);
			Assert.Equal(expectedDataCommand.DataBytes, actualDataCommand.DataBytes);
		}
		else if (expectedCommand is Acknowledgement expectedAcknowledgementCommand)
		{
			var actualAcknowledgementCommand = Assert.IsType<Acknowledgement>(actualCommand);
			Assert.Equal(expectedAcknowledgementCommand.BlockNumber, actualAcknowledgementCommand.BlockNumber);
		}
		else if (expectedCommand is Error expectedErrorCommand)
		{
			var actualErrorCommand = Assert.IsType<Error>(actualCommand);
			Assert.Equal(expectedErrorCommand.ErrorCode, actualErrorCommand.ErrorCode);
			Assert.Equal(expectedErrorCommand.Message, actualErrorCommand.Message);
		}
	}

	[Theory]
	[ClassData(typeof(InvalidCommandTestData))]
	public void TryParse_ShouldReturnFalseForInvalidCommand(byte[] bytes)
	{
		// Arrange
		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var logger = serviceProvider.GetRequiredService<ILogger<CommandParserTests>>();

		// Act
		var parseResult = CommandParser.TryParse(bytes, out var actualCommand, logger);

		// Assert
		Assert.False(parseResult);
		Assert.Null(actualCommand);
	}

	[Theory]
	[ClassData(typeof(SemanticRequestTestData))]
	public void TryParse_ShouldParseSemanticallyInvalidRequests_Syntactically(byte[] bytes, string expectedMode)
	{
		// Arrange
		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var logger = serviceProvider.GetRequiredService<ILogger<CommandParserTests>>();

		// Act
		var parseResult = CommandParser.TryParse(bytes, out var actualCommand, logger);

		// Assert - parsing is purely syntactic, so an undefined mode or unusable option values do
		// not prevent parsing. Semantic validation is the caller's responsibility.
		Assert.True(parseResult);
		var readRequest = Assert.IsType<ReadRequest>(actualCommand);
		Assert.Equal(expectedMode, readRequest.Mode);
	}
}

public class InvalidCommandTestData : TheoryData<byte[]>
{
	public InvalidCommandTestData()
	{
		// Empty filename
		Add(
			[0, 1, // Opcode - ReadRequest (1)
			0, // Filename - "" ASCII encoded followed by null terminator
			109, 97, 105, 108, 0, // Mode - "mail" ASCII encoded followed by null terminator
			116, 105, 109, 101, 111, 117, 116, 0, // Option 1 - "timeout" ASCII encoded followed by null terminator
			53, 0, // Option 1 value - "5" ASCII encoded followed by null terminator
			98, 108, 107, 115, 105, 122, 101, 0, // Option 2 - "blksize" ASCII encoded followed by null terminator
			56, 0, // Option 2 value - "8" ASCII encoded followed by null terminator
			116, 115, 105, 122, 101, 0, // Option 3 - "tsize" ASCII encoded followed by null terminator
			48, 0 // Option 3 value - "0" ASCII encoded followed by null terminator
		]);

		// Invalid opcode
		Add(
			[0, 8, // Opcode - Invalid (8)
			0, 1, // Block number (1)
			1, 2, 3 // Data bytes
		]);

		// Valid ReadRequest but with junk after the valid command
		Add(
			[0, 1, // Opcode - ReadRequest (1)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			111, 99, 116, 101, 116, 0, // Mode - "octet" ASCII encoded followed by null terminator
			116, 105, 109, 101, 111, 117, 116, 0, // Option 1 - "timeout" ASCII encoded followed by null terminator
			53, 0, // Option 1 value - "5" ASCII encoded followed by null terminator
			98, 108, 107, 115, 105, 122, 101, 0, // Option 2 - "blksize" ASCII encoded followed by null terminator
			56, 0, // Option 2 value - "8" ASCII encoded followed by null terminator
			116, 115, 105, 122, 101, 0, // Option 3 - "tsize" ASCII encoded followed by null terminator
			48, 0, // Option 3 value - "0" ASCII encoded followed by null terminator
			255, 255, 255 // Junk bytes after valid command
		]);
	}
}

public class SemanticRequestTestData : TheoryData<byte[], string>
{
	public SemanticRequestTestData()
	{
		// Undefined transfer mode - parses syntactically; the caller decides it is invalid
		Add(
			[0, 1, // Opcode - ReadRequest (1)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			49, 99, 116, 101, 116, 0, // Mode - undefined mode "1ctet" ASCII encoded followed by null terminator
			116, 105, 109, 101, 111, 117, 116, 0, // Option 1 - "timeout" ASCII encoded followed by null terminator
			53, 0 // Option 1 value - "5" ASCII encoded followed by null terminator
		],
			"1ctet");

		// Out of range timeout ("555") - parses syntactically
		Add(
			[0, 1, // Opcode - ReadRequest (1)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			110, 101, 116, 97, 115, 99, 105, 105, 0, // Mode - "netascii" ASCII encoded followed by null terminator
			116, 105, 109, 101, 111, 117, 116, 0, // Option 1 - "timeout" ASCII encoded followed by null terminator
			53, 53, 53, 0 // Option 1 value - out of range timeout "555" ASCII encoded followed by null terminator
		],
			"netascii");

		// Non-numeric option values ("-1024", "65536") - parse syntactically
		Add(
			[0, 1, // Opcode - ReadRequest (1)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			110, 101, 116, 97, 115, 99, 105, 105, 0, // Mode - "netascii" ASCII encoded followed by null terminator
			116, 115, 105, 122, 101, 0, // Option 1 - "tsize" ASCII encoded followed by null terminator
			45, 49, 48, 50, 52, 0, // Option 1 value - non-numeric transfer size "-1024" ASCII encoded followed by null terminator
			119, 105, 110, 115, 105, 122, 101, 0, // Option 2 - "winsize" ASCII encoded followed by null terminator
			54, 53, 53, 51, 54, 0 // Option 2 value - unrepresentable window size "65536" ASCII encoded followed by null terminator
		],
			"netascii");

		// Window size below the protocol minimum ("0") - parses syntactically
		Add(
			[0, 1, // Opcode - ReadRequest (1)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			111, 99, 116, 101, 116, 0, // Mode - "octet" ASCII encoded followed by null terminator
			119, 105, 110, 115, 105, 122, 101, 0, // Option 1 - "winsize" ASCII encoded followed by null terminator
			48, 0 // Option 1 value - out of range window size "0" ASCII encoded followed by null terminator
		],
			"octet");
	}
}
