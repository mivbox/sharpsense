import { clickButton } from "./browserActions";
import assert from "node:assert/strict";
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import type { Browser, HTTPRequest, Page } from "puppeteer";
import { idleIndexingStatus } from "./indexingFixture";
import { launchBrowser, startUiPreview } from "./browserFixture";

const artifacts =
  process.env.SHARPSENSE_E2E_ARTIFACTS ??
  (await mkdtemp(path.join(tmpdir(), "sharpsense-inspector-artifacts-")));
await mkdir(artifacts, { recursive: true });
const workspaces = [
  {
    id: "10e9b807-289d-4f36-87f9-fecdb5cd0bea",
    name: "Inspector Alpha",
    repositoryRoot: "/fixture/alpha",
    sources: [{ kind: "CSharp", path: "Fixture.csproj" }],
  },
  {
    id: "20e9b807-289d-4f36-87f9-fecdb5cd0bea",
    name: "Inspector Beta",
    repositoryRoot: "/fixture/beta",
    sources: [{ kind: "CSharp", path: "Fixture.csproj" }],
  },
];
type Scenario =
  | "normal"
  | "conflict"
  | "fresh"
  | "cancel"
  | "isolation"
  | "missing"
  | "deleted";
let scenario: Scenario = "normal";
let held: HTTPRequest | undefined;
let releaseHeld: (() => void) | undefined;
const cancelled = new Set<HTTPRequest>();
const errors: string[] = [];
const unsafeLabel =
  "<img src=x onerror=window.__graphLabelExecuted=true> · List<T>";
const requests: { url: URL; workspace: string; scenario: Scenario }[] = [];
const server = await startUiPreview();
let browser: Browser | undefined;
let page: Page | undefined;

