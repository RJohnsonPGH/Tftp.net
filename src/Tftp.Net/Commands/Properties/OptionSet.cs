using System.Collections;

namespace Tftp.Net.Commands.Properties;

public sealed record OptionSet : IEnumerable<KeyValuePair<string, string>>
{
	/// <summary>
	/// The protocol-defined range of the timeout option in seconds (RFC 2349).
	/// </summary>
	public const ushort MinTimeoutValue = 1;
	public const ushort MaxTimeoutValue = 255;

	/// <summary>
	/// The protocol-defined range of the block size option in bytes (RFC 2348).
	/// </summary>
	public const ushort MinBlockSizeValue = 8;
	public const ushort MaxBlockSizeValue = 65464;

	/// <summary>
	/// The protocol-defined range of the window size option in blocks (RFC 7440).
	/// </summary>
	public const ushort MinWindowSizeValue = 1;
	public const ushort MaxWindowSizeValue = 65535;

	public ushort? Timeout { get; }
	public ushort? BlockSize { get; }
	public ushort? WindowSize { get; }
	public ulong? TransferSize { get; }

	/// <summary>
	/// Gets the empty option set which carries no options at all.
	/// </summary>
	/// <remarks>An empty option set signals that no option negotiation is to take place and that the
	/// protocol defaults (e.g. a block size of 512 bytes and a window size of 1) apply.</remarks>
	public static OptionSet Empty { get; } = new();

	internal OptionSet(ushort? timeout = null, ushort? blockSize = null, ulong? transferSize = null, ushort? windowSize = null)
	{
		if (timeout is not null && (timeout < 1 || timeout > 255))
		{
			throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be between 1 and 255 seconds.");
		}

		if (blockSize is not null && (blockSize < 8 || blockSize > 65464))
		{
			throw new ArgumentOutOfRangeException(nameof(blockSize), "Block size must be between 8 and 65464 bytes.");
		}

		if (windowSize is not null && windowSize < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be between 1 and 65535 blocks.");
		}

		Timeout = timeout;
		BlockSize = blockSize;
		WindowSize = windowSize;
		TransferSize = transferSize;
	}

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        if (Timeout is not null)
		{
			yield return new KeyValuePair<string, string>("timeout", Timeout.Value.ToString());
		}

		if (BlockSize is not null)
		{
			yield return new KeyValuePair<string, string>("blksize", BlockSize.Value.ToString());
		}

		if (TransferSize is not null)
		{
			yield return new KeyValuePair<string, string>("tsize", TransferSize.Value.ToString());
		}

		if (WindowSize is not null)
		{
			yield return new KeyValuePair<string, string>("winsize", WindowSize.Value.ToString());
		}
	}

    IEnumerator IEnumerable.GetEnumerator() => 
		GetEnumerator();
}

public static class OptionSetExtensions
{
	public static OptionSet Clamp(this OptionSet optionSet, ushort maxTimeout, ushort maxBlockSize, ushort maxWindowSize, ulong? actualTransferSize)
	{
		if (maxTimeout < OptionSet.MinTimeoutValue || maxTimeout > OptionSet.MaxTimeoutValue)
		{
			throw new ArgumentOutOfRangeException(nameof(maxTimeout), $"maxTimeout must be between {OptionSet.MinTimeoutValue} and {OptionSet.MaxTimeoutValue}.");
		}

		if (maxBlockSize < OptionSet.MinBlockSizeValue || maxBlockSize > OptionSet.MaxBlockSizeValue)
		{
			throw new ArgumentOutOfRangeException(nameof(maxBlockSize), $"maxBlockSize must be between {OptionSet.MinBlockSizeValue} and {OptionSet.MaxBlockSizeValue}.");
		}

		if (maxWindowSize < OptionSet.MinWindowSizeValue || maxWindowSize > OptionSet.MaxWindowSizeValue)
		{
			throw new ArgumentOutOfRangeException(nameof(maxWindowSize), $"maxWindowSize must be between {OptionSet.MinWindowSizeValue} and {OptionSet.MaxWindowSizeValue}.");
		}

		if (actualTransferSize < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(actualTransferSize), "transferSize must be non-negative.");
		}

		// If empty, just return the empty option set.
		if (optionSet == OptionSet.Empty)
		{
			return optionSet;
		}

		// Keep null options null, but clamp the values of non-null options to the maximums.
		return new OptionSet(
			timeout: optionSet.Timeout is not null ? Math.Min(optionSet.Timeout.Value, maxTimeout) : null,
			blockSize: optionSet.BlockSize is not null ? Math.Min(optionSet.BlockSize.Value, maxBlockSize) : null,
			windowSize: optionSet.WindowSize is not null ? Math.Min(optionSet.WindowSize.Value, maxWindowSize) : null,
			transferSize: optionSet.TransferSize is not null && actualTransferSize is not null ? actualTransferSize.Value : null
		);
	}
}
