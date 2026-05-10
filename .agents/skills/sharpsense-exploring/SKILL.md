---
name: sharpsense-exploring
description: |
  Delegates architectural discovery and codebase exploration to a subagent to protect the main context window.
  Use when tracing execution flows, understanding project structure, or finding where logic lives.
  Examples: "How does X work?", "Show me the auth flow", "What calls this function?".
---

# Exploring Codebases via Subagent Delegation

## MANDATORY ROUTING

Do NOT perform deep architectural tracing, codebase exploration, or multiple `sharpsense` discovery tool calls in the
main thread.
When asked to explain a flow, find a component, or map out an unknown area of the codebase, you MUST spawn a subagent to
execute the graph discovery workflow.

## 1. Subagent Initialisation Protocol

When spawning the subagent, instruct it to use the SharpSense tools to map the architecture and strictly constrain its
output. Use this exact template for the task definition:

> "Task: [Explicitly state the user's goal, e.g. 'Map out the payment processing execution flows'].
> Tools: Use `sharpsense:semantic_search`, `sharpsense:context`, `sharpsense:trace_node`, and
`sharpsense:get_inheritors`. Do NOT read file contents.
> Output Constraint: You MUST compress your analysis into a terse, multi-path architectural map. Return ALL relevant
> execution branches discovered.
>
> Schema:
> `[Primary Path]: [Entry] -> [Node] -> [Exit]`
> `[Async/Event Path]: [Trigger] -> [Node] -> [Exit]`
> `[Error/Fallback Path]: [Node] -> [Handler]`
> `[File Targets]: [List of specific file paths WITH line numbers (e.g., path/to/file.cs:10-25)]`
> `[Summary]: [Terse, fragment-based description of the system's structural behavior].`"

## 2. Subagent Workflow (Internal Checklist)

*Instructions for the spawned subagent: Execute these steps internally to build the architectural map.*

1. `semantic_search({query: "<concept>"})` → Discover relevant nodes and extract the persisted ID.
2. `context({nodeId: <id>})` → Load immediate callers, callees, and hierarchy breadth.
3. `trace_node({nodeId: "<id>", direction: "callee"})` → Follow downstream execution flow.
4. `trace_node({nodeId: "<id>", direction: "caller"})` → Find upstream entry points if needed.
5. `get_inheritors({nodeId: <id>})` → Expand inheritance / implementation details.

*(Note: If the index is stale or missing the target area, run `sharpsense analyze <target>` via `ctx_execute` first).*

## 3. Parent Agent Re-Integration (The Read Phase)

When the subagent completes its traversal and returns the compressed multi-path map:

1. Accept the map exactly as provided to establish your architectural context.
2. **The Read Phase:** If implementation details, bug fixing, or code modifications are required, YOU (the main agent)
   must now use standard file reading tools to inspect the specific line spans listed in the subagent's `[File Targets]`
   block. Do not read the entire file if line numbers are provided.
3. Present the final findings, explanation, or proposed code edits to the user based on the combined graph logic and
   source code.

**Example of Expected Subagent Return Payload:**
`[Primary Path]: PaymentsController.Charge() -> StripeProcessor.ProcessPayment() -> TransactionRepo.Save().`
`[Error Path]: StripeProcessor.ProcessPayment() -> throws PaymentDeclinedException -> GlobalErrorHandler.Handle().`
`[File Targets]: src/Payments/StripeProcessor.cs:12-30, src/Data/TransactionRepo.cs:45-50`
`[Summary]: Controller validates DTO. Processor hits Stripe API. Repo persists to Postgres. Errors caught globally.`