try {
  const { baseUrl } = server;
  browser = await launchBrowser({
    args: [
      "--enable-unsafe-swiftshader",
      ...(process.env.CI ? ["--no-sandbox", "--disable-dev-shm-usage"] : []),
    ],
  });
  page = await browser.newPage();
  page.setDefaultTimeout(15_000);
  await page.setViewport({ width: 1600, height: 1050 });
  page.on("pageerror", (error) => errors.push(String(error)));
  page.on("requestfailed", (request) => cancelled.add(request));
  await page.setRequestInterception(true);
  page.on("request", (request) => {
    void respond(request).catch((error: unknown) => {
      if (!request.failure()) errors.push(String(error));
    });
  });
  const route = (selected = 42) => {
    const url = new URL("/explorer", baseUrl);
    url.searchParams.set("workspace", workspaces[0]!.id);
    url.searchParams.set("scopes", JSON.stringify(["/"]));
    url.searchParams.set("mode", "list");
    url.searchParams.set("selected", JSON.stringify(String(selected)));
    return url.href;
  };

  await page.goto(route());
  await waitForConnections(page, 20, false);
  assert.equal(
    await page.$eval(
      '[data-testid="toggle-edges-checkbox"] input',
      (input) => (input as HTMLInputElement).checked,
    ),
    false,
  );
  await page
    .locator('::-p-aria([name="Load more connections"][role="button"])')
    .click();
  await waitForConnections(page, 40, false);
  await page
    .locator('::-p-aria([name="Load more connections"][role="button"])')
    .click();
  await waitForConnections(page, 45, true);
  const escaped = await page.$eval(
    '[data-testid="node-connections"] [data-graph-node-id="143"]',
    (row) => ({
      text: row.textContent,
      elements: row.querySelectorAll("img,script,b").length,
    }),
  );
  assert.ok(
    escaped.text?.includes(unsafeLabel),
    "Real inspector must render symbol markup as literal text.",
  );
  assert.equal(escaped.elements, 0);
  assert.equal(
    await page.evaluate(
      () => (window as unknown as Record<string, unknown>).__graphLabelExecuted,
    ),
    undefined,
  );
  assert.equal(
    await page.$$eval(
      '[data-testid="node-connections"] [data-graph-node-id]',
      (rows) =>
        new Set(rows.map((row) => row.getAttribute("data-graph-node-id"))).size,
    ),
    45,
  );
  const initialRequests = requests.filter((item) =>
    item.url.pathname.endsWith("/42/connections"),
  );
  assert.equal(initialRequests.length, 3);
  assert.equal(
    initialRequests[0]!.url.searchParams.get("includeTotal"),
    "true",
  );
  assert.equal(
    initialRequests[1]!.url.searchParams.get("revision"),
    "alpha-normal",
  );
  const graphPageCount = requests.filter((item) =>
    item.url.pathname.endsWith("/nodes/page"),
  ).length;
  await page.screenshot({
    path: path.join(artifacts, "inspector-all-connections.png"),
    fullPage: true,
  });
  await page.click(
    '[data-testid="node-connections"] [data-graph-node-id="144"]',
  );
  await waitForSelected(page, 144, "Alpha.Peer44");
  assert.equal(
    requests.filter((item) => item.url.pathname.endsWith("/nodes/page")).length,
    graphPageCount,
    "Inspecting an unloaded peer must not broaden canvas scope.",
  );
  assert.equal(
    JSON.parse(new URL(page.url()).searchParams.get("selected") ?? "null"),
    "144",
  );
  await page.reload();
  await waitForSelected(page, 144, "Alpha.Peer44");
  await page.screenshot({
    path: path.join(artifacts, "inspector-unloaded-peer.png"),
    fullPage: true,
  });

  scenario = "conflict";
  await page.goto(route());
  await waitForConnections(page, 20, false);
  await page
    .locator('::-p-aria([name="Load more connections"][role="button"])')
    .click();
  await page.waitForFunction(() =>
    document
      .querySelector('[data-testid="node-connections"]')
      ?.textContent?.includes("Connection snapshot changed"),
  );
  await waitForConnections(page, 20, false);
  await delay(150);
  assert.equal(
    requests.filter(
      (item) =>
        item.scenario === "conflict" && item.url.searchParams.has("cursor"),
    ).length,
    1,
    "409 must retain partial connections without retrying stale cursor.",
  );
  scenario = "fresh";
  await page
    .locator(
      '[data-testid="node-connections"] ::-p-aria([name="Retry"][role="button"])',
    )
    .click();
  await waitForConnections(page, 20, false);
  await page.waitForFunction(
    () =>
      !document
        .querySelector('[data-testid="node-connections"]')
        ?.textContent?.includes("Connection snapshot changed"),
  );
  const fresh = requests.find(
    (item) =>
      item.scenario === "fresh" && item.url.pathname.endsWith("/connections"),
  );
  assert.ok(fresh);
  assert.equal(fresh.url.searchParams.has("cursor"), false);
  assert.equal(fresh.url.searchParams.has("revision"), false);

  scenario = "cancel";
  await page.goto(route());
  await waitForConnections(page, 20, false);
  await page
    .locator('::-p-aria([name="Load more connections"][role="button"])')
    .click();
  const abandonedNode = await waitForHeld();
  await page.click('[data-node-id="43"]');
  await waitForCancelled(abandonedNode);
  await waitForSelected(page, 43, "Alpha.Alternate");
  await waitForConnections(page, 0, true);

  scenario = "isolation";
  await page.goto(route());
  await waitForConnections(page, 20, false);
  await page
    .locator('::-p-aria([name="Load more connections"][role="button"])')
    .click();
  const abandonedWorkspace = await waitForHeld();
  await page.click('[role="combobox"]');
  await page
    .locator('::-p-aria([name="Inspector Beta"][role="option"])')
    .click();
  await waitForCancelled(abandonedWorkspace);
  await page.waitForFunction(
    (id) => new URL(window.location.href).searchParams.get("workspace") === id,
    {},
    workspaces[1]!.id,
  );
  await page.click('[data-tree-checkbox-trigger="/"]');
  await page.click('[data-testid="node-list-toggle"]');
  await page.click('[data-node-id="42"]');
  await waitForSelected(page, 42, "Beta.Target");
  await waitForConnections(page, 20, false);
  assert.equal(
    await page.$eval('[data-testid="node-inspector"]', (element) =>
      element.textContent?.includes("Alpha."),
    ),
    false,
    "Repeated numeric IDs must not reuse another workspace's inspector cache.",
  );

  scenario = "normal";
  await page.goto(route());
  await waitForConnections(page, 20, false);
  const associations = await page.$eval(
    '[data-testid="node-inspector"]',
    (element) =>
      [...element.querySelectorAll('[role="tab"]')].map((tab) => {
        const panel = document.getElementById(
          tab.getAttribute("aria-controls") ?? "",
        );
        return {
          linked: Boolean(
            tab.id && panel?.getAttribute("aria-labelledby") === tab.id,
          ),
          role: panel?.getAttribute("role"),
        };
      }),
  );
  assert.deepEqual(associations, [
    { linked: true, role: "tabpanel" },
    { linked: true, role: "tabpanel" },
  ]);
  scenario = "deleted";
  await clickButton(page, "Analyze");
  await page.waitForFunction(() =>
    document
      .querySelector('[data-testid="node-inspector"]')
      ?.textContent?.includes("No graph node exists for id 42"),
  );
  assert.equal(await page.$('[data-testid="selected-node-id"]'), null);
  assert.equal(await page.$('[data-testid="memories-tab"]'), null);
  assert.equal(
    await page.$eval('[data-testid="node-inspector"]', (element) =>
      [...element.querySelectorAll("button")].some((button) =>
        /Inspect context|Trace calls|Assess impact|Find inheritors/.test(
          button.textContent ?? "",
        ),
      ),
    ),
    false,
  );

  scenario = "missing";
  await page.goto(route(999));
  await page.waitForFunction(() =>
    document
      .querySelector('[data-testid="node-inspector"]')
      ?.textContent?.includes("No graph node exists for id 999"),
  );
  assert.equal(
    requests.some((item) => item.url.pathname === "/api/graph/edges/page"),
    false,
    "Inspector must never fetch whole-graph edges.",
  );
  assert.deepEqual(
    errors,
    [],
    "Inspector must not emit browser or request errors.",
  );
  console.log(
    "Inspector passed: 45 peers across 3 pages, edges off, unloaded-peer reload, revision retry, node/workspace cancellation and isolation, missing-node error.",
  );
  console.log("Browser artifacts: " + artifacts);
} catch (error) {
  if (page) {
    await page
      .screenshot({ path: path.join(artifacts, "failure.png"), fullPage: true })
      .catch(() => undefined);
    const details = await page
      .evaluate(() => ({
        url: window.location.href,
        body: document.body.innerText,
      }))
      .catch(() => undefined);
    await writeFile(
      path.join(artifacts, "failure.json"),
      JSON.stringify({ details, errors, requests }, null, 2),
    );
  }
  console.error("Inspector artifacts: " + artifacts);
  throw error;
} finally {
  releaseHeld?.();
  await browser?.close();
  await server.close();
}

