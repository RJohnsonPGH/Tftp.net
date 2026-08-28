using Tftp.Net.Commands.Properties;

namespace Tftp.Net.Tests.Commands;

public class TftpTransferModeTests
{
    [Theory]
	[ClassData(typeof(TftpTransferModeTestData))]
	public void FromString_ShouldReturnExpectedMode(string modeString)
    {
		for (int i = 0; i < 1000; i++)
		{
			// Arrange
			var expectedMode = modeString switch
			{
				"netascii" => TransferMode.NetAscii,
				"octet" => TransferMode.Octet,
				"mail" => TransferMode.Mail,
				_ => throw new ArgumentException($"Unexpected mode string: {modeString}")
			};
			var randomizedModeString = RandomlyCapitalize(modeString);

			// Act
			var parseResult = TransferMode.TryParse(randomizedModeString, out var parsedMode);

			// Assert
			Assert.True(parseResult);
			Assert.Equal(expectedMode, parsedMode);
		}
	}

	public static string RandomlyCapitalize(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return input;
		}

		var rng = new Random();
		var result = new char[input.Length];

		for (int i = 0; i < input.Length; i++)
		{
			char c = input[i];
			if (rng.NextDouble() < 0.5)
			{
				c = char.ToUpper(c);
			}
			result[i] = c;
		}

		return new(result);
	}
}

public class TftpTransferModeTestData : TheoryData<string>
{
    public TftpTransferModeTestData()
    {
        Add("netascii");
        Add("octet");
        Add("mail");
    }
}