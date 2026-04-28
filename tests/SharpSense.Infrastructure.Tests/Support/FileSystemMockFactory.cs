using Moq;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Tests.Support;

public static class FileSystemMockFactory
{
    public static IFileSystem Create(params (string Path, string Contents)[] files)
    {
        var file = new Mock<IFile>(MockBehavior.Strict);
        var path = new Mock<IPath>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var fileContentsByPath = files.ToDictionary(
            static item => item.Path,
            static item => item.Contents,
            StringComparer.Ordinal);

        file.Setup(candidate => candidate.Exists(It.IsAny<string>()))
            .Returns((string candidatePath) => fileContentsByPath.ContainsKey(candidatePath));
        file.Setup(candidate => candidate.ReadAllTextAsync(
                It.IsAny<string>(),
                TestContext.Current.CancellationToken))
            .Returns((string candidatePath, CancellationToken _) => Task.FromResult(fileContentsByPath[candidatePath]));
        path.Setup(candidate => candidate.IsPathRooted(It.IsAny<string>()))
            .Returns((string candidatePath) => candidatePath.StartsWith("/", StringComparison.Ordinal));
        path.Setup(candidate => candidate.GetFullPath(It.IsAny<string>()))
            .Returns((string candidatePath) => candidatePath);
        fileSystem.SetupGet(candidate => candidate.File)
            .Returns(file.Object);
        fileSystem.SetupGet(candidate => candidate.Path)
            .Returns(path.Object);

        return fileSystem.Object;
    }
}
