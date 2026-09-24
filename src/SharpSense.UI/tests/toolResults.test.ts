import assert from "node:assert/strict";
import test from "node:test";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { executeTool } from "../src/features/tools/api";
import type { ToolResult } from "../src/features/tools/models";
import {
  contextOverview,
  impactOverview,
  inheritorsOverview,
  traceOverview,
} from "../src/features/tools/resultPresentation";
import { ToolResultView } from "../src/features/tools/ToolResultView";
import type {
  CodeNodeResult,
  Context360Result,
  GraphStatsSnapshot,
  ImpactAnalysisResult,
  TraceResponse,
} from "../src/shared/api/generated/models";

test("context results distinguish graph identifiers from valid code tool targets", () => {
  const data: Context360Result = {
    targetNode: { id: 7, name: "Service.Run" },
    callers: [{ id: 501, codeNodeId: 17, name: "Caller" }],
    callees: null,
    parents: [
      { id: 502, codeNodeId: null, name: "Project" },
      { id: 503, codeNodeId: 18, name: "Containing class" },
    ],
    children: [{ id: 504, codeNodeId: -1, name: null }],
  };
  const overview = contextOverview(data);
  assert.equal(overview.targetName, "Service.Run");
  assert.deepEqual(
    overview.groups.map((group) => group.title),
    [
      "Callers",
      "Callees",
      "Implementations",
      "Inherits",
      "Parents",
      "Children",
    ],
  );
  assert.deepEqual(overview.groups[0]?.items[0], {
    id: 501,
    targetId: 17,
    name: "Caller",
  });
  assert.equal(overview.groups[4]?.items[0]?.targetId, undefined);
  assert.equal(overview.groups[4]?.items[1]?.targetId, 18);
  assert.equal(overview.groups[5]?.items[0]?.targetId, undefined);
  assert.equal(overview.groups[5]?.items[0]?.name, "Unnamed symbol");

  const markup = renderResult({ tool: "context", data, elapsedMs: 1 });
  const projectButton = markup.match(
    /<[^>]*aria-label="Use Project as tool target"[^>]*>/,
  )?.[0];
  const callerButton = markup.match(
    /<[^>]*aria-label="Use Caller as tool target"[^>]*>/,
  )?.[0];
  assert.ok(projectButton);
  assert.ok(callerButton);
  assert.match(projectButton, /aria-disabled="true"/);
  assert.doesNotMatch(callerButton, /aria-disabled/);
});

test("trace results retain truncation, dependency counts, names and source paths", () => {
  const data: TraceResponse = {
    root: { id: 7, fullyQualifiedName: "Service.Run" },
    nodes: [
      {
        id: 17,
        displayName: "Caller",
        fullyQualifiedName: "App.Caller",
        nodeType: "Method",
        relativeFilePath: "src/Caller.cs",
      },
      {
        id: Number.MAX_SAFE_INTEGER + 1,
        fullyQualifiedName: "Invalid identifier",
      },
    ],
    truncated: true,
    dependencies: [
      {
        callerId: "App.Caller",
        calleeId: "Service.Run",
        edgeType: "MethodCall",
      },
    ],
  };
  const overview = traceOverview(data);
  assert.equal(overview.targetName, "Service.Run");
  assert.equal(overview.truncated, true);
  assert.equal(overview.dependencyCount, 1);
  assert.deepEqual(overview.groups[0]?.items[0], {
    id: 17,
    targetId: 17,
    name: "Caller",
    path: "src/Caller.cs",
    kind: "Method",
  });
  assert.equal(overview.groups[0]?.items[1]?.targetId, undefined);
  const markup = renderResult({ tool: "trace", data, elapsedMs: 2 });
  assert.match(markup, /The trace reached its result limit/);
  assert.match(markup, /dependency relationships returned/);
  assert.match(markup, /src\/Caller.cs/);
});

test("impact and inheritors use their generated response fields and tolerate optional metadata", () => {
  const impact: ImpactAnalysisResult = {
    targetSymbol: "App.Service",
    impactedNodes: [
      { id: 19, fullyQualifiedName: "App.Consumer", relativeFilePath: null },
    ],
    dependencies: [],
  };
  const overview = impactOverview(impact);
  assert.equal(overview.targetName, "App.Service");
  assert.equal(overview.groups[0]?.items[0]?.name, "App.Consumer");
  assert.equal(overview.groups[0]?.items[0]?.targetId, 19);
  assert.equal(overview.dependencyCount, 0);
  assert.match(
    renderResult({ tool: "impact", data: impact, elapsedMs: 1 }),
    /Impacted symbols/,
  );

  const inheritors = inheritorsOverview([
    { id: 20, fullyQualifiedName: "App.Derived" },
    {},
  ]);
  assert.equal(inheritors.groups[0]?.items[0]?.targetId, 20);
  assert.equal(inheritors.groups[0]?.items[1]?.name, "Unnamed symbol");
  assert.equal(inheritors.groups[0]?.items[1]?.targetId, undefined);
  assert.match(
    renderResult({ tool: "inheritors", data: [], elapsedMs: 1 }),
    /No inheriting symbols/,
  );
  assert.ok(
    contextOverview({}).groups.every((group) => group.items.length === 0),
  );
  assert.equal(traceOverview({}).dependencyCount, undefined);
  assert.deepEqual(impactOverview({}).groups[0]?.items, []);
});

