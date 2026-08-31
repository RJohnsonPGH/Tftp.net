using Tftp.Net.Server;

namespace Tftp.Net.Tests.Server;

public class TftpServerPathResolutionTests
{
	private static readonly string RootDirectory = Path.Combine(Path.GetTempPath(), "tftp-test-root");

	[Theory]
	[ClassData(typeof(ResolvableFilenameTestData))]
	public void TryResolveRequestedFilePath_ShouldResolvePath_WhenFilenameIsWithinRootDirectory(string filename, bool addSeparatorChar)
	{
		var rootDirectory = RootDirectory;
		if (addSeparatorChar)
		{
			rootDirectory += Path.DirectorySeparatorChar;
		}

		// Act
		var success = TftpServer.TryResolveRequestedFilePath(rootDirectory, filename, out var filePath);

		// Assert
		Assert.True(success);
		Assert.NotNull(filePath);
		Assert.Equal(Path.GetFullPath(filename, rootDirectory), filePath);
		Assert.True(Path.IsPathRooted(filePath!));
	}

	[Theory]
	[ClassData(typeof(UnresolvableFilenameTestData))]
	public void TryResolveRequestedFilePath_ShouldRejectPath_WhenFilenameIsInDifferentRootDrive(string filename, bool addSeparatorChar)
	{
		var rootDirectory = RootDirectory;
		if (addSeparatorChar)
		{
			rootDirectory += Path.DirectorySeparatorChar;
		}

		// Act
		var success = TftpServer.TryResolveRequestedFilePath(rootDirectory, filename, out var filePath);

		// Assert
		Assert.False(success);
		Assert.Null(filePath);
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
internal sealed class ResolvableFilenameTestData : TheoryData<string, bool>
{
	public ResolvableFilenameTestData()
	{
		Add("file.txt", true);
		Add("file.txt", false);
		Add("subdir/file.txt", true);
		Add("subdir/file.txt", false);
		Add("subdir\\file.txt", true);
		Add("subdir\\file.txt", false);
		Add("a/b/c/file.txt", true);
		Add("a/b/c/file.txt", false);
		Add("subdir/../file.txt", true);
		Add("subdir/../file.txt", false);
		Add("./file.txt", true);
		Add("./file.txt", false);

		if (!OperatingSystem.IsWindows())
		{
			Add("..\\file.txt", true);
			Add("..\\file.txt", false);
			Add("C:\\Windows\\win.ini", true);
			Add("C:\\Windows\\win.ini", false);

		}
	}
}

/// <summary>
/// Filenames which resolve to a different root
/// </summary>
internal sealed class UnresolvableFilenameTestData : TheoryData<string, bool>
{
	public UnresolvableFilenameTestData()
	{
		if (OperatingSystem.IsWindows())
		{
			Add("D:\\file.txt", true);
			Add("D:\\file.txt", false);
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
