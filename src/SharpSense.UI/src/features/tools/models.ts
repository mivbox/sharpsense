import type {
  CodeNodeResult,
  Context360Result,
  GraphStatsSnapshot,
  ImpactAnalysisResult,
  TraceResponse,
} from "../../shared/api/generated/models";
import type { ToolId } from "../../shared/api/models";

type ToolResponses = {
  context: Context360Result;
  trace: TraceResponse;
  inheritors: CodeNodeResult[];
  impact: ImpactAnalysisResult;
  graph_stats: GraphStatsSnapshot;
};

export type ToolResult = {
  [Tool in ToolId]: {
    tool: Tool;
    data: ToolResponses[Tool];
    elapsedMs: number;
  };
}[ToolId];
