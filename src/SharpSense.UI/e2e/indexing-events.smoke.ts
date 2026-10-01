import { clickButton } from "./browserActions";
import assert from "node:assert/strict";
import {
  createServer,
  type IncomingMessage,
  type ServerResponse,
} from "node:http";
import { setTimeout as delay } from "node:timers/promises";
import type { Browser, Page } from "puppeteer";
import type { WorkspaceIndexingStatus } from "../src/shared/api/generated/models";
import { launchBrowser, startUiPreview } from "./browserFixture";

const workspaces = [
  {
    id: "0fb315a3-bae9-43f0-be3c-1b971cb3fef6",
    name: "Alpha",
    repositoryRoot: "/alpha",
    sources: [{ kind: "CSharp", path: "App.csproj" }],
  },
  {
    id: "e622fdb5-3adf-4605-9268-bf7f0a2c9271",
    name: "Beta",
    repositoryRoot: "/beta",
    sources: [{ kind: "Markdown", path: "docs/**/*.md" }],
  },
];
const alpha = workspaces[0]!;
const beta = workspaces[1]!;
const streamId = "f731a197-e06c-4bdb-8e37-5eb7298f9eac";
const snapshots = new Map<string, WorkspaceIndexingStatus>(
  workspaces.map((workspace) => [
    workspace.id,
    {
      workspaceId: workspace.id,
      streamId,
      sequence: 0,
      revision: 0,
      state: "idle",
      updatedAt: new Date(),
      diagnostics: [],
    },
  ]),
);
const subscribers = new Map<string, Set<ServerResponse>>();
const polls = new Map<string, number>();
const overviewReads = new Map<string, number>();
let holdFirstPoll = true;
let releaseFirstPoll: (() => void) | undefined;
let allowStreams = true;
const errors: string[] = [];
const api = createServer((request, response) => {
  void respond(request, response).catch((error: unknown) => {
    errors.push(String(error));
    response.writeHead(500).end();
  });
});
await new Promise<void>((resolve) => api.listen(0, "127.0.0.1", resolve));
const apiAddress = api.address();
assert.ok(apiAddress && typeof apiAddress === "object");
const server = await startUiPreview({
  proxy: { "/api": `http://127.0.0.1:${apiAddress.port}` },
});
let browser: Browser | undefined;