function node(id: number, beta: boolean) {
  const prefix = beta ? "Beta" : "Alpha";
  return {
    id,
    codeNodeId: id,
    label:
      prefix +
      "." +
      (id === 42
        ? "Target"
        : id === 43
          ? "Alternate"
          : id === 143
            ? unsafeLabel
            : "Peer" + String(id - 100).padStart(2, "0")),
    type: "class",
    relativePath: id < 100 ? "src/Target.cs" : "outside/Peers.cs",
    projectId: null,
  };
}

async function respond(request: HTTPRequest) {
  const url = new URL(request.url());
  if (url.pathname.endsWith("/indexing/events")) {
    await request.respond({ status: 204 });
    return;
  }
  if (!url.pathname.startsWith("/api/")) {
    await request.continue();
    return;
  }
  const workspace = request.headers()["x-sharpsense-workspace"] ?? "";
  requests.push({ url, workspace, scenario });
  const beta = workspace === workspaces[1]!.id;
  const selectedWorkspace = workspaces[beta ? 1 : 0]!;
  let body: unknown;
  if (url.pathname === "/api/workspaces")
    body = { workspaces, initialWorkspaceId: null };
  else if (/^\/api\/workspaces\/[^/]+\/indexing$/.test(url.pathname))
    body = {
      ...idleIndexingStatus(url.pathname.split("/")[3]!),
      revision: scenario === "deleted" ? 1 : 0,
      sequence: scenario === "deleted" ? 1 : 0,
    };
  else {
    assert.ok(
      workspaces.some((item) => item.id === workspace),
      "Scoped request must use a registered workspace header.",
    );
    if (url.pathname === "/api/overview")
      body = {
        workspaceId: workspace,
        name: selectedWorkspace.name,
        indexed: true,
        nodeCount: 47,
        edgeCount: 45,
        projectCount: 1,
        documentCount: 2,
      };
    else if (url.pathname === "/api/tree")
      body = { parentPath: "/", parentDirectoryId: 1, nodes: [] };
    else if (url.pathname === "/api/graph/nodes/page")
      body = {
        revision: "canvas",
        items: [42, 43].map((id) => ({
          ...node(id, beta),
          scope: "selected",
          isClickable: true,
        })),
        nextCursor: null,
        totalCount: 2,
      };
    else {
      const match = /^\/api\/graph\/nodes\/(\d+)\/connections$/.exec(
        url.pathname,
      );
      assert.ok(match, "Unexpected API request: " + url.pathname);
      const id = Number(match[1]);
      const cursor = Number(url.searchParams.get("cursor") ?? "0");
      const revision = `${beta ? "beta" : "alpha"}-${scenario}`;
      assert.equal(url.searchParams.get("pageSize"), "20");
      if (cursor) assert.equal(url.searchParams.get("revision"), revision);
      if (id === 999 || scenario === "deleted") {
        await problem(request, 404, `No graph node exists for id ${id}.`);
        return;
      }
      if (cursor && scenario === "conflict") {
        await problem(
          request,
          409,
          "Connection snapshot changed. Retry to use the new graph.",
        );
        return;
      }
      if (
        cursor &&
        !beta &&
        (scenario === "cancel" || scenario === "isolation")
      ) {
        held = request;
        await new Promise<void>((resolve) => {
          releaseHeld = resolve;
        });
        if (request.failure()) return;
      }
      const peers =
        id === 42
          ? Array.from({ length: 45 }, (_, index) => ({
              node: node(100 + index, beta),
              relationships: [
                { type: "calls", direction: "outgoing" },
                ...(index === 0
                  ? [{ type: "inherits", direction: "incoming" }]
                  : []),
              ],
            }))
          : [];
      body = {
        node: node(id, beta),
        revision,
        items: peers.slice(cursor, cursor + 20),
        nextCursor: cursor + 20 < peers.length ? String(cursor + 20) : null,
        totalCount: cursor ? null : peers.length,
      };
    }
  }
  await request.respond({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function problem(request: HTTPRequest, status: number, detail: string) {
  await request.respond({
    status,
    contentType: "application/problem+json",
    body: JSON.stringify({ title: "Request failed", status, detail }),
  });
}

async function waitForConnections(
  page: Page,
  count: number,
  complete: boolean,
) {
  await page.waitForSelector(
    `[data-testid="node-connections-state"][data-loaded-count="${count}"][data-complete="${complete}"]`,
  );
}

async function waitForSelected(page: Page, id: number, label: string) {
  await page.waitForFunction(
    (nodeId, expected) =>
      document.querySelector('[data-testid="selected-node-id"]')
        ?.textContent ===
        "#" + nodeId &&
      document
        .querySelector('[data-testid="node-inspector"]')
        ?.textContent?.includes(expected),
    {},
    id,
    label,
  );
}

async function waitForHeld() {
  const deadline = Date.now() + 5000;
  while (!held && Date.now() < deadline) await delay(20);
  assert.ok(held, "Load more must start a cancellable request.");
  return held;
}

async function waitForCancelled(request: HTTPRequest) {
  const deadline = Date.now() + 5000;
  while (!cancelled.has(request) && Date.now() < deadline) await delay(20);
  assert.ok(
    cancelled.has(request),
    "Changing node or workspace must abort pending connection request.",
  );
  releaseHeld?.();
  releaseHeld = undefined;
  held = undefined;
}
