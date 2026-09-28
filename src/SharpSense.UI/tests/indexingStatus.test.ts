import assert from "node:assert/strict";
import test from "node:test";
import { QueryClient } from "@tanstack/react-query";
import type { WorkspaceIndexingStatus } from "../src/shared/api/generated/models";
import {
  IndexingStatusOrder,
  elapsedTime,
  indexingProgress,
} from "../src/features/workspaces/indexingStatus";
import {
  parseIndexingEvent,
  subscribeToIndexing,
} from "../src/features/workspaces/indexingEvents";

const workspaceId = "030d4805-30f0-4416-a2aa-1a3cb8d29771";
const streamId = "a40c5309-3be2-4d9e-9b2c-12d31fbe9783";
const startedAt = new Date("2026-09-24T23:00:00Z");
const snapshot = (
  sequence: number,
  overrides: Partial<WorkspaceIndexingStatus> = {},
): WorkspaceIndexingStatus => ({
  workspaceId,
  streamId,
  sequence,
  state: "indexing",
  revision: 0,
  updatedAt: new Date(startedAt.getTime() + sequence * 1000),
  ...overrides,
});

test("progress ordering survives job changes and rejects late HTTP responses", async () => {
  const order = new IndexingStatusOrder(workspaceId);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const key = ["indexing", workspaceId];
  let resolveRequest: (value: WorkspaceIndexingStatus) => void = () => {};
  const request = client.fetchQuery({
    queryKey: key,
    queryFn: () =>
      new Promise<WorkspaceIndexingStatus>((resolve) => {
        resolveRequest = resolve;
      }),
    structuralSharing: (_old, next) =>
      order.accept(next as WorkspaceIndexingStatus),
  });
  const streamed = snapshot(8, {
    jobId: "07d08f9c-ed60-4a08-8f80-869fb73783a5",
    revision: 2,
  });
  client.setQueryData(key, order.accept(streamed, true));
  resolveRequest(
    snapshot(2, { jobId: "072bf3a7-f593-4186-a9f5-ec8f33f3b4bd" }),
  );
  await request;
  assert.equal(client.getQueryData(key), streamed);
  assert.equal(order.accept(snapshot(7, { state: "completed" })), streamed);
  assert.equal(
    order.accept(snapshot(8, { message: "Duplicate event" })),
    streamed,
  );
  assert.equal(order.accept(snapshot(9))?.sequence, 9);
  client.clear();
});

test("reconnect establishes server lifetime and ignores retired server responses", () => {
  const order = new IndexingStatusOrder(workspaceId);
  order.accept(snapshot(50));
  const restarted = snapshot(1, {
    streamId: "6a47f45c-fa3f-4b67-a38c-4c1c5f9d488e",
  });
  assert.equal(
    order.accept(restarted, true),
    restarted,
    "Authoritative snapshot tolerates restarted clock and sequence.",
  );
  assert.equal(order.accept(snapshot(100)), restarted);
  assert.equal(order.accept(snapshot(101), true), restarted);
  assert.equal(
    order.accept(
      snapshot(200, { workspaceId: "79fcb288-ddac-47d9-8880-e459179b984f" }),
      true,
    ),
    restarted,
  );
});

test("polling can discover new server while streaming is unavailable", () => {
  const order = new IndexingStatusOrder(workspaceId);
  order.accept(snapshot(5));
  const newerServer = snapshot(0, {
    streamId: "6a47f45c-fa3f-4b67-a38c-4c1c5f9d488e",
    updatedAt: new Date(startedAt.getTime() + 60_000),
  });
  assert.equal(order.accept(newerServer), newerServer);
  assert.equal(order.accept(snapshot(100)), newerServer);
});

