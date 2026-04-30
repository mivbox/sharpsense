using Microsoft.Extensions.FileProviders;
using System.IO.Abstractions;

namespace SharpSense.Cli.Skills;

internal sealed class SkillPackInstaller(IFileSystem fileSystem)
{
    private const string _embeddedNamespace = "SharpSense.Skills";

    public SkillPackInstallResult Install(string installRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRootPath);

        var normalizedInstallRoot = fileSystem.Path.GetFullPath(installRootPath);
        var skillsDirectoryPath = fileSystem.Path.Combine(normalizedInstallRoot, ".agents", "skills");
        var fileProvider = new ManifestEmbeddedFileProvider(typeof(SkillPackInstaller).Assembly);
        var embeddedFiles = EnumerateFiles(fileProvider, _embeddedNamespace, string.Empty).ToArray();

        if (embeddedFiles.Length == 0)
        {
            throw new InvalidOperationException("Embedded SharpSense skills were not found.");
        }

        foreach (var embeddedFile in embeddedFiles)
        {
            var destinationPath = fileSystem.Path.Combine(
                skillsDirectoryPath,
                embeddedFile.RelativePath.Replace('/', fileSystem.Path.DirectorySeparatorChar));
            var destinationDirectory = fileSystem.Path.GetDirectoryName(destinationPath) ?? skillsDirectoryPath;

            fileSystem.Directory.CreateDirectory(destinationDirectory);

            using var sourceStream = embeddedFile.FileInfo.CreateReadStream();
            using var destinationStream = fileSystem.File.Create(destinationPath);
            sourceStream.CopyTo(destinationStream);
        }

        return new SkillPackInstallResult(
            normalizedInstallRoot,
            skillsDirectoryPath,
            [.. embeddedFiles.Select(static file => file.RelativePath)]);
    }

    private static IEnumerable<EmbeddedSkillFile> EnumerateFiles(
        IFileProvider fileProvider,
        string subpath,
        string relativePath)
    {
        var contents = fileProvider.GetDirectoryContents(subpath);
        if (!contents.Exists)
        {
            yield break;
        }

        foreach (var item in contents.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            var itemPath = string.IsNullOrEmpty(subpath)
                ? item.Name
                : $"{subpath}/{item.Name}";
            var itemRelativePath = string.IsNullOrEmpty(relativePath)
                ? item.Name
                : $"{relativePath}/{item.Name}";

            if (item.IsDirectory)
            {
                foreach (var nestedFile in EnumerateFiles(fileProvider, itemPath, itemRelativePath))
                {
                    yield return nestedFile;
                }

                continue;
            }

            yield return new EmbeddedSkillFile(itemRelativePath, item);
        }
    }

    private sealed record EmbeddedSkillFile(
        string RelativePath,
        Microsoft.Extensions.FileProviders.IFileInfo FileInfo);
}
