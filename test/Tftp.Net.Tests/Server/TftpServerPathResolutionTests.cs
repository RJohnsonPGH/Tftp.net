using Tftp.Net.Server;

namespace Tftp.Net.Tests.Server;

public class TftpServerPathResolutionTests
{
	private static readonly string RootDirectory = Path.Combine(Path.GetTempPath(), "tftp-test-root");

	[Theory]
	[InlineData("file.txt")]
	[InlineData("subdir/file.txt")]
	[InlineData("subdir\\file.txt")]
	[InlineData("a/b/c/file.txt")]
	[InlineData("subdir/../file.txt")]
	[InlineData("./file.txt")]
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
	[InlineData("../file.txt")]
	[InlineData("..\\file.txt")]
	[InlineData("../../file.txt")]
	[InlineData("subdir/../../file.txt")]
	[InlineData("subdir/../../../file.txt")]
	[InlineData("../subdir/file.txt")]
	[InlineData("..")]
	[InlineData("/etc/passwd")]
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
