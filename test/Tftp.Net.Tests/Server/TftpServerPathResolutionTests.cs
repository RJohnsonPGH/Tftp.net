using Tftp.Net.Server;

namespace Tftp.Net.Tests.Server;

public class TftpServerPathResolutionTests
{
	private static readonly string RootDirectory = Path.Combine(Path.GetTempPath(), "tftp-test-root");

	[Theory]
	[ClassData(typeof(ResolvableFilenameTestData))]
	public void TryResolveRequestedFilePath_ShouldResolvePath_WhenFilenameIsWithinRootDirectory(string filename)
	{
		// Act
		var success = TftpServer.TryResolveRequestedFilePath(RootDirectory, filename, out var filePath);

		// Assert
		Assert.True(success);
		Assert.NotNull(filePath);
		Assert.Equal(Path.GetFullPath(filename, RootDirectory), filePath);
		Assert.True(Path.IsPathRooted(filePath!));
	}

	[Theory]
	[ClassData(typeof(EscapingFilenameTestData))]
	public void TryResolveRequestedFilePath_ShouldRejectPath_WhenFilenameEscapesRootDirectory(string filename)
	{
		// Act
		var success = TftpServer.TryResolveRequestedFilePath(RootDirectory, filename, out var filePath);

		// Assert
		Assert.False(success);
		Assert.Null(filePath);
	}

	[Fact]
	public void TryResolveRequestedFilePath_ShouldRejectPath_WhenResolvedPathIsSiblingDirectorySharingRootNamePrefix()
	{
		// Arrange - 'tftp-root-evil' shares the root directory's name as a prefix, so a
		// containment check that forgets to append the directory separator after the root
		// would incorrectly treat the sibling's files as being inside the root.
		var root = Path.Combine(Path.GetTempPath(), "tftp-root");
		var filename = Path.Combine("..", "tftp-root-evil", "file.txt");

		// Act
		var success = TftpServer.TryResolveRequestedFilePath(root, filename, out var filePath);

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
/// Filenames which resolve to a location outside the root directory. Backslash is a directory
/// separator and "C:\" a rooted path only on Windows; on Linux the same names resolve to plain
/// filenames within the root and appear in <see cref="ResolvableFilenameTestData"/> instead.
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
		}
	}
}
