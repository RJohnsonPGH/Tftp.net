using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tftp.Net.Channel;
using Tftp.Net.Channel.Client;
using Tftp.Net.Configuration;
using Tftp.Net.Server;

namespace Tftp.Net.Tests.Server;

public class TftpServerPathResolutionTests
{
	private static readonly string RootDirectory = Path.Combine(Path.GetTempPath(), "tftp-test-root");

	private static TftpServer CreateServer() => new(
		NullLogger<TftpServer>.Instance,
		Substitute.For<ITftpConfigurationProvider>(),
		Substitute.For<IUdpClientFactory>(),
		Substitute.For<ITftpChannelFactory>());

	[Theory]
	[ClassData(typeof(ResolvableFilenameTestData))]
	public void TryResolveRequestedFilePath_ShouldResolvePath_WhenFilenameIsWithinRootDirectory(string filename)
	{
		// Act
		var success = CreateServer().TryResolveRequestedFilePath(RootDirectory, filename, out var filePath);

		// Assert
		Assert.True(success);
		Assert.NotNull(filePath);
		Assert.Equal(Path.GetFullPath(filename, RootDirectory), filePath);
		Assert.True(Path.IsPathRooted(filePath!));
	}

	[Fact]
	public void TryResolveRequestedFilePath_ShouldResolvePath_WhenRootDirectoryHasTrailingSeparator()
	{
		// Arrange - the root directory may be configured with a trailing separator
		var root = RootDirectory + Path.DirectorySeparatorChar;

		// Act
		var success = CreateServer().TryResolveRequestedFilePath(root, "file.txt", out var filePath);

		// Assert
		Assert.True(success);
		Assert.Equal(Path.GetFullPath("file.txt", RootDirectory), filePath);
	}

	[Theory]
	[ClassData(typeof(EscapingFilenameTestData))]
	public void TryResolveRequestedFilePath_ShouldRejectPath_WhenFilenameEscapesRootDirectory(string filename)
	{
		// Act
		var success = CreateServer().TryResolveRequestedFilePath(RootDirectory, filename, out var filePath);

		// Assert
		Assert.False(success);
		Assert.Null(filePath);
	}

	[Theory]
	[ClassData(typeof(MalformedPathTestData))]
	public void TryResolveRequestedFilePath_ShouldRejectPath_WhenInputIsMalformed(string rootDirectory, string filename)
	{
		// Act - the method must reject malformed input instead of throwing
		var success = CreateServer().TryResolveRequestedFilePath(rootDirectory, filename, out var filePath);

		// Assert
		Assert.False(success);
		Assert.Null(filePath);
	}

	[Fact]
	public void TryResolveRequestedFilePath_ShouldRejectPath_WhenResolvedPathIsSiblingDirectorySharingRootNamePrefix()
	{
		// Arrange - 'tftp-root-evil' shares the root directory's name as a prefix; its files
		// must not be treated as being inside the root.
		var root = Path.Combine(Path.GetTempPath(), "tftp-root");
		var filename = Path.Combine("..", "tftp-root-evil", "file.txt");

		// Act
		var success = CreateServer().TryResolveRequestedFilePath(root, filename, out var filePath);

		// Assert
		Assert.False(success);
		Assert.Null(filePath);
	}
}

/// <summary>
/// Filenames which resolve to a location inside the root directory. On Linux, backslash and
/// colon are ordinary filename characters, so names which traverse or root a path on Windows
/// are plain filenames within the root there and are resolvable as well.
/// </summary>
internal sealed class ResolvableFilenameTestData : TheoryData<string>
{
	public ResolvableFilenameTestData()
	{
		Add("file.txt");
		Add("subdir/file.txt");
		Add("subdir\\file.txt");
		Add("a/b/c/file.txt");
		Add("subdir/../file.txt");
		Add("./file.txt");

		if (!OperatingSystem.IsWindows())
		{
			Add("..\\file.txt");
			Add("C:\\Windows\\win.ini");
		}
	}
}

/// <summary>
/// Filenames which resolve outside the root directory, or to the root directory itself. Backslash
/// is a directory separator and drive-letter paths are rooted only on Windows; on Linux the same
/// names resolve to plain filenames within the root and appear in <see cref="ResolvableFilenameTestData"/>
/// instead.
/// </summary>
internal sealed class EscapingFilenameTestData : TheoryData<string>
{
	public EscapingFilenameTestData()
	{
		Add("../file.txt");
		Add("../../file.txt");
		Add("subdir/../../file.txt");
		Add("subdir/../../../file.txt");
		Add("../subdir/file.txt");
		Add("..");
		Add("/etc/passwd");

		if (OperatingSystem.IsWindows())
		{
			Add("..\\file.txt");
			Add("C:\\Windows\\win.ini");
			Add("D:\\evil\\file.txt");
		}
	}
}

/// <summary>
/// Malformed root or filename inputs which make path normalization throw (e.g. exceeding the
/// platform's maximum path length). The method must reject such input instead of propagating
/// the exception, since it is called with untrusted client input on the request path.
/// </summary>
internal sealed class MalformedPathTestData : TheoryData<string, string>
{
	public MalformedPathTestData()
	{
		Add(new string('a', 40_000), "file.txt");
		Add("C:\\tftp-root", new string('a', 40_000));
	}
}
