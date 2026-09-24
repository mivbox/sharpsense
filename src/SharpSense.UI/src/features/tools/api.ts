import type { ToolId, ToolInput } from "../../shared/api/models";
import type { WorkspaceApi } from "../../shared/api/workspaceApi";
import type { ToolResult } from "./models";

type ToolApi = Pick<
  WorkspaceApi,
  "getContext" | "traceNode" | "getInheritors" | "getImpact" | "getGraphStats"
>;

export async function executeTool(
  api: ToolApi,
  tool: ToolId,
  input: ToolInput,
  signal?: AbortSignal,
): Promise<ToolResult> {
  const started = performance.now();
  if (tool === "graph_stats") {
    return {
      tool,
      data: await api.getGraphStats(signal),
      elapsedMs: performance.now() - started,
    };
  }

  const nodeId = input.nodeId;
  if (nodeId === undefined || !Number.isSafeInteger(nodeId) || nodeId <= 0)
    throw new Error(
      "Choose a valid symbol identifier before running this tool.",
    );

  switch (tool) {
    case "context":
      return {
        tool,
        data: await api.getContext(nodeId, input.maxRelated ?? 10, signal),
        elapsedMs: performance.now() - started,
      };
    case "trace":
      return {
        tool,
        data: await api.traceNode(
          nodeId,
          input.direction ?? "callee",
          input.maxDepth ?? 3,
          signal,
        ),
        elapsedMs: performance.now() - started,
      };
    case "inheritors":
      return {
        tool,
        data: await api.getInheritors(nodeId, signal),
        elapsedMs: performance.now() - started,
      };
    case "impact":
      return {
        tool,
        data: await api.getImpact(nodeId, input.maxDepth ?? 3, signal),
        elapsedMs: performance.now() - started,
      };
  }
}
