import assert from "node:assert/strict";
import test from "node:test";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { validateToolsSearch } from "../src/app/searchState";
import { GraphStatsView } from "../src/features/diagnostics/GraphStatsView";
import {
  formatDuration,
  formatTimestamp,
  indexState,
} from "../src/features/diagnostics/presentation";
import { ToolResultView } from "../src/features/tools/ToolResultView";

test("existing indexes without run history keep their successful indexing time unknown", () => {
  const stats = { databaseState: "ready", isIndexed: true };
  assert.equal(indexState(stats).severity, "info");
  assert.match(indexState(stats).detail, /unknown/);
  const markup = renderToStaticMarkup(createElement(GraphStatsView, { stats }));
  assert.match(markup, /Last successful index/);
  assert.match(markup, /Unknown/);
  assert.doesNotMatch(markup, /Completed|Succeeded|watcher/i);
});

test("failed updates warn that retained graph data may be older and preserve the previous success time", () => {
  const completedAt = new Date("2026-09-23T08:30:00Z");
  const stats = {
    databaseState: "ready",
    isIndexed: true,
    lastSuccessfulIndex: { completedAt, outcome: "succeeded" },
    lastAttempt: {
      completedAt: new Date("2026-09-24T08:30:00Z"),
      outcome: "failed",
      kind: "incremental",
      diagnostics: [
        {
          severity: "error",
          filePath: "src/broken.ts",
          message: "Could not parse this file.",
          suggestion: "Fix the syntax error and analyze again.",
        },
      ],
    },
  };
  assert.equal(indexState(stats).severity, "error");
  const markup = renderToStaticMarkup(createElement(GraphStatsView, { stats }));
  assert.ok(markup.includes(completedAt.toLocaleString()));
  assert.match(markup, /may not reflect your latest changes/);
  assert.match(markup, /src\/broken.ts/);
  assert.match(markup, /Fix the syntax error/);
});

test("missing databases do not imply successful indexing or show zero coverage", () => {
  const stats = { databaseState: "missing", isIndexed: false };
  const markup = renderToStaticMarkup(createElement(GraphStatsView, { stats }));
  assert.match(markup, /No index yet/);
  assert.doesNotMatch(markup, /Embedding coverage|Succeeded/);
});

test("graph statistics links need no node and render metrics instead of symbol results", () => {
  const search = validateToolsSearch({ tool: "graph_stats" });
  assert.equal(search.tool, "graph_stats");
  assert.equal(search.nodeId, undefined);
  const markup = renderToStaticMarkup(
    createElement(ToolResultView, {
      tool: "graph_stats",
      result: {
        tool: "graph_stats",
        elapsedMs: 5,
        data: {
          databaseState: "ready",
          isIndexed: true,
          codeNodeCount: 12,
          embeddedNodeCount: 6,
          memoryCount: 4,
        },
      },
      loading: false,
      onInspect: () => assert.fail("Statistics must not select a symbol"),
    }),
  );
  assert.match(markup, /Graph nodes/);
  assert.match(markup, /Memories/);
  assert.match(markup, /50.0%/);
  assert.doesNotMatch(markup, /No related symbols/);
});

test("unavailable timestamps and durations stay unknown", () => {
  assert.equal(formatTimestamp(undefined), "Unknown");
  assert.equal(formatTimestamp(new Date("invalid")), "Unknown");
  assert.equal(formatDuration(undefined), "Unknown");
  assert.equal(formatDuration(-1), "Unknown");
  assert.equal(formatDuration(1500), "1.5 s");
  assert.equal(formatDuration(119_999), "2 min 0 s");
});
