using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tftp.Net.Commands.Properties;
using Tftp.Net.Commands.Validation;

namespace Tftp.Net.Tests.Commands;

/// <summary>
/// Tests parsing and clamping of the RFC 7440 window size ("winsize") option.
/// </summary>
public class WindowSizeOptionTests(ITestOutputHelper outputHelper)
{
	[Theory]
	[InlineData("1", (ushort)1)]        // Minimum valid window size
	[InlineData("16", (ushort)16)]
	[InlineData("65535", (ushort)65535)] // Maximum valid window size
	public void TryParse_ShouldReturnExpectedWindowSize(string value, ushort expectedWindowSize)
	{
		using var serviceProvider = new ServiceCollection()
			.AddSingleton<OptionSetValidator>()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var optionSetValidator = serviceProvider.GetRequiredService<OptionSetValidator>();

		var options = new List<KeyValuePair<string, string>> { new("winsize", value) };
		var success = optionSetValidator.TryParseRequestOptionSet(options, out var optionSet);

		Assert.True(success);
		Assert.NotNull(optionSet);
		Assert.Equal(expectedWindowSize, optionSet!.WindowSize);
	}

	[Theory]
	[ClassData(typeof(WindowSizeTryParseTestData))]
	public void TryParse_ShouldReturnExpectedResult(
		IEnumerable<KeyValuePair<string, string>> unparsedOptions,
		bool expectedSuccess,
		ushort? expectedWindowSize)
	{
		using var serviceProvider = new ServiceCollection()
			.AddSingleton<OptionSetValidator>()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var optionSetValidator = serviceProvider.GetRequiredService<OptionSetValidator>();
		var success = optionSetValidator.TryParseRequestOptionSet(unparsedOptions, out var optionSet);

		Assert.Equal(expectedSuccess, success);

		if (expectedSuccess)
		{
			Assert.NotNull(optionSet);
			Assert.Equal(expectedWindowSize, optionSet!.WindowSize);
		}
		else
		{
			Assert.Null(optionSet);
		}
	}

	[Fact]
	public void ToOptionList_ShouldRoundTripWindowSize()
	{
		using var serviceProvider = new ServiceCollection()
			.AddSingleton<OptionSetValidator>()
			.AddLogging((builder) =>
			{
				builder.AddXUnit(outputHelper);
				builder.SetMinimumLevel(LogLevel.Trace);
			})
			.BuildServiceProvider();

		var optionSetValidator = serviceProvider.GetRequiredService<OptionSetValidator>();

		var optionSet = new OptionSet(windowSize: 32);
		var success = optionSetValidator.TryParseRequestOptionSet([new("winsize", "32")], out var parsed);

		Assert.True(success);
		Assert.Equal((ushort)32, parsed!.WindowSize);
	}

	[Fact]
	public void Clamp_ShouldClampWindowToMaximum()
	{
		var optionSet = new OptionSet(windowSize: 1024);

		var clamped = optionSet.Clamp(maxTimeout: 5, maxBlockSize: 512, maxWindowSize: 8, actualTransferSize: null);

		Assert.Equal((ushort)8, clamped.WindowSize);
	}

	[Fact]
	public void Clamp_ShouldPreserveWindow_WhenWithinLimit()
	{
		var optionSet = new OptionSet(timeout: 5, blockSize: 512, transferSize: 0, windowSize: 8);

		var clamped = optionSet.Clamp(maxTimeout: 10, maxBlockSize: 65464, actualTransferSize: 1234, maxWindowSize: 16);

		Assert.Equal((ushort)8, clamped.WindowSize);
		Assert.Equal((ushort)5, clamped.Timeout);
		Assert.Equal((ushort)512, clamped.BlockSize);
		Assert.Equal((ulong)1234, clamped.TransferSize);
	}

	[Fact]
	public void Clamp_ShouldKeepWindowNull_WhenNotRequested()
	{
		var optionSet = new OptionSet(blockSize: 512);

		var clamped = optionSet.Clamp(maxTimeout: 5, maxBlockSize: 512, maxWindowSize: 8, actualTransferSize: null);

		Assert.Null(clamped.WindowSize);
	}

	[Fact]
	public void Clamp_ShouldReturnSameInstance_WhenEmpty()
	{
		var result = OptionSet.Empty.Clamp(maxTimeout: 5, maxBlockSize: 512, maxWindowSize: 8, actualTransferSize: null);

		Assert.Same(OptionSet.Empty, result);
	}

	[Theory]
	[InlineData((ushort)0)]
	public void Clamp_ShouldThrow_WhenMaxWindowSizeIsInvalid(ushort maxWindowSize)
	{
		var optionSet = new OptionSet(windowSize: 8);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => optionSet.Clamp(maxTimeout: 5, maxBlockSize: 512, maxWindowSize: maxWindowSize, actualTransferSize: null));
	}
}

public class WindowSizeTryParseTestData : TheoryData<IEnumerable<KeyValuePair<string, string>>, bool, ushort?>
{
	public WindowSizeTryParseTestData()
	{
		// Valid combined with other options
		Add(
			[
				new KeyValuePair<string, string>("timeout", "5"),
				new KeyValuePair<string, string>("blksize", "1024"),
				new KeyValuePair<string, string>("tsize", "1048576"),
				new KeyValuePair<string, string>("winsize", "64")
			],
			true,
			64
		);

		// Unknown options coexist with winsize
		Add(
			[
				new KeyValuePair<string, string>("winsize", "4"),
				new KeyValuePair<string, string>("unknown", "value")
			],
			true,
			4
		);

		// Above the protocol maximum of 65535 cannot be represented as ushort and is declined
		Add(
			[new KeyValuePair<string, string>("winsize", "65536")],
			true,
			null
		);

		// Values below minimum are declined
		Add(
			[new KeyValuePair<string, string>("winsize", "-5")],
			true,
			null
		);

		// Non-numeric values are declined
		Add(
			[new KeyValuePair<string, string>("winsize", "abc")],
			true,
			null
		);

		// Duplicates (case-insensitive) are rejected
		Add(
			[
				new KeyValuePair<string, string>("winsize", "4"),
				new KeyValuePair<string, string>("WINSIZE", "8")
			],
			false,
			null
		);
	}
}
