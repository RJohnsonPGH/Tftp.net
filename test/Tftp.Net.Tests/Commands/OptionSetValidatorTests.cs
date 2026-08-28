using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tftp.Net.Commands.Properties;
using Tftp.Net.Commands.Validation;
using Tftp.Net.Tests.Serializers;
using Xunit.Sdk;

[assembly: RegisterXunitSerializer(
	typeof(OptionSetSerializer),
	typeof(OptionSet)
)]

[assembly: RegisterXunitSerializer(
	typeof(OptionCollectionSerializer),
	typeof(IEnumerable<KeyValuePair<string, string>>)
)]

namespace Tftp.Net.Tests.Commands;

public class OptionSetValidatorTests(ITestOutputHelper outputHelper)
{
	[Theory]
	[ClassData(typeof(TryParseTestData))]
	public void TryParse_ShouldReturnExpectedResult(
		IEnumerable<KeyValuePair<string, string>> unparsedOptions,
		bool expectedSuccess,
		ushort? expectedTimeout,
		ushort? expectedBlockSize,
		ulong? expectedTransferSize)
	{
		// Arrange
		using var serviceProvider = new ServiceCollection()
			.AddSingleton<OptionSetValidator>()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var optionSetValidator = serviceProvider.GetRequiredService<OptionSetValidator>();

		// Act
		bool actualSuccess = optionSetValidator.TryParseRequestOptionSet(unparsedOptions, out var optionSet);

		// Assert
		Assert.Equal(expectedSuccess, actualSuccess);

		if (expectedSuccess)
		{
			Assert.NotNull(optionSet);
			Assert.Equal(expectedTimeout, optionSet.Timeout);
			Assert.Equal(expectedBlockSize, optionSet.BlockSize);
			Assert.Equal(expectedTransferSize, optionSet.TransferSize);
		}
		else
		{
			Assert.Null(optionSet);
		}
	}

	[Theory]
	[ClassData(typeof(ClampTestData))]
	public void Clamp_ShouldReturnExpectedResult(
		OptionSet originalOptionSet,
		ushort maxTimeout,
		ushort maxBlockSize,
		ushort maxWindowSize,
		ulong? actualTransferSize,
		ushort? expectedTimeout,
		ushort? expectedBlockSize,
		ushort? expectedWindowSize,
		ulong? expectedTransferSize)
	{
		// Act
		var clampedOptionSet = originalOptionSet.Clamp(maxTimeout, maxBlockSize, maxWindowSize, actualTransferSize);

		// Assert
		Assert.Equal(expectedTimeout, clampedOptionSet.Timeout);
		Assert.Equal(expectedBlockSize, clampedOptionSet.BlockSize);
		Assert.Equal(expectedWindowSize, clampedOptionSet.WindowSize);
		Assert.Equal(expectedTransferSize, clampedOptionSet.TransferSize);
	}

	[Theory]
	[InlineData(0, 512, 1)] // Timeout too low
	[InlineData(256, 512, 1)] // Timeout too high
	[InlineData(5, 7, 1)] // Block size too low
	[InlineData(5, 65465, 1)] // Block size too high
	[InlineData(5, 512, 0)] // Window size too low - no test for window size too high, as ushort.MaxValue is the max valid value
	public void Clamp_ShouldThrowArgumentOutOfRangeException_WhenParametersAreInvalid(ushort maxTimeout, ushort maxBlockSize, ushort maxWindowSize)
	{
		// Arrange
		var optionSet = new OptionSet(10, 1024, 0, 10);

		// Act & Assert
		Assert.Throws<ArgumentOutOfRangeException>(() => optionSet.Clamp(maxTimeout, maxBlockSize, maxWindowSize, null));
	}

	[Fact]
	public void Clamp_ShouldReturnSameInstance_WhenOptionSetIsEmpty()
	{
		// Arrange
		var emptyOptionSet = OptionSet.Empty;

		// Act
		var result = emptyOptionSet.Clamp(10, 1024, 1, null);

		// Assert
		Assert.Same(emptyOptionSet, result);
	}
}

