---
title: "Application Vertical Slice Layout"
type: architecture
tags: [cqrs, csharp, implemented]
created: 2026-04-27
updated: 2026-09-24
confidence: high
---

## The Problem

The Application layer had drifted into a generic `Features/` root with inconsistent sub-folder naming and thin handlers that delegated real orchestration elsewhere. That structure hid slice boundaries, diluted command/query locality, and encouraged orchestration to leak out of the handler.

## The Approach

SharpSense now treats each Application feature as a first-class Vertical Slice rooted directly under `src/SharpSense.Application/{FeatureName}/`. Shared boundaries live in `Abstractions/`, shared feature records live in `Models/`, and each command/query owns its own folder containing the handler plus a local `Models/` directory for payloads that should not leak across the slice. This keeps CQRS entrypoints physically close to their models, preserves modular DI via `{FeatureName}ServiceCollectionExtensions.cs`, and aligns feature structure with [cqrs pipeline](cqrs-pipeline.md) and [host composition](host-composition.md).

## Components Involved

| Component | Role |
| --- | --- |
| `src/SharpSense.Application/{FeatureName}/Abstractions/` | Defines Application-facing boundaries implemented by Infrastructure or adjacent layers. |
| `src/SharpSense.Application/{FeatureName}/Models/` | Holds shared feature records reused by multiple handlers in the slice. |
| `src/SharpSense.Application/{FeatureName}/{CommandOrQueryName}/` | Local home for a specific command/query handler. |
| `src/SharpSense.Application/{FeatureName}/{CommandOrQueryName}/Models/` | Holds payloads scoped to one command/query. |
| `{FeatureName}ServiceCollectionExtensions.cs` | Registers the slice handlers without polluting `Program.cs`. |

## Strict Rules

1. Do not create or reintroduce `src/SharpSense.Application/Features/`.
2. Put shared feature interfaces in `Abstractions/`; do not hide them under `Infrastructure/` inside Application.
3. Put shared feature records in `Models/`; do not use `Contracts/` for Application slice records.
4. Keep command/query payloads in the owning command/query folder under `Models/`.
5. Keep orchestration in the handler for that command/query unless there is a clear cross-slice abstraction boundary.
6. Keep feature registration in `{FeatureName}ServiceCollectionExtensions.cs` and compose it through the host as described in [host composition](host-composition.md).
