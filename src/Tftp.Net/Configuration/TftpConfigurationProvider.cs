namespace Tftp.Net.Configuration;

/// <summary>
/// Represents configuration options for a Trivial File Transfer Protocol (TFTP) server or client, including root directory, write
/// permissions, block size, and timeout settings.
/// </summary>
/// <remarks>Use this class to specify the operational parameters for a TFTP server or clientinstance. The options defined
/// by this type determine how the server or client handles file transfers, write requests, and protocol-level behaviors
/// such as block size and timeouts. All properties are read-only and must be set at construction time.</remarks>
public sealed record TftpConfigurationProvider : ITftpConfigurationProvider
{
	/// <summary>
	/// Gets the absolute path of the root directory used by the server.
	/// </summary>
	public string RootDirectory => _rootDirectory;
	private readonly string _rootDirectory;

	/// <summary>
	/// Gets a value indicating whether write requests are permitted.
	/// </summary>
	public bool AllowWriteRequests => _allowWriteRequests;
	private readonly bool _allowWriteRequests;

	/// <summary>
	/// Gets the maximum block size, in bytes, that the server will negotiate.
	/// </summary>
	public ushort MaxBlockSize => _maxBlockSize;
	private readonly ushort _maxBlockSize;

	/// <summary>
	/// Gets the maximum timeout duration, in seconds, that the server will negotiate.
	/// </summary>
	public ushort MaxTimeoutSeconds => _maxTimeoutSeconds;
	private readonly ushort _maxTimeoutSeconds;

	/// <summary>
	/// Gets the maximum window size, in blocks, that the server will negotiate (RFC 7440).
	/// </summary>
	public ushort MaxWindowSize => _maxWindowSize;
	private readonly ushort _maxWindowSize;

	/// <summary>
	/// Gets the maximum number of transfers the server will process concurrently.
	/// </summary>
	public int MaxConcurrentTransfers => _maxConcurrentTransfers;
	private readonly int _maxConcurrentTransfers;

	/// <summary>
	/// Initializes a new instance of the TftpConfigurationProvider class with the specified root directory and configuration
	/// settings.
	/// </summary>
	/// <param name="rootDirectory">The path to the root directory from which files will be served. The directory must exist.</param>
	/// <param name="allowWriteRequests">true to allow write requests from clients; otherwise, false. Defaults to false.</param>
	/// <param name="maxBlockSize">The block size, in bytes, to use for TFTP data transfers. Must be between 8 and 65464.</param>
	/// <param name="maxTimeoutSeconds">The timeout interval, in seconds, for TFTP operations. Must be between 1 and 255.</param>
	/// <param name="maxWindowSize">The maximum window size, in blocks, for windowed transfers per RFC 7440. Must be between 1 and 65535.</param>
	/// <param name="maxConcurrentTransfers">The maximum number of transfers to process concurrently. Must be at least 1.</param>
	/// <exception cref="DirectoryNotFoundException">Thrown if the specified root directory does not exist.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown if maxBlockSize is not between 8 and 65464, if maxTimeoutSeconds is not between 1 and 255,
	/// if maxWindowSize is less than 1, or if maxConcurrentTransfers is less than 1.</exception>
	public TftpConfigurationProvider(string rootDirectory, 
		bool allowWriteRequests = false, 
		ushort maxBlockSize = ITftpConfigurationProvider.MaxBlockSizeValue, 
		ushort maxTimeoutSeconds = ITftpConfigurationProvider.MaxTimeoutValue, 
		ushort maxWindowSize = ITftpConfigurationProvider.MaxWindowSizeValue, 
		int maxConcurrentTransfers = 10)
	{
		if (!Path.Exists(rootDirectory))
		{
			throw new DirectoryNotFoundException($"The specified root directory does not exist: {rootDirectory}");
		}

		if (maxBlockSize < ITftpConfigurationProvider.MinBlockSizeValue || maxBlockSize > ITftpConfigurationProvider.MaxBlockSizeValue)
		{
			throw new ArgumentOutOfRangeException(nameof(maxBlockSize), $"Block size must be between {ITftpConfigurationProvider.MinBlockSizeValue} and {ITftpConfigurationProvider.MaxBlockSizeValue} bytes.");
		}

		if (maxTimeoutSeconds < ITftpConfigurationProvider.MinTimeoutValue || maxTimeoutSeconds > ITftpConfigurationProvider.MaxTimeoutValue)
		{
			throw new ArgumentOutOfRangeException(nameof(maxTimeoutSeconds), $"Timeout must be between {ITftpConfigurationProvider.MinTimeoutValue} and {ITftpConfigurationProvider.MaxTimeoutValue} seconds.");
		}

		if (maxWindowSize < ITftpConfigurationProvider.MinWindowSizeValue || maxWindowSize > ITftpConfigurationProvider.MaxWindowSizeValue)
		{
			throw new ArgumentOutOfRangeException(nameof(maxWindowSize), $"Window size must be between {ITftpConfigurationProvider.MinWindowSizeValue} and {ITftpConfigurationProvider.MaxWindowSizeValue} blocks.");
		}

		if (maxConcurrentTransfers < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(maxConcurrentTransfers), "Max concurrent transfers must be at least 1.");
		}

		_rootDirectory = rootDirectory;
		_allowWriteRequests = allowWriteRequests;
		_maxBlockSize = maxBlockSize;
		_maxTimeoutSeconds = maxTimeoutSeconds;
		_maxWindowSize = maxWindowSize;
		_maxConcurrentTransfers = maxConcurrentTransfers;
	}
}