test("result discriminant controls presentation and labels remain escaped", () => {
  const markup = renderToStaticMarkup(
    createElement(ToolResultView, {
      tool: "trace",
      result: {
        tool: "context",
        data: { targetNode: { name: "<script>alert(1)</script>" } },
        elapsedMs: 3,
      },
      loading: false,
      onInspect: () => {},
    }),
  );
  assert.match(markup, /Callers/);
  assert.doesNotMatch(markup, /Traced symbols|<script>/);
  assert.match(markup, /&lt;script&gt;/);
});

test("feature execution preserves typed responses, tool defaults and cancellation signals", async () => {
  const signal = new AbortController().signal;
  const context: Context360Result = { targetNode: { id: 7, name: "Root" } };
  const trace: TraceResponse = { root: { id: 7 }, nodes: [] };
  const inheritors: CodeNodeResult[] = [{ id: 8, displayName: "Derived" }];
  const impact: ImpactAnalysisResult = {
    targetSymbol: "Root",
    impactedNodes: [],
  };
  const stats: GraphStatsSnapshot = { codeNodeCount: 5 };
  const api = {
    getContext: async (
      nodeId: number,
      maxRelated: number,
      actualSignal?: AbortSignal,
    ) => {
      assert.equal(nodeId, 7);
      assert.equal(maxRelated, 10);
      assert.equal(actualSignal, signal);
      return context;
    },
    traceNode: async (
      nodeId: number,
      direction: "caller" | "callee",
      maxDepth: number,
      actualSignal?: AbortSignal,
    ) => {
      assert.equal(nodeId, 7);
      assert.equal(direction, "callee");
      assert.equal(maxDepth, 3);
      assert.equal(actualSignal, signal);
      return trace;
    },
    getInheritors: async (nodeId: number, actualSignal?: AbortSignal) => {
      assert.equal(nodeId, 7);
      assert.equal(actualSignal, signal);
      return inheritors;
    },
    getImpact: async (
      nodeId: number,
      maxDepth: number,
      actualSignal?: AbortSignal,
    ) => {
      assert.equal(nodeId, 7);
      assert.equal(maxDepth, 3);
      assert.equal(actualSignal, signal);
      return impact;
    },
    getGraphStats: async (actualSignal?: AbortSignal) => {
      assert.equal(actualSignal, signal);
      return stats;
    },
  };
  const responses = { context, trace, inheritors, impact, graph_stats: stats };
  for (const tool of [
    "context",
    "trace",
    "inheritors",
    "impact",
    "graph_stats",
  ] as const) {
    const result = await executeTool(
      api,
      tool,
      tool === "graph_stats" ? {} : { nodeId: 7 },
      signal,
    );
    assert.equal(result.tool, tool);
    assert.equal(result.data, responses[tool]);
    assert.ok(result.elapsedMs >= 0);
  }
  for (const nodeId of [
    undefined,
    0,
    -1,
    1.5,
    Number.NaN,
    Number.MAX_SAFE_INTEGER + 1,
  ]) {
    await assert.rejects(
      executeTool(api, "context", { nodeId }, signal),
      /valid symbol identifier/,
    );
  }
});

function renderResult(result: ToolResult): string {
  return renderToStaticMarkup(
    createElement(ToolResultView, {
      tool: result.tool,
      result,
      loading: false,
      onInspect: () => {},
    }),
  );
}

test("large typed results render one page while retaining every result", () => {
  const impactedNodes = Array.from({ length: 5_000 }, (_, index) => ({
    id: index + 1,
    displayName: `Symbol${index + 1}`,
    fullyQualifiedName: `Fixture.Symbol${index + 1}`,
    nodeType: "Method" as const,
  }));
  const result = {
    tool: "impact",
    data: { targetSymbol: "Fixture.Target", impactedNodes, dependencies: [] },
    elapsedMs: 1,
  } satisfies ToolResult;
  const markup = renderResult(result);
  assert.equal((markup.match(/as tool target"/g) ?? []).length, 50);
  assert.match(markup, /Use Symbol50 as tool target/);
  assert.doesNotMatch(markup, /Use Symbol51 as tool target/);
  assert.match(markup, /Impacted symbols pages/);
  assert.match(markup, /Go to page 100/);
  assert.equal(result.data.impactedNodes?.length, 5_000);
});
