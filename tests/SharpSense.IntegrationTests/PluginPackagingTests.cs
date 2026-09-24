using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.FileProviders;

namespace SharpSense.IntegrationTests;

public sealed class PluginPackagingTests
{
    private static readonly string _fixtureRoot = Path.Combine(AppContext.BaseDirectory, "PluginFixtures");
    private static readonly string _pluginRoot = Path.Combine(_fixtureRoot, "plugins", "sharpsense");

    [Fact]
    public void WhenPluginManifestsAreRead_ThenPortableAndCodexIdentitiesAgree()
    {
        using var portableDocument = ReadJson(Path.Combine(_pluginRoot, "plugin.json"));
        using var codexDocument = ReadJson(Path.Combine(_pluginRoot, ".codex-plugin", "plugin.json"));
        var portable = portableDocument.RootElement;
        var codex = codexDocument.RootElement;

        portable.GetProperty("$schema").GetString()
            .Should().Be("https://agent-plugins.org/schemas/1.0.0/plugin.schema.json");
        portable.GetProperty("name").GetString().Should().Be("sharpsense");
        portable.GetProperty("author").GetProperty("name").GetString().Should().Be("Mitchel Box");
        portable.GetProperty("license").GetString().Should().Be("MIT");

        var version = portable.GetProperty("version").GetString();
        version.Should().MatchRegex(
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)" +
            @"(?:-(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*)?" +
            @"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$");

        foreach (var property in new[] { "name", "version", "description", "homepage", "repository", "license" })
        {
            codex.GetProperty(property).GetString().Should().Be(portable.GetProperty(property).GetString());
        }

        codex.GetProperty("author").GetProperty("name").GetString()
            .Should().Be(portable.GetProperty("author").GetProperty("name").GetString());
        codex.GetProperty("skills").GetString().Should().Be("./skills/");
        Directory.Exists(Path.Combine(_pluginRoot, codex.GetProperty("skills").GetString()!))
            .Should().BeTrue();
    }

    [Fact]
    public void WhenMarketplacesAreRead_ThenBothResolveTheSamePlugin()
    {
        using var pluginDocument = ReadJson(Path.Combine(_pluginRoot, "plugin.json"));
        using var codexDocument = ReadJson(Path.Combine(_fixtureRoot, ".agents", "plugins", "marketplace.json"));
        using var copilotDocument = ReadJson(Path.Combine(_fixtureRoot, ".github", "plugin", "marketplace.json"));
        var plugin = pluginDocument.RootElement;
        var codex = codexDocument.RootElement;
        var copilot = copilotDocument.RootElement;

        foreach (var marketplace in new[] { codex, copilot })
        {
            marketplace.GetProperty("name").GetString().Should().Be("sharpsense-marketplace");
            marketplace.GetProperty("plugins").GetArrayLength().Should().Be(1);
            marketplace.GetProperty("plugins")[0].GetProperty("name").GetString()
                .Should().Be(plugin.GetProperty("name").GetString());
        }

        var codexSource = codex.GetProperty("plugins")[0].GetProperty("source");
        codexSource.GetProperty("source").GetString().Should().Be("local");
        var copilotEntry = copilot.GetProperty("plugins")[0];
        copilotEntry.GetProperty("version").GetString().Should().Be(plugin.GetProperty("version").GetString());
        copilot.GetProperty("metadata").GetProperty("version").GetString()
            .Should().Be(plugin.GetProperty("version").GetString());

        foreach (var source in new[] { codexSource.GetProperty("path").GetString(), copilotEntry.GetProperty("source").GetString() })
        {
            source.Should().Be("./plugins/sharpsense");
            Path.GetFullPath(Path.Combine(_fixtureRoot, source!)).Should().Be(Path.GetFullPath(_pluginRoot));
            File.Exists(Path.Combine(_fixtureRoot, source!, "plugin.json")).Should().BeTrue();
        }
    }

    [Fact]
    public void WhenSkillsAreDiscovered_ThenEachExistingSkillRetainsItsMetadata()
    {
        var skillsRoot = Path.Combine(_pluginRoot, "skills");
        var skillDirectories = Directory.GetDirectories(skillsRoot);
        skillDirectories.Select(Path.GetFileName).Should().BeEquivalentTo(new[]
        {
            "sharpsense-context-mode",
            "sharpsense-exploring",
            "sharpsense-impact-analysis"
        });

        foreach (var directory in skillDirectories)
        {
            var skillPath = Path.Combine(directory, "SKILL.md");
            File.Exists(skillPath).Should().BeTrue();
            var lines = File.ReadAllLines(skillPath);
            lines[0].Should().Be("---");
            var frontmatterEnd = Array.IndexOf(lines, "---", 1);
            frontmatterEnd.Should().BeGreaterThan(1);
            var frontmatter = lines[1..frontmatterEnd];
            var nameLine = frontmatter.Should()
                .ContainSingle(line => line.StartsWith("name:", StringComparison.Ordinal)).Which;
            var descriptionLine = frontmatter.Should()
                .ContainSingle(line => line.StartsWith("description:", StringComparison.Ordinal)).Which;

            nameLine["name:".Length..].Trim().Should().Be(Path.GetFileName(directory));
            descriptionLine["description:".Length..].Trim().Should().NotBeNullOrWhiteSpace();
            string.Join("\n", lines[(frontmatterEnd + 1)..]).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void WhenPluginContentsAreInspected_ThenOnlySkillsAndMetadataArePackaged()
    {
        Directory.GetDirectories(_pluginRoot).Select(Path.GetFileName)
            .Should().BeEquivalentTo(new[] { "skills", ".codex-plugin" });
        Directory.GetFiles(Path.Combine(_pluginRoot, ".codex-plugin")).Select(Path.GetFileName)
            .Should().BeEquivalentTo(new[] { "plugin.json" });
        Directory.GetFiles(_pluginRoot).Select(Path.GetFileName)
            .Should().OnlyContain(name => name == "plugin.json" || name == "README.md" || name == "LICENSE");

        foreach (var path in new[] { "plugin.json", Path.Combine(".codex-plugin", "plugin.json") })
        {
            using var document = ReadJson(Path.Combine(_pluginRoot, path));
            var propertyNames = document.RootElement.EnumerateObject().Select(property => property.Name);

            propertyNames.Should().NotIntersectWith(new[]
            {
                "mcpServers", "hooks", "commands", "agents", "lspServers", "scripts", "outputStyles"
            });
        }
    }

    [Fact]
    public void WhenCliResourcesAreInspected_ThenSkillsAreAbsentAndEmbeddedUiRemainsAvailable()
    {
        var assembly = typeof(Cli.Program).Assembly;
        assembly.GetManifestResourceNames()
            .Should().NotContain(name => name.Contains("SharpSense.Skills", StringComparison.OrdinalIgnoreCase));
        assembly.GetManifestResourceNames()
            .Should().NotContain(name => name.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase));

        var provider = new ManifestEmbeddedFileProvider(assembly);
        provider.GetDirectoryContents("SharpSense.Skills").Exists.Should().BeFalse();
        var ui = provider.GetFileInfo("SharpSense.UI/index.html");
        ui.Exists.Should().BeTrue();
        ui.Length.Should().BeGreaterThan(0);
        using var stream = ui.CreateReadStream();
        stream.CanRead.Should().BeTrue();
    }

    private static JsonDocument ReadJson(string path)
    {
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
