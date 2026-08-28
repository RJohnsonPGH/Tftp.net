using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tftp.Net.Commands;
using Tftp.Net.Commands.Properties;
using Tftp.Net.Commands.Serializer;
using Tftp.Net.Tests.Serializers;
using Xunit.Sdk;

[assembly: RegisterXunitSerializer(
    typeof(ITftpCommandSerializer),
    typeof(ICommand)
)]

namespace Tftp.Net.Tests.Commands;

public class CommandSerializerTests(ITestOutputHelper outputHelper)
{
	[Theory]
	[ClassData(typeof(CommandSerializerTestData))]
	public void SerializeCommand_ReadRequest_ShouldReturnExpectedBytes(ICommand command, byte[] expectedBytes)
	{
		// Arrange
		using var serviceProvider = new ServiceCollection()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var logger = serviceProvider.GetRequiredService<ILogger<CommandSerializerTests>>();

		// Act
		var actualBytes = CommandSerializer.Serialize(command);

		// Assert
		Assert.Equal(expectedBytes, actualBytes);
	}
}

public class CommandSerializerTestData : TheoryData<ICommand, byte[]>
{
	public CommandSerializerTestData()
	{
		Add(new ReadRequest("file.txt", TransferMode.Octet, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "timeout", "5" },
				{ "blksize", "8" },
				{ "tsize", "0" }
			}),
			[0, 1, // Opcode - ReadRequest (1)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			111, 99, 116, 101, 116, 0, // Mode - "octet" ASCII encoded followed by null terminator
			116, 105, 109, 101, 111, 117, 116, 0, // Option 1 - "timeout" ASCII encoded followed by null terminator
			53, 0, // Option 1 value - "5" ASCII encoded followed by null terminator
			98, 108, 107, 115, 105, 122, 101, 0, // Option 2 - "blksize" ASCII encoded followed by null terminator
			56, 0, // Option 2 value - "8" ASCII encoded followed by null terminator
			116, 115, 105, 122, 101, 0, // Option 3 - "tsize" ASCII encoded followed by null terminator
			48, 0 // Option 3 value - "0" ASCII encoded followed by null terminator
		]);

		Add(new ReadRequest("file.txt", TransferMode.NetAscii, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "timeout", "5" },
				{ "blksize", "8" },
				{ "tsize", "0" }
			}),
			[0, 1, // Opcode - ReadRequest (1)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			110, 101, 116, 97, 115, 99, 105, 105, 0, // Mode - "netascii" ASCII encoded followed by null terminator
			116, 105, 109, 101, 111, 117, 116, 0, // Option 1 - "timeout" ASCII encoded followed by null terminator
			53, 0, // Option 1 value - "5" ASCII encoded followed by null terminator
			98, 108, 107, 115, 105, 122, 101, 0, // Option 2 - "blksize" ASCII encoded followed by null terminator
			56, 0, // Option 2 value - "8" ASCII encoded followed by null terminator
			116, 115, 105, 122, 101, 0, // Option 3 - "tsize" ASCII encoded followed by null terminator
			48, 0 // Option 3 value - "0" ASCII encoded followed by null terminator
		]);

		Add(new ReadRequest("file.txt", TransferMode.Mail, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "timeout", "5" },
				{ "blksize", "8" },
				{ "tsize", "0" }
			}),
			[0, 1, // Opcode - ReadRequest (1)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			109, 97, 105, 108, 0, // Mode - "mail" ASCII encoded followed by null terminator
			116, 105, 109, 101, 111, 117, 116, 0, // Option 1 - "timeout" ASCII encoded followed by null terminator
			53, 0, // Option 1 value - "5" ASCII encoded followed by null terminator
			98, 108, 107, 115, 105, 122, 101, 0, // Option 2 - "blksize" ASCII encoded followed by null terminator
			56, 0, // Option 2 value - "8" ASCII encoded followed by null terminator
			116, 115, 105, 122, 101, 0, // Option 3 - "tsize" ASCII encoded followed by null terminator
			48, 0 // Option 3 value - "0" ASCII encoded followed by null terminator
		]);

		Add(new WriteRequest("file.txt", TransferMode.Octet, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "timeout", "5" },
				{ "blksize", "8" },
				{ "tsize", "0" }
			}),
			[0, 2, // Opcode - WriteRequest (2)
			102, 105, 108, 101, 46, 116, 120, 116, 0, // Filename - "file.txt" ASCII encoded followed by null terminator
			111, 99, 116, 101, 116, 0, // Mode - "octet" ASCII encoded followed by null terminator
			116, 105, 109, 101, 111, 117, 116, 0, // Option 1 - "timeout" ASCII encoded followed by null terminator
			53, 0, // Option 1 value - "5" ASCII encoded followed by null terminator
			98, 108, 107, 115, 105, 122, 101, 0, // Option 2 - "blksize" ASCII encoded followed by null terminator
			56, 0, // Option 2 value - "8" ASCII encoded followed by null terminator
			116, 115, 105, 122, 101, 0, // Option 3 - "tsize" ASCII encoded followed by null terminator
			48, 0 // Option 3 value - "0" ASCII encoded followed by null terminator
		]);

		Memory<byte> dataBytes = new byte[] { 0x01, 0x02, 0x03 };
		Add(new Data(1, dataBytes),
			[0, 3, // Opcode - Data (3)
			0, 1, // Block number (1)
			1, 2, 3 // Data bytes
		]);
		Add(new Acknowledgement(1),
			[0, 4, // Opcode - Acknowledgement (4)
			0, 1 // Block number (1)
		]);
		Add(Error.AccessViolation,
			[0, 5, // Opcode - Error (5)
			0, 2, // Error code - Access Violation (2)
			65, 99, 99, 101, 115, 115, 32, 118, 105, 111, 108, 97, 116, 105, 111, 110, 0 // Error message - "Access Violation" ASCII encoded followed by null terminator
		]);
		Add(new OptionAcknowledgement( new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "blksize", "512" },
			}),
			[0, 6, // Opcode - Option Acknowledgement (6)
			98, 108, 107, 115, 105, 122, 101, 0, // Option name - "blksize" ASCII encoded followed by null terminator
			53, 49, 50, 0 // Option value - "512" ASCII encoded followed by null terminator
		]);
	}
}