try {
  const { baseUrl } = server;
  browser = await launchBrowser();
  const page = await browser.newPage();
  page.setDefaultTimeout(15_000);
  await page.setViewport({ width: 1440, height: 1000 });
  page.on("pageerror", (error) => errors.push(String(error)));
  await page.goto(`${baseUrl}/search?workspace=${alpha.id}`);
  await waitUntil(
    () =>
      (subscribers.get(alpha.id)?.size ?? 0) === 1 && Boolean(releaseFirstPoll),
  );
  await clickButton(page, "Analyze & watch");
  await visibleText(page, "Reading source relationships");
  const progress = snapshots.get(alpha.id)!;
  update(alpha.id, {
    message: "Processing project symbols",
    analysis: { ...progress.analysis, completedItems: 12, totalItems: 24 },
  });
  await visibleText(page, "Processing project symbols");
  releaseFirstPoll?.();
  await delay(150);
  assert.ok(
    await hasText(page, "Processing project symbols"),
    "Stale initial HTTP must not replace streamed progress.",
  );
  assert.equal(
    (await page.$(
      '[aria-label="Reading source relationships"][aria-valuenow="50"]',
    )) !== null,
    true,
  );

  const pollsBefore = polls.get(alpha.id);
  const readsBefore = overviewReads.get(alpha.id) ?? 0;
  // More than active fallback interval: connected streams must suppress polling.
  await delay(1800);
  assert.equal(polls.get(alpha.id), pollsBefore);
  assert.equal(
    overviewReads.get(alpha.id),
    readsBefore,
    "Progress must not invalidate graph queries.",
  );

  update(alpha.id, {
    state: "watching",
    revision: 1,
    message: "Watching selected sources",
    analysis: {
      ...progress.analysis,
      state: "completed",
      completedAt: new Date(),
      sources: [{ kind: "CSharp", path: "App.csproj", state: "completed" }],
      summary: {
        nodes: 42,
        edges: 84,
        documents: 3,
        extractedSources: 1,
        reusedSources: 0,
      },
    },
  });
  await visibleText(page, "42 nodes");
  await waitUntil(() => (overviewReads.get(alpha.id) ?? 0) === readsBefore + 1);
  update(alpha.id, { message: "Watching for changes" });
  await visibleText(page, "Watching for changes");
  await delay(150);
  assert.equal(overviewReads.get(alpha.id), readsBefore + 1);

  allowStreams = false;
  for (const response of subscribers.get(alpha.id) ?? []) response.end();
  await visibleText(page, "Reconnecting to live updates");
  update(alpha.id, { message: "Status received by polling" });
  await visibleText(page, "Status received by polling");
  assert.ok((polls.get(alpha.id) ?? 0) > (pollsBefore ?? 0));
  allowStreams = true;
  await waitUntil(() => (subscribers.get(alpha.id)?.size ?? 0) === 1);
  await page.waitForFunction(
    () => !document.body.textContent?.includes("Reconnecting to live updates"),
  );
  update(alpha.id, { message: "Stream reconnected" });
  await visibleText(page, "Stream reconnected");

  const readsBeforeRestart = overviewReads.get(alpha.id) ?? 0;
  const beforeRestart = snapshots.get(alpha.id)!;
  const restarted = {
    ...beforeRestart,
    streamId: "330b234e-ff52-421d-ab74-dfdb9c6926ee",
    sequence: 0,
    revision: 1,
    updatedAt: new Date(),
    message: "Restarted server with committed graph",
  };
  snapshots.set(alpha.id, restarted);
  for (const response of subscribers.get(alpha.id) ?? [])
    send(response, restarted);
  await visibleText(page, restarted.message);
  await waitUntil(
    () => (overviewReads.get(alpha.id) ?? 0) === readsBeforeRestart + 1,
  );
  for (const response of subscribers.get(alpha.id) ?? [])
    send(response, {
      ...beforeRestart,
      sequence: 100,
      message: "Retired server event",
    });
  await delay(150);
  assert.equal(await hasText(page, "Retired server event"), false);
  assert.equal(overviewReads.get(alpha.id), readsBeforeRestart + 1);

  await clickButton(page, "Stop");
  await visibleText(page, "Stopping analysis safely");
  await visibleText(page, "Analysis stopped");

  await page.locator('::-p-aria(Selected workspace[role="combobox"])').click();
  await page.locator('::-p-aria(Beta[role="option"])').click();
  await waitUntil(
    () =>
      (subscribers.get(beta.id)?.size ?? 0) === 1 &&
      (subscribers.get(alpha.id)?.size ?? 0) === 0,
  );
  update(alpha.id, { message: "Old workspace late event" });
  update(beta.id, { message: "Beta live status" });
  await visibleText(page, "Beta live status");
  assert.equal(await hasText(page, "Old workspace late event"), false);
  await clickButton(page, "Manage workspaces");
  await waitUntil(() =>
    [...subscribers.values()].every((items) => items.size === 0),
  );
  assert.deepEqual(errors, []);
  console.log(
    "Native SSE: initial snapshot, stale HTTP ordering, commit-only invalidation, polling fallback, reconnect, stopping, workspace isolation and disposal passed.",
  );
} finally {
  releaseFirstPoll?.();
  await browser?.close();
  for (const items of subscribers.values())
    for (const response of items) response.end();
  await server.close();
  api.closeAllConnections();
  await new Promise<void>((resolve, reject) =>
    api.close((error) => (error ? reject(error) : resolve())),
  );
}

