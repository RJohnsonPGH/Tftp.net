namespace Tftp.Net.Configuration;

/// <summary>
/// Provides configuration options for a TFTP server or client instance.
/// </summary>
/// <remarks>Implementations of this interface supply settings that control the behavior and capabilities of the
/// TFTP server or client, such as file access permissions, root directory location, and protocol limits. These options are
/// typically used during server or client initialization and may affect how requests are handled.</remarks>
public interface ITftpConfigurationProvider
{
	public const ushort MinBlockSizeValue = 8;
	public const ushort MaxBlockSizeValue = 65464;
	public const ushort MinTimeoutValue = 1;
	public const ushort MaxTimeoutValue = 255;
	public const ushort MinWindowSizeValue = 1;
	public const ushort MaxWindowSizeValue = 65535;

	/// <summary>
	/// Gets the absolute path of the root directory used by the server or client.
	/// </summary>
	public string RootDirectory { get; }

	/// <summary>
	/// Gets a value indicating whether write requests are permitted.
	/// </summary>
	public bool AllowWriteRequests { get; }

	/// <summary>
	/// Gets maximum block size, in bytes that the server or client will negotiate.
	/// </summary>
	public ushort MaxBlockSize { get; }

	/// <summary>
	/// Gets the maximum timeout duration, in seconds, that the server or client will negotiate.
	/// </summary>
	public ushort MaxTimeoutSeconds { get; }

	/// <summary>
	/// Gets the maximum window size, in blocks, that the server or client will negotiate (RFC 7440).
	/// </summary>
	public ushort MaxWindowSize { get; }

	/// <summary>
	/// Gets the maximum number of transfers the server will process concurrently. Additional incoming
	/// requests are queued (up to the request queue's capacity) until a slot becomes available.
	/// </summary>
	public int MaxConcurrentTransfers { get; }

	void Validate()
	{
		if (MaxBlockSize < MinBlockSizeValue || MaxBlockSize > MaxBlockSizeValue)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxBlockSize), $"Block size must be between {MinBlockSizeValue} and {MaxBlockSizeValue} bytes.");
		}

		if (MaxTimeoutSeconds < MinTimeoutValue || MaxTimeoutSeconds > MaxTimeoutValue)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxTimeoutSeconds), $"Timeout must be between {MinTimeoutValue} and {MaxTimeoutValue} seconds.");
		}

		if (MaxWindowSize < MinWindowSizeValue || MaxWindowSize > MaxWindowSizeValue)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxWindowSize), $"Window size must be between {MinWindowSizeValue} and {MaxWindowSizeValue} blocks.");
		}

		if (MaxConcurrentTransfers < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxConcurrentTransfers), "Max concurrent transfers must be at least 1.");
		}
	}
}
