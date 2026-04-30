---
title: "Skills Command"
type: cli
tags: [spectre, implemented]
created: 2026-05-01
updated: 2026-05-01
confidence: high
---

## Command

`sharp-sense skills [install-root]` writes the embedded SharpSense skills into `.agents/skills` for the current working directory or for an explicit install root. It uses the shared host bootstrap from [[architecture/host-composition]] and is now the only supported install path for repo-provided agent skills.

## Options

| Setting | Source | Purpose |
| --- | --- | --- |
| `InstallRoot` | positional `[install-root]` | Optional root directory that should receive the `.agents/skills` export. |
| `IsVerbose` | `-v\|--verbose` | Enables verbose command-host logging. |

`SkillsCommand.Configure()` keeps the composition small: it reuses the existing `IFileSystem` binding and registers `SkillPackInstaller` as the embedded export boundary.

## Execution Flow

1. `Program.CommandApp.cs` routes `skills` to `SkillsCommand`.
2. `AbstractAsyncCommand<TSettings>` builds the host using the shared rules in [[architecture/host-composition]].
3. `Configure()` ensures `IFileSystem` is available and registers `SkillPackInstaller`.
4. `Execute()` resolves the install root from the injected file system's current directory when no path is supplied, or resolves the explicit path against that same working directory.
5. `SkillPackInstaller` opens the CLI assembly with `ManifestEmbeddedFileProvider`, enumerates the embedded `SharpSense.Skills` tree, and preserves the relative skill-folder layout for the explicitly embedded skill resources.
6. The installer writes `.agents/skills/<skill-name>/SKILL.md` under the chosen root and returns the installed relative paths.
7. `SkillsCommand` prints a compact summary plus the relative installed file list so shell automation can confirm what was exported.

The current CLI package explicitly embeds `sharpsense/SKILL.md`, so the installed tree currently contains `./.agents/skills/sharpsense/SKILL.md`.