async function respond(request: IncomingMessage, response: ServerResponse) {
  const url = new URL(request.url ?? "/", "http://fixture");
  if (url.pathname === "/api/workspaces") {
    json(response, { workspaces, initialWorkspaceId: null });
    return;
  }
  const match = /^\/api\/workspaces\/([^/]+)\/indexing(\/events)?$/.exec(
    url.pathname,
  );
  if (match) {
    const id = match[1]!;
    assert.ok(snapshots.has(id));
    if (match[2]) {
      if (!allowStreams) {
        response.writeHead(503).end();
        return;
      }
      response.writeHead(200, {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache",
      });
      response.write("retry: 1000\n\n");
      const items = subscribers.get(id) ?? new Set<ServerResponse>();
      subscribers.set(id, items);
      items.add(response);
      response.on("close", () => items.delete(response));
      send(response, snapshots.get(id)!);
    } else if (request.method === "POST") {
      update(id, {
        state: "indexing",
        watch: true,
        message: "Preparing workspace",
        startedAt: new Date(),
        jobId: "fb655609-6c50-45d1-bc5e-247ae9f786a3",
        analysis: {
          operationId: "5ade7bd7-fbe9-4f81-ae13-8f69c77d493a",
          operationKind: "Full",
          sequence: 1,
          state: "running",
          phase: "Extraction",
          startedAt: new Date(),
          updatedAt: new Date(),
          sources: [
            {
              kind: "CSharp",
              path: "App.csproj",
              state: "running",
              message: "Reading syntax trees",
            },
          ],
        },
      });
      json(response, snapshots.get(id));
    } else if (request.method === "DELETE") {
      update(id, { state: "stopping", message: "Stopping analysis safely" });
      json(response, snapshots.get(id));
      setTimeout(
        () =>
          update(id, {
            state: "stopped",
            message: "Analysis stopped",
            completedAt: new Date(),
          }),
        300,
      );
    } else {
      polls.set(id, (polls.get(id) ?? 0) + 1);
      const snapshot = snapshots.get(id);
      if (holdFirstPoll && id === alpha.id) {
        holdFirstPoll = false;
        await new Promise<void>((resolve) => {
          releaseFirstPoll = resolve;
        });
      }
      json(response, snapshot);
    }
    return;
  }
  const id = String(request.headers["x-sharpsense-workspace"] ?? "");
  const workspace = workspaces.find((item) => item.id === id);
  assert.ok(workspace, `Missing workspace header for ${url.pathname}`);
  if (url.pathname === "/api/overview") {
    overviewReads.set(id, (overviewReads.get(id) ?? 0) + 1);
    json(response, {
      name: workspace.name,
      workspaceId: id,
      indexed: true,
      nodeCount: 42,
      projectCount: 1,
      documentCount: 3,
    });
  } else if (url.pathname === "/api/tree") {
    json(response, { parentPath: "/", parentDirectoryId: null, nodes: [] });
  } else if (url.pathname === "/api/tools") {
    json(response, []);
  } else throw new Error(`Unexpected request ${url.pathname}`);
}

function update(workspaceId: string, change: Partial<WorkspaceIndexingStatus>) {
  const previous = snapshots.get(workspaceId)!;
  const next = {
    ...previous,
    ...change,
    sequence: (previous.sequence ?? 0) + 1,
    updatedAt: new Date(),
  };
  snapshots.set(workspaceId, next);
  for (const response of subscribers.get(workspaceId) ?? [])
    send(response, next);
}

function send(response: ServerResponse, status: WorkspaceIndexingStatus) {
  response.write(
    `id: ${status.streamId}:${status.sequence}\nevent: status\ndata: ${JSON.stringify(status)}\n\n`,
  );
}

function json(response: ServerResponse, value: unknown) {
  response.writeHead(200, { "Content-Type": "application/json" });
  response.end(JSON.stringify(value));
}

async function visibleText(page: Page, text: string) {
  await page.waitForFunction(
    (value) => document.body.textContent?.includes(value),
    {},
    text,
  );
}

async function hasText(page: Page, text: string) {
  return page.evaluate(
    (value) => document.body.textContent?.includes(value) ?? false,
    text,
  );
}

async function waitUntil(predicate: () => boolean) {
  const deadline = Date.now() + 15_000;
  while (Date.now() < deadline) {
    if (predicate()) return;
    await delay(50);
  }
  throw new Error("Timed out waiting for SSE fixture state.");
}
