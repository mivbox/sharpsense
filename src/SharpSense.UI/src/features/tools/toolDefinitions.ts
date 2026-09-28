import type { ToolId } from "../../shared/api/models";
export const toolDefinitions: {
  id: ToolId;
  label: string;
  description: string;
  detail: string;
}[] = [
  {
    id: "graph_stats",
    label: "Graph statistics",
    description: "Check the index behind your queries.",
    detail:
      "Inspect graph size, languages, memories, embedding coverage, and the latest indexing outcome. This tool reads the whole workspace and needs no symbol.",
  },
  {
    id: "context",
    label: "Context",
    description: "Understand a symbol’s neighborhood.",
    detail:
      "See the callers, callees, parents, and implementations directly connected to a symbol.",
  },
  {
    id: "trace",
    label: "Call trace",
    description: "Follow an execution path.",
    detail:
      "Follow calls outward from a symbol or trace back through the code that invokes it.",
  },
  {
    id: "inheritors",
    label: "Inheritors",
    description: "Explore implementations and subclasses.",
    detail:
      "Find classes that implement an interface or inherit from a base class.",
  },
  {
    id: "impact",
    label: "Impact analysis",
    description: "Understand the reach of a change.",
    detail:
      "Inspect symbols that depend on a target before changing its contract or behavior.",
  },
];
