namespace SharpSense.Infrastructure.Storage;

public interface IRepositoryWorkspace
{
    string RootPath { get; }

    string DatabasePath { get; }

    string ToRepositoryRelativePath(string? filePath);

    string GetRequiredTargetDirectoryPath(string targetPath);

    bool TryToRepositoryRelativePath(string? filePath, out string relativePath);

    bool IsSameOrSubPath(string? filePath);

    string NormalizeDirectorySeparators(string path);
}
