---
name: sharpsense-architect
description: "Expert AI architectural assistant for querying repository context,
    executing impact analysis, and tracing execution flows. Use when exploring the codebase,
    understanding Markdown documentation, or planning code changes."
---

# SharpSense Codebase Navigation

## When to Use

- "How does X work?" or "Where is the billing logic?" (Exploration)
- "What calls this method?" (Blast Radius / Upstream Impact)
- "What does this service depend on?" (Execution Path / Downstream Dependencies)
- Understanding architectural boundaries, finding Markdown documentation, and navigating chunks of ADRs/READMEs.

## Workflow (The 1-2 Punch)

1. `semantic_search({query: "<what you want to find>"})` → Discover the exact TOON `NodeId`.
2. `trace_node({nodeId: "<NodeId>", direction: "<caller|callee>"})` → Traverse the graph.

## Checklist

- [ ] Read the user's prompt to determine the core concept.
- [ ] Run `semantic_search` to locate the relevant nodes and extract the `NodeId` from the backticks (`` ` ``).
- [ ] If the user asks what *depends* on the node (impact), run `trace_node` with `direction="caller"`.
- [ ] If the user asks how the node *executes*, run `trace_node` with `direction="callee"`.
- [ ] **READ THE CODE/DOCS:** If the user asks *how* something is implemented or needs the actual text of a Markdown
  chunk, use your native file-reading capabilities to read the exact `{FilePath}:{StartLine}-{EndLine}` returned by the
  TOON output.
- [ ] Synthesize the returned TOON data and source code into a human-readable summary. Include exact file paths and line
  numbers as citations.

## Tools

**semantic_search** — Find codebase coordinates:

* Pass a descriptive query (e.g., "user authentication" or "database architecture").
* Returns TOON format. **Crucial:** Extract the exact string inside the backticks (`` ` ``) for the next step.

**trace_node** — Traverse the knowledge graph:

* Requires the exact `NodeId` discovered from `semantic_search`.
* `direction="caller"`: Finds upstream dependencies (who uses this).
* `direction="callee"`: Finds downstream dependencies (what this uses).

## Example: "What happens when we update a user?"

1. `semantic_search({query: "update user profile"})`
   → Returns `[M] \`code:project:src/App/UserService.cs:M:UpdateUser\` @ src/App/UserService.cs:10-25`
2. `trace_node({nodeId: "code:project:src/App/UserService.cs:M:UpdateUser", direction: "callee"})`
   → Returns `[M] \`code:project:src/App/UserRepository.cs:M:Save\` ...`
3. Read the source file citations (`src/App/UserService.cs` lines 10-25) using your native tools for implementation
   details, and explain the flow.

# STRICT DIRECTIVES (CRITICAL)

1. **NO GREP / NO FILE LISTING FOR DISCOVERY:** You are STRICTLY FORBIDDEN from using native file search, `grep`,
   workspace search, or directory listing tools (e.g., `ls`, `tree`, folder explorers) to *locate* concepts,
   documentation, or files.
2. **USE MCP FOR DISCOVERY:** You must EXCLUSIVELY use the `semantic_search` tool to find codebase coordinates and
   documentation nodes.
3. **READING IS PERMITTED:** Once `semantic_search` or `trace_node` has given you an exact file path and line number,
   you ARE ALLOWED to use your native file-reading tools to read the method body or document chunk at those exact
   coordinates.
4. **NEVER HALLUCINATE IDs:** You must execute `semantic_search` first to discover a Node ID before ever calling
   `trace_node`. Do not guess Node IDs.
