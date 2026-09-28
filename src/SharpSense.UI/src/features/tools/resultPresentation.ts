import type {
  CodeNodeResult,
  Context360RelatedNode,
  Context360Result,
  ImpactAnalysisResult,
  ImpactedCodeNode,
  TraceResponse,
} from "../../shared/api/generated/models";

type ResultItem = {
  id?: number;
  targetId?: number;
  name: string;
  path?: string;
  kind?: string;
};

export type SymbolOverview = {
  targetName?: string;
  groups: { title: string; items: ResultItem[] }[];
  emptyMessage: string;
  truncated?: boolean;
  dependencyCount?: number;
};

function validId(value: number | null | undefined): number | undefined {
  return value != null && Number.isSafeInteger(value) && value > 0
    ? value
    : undefined;
}

function contextItem(node: Context360RelatedNode): ResultItem {
  return {
    id: validId(node.id),
    // Graph parents can be projects; only codeNodeId identifies a tool target.
    targetId: validId(node.codeNodeId),
    name: node.name || "Unnamed symbol",
  };
}

function codeItem(node: CodeNodeResult | ImpactedCodeNode): ResultItem {
  return {
    id: validId(node.id),
    targetId: validId(node.id),
    name: node.displayName || node.fullyQualifiedName || "Unnamed symbol",
    path: node.relativeFilePath ?? undefined,
    kind: node.nodeType ?? undefined,
  };
}

const noRelatedSymbols = "No related symbols were found for these parameters.";

export function contextOverview(data: Context360Result): SymbolOverview {
  return {
    targetName: data.targetNode?.name ?? undefined,
    groups: [
      { title: "Callers", items: (data.callers ?? []).map(contextItem) },
      { title: "Callees", items: (data.callees ?? []).map(contextItem) },
      {
        title: "Implementations",
        items: (data.implementers ?? []).map(contextItem),
      },
      { title: "Inherits", items: (data.inherits ?? []).map(contextItem) },
      { title: "Parents", items: (data.parents ?? []).map(contextItem) },
      { title: "Children", items: (data.children ?? []).map(contextItem) },
    ],
    emptyMessage: noRelatedSymbols,
  };
}

export function traceOverview(data: TraceResponse): SymbolOverview {
  return {
    targetName:
      data.root?.fullyQualifiedName || data.root?.displayName || undefined,
    groups: [
      { title: "Traced symbols", items: (data.nodes ?? []).map(codeItem) },
    ],
    emptyMessage: noRelatedSymbols,
    truncated: data.truncated === true,
    dependencyCount: data.dependencies?.length,
  };
}

export function inheritorsOverview(data: CodeNodeResult[]): SymbolOverview {
  return {
    groups: [{ title: "Inheriting symbols", items: data.map(codeItem) }],
    emptyMessage: "No inheriting symbols were found in this workspace.",
  };
}

export function impactOverview(data: ImpactAnalysisResult): SymbolOverview {
  return {
    targetName: data.targetSymbol ?? undefined,
    groups: [
      {
        title: "Impacted symbols",
        items: (data.impactedNodes ?? []).map(codeItem),
      },
    ],
    emptyMessage: noRelatedSymbols,
    dependencyCount: data.dependencies?.length,
  };
}
