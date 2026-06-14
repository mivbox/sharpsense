name: sharpsense-exploring
description: |
Delegates architectural discovery and codebase exploration to a subagent to protect the main context window.
Use when tracing execution flows, understanding project structure, finding where logic lives, or retrieving
node-specific historical context.
Examples: "How does X work?", "Show me the auth flow", "What calls this function?", "Why is this implemented this way?".
---

# Exploring Codebases via Subagent Delegation

## MANDATORY ROUTING

Do NOT perform deep architectural tracing, codebase exploration, or multiple `sharpsense` discovery tool calls in the
main thread.
When asked to explain a flow, find a component, or map out an unknown area of the codebase, you MUST spawn a subagent to
execute the graph discovery workflow.

## 1. Subagent Initialisation Protocol

When spawning the subagent, instruct it to use the SharpSense tools to map the architecture, retrieve graph-native
memories, and strictly constrain its output. Use this exact template for the task definition:

> "
> Task: [Explicitly state the user's goal, e.g. 'Map out the payment processing execution flows and retrieve any historical constraints'].
> Tools: Use `sharpsense:semantic_search`, `sharpsense:context`, `sharpsense:trace_node`, `sharpsense:get_inheritors`,
> and `sharpsense:get_memory`. Do NOT read file contents.
> Output Constraint: You MUST compress your analysis into a terse, multi-path architectural map. Return ALL relevant
> execution branches and crucial memory context.
>
> Schema:
> `[Primary Path]: [Entry] -> [Node] -> [Exit]`
> `[Async/Event Path]: [Trigger] -> [Node] -> [Exit]`
> `[Error/Fallback Path]: [Node] -> [Handler]`
> `[Active Memories]: [Terse summary of insights retrieved via get_memory. Flag any that are marked IsStale: true].`
> `[File Targets]: [List of specific file paths WITH line numbers (e.g., path/to/file.cs:10-25)]`
> `[Summary]: [Terse, fragment-based description of the system's structural behavior].`"

## 2. Subagent Workflow (Internal Checklist)

*Instructions for the spawned subagent: Execute these steps internally to build the architectural map.*

1. `semantic_search({query: "<concept>"})` → Discover relevant nodes and extract the persisted ID.
2. `context({nodeId: <id>})` → Load immediate callers, callees, and hierarchy breadth. Note any attached memory IDs.
3. `trace_node({nodeId: "<id>", direction: "callee"})` → Follow downstream execution flow.
4. `trace_node({nodeId: "<id>", direction: "caller"})` → Find upstream entry points if needed.
5. `get_inheritors({nodeId: <id>})` → Expand inheritance / implementation details.
6. **[Memory Retrieval]**: If `context` or `trace` surfaces memory IDs (shown as `id + tags + stale`), call
   `get_memory(memoryId)` to read the full markdown content for relevant tags.

*(Note: If the index is missing the target area, run `sharpsense analyze <target>` via `ctx_execute` first).*

## 3. Parent Agent Re-Integration (The Read Phase)

When the subagent completes its traversal and returns the compressed payload:

1. Accept the map exactly as provided to establish your architectural context.
2. **The Read Phase:** If implementation details, bug fixing, or code modifications are required, YOU (the main agent)
   must now use standard file reading tools to inspect the specific line spans listed in the subagent's `[File Targets]`
   block.
3. **Verify Stale Memories:** If the subagent reported any `[Active Memories]` flagged as `IsStale: true`, you must read
   the associated source code to verify if the memory's claim is still accurate despite the code change.

## 4. Memory Lifecycle & Hygiene

Memories are graph-native, immutable context nodes used to prevent future rabbit holes. As the main agent, you are
responsible for maintaining them after reading the actual file contents:

* **Create:** If you confirm a non-obvious behaviour, workaround, invariant, or convention in the code, call
  `attach_memory(nodeId, content, tags?)` to persist it to the graph.
* **Resolve Stale:** If you verified a stale memory and it is NO LONGER accurate, call `delete_memory(memoryId)`.
* **Update:** Memories are immutable. There is no update verb. To correct an existing or stale memory, you must
  `delete_memory(memoryId)` and then `attach_memory(...)` with the revised context.