public class TryParseTestData : TheoryData<IEnumerable<KeyValuePair<string, string>>, bool, ushort?, ushort?, ulong?>
{
	public TryParseTestData()
	{
		// Empty options - should succeed
		Add(
			[],
			true,
			null,
			null,
			null
		);

		// Valid single options
		Add(
			[new KeyValuePair<string, string>("timeout", "5")],
			true,
			5,
			null,
			null
		);

		Add(
			[new KeyValuePair<string, string>("blksize", "512")],
			true,
			null,
			512,
			null
		);

		Add(
			[new KeyValuePair<string, string>("tsize", "1048576")],
			true,
			null,
			null,
			1048576
		);

		// Valid multiple options
		Add(
			[
				new KeyValuePair<string, string>("timeout", "10"),
				new KeyValuePair<string, string>("blksize", "1024"),
				new KeyValuePair<string, string>("tsize", "2097152")
			],
			true,
			10,
			1024,
			2097152
		);

		// Case insensitive keys
		Add(
			[
				new KeyValuePair<string, string>("TIMEOUT", "5"),
				new KeyValuePair<string, string>("BlkSize", "512"),
				new KeyValuePair<string, string>("TSize", "1024")
			],
			true,
			5,
			512,
			1024
		);

		// Edge case valid values
		Add(
			[
				new KeyValuePair<string, string>("timeout", "1"), // Min timeout
				new KeyValuePair<string, string>("blksize", "8"), // Min block size
				new KeyValuePair<string, string>("tsize", "0")
			],
			true,
			1,
			8,
			0
		);

		Add(
			[
				new KeyValuePair<string, string>("timeout", "255"), // Max timeout
				new KeyValuePair<string, string>("blksize", "65464"), // Max block size
				new KeyValuePair<string, string>("tsize", "18446744073709551615") // Max ulong
			],
			true,
			255,
			65464,
			18446744073709551615
		);

		// Out of range timeout values - clamp if over max, discard if under min
		Add(
			[new KeyValuePair<string, string>("timeout", "0")],
			true,
			null,
			null,
			null
		);

		Add(
			[new KeyValuePair<string, string>("blksize", "7")],
			true,
			null,
			null,
			null
		);

		Add(
			[new KeyValuePair<string, string>("blksize", "65465")],
			true,
			null,
			null,
			null
		);

		// Timeout cannot be negotiated, it is accept or drop
		// 256 will be dropped as its over max
		Add(
			[new KeyValuePair<string, string>("timeout", "256")],
			true,
			null,
			null,
			null
		);

		// Unusable timeout values - declined (option omitted), request still valid
		Add(
			[new KeyValuePair<string, string>("timeout", "abc")],
			true,
			null,
			null,
			null
		);

		Add(
			[new KeyValuePair<string, string>("timeout", "-5")],
			true,
			null,
			null,
			null
		);

		// Unusable block size value - declined (option omitted)
		Add(
			[new KeyValuePair<string, string>("blksize", "abc")],
			true,
			null,
			null,
			null
		);

		// Unusable transfer size values - declined (option omitted)
		Add(
			[new KeyValuePair<string, string>("tsize", "abc")],
			true,
			null,
			null,
			null
		);

		Add(
			[new KeyValuePair<string, string>("tsize", "-1024")],
			true,
			null,
			null,
			null
		);

		// Duplicate keys - should fail
		Add(
			[
				new KeyValuePair<string, string>("timeout", "5"),
				new KeyValuePair<string, string>("timeout", "10")
			],
			false,
			null,
			null,
			null
		);

		Add(
			[
				new KeyValuePair<string, string>("blksize", "512"),
				new KeyValuePair<string, string>("BLKSIZE", "1024") // Case insensitive duplicate
			],
			false,
			null,
			null,
			null
		);

		// Unknown options - should be ignored and parsing should succeed
		Add(
			[
				new KeyValuePair<string, string>("timeout", "5"),
				new KeyValuePair<string, string>("unknown", "value")
			],
			true,
			5,
			null,
			null
		);
	}
}

public class ClampTestData : TheoryData<OptionSet, ushort, ushort, ushort, ulong?, ushort?, ushort?, ushort?, ulong?>
{
	public ClampTestData()
	{
		// No clamping needed - values within limits
		Add(
			new OptionSet(5, 512, 1024, 8),
			10,
			1024,
			8,
			2048,
			5,
			512,
			8,
			2048
		);

		// Clamp timeout
		Add(
			new OptionSet(100, 512, 1024, 8),
			10,
			1024,
			8,
			2048,
			10, // Clamped from 100 to 10
			512,
			8,
			2048
		);

		// Clamp block size
		Add(
			new OptionSet(5, 8192, 1024, 8),
			10,
			1024,
			8,
			2048,
			5,
			1024, // Clamped from 8192 to 1024
			8,
			2048
		);

		// Clamp window size
		Add(
			new OptionSet(5, 8192, 1024, 8),
			10,
			1024,
			1,
			2048,
			5,
			1024,
			1,  // Clamped from 8 to 1
			2048
		);

		// Clamp both timeout and block size
		Add(
			new OptionSet(100, 8192, 1024, 8),
			10,
			1024,
			8,
			2048,
			10, // Clamped from 100 to 10
			1024, // Clamped from 8192 to 1024
			8,
			2048
		);

		// Replace transfer size
		Add(
			new OptionSet(5, 512, 0, 8),
			10,
			1024,
			8,
			1048576,
			5,
			512,
			8,
			1048576 // Replaced from 0 to actual size
		);

		// Null options remain null
		Add(
			new OptionSet(null, null, null, null),
			10,
			1024,
			8,
			null,
			null,
			null,
			null,
			null
		);

		// Empty option set returns same instance
		Add(
			OptionSet.Empty,
			10,
			1024,
			8,
			null,
			null,
			null,
			null,
			null
		);

		// Partial options with clamping
		Add(
			new OptionSet(timeout: 50, blockSize: null, transferSize: 1024, windowSize: 8),
			10,
			1024,
			8,
			2048,
			10, // Clamped from 50 to 10
			null, // Remains null
			8,
			2048 // Replaced
		);

		// Edge cases - values at boundaries
		Add(
			new OptionSet(1, 8, 0, 1),
			255,
			65464,
			65535,
			null,
			1, // Min valid value, no clamping
			8, // Min valid value, no clamping
			1, 
			null // actualTransferSize is null, so remains null
		);

		Add(
			new OptionSet(255, 65464, 18446744073709551615, 65535),
			255,
			65464,
			65535,
			1048576,
			255, // Max valid value, no clamping
			65464, // Max valid value, no clamping
			65535, // Max valid value, no clamping
			1048576 // Replaced with actual size
		);
	}
}
