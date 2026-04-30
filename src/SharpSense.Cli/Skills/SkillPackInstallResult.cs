namespace SharpSense.Cli.Skills;

internal sealed record SkillPackInstallResult(
    string InstallRootPath,
    string SkillsDirectoryPath,
    string[] InstalledFiles);
