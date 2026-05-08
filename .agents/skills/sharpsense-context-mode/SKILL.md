---
name: sharpsense-context-mode
description: |
  Use context-mode tools (sharpsense:ctx_execute) instead of Bash/cat
  when processing large outputs. Triggers: "dotnet build", "dotnet test", "analyze logs",
  "summarize output", "process data", "parse JSON", "filter results", "extract errors",
  "check build output", "analyze dependencies", "process API response", "large file analysis",
  "run tests", "test output", "coverage report", "git log", "recent commits",
  "diff between branches", "list containers", "pod status", "disk usage", "fetch docs",
  "API reference", "index documentation", "call API", "check response", "query results",
  "find TODOs", "count lines", "codebase statistics", "security audit", "outdated packages",
  "dependency tree", "cloud resources", "CI/CD output". Also triggers on ANY tool output that may exceed 20 lines.
---

# Context Mode & Subagent Delegation

## 1. MANDATORY ROUTING RULE

Default to context-mode for ALL commands. Only use Bash for guaranteed-small-output operations.

**Bash Whitelist (Safe for main thread):**

- File mutations: `mkdir`, `mv`, `cp`, `rm`, `touch`, `chmod`
- Git writes: `git add`, `git commit`, `git push`, `git checkout`, `git branch`, `git merge`
- Navigation/Control: `cd`, `pwd`, `which`, `kill`, `pkill`
- Simple output: `echo`, `printf`

**Context-Mode (Requires Subagent):**

- **.NET & Architecture**: `dotnet build`, `dotnet test`, `nuget` -> Use `sharpsense:ctx_execute`.
- **Code Discovery**: finding codebase context -> Use `sharpsense:semantic_search` or `sharpsense:trace_node`.
- **Generic CLI & Logs**: `npm`, `gh`, parsing JSON, reading logs -> Use `ctx_execute`.

## 2. SUBAGENT DELEGATION PROTOCOL

For any command falling under Context-Mode, you MUST NOT execute it directly in the main thread to prevent context
window exhaustion. You must spawn a subagent.

**Subagent Task Initialisation:**
When spawning the subagent, your task description MUST explicitly pass the output constraints. Use this exact structure
for the subagent prompt:

> "Task: [Command/Goal, e.g. 'Run dotnet build and report errors'].
> Tool: [Selected tool, e.g., 'sharpsense:ctx_execute'].
> Constraint: Do not return raw logs or conversational text. You MUST compress your final findings into this exact
> schema: `[File/Component] [State] [Reason/Code]. [Next Step].` Fragments only. Drop articles."

## 3. PARENT RE-INTEGRATION

When the subagent completes and returns the compressed payload:

1. Accept the payload exactly as written (e.g.,
   `[AuthController.cs:42] [build failed] [CS1002: missing ;]. [add semicolon].`).
2. Do NOT expand, summarise, or apologise for the output.
3. Print the compressed output to the user and proceed immediately to the next required action.
4. Auto-expand to standard English ONLY for security warnings or irreversible actions.