test("SSE uses generated Kiota deserialization for nested status and dates", () => {
  const data = parseIndexingEvent(
    JSON.stringify(
      snapshot(1, {
        analysis: {
          operationId: "5629471c-1d4f-4a8b-a026-0898cc9dabbb",
          sequence: 3,
          operationKind: "Incremental",
          state: "running",
          phase: "Extraction",
          startedAt,
          updatedAt: startedAt,
          sources: [
            {
              kind: "TypeScript",
              path: "web/tsconfig.json",
              state: "running",
              completedItems: 2,
              totalItems: 4,
            },
          ],
        },
      }),
    ),
    workspaceId,
  );
  assert.ok(data.updatedAt instanceof Date);
  assert.ok(data.analysis?.startedAt instanceof Date);
  assert.equal(data.analysis?.sources?.[0]?.kind, "TypeScript");
  assert.equal(data.analysis?.sources?.[0]?.totalItems, 4);
  assert.throws(() =>
    parseIndexingEvent(JSON.stringify(snapshot(1)), "another-workspace"),
  );
  assert.throws(() =>
    parseIndexingEvent('{"workspaceId": "bad"}', workspaceId),
  );
  assert.throws(() =>
    parseIndexingEvent(
      JSON.stringify(snapshot(1, { updatedAt: undefined })),
      workspaceId,
    ),
  );
});

test("subscription waits for snapshot, handles malformed data, and closes on disposal", () => {
  class Source extends EventTarget {
    closed = false;
    close() {
      this.closed = true;
    }
  }
  const source = new Source();
  const received: WorkspaceIndexingStatus[] = [];
  const connections: boolean[] = [];
  const dispose = subscribeToIndexing(
    "/events",
    workspaceId,
    (status) => received.push(status),
    (connected) => connections.push(connected),
    () => source as unknown as EventSource,
  );
  source.dispatchEvent(new Event("open"));
  assert.deepEqual(connections, []);
  source.dispatchEvent(
    new MessageEvent("status", { data: JSON.stringify(snapshot(1)) }),
  );
  source.dispatchEvent(new MessageEvent("heartbeat", { data: "ignored" }));
  source.dispatchEvent(new Event("error"));
  source.dispatchEvent(new MessageEvent("status", { data: "malformed" }));
  source.dispatchEvent(
    new MessageEvent("status", { data: JSON.stringify(snapshot(2)) }),
  );
  assert.deepEqual(connections, [true, false, false, true]);
  assert.deepEqual(
    received.map((item) => item.sequence),
    [1, 2],
  );
  dispose();
  source.dispatchEvent(
    new MessageEvent("status", { data: JSON.stringify(snapshot(3)) }),
  );
  source.dispatchEvent(new Event("error"));
  assert.equal(source.closed, true);
  assert.equal(received.length, 2);
  assert.equal(connections.length, 4);
});

test("progress represents known denominators and elapsed time remains bounded", () => {
  assert.equal(indexingProgress(2, 4), 50);
  assert.equal(indexingProgress(0, 0), undefined);
  assert.equal(indexingProgress(2, undefined), undefined);
  assert.equal(indexingProgress(6, 4), 100);
  assert.equal(indexingProgress(-1, 4), 0);
  assert.equal(indexingProgress(NaN, 4), undefined);
  assert.equal(
    elapsedTime(startedAt, new Date(startedAt.getTime() + 75_000)),
    "1m 15s",
  );
  assert.equal(
    elapsedTime(startedAt, new Date(startedAt.getTime() - 1000)),
    "0s",
  );
});

test("closed HTTP handshakes retry without duplicating native retries or surviving disposal", (context) => {
  context.mock.timers.enable({ apis: ["setTimeout"] });
  class Source extends EventTarget {
    readyState = 0;
    close() {
      this.readyState = 2;
    }
  }
  const sources: Source[] = [];
  const dispose = subscribeToIndexing(
    "/events",
    workspaceId,
    () => {},
    () => {},
    () => {
      const source = new Source();
      sources.push(source);
      return source as unknown as EventSource;
    },
  );
  sources[0]!.dispatchEvent(new Event("error"));
  context.mock.timers.tick(3000);
  assert.equal(
    sources.length,
    1,
    "Connecting EventSource owns its native retry.",
  );
  sources[0]!.readyState = 2;
  sources[0]!.dispatchEvent(new Event("error"));
  context.mock.timers.tick(3000);
  assert.equal(
    sources.length,
    2,
    "Closed handshake requires a new EventSource.",
  );
  sources[1]!.readyState = 2;
  sources[1]!.dispatchEvent(new Event("error"));
  dispose();
  context.mock.timers.tick(3000);
  assert.equal(
    sources.length,
    2,
    "Workspace disposal cancels scheduled retry.",
  );
});
