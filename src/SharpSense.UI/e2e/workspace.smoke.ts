import assert from "node:assert/strict";
import { spawn, type ChildProcess } from "node:child_process";
import { existsSync } from "node:fs";
import { mkdir, mkdtemp, realpath, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
import puppeteer, {
  type Browser,
  type HTTPRequest,
  type Page,
} from "puppeteer";
import type { GraphNode, GraphPage } from "../src/shared/api/models";
import { stopChildProcess } from "./stopChildProcess";

type GraphPageNode = Omit<GraphNode, "id" | "projectId"> & {
  id: number;
  projectId: number | null;
};
type GraphNodesPage = GraphPage<GraphPageNode>;

const here = path.dirname(fileURLToPath(import.meta.url));
const cliDll =
  process.env.SHARPSENSE_E2E_CLI_DLL ??
  path.resolve(
    here,
    "../../SharpSense.Cli/bin/Release/net10.0/SharpSense.Cli.dll",
  );
const timeout = Number(process.env.SHARPSENSE_E2E_TIMEOUT_MS ?? "90000");
const fixture = await realpath(
  await mkdtemp(path.join(tmpdir(), "sharpsense-workspace-e2e-")),
);
const workspaceHome = await mkdtemp(
  path.join(tmpdir(), "sharpsense-workspace-home-"),
);
const screenshotDir =
  process.env.SHARPSENSE_E2E_ARTIFACTS ??
  process.env.SHARPSENSE_E2E_SCREENSHOT_DIR ??
  (await mkdtemp(path.join(tmpdir(), "sharpsense-ui-artifacts-")));
await mkdir(screenshotDir, { recursive: true });
let workspaceId = "";
let server: ChildProcess | undefined;
let browser: Browser | undefined;
let serverOutput = "";
let serverError: Error | undefined;
const pageErrors: string[] = [];
const failedApiResponses: string[] = [];

try {
  assert.ok(
    existsSync(cliDll),
    "Build the Release CLI before browser tests; missing " + cliDll,
  );
  await createFixture();
  await command("dotnet", [
    "restore",
    path.join(fixture, "App.csproj"),
    "--ignore-failed-sources",
    "--verbosity",
    "quiet",
  ]);
  await command("dotnet", [
    cliDll,
    "workspace",
    "create",
    "browser-fixture",
    "--repo-root",
    fixture,
    "--csharp",
    "App.csproj",
    "--csharp",
    "src/Fixture/Fixture.csproj",
    "--markdown",
    "docs/**/*.md",
    "--markdown",
    ".agents/**/*.md",
  ]);
  await command("dotnet", [
    cliDll,
    "analyze",
    "--workspace",
    "browser-fixture",
    "--no-embeddings",
  ]);
  const baseUrl = "http://127.0.0.1:" + (await availablePort());
  server = spawn("dotnet", [cliDll, "ui", "--url", baseUrl], {
    cwd: fixture,
    env: {
      ...process.env,
      SHARPSENSE_HOME: workspaceHome,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
    },
    stdio: ["ignore", "pipe", "pipe"],
  });
  const capture = (chunk: Buffer) => {
    serverOutput = (serverOutput + chunk.toString()).slice(-32_000);
  };
  server.stdout?.on("data", capture);
  server.stderr?.on("data", capture);
  server.on("error", (error) => {
    serverError = error;
  });
  await ready(baseUrl, server);
  const catalogResponse = await fetch(baseUrl + "/api/workspaces");
  assert.equal(catalogResponse.status, 200);
  const catalog = (await catalogResponse.json()) as {
    workspaces: { id: string; name: string }[];
  };
  assert.equal(catalog.workspaces.length, 1);
  assert.equal(catalog.workspaces[0]?.name, "browser-fixture");
  workspaceId = catalog.workspaces[0]!.id;
  browser = await puppeteer.launch({
    headless: true,
    executablePath: executable(),
    args: [
      "--enable-unsafe-swiftshader",
      ...(process.env.CI ? ["--no-sandbox", "--disable-dev-shm-usage"] : []),
    ],
  });
  const page = await browser.newPage();
  page.setDefaultTimeout(timeout);
  await page.setViewport({ width: 1600, height: 1000, deviceScaleFactor: 1 });
  page.on("pageerror", (error) => pageErrors.push(String(error)));
  page.on("response", (response) => {
    if (response.url().includes("/api/") && response.status() >= 400)
      failedApiResponses.push(response.status() + " " + response.url());
  });
  const requests: string[] = [];
  page.on("request", (request) => {
    requests.push(request.url());
    const pathname = new URL(request.url()).pathname;
    if (
      pathname.startsWith("/api/") &&
      !pathname.startsWith("/api/workspaces") &&
      request.headers()["x-sharpsense-workspace"] !== workspaceId
    ) {
      failedApiResponses.push("Missing workspace header: " + pathname);
    }
  });
  await page.goto(workspaceUrl(baseUrl, "/explorer"), {
    waitUntil: "networkidle0",
  });
  await page.waitForSelector("[data-tree-path]");
  await page.waitForSelector('[data-testid="graph-empty-state"]');
  assert.equal(
    requests.filter((url) => url.includes("/api/graph/")).length,
    0,
    "Graph must stay unloaded until scope selection.",
  );

  const initialTreeRequests = requests.filter((url) =>
    url.includes("/api/tree"),
  ).length;
  await page.click('[data-expand-path="src"]');
  await page.waitForSelector('[data-tree-path="src/Fixture"]');
  assert.ok(
    requests.filter((url) => url.includes("/api/tree")).length >
      initialTreeRequests,
    "Expansion must lazy-load children.",
  );
  const graphResponse = page.waitForResponse(
    (response) =>
      response.url().includes("/api/graph/nodes/page") && response.ok(),
  );
  await page.click('[data-tree-checkbox-trigger="src/Fixture"]');
  const graphPage = (await (await graphResponse).json()) as GraphNodesPage;
  assert.ok(
    graphPage.revision,
    "Graph pages must carry their snapshot revision.",
  );
  const selectedNode = graphPage.items.find(
    (node) =>
      node.type.toLowerCase() === "class" &&
      node.label.endsWith("MessageConsumer"),
  );
  assert.ok(
    selectedNode && Number.isInteger(selectedNode.codeNodeId),
    "Graph must expose persisted numeric symbol IDs.",
  );
  assert.equal(
    requests.some((url) => url.includes("/api/graph/edges/page")),
    false,
    "Edges must remain opt-in.",
  );
  const connectionsResponse = page.waitForResponse(
    (response) =>
      response
        .url()
        .includes(`/api/graph/nodes/${selectedNode.id}/connections`) &&
      response.ok(),
  );
  await page.click('[data-testid="node-list-toggle"]');
  await page.click('[data-node-id="' + selectedNode.codeNodeId + '"]');
  const connections = (await (await connectionsResponse).json()) as {
    node: { id: number };
    items: unknown[];
  };
  assert.equal(connections.node.id, selectedNode.id);
  assert.ok(
    connections.items.length > 0,
    "Inspector must show real symbol relationships with canvas edges off.",
  );
  await page.waitForFunction(
    () =>
      Number(
        document
          .querySelector('[data-testid="node-connections-state"]')
          ?.getAttribute("data-loaded-count"),
      ) > 0,
  );
  assert.equal(
    requests.some((url) => url.includes("/api/graph/edges/page")),
    false,
    "Inspecting a symbol must not request whole graph edges.",
  );
  const edgeResponse = page.waitForResponse(
    (response) =>
      response.url().includes("/api/graph/edges/page") && response.ok(),
  );
  await page.click('[data-testid="toggle-edges-checkbox"]');
  const graphEdges = (await (await edgeResponse).json()) as GraphPage<unknown>;
  assert.ok(graphEdges.items.length > 0);
  assert.equal(graphEdges.revision, graphPage.revision);
  await page.waitForFunction(
    (id) =>
      document.querySelector('[data-testid="selected-node-id"]')
        ?.textContent ===
      "#" + id,
    {},
    selectedNode.codeNodeId,
  );
  const explorerUrl = page.url();
  assert.equal(new URL(explorerUrl).pathname, "/explorer");
  assert.ok(
    new URL(explorerUrl).searchParams.has("selected"),
    "Graph selection must be shareable in the URL.",
  );
  await page.reload({ waitUntil: "networkidle0" });
  await page.waitForFunction(
    (id) =>
      document.querySelector('[data-testid="selected-node-id"]')
        ?.textContent ===
      "#" + id,
    {},
    selectedNode.codeNodeId,
  );
  await page.click('[data-expand-path="src"]');
  await page.waitForFunction(
    (id) =>
      document.querySelector('[data-testid="selected-node-id"]')
        ?.textContent ===
      "#" + id,
    {},
    selectedNode.codeNodeId,
  );
  await page.screenshot({
    path: path.join(screenshotDir, "workspace-initial.png"),
    fullPage: true,
  });
  console.log(
    "Graph selection and route reload passed; screenshot: " +
      path.join(screenshotDir, "workspace-initial.png"),
  );
  await page.click('[data-testid="memories-tab"]');
  await page.waitForSelector('[data-testid="add-memory-button"]');
  await page.click('[data-testid="add-memory-button"]');
  const memoryContent =
    "Keep <b>literal markup</b> as text. Always preserve the provider contract.";
  await field(page, "Memory content", memoryContent);
  await field(page, "Tags", "browser-test, invariant");
  const added = page.waitForResponse(
    (response) =>
      response.url().includes("/api/memory/node/") &&
      response.request().method() === "POST",
  );
  let resolveMemoryRequest!: (request: HTTPRequest) => void;
  const memoryRequest = new Promise<HTTPRequest>((resolve) => {
    resolveMemoryRequest = resolve;
  });
  await page.setRequestInterception(true);
  const holdMemoryWrite = (request: HTTPRequest) => {
    if (
      request.url().includes("/api/memory/node/") &&
      request.method() === "POST"
    ) {
      resolveMemoryRequest(request);
    } else {
      void request
        .continue()
        .catch((error: unknown) => pageErrors.push(String(error)));
    }
  };
  page.on("request", holdMemoryWrite);
  await page.click('[data-testid="save-memory-button"]');
  const pendingMemoryRequest = await memoryRequest;
  try {
    await page.waitForFunction(() =>
      ["Memory content", "Intent", "Tags"].every((text) => {
        const label = [...document.querySelectorAll("label")].find(
          (element) => element.textContent === text,
        );
        const control = label?.htmlFor
          ? document.getElementById(label.htmlFor)
          : null;
        return (
          control?.matches(":disabled") ||
          control?.getAttribute("aria-disabled") === "true"
        );
      }),
    );
    assert.deepEqual(JSON.parse(pendingMemoryRequest.postData() ?? "{}"), {
      content: memoryContent,
      tags: ["browser-test", "invariant"],
      intent: "Convention",
    });
  } finally {
    await pendingMemoryRequest.continue();
    page.off("request", holdMemoryWrite);
    await page.setRequestInterception(false);
  }
  assert.equal(
    (await added).status(),
    200,
    "Memory creation must use the real API and bundled embedding model.",
  );
  await page.waitForSelector('[role="dialog"]', { hidden: true });
  await page.waitForSelector("[data-memory-id]");
  await page.click('button[aria-label="Read memory"]');
  await page.waitForFunction(
    (content) =>
      document.querySelector('[data-testid="memory-content"]')?.textContent ===
      content,
    {},
    memoryContent,
  );
  assert.equal(
    await page.$eval(
      '[data-testid="memory-content"]',
      (element) => element.querySelectorAll("b").length,
    ),
    0,
    "Memory markup must remain text.",
  );
  await page.click('button[aria-label="Delete memory"]');
  const deleted = page.waitForResponse(
    (response) =>
      response.url().includes("/api/memory/") &&
      response.request().method() === "DELETE",
  );
  await page.click('[data-testid="confirm-delete-memory"]');
  assert.equal((await deleted).status(), 200);
  await page.waitForSelector("[data-memory-id]", { hidden: true });
  await page.waitForSelector('[role="dialog"]', { hidden: true });
  console.log("Real memory create/read/delete passed.");

  if (screenshotDir) {
    await mkdir(screenshotDir, { recursive: true });
    await page.click('[aria-label="Symbol details"] [role="tab"]');
    await page.click('button[aria-label="3D graph view"]');
    await page.waitForSelector(
      '[data-testid="graph-canvas"][aria-busy="false"]',
    );
    await delay(700);
    await page.screenshot({
      path: path.join(screenshotDir, "workspace-desktop.png"),
      fullPage: true,
    });
  }
  await page.click('[data-testid="nav-search"]');
  await field(page, "Search code and documentation", "MessageConsumer");
  const searched = page.waitForResponse(
    (response) =>
      response.url().includes("/api/tools/search") &&
      response.request().method() === "POST",
  );
  await page.click('[data-testid="run-search"]');
  assert.equal((await searched).status(), 200);
  await page.waitForSelector("[data-search-node-id]");
  assert.equal(new URL(page.url()).searchParams.get("q"), "MessageConsumer");
  await page.goBack({ waitUntil: "networkidle0" });
  assert.equal(
    new URL(page.url()).searchParams.get("q"),
    null,
    "Back must restore the unsubmitted search route.",
  );
  await page.goForward({ waitUntil: "networkidle0" });
  await page.waitForSelector("[data-search-node-id]");
  await page.reload({ waitUntil: "networkidle0" });
  await page.waitForSelector("[data-search-node-id]");
  await page.click("[data-search-node-id]");
  await page.screenshot({
    path: path.join(screenshotDir, "workspace-search.png"),
    fullPage: true,
  });
  await page.click('[data-testid="search-inspect-context"]');
  await page.waitForSelector('[data-testid="tool-context"]');
  const traceTarget = graphPage.items.find((node) =>
    node.label.includes("MessageConsumer.Consume"),
  );
  const impactTarget = graphPage.items.find((node) =>
    node.label.includes("IMessageProvider.GetMessage"),
  );
  const inheritorsTarget = graphPage.items.find((node) =>
    node.label.endsWith("IMessageProvider"),
  );
  assert.ok(
    traceTarget?.codeNodeId &&
      impactTarget?.codeNodeId &&
      inheritorsTarget?.codeNodeId,
    "Tool fixture must expose call, impact and inheritance targets.",
  );
  const toolTargets = {
    context: selectedNode.codeNodeId,
    trace: traceTarget.codeNodeId,
    impact: impactTarget.codeNodeId,
    inheritors: inheritorsTarget.codeNodeId,
  };
  for (const tool of [
    "context",
    "trace",
    "impact",
    "inheritors",
    "graph_stats",
  ] as const) {
    await page.click('[data-testid="tool-' + tool + '"]');
    if (tool !== "graph_stats")
      await field(page, "Node ID", String(toolTargets[tool]));
    const toolResponse = page.waitForResponse(
      (response) =>
        response
          .url()
          .includes(
            "/api/tools/" + (tool === "graph_stats" ? "graph-stats" : tool),
          ) &&
        response.request().method() ===
          (tool === "graph_stats" ? "GET" : "POST"),
    );
    await page.click('[data-testid="run-tool"]');
    assert.equal(
      (await toolResponse).status(),
      200,
      tool + " should execute through the real generated API client.",
    );
    await page.waitForFunction(() =>
      document
        .querySelector('[data-testid="run-tool"]')
        ?.textContent?.startsWith("Run "),
    );
    const payload = (await (await toolResponse).json()) as
      | Record<string, unknown>
      | { displayName?: string; fullyQualifiedName?: string }[];
    const resultText = await page.$eval(
      '[data-testid="tool-results"]',
      (element) => element.textContent ?? "",
    );
    if (tool === "graph_stats") {
      const stats = payload as { graphNodeCount: number };
      assert.ok(stats.graphNodeCount > 0);
      assert.ok(
        resultText.includes("Graph nodes") &&
          resultText.includes(stats.graphNodeCount.toLocaleString()),
        "Graph statistics must render actual workspace counts.",
      );
    } else if (tool !== "context") {
      const result = payload as {
        nodes?: { displayName?: string; fullyQualifiedName?: string }[];
        impactedNodes?: { displayName?: string; fullyQualifiedName?: string }[];
      };
      const nodes =
        tool === "inheritors"
          ? (payload as { displayName?: string; fullyQualifiedName?: string }[])
          : tool === "trace"
            ? result.nodes
            : result.impactedNodes;
      assert.ok(
        nodes?.length,
        tool + " fixture must contain real related symbols.",
      );
      assert.ok(
        nodes.some((node) =>
          resultText.includes(
            node.displayName ?? node.fullyQualifiedName ?? "__missing__",
          ),
        ),
        tool + " must render related symbols from its typed result.",
      );
    }
    if (tool === "context") {
      const context = (await (await toolResponse).json()) as Record<
        string,
        unknown
      >;
      assert.ok(
        Object.values(context).some(
          (value) => Array.isArray(value) && value.length > 0,
        ),
        "Context fixture must expose real symbol relationships.",
      );
      await page.screenshot({
        path: path.join(screenshotDir, "workspace-context.png"),
        fullPage: true,
      });
    }
  }
  // Project parents carry graph IDs but cannot become code-tool targets.
  await page.goto(
    workspaceUrl(
      baseUrl,
      "/tools?tool=context&nodeId=" + selectedNode.codeNodeId,
    ),
  );
  const classContext = await runContext(page);
  const projectParent = classContext.parents.find(
    (parent) => parent.codeNodeId == null,
  );
  assert.ok(projectParent, "Class context must include its project parent.");
  const projectParentSelector =
    "[aria-label=" +
    JSON.stringify(`Use ${projectParent.name} as tool target`) +
    "]";
  assert.equal(
    await page.$eval(projectParentSelector, (element) =>
      element.getAttribute("aria-disabled"),
    ),
    "true",
  );
  const child = classContext.children.find((node) => node.codeNodeId != null);
  assert.ok(child, "Class context must include an inspectable member.");
  await page.click(
    "[aria-label=" + JSON.stringify(`Use ${child.name} as tool target`) + "]",
  );
  await page.waitForFunction(
    (id) =>
      new URL(window.location.href).searchParams.get("nodeId") === String(id),
    {},
    child.codeNodeId,
  );
  const memberContext = await runContext(page);
  const codeParent = memberContext.parents.find(
    (parent) => parent.codeNodeId === selectedNode.codeNodeId,
  );
  assert.ok(
    codeParent,
    "Member context must return its inspectable declaring class.",
  );
  const codeParentSelector =
    "[aria-label=" +
    JSON.stringify(`Use ${codeParent.name} as tool target`) +
    "]";
  assert.notEqual(
    await page.$eval(codeParentSelector, (element) =>
      element.getAttribute("aria-disabled"),
    ),
    "true",
  );
  await page.click(codeParentSelector);
  await page.waitForFunction(
    (id) =>
      new URL(window.location.href).searchParams.get("nodeId") === String(id),
    {},
    selectedNode.codeNodeId,
  );
  const traceCount = requests.filter((url) =>
    url.includes("/api/tools/trace"),
  ).length;
  await page.goto(
    workspaceUrl(
      baseUrl,
      "/tools?tool=trace&nodeId=" +
        selectedNode.codeNodeId +
        "&depth=2&direction=caller",
    ),
    { waitUntil: "networkidle0" },
  );
  await page.waitForFunction(
    (id) =>
      [...document.querySelectorAll("input")].some(
        (input) => input.value === String(id),
      ),
    {},
    selectedNode.codeNodeId,
  );
  assert.equal(
    requests.filter((url) => url.includes("/api/tools/trace")).length,
    traceCount,
    "Tool deep links must not execute automatically.",
  );
  await page.reload({ waitUntil: "networkidle0" });
  assert.equal(new URL(page.url()).searchParams.get("depth"), "2");
  const deepLinkRun = page.waitForResponse(
    (response) =>
      response.url().includes("/api/tools/trace") &&
      response.request().method() === "POST",
  );
  await page.click('[data-testid="run-tool"]');
  assert.equal((await deepLinkRun).status(), 200);
  await page.waitForFunction(() =>
    document
      .querySelector('[data-testid="run-tool"]')
      ?.textContent?.startsWith("Run "),
  );
  await page.screenshot({
    path: path.join(screenshotDir, "workspace-tools.png"),
    fullPage: true,
  });
  await field(page, "Node ID", "0");
  assert.equal(
    await page.$eval(
      '[data-testid="run-tool"]',
      (button) => (button as HTMLButtonElement).disabled,
    ),
    true,
    "Invalid node IDs must not submit.",
  );
  await page.setViewport({ width: 390, height: 844, deviceScaleFactor: 1 });
  await page.click('[data-testid="mobile-nav-explorer"]');
  await page.waitForSelector('[data-testid="workspace-explorer-panel"]');
  await page.goto(explorerUrl, { waitUntil: "networkidle0" });
  await page.waitForSelector('[data-testid="selected-node-id"]');
  assert.ok(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth + 1,
    ),
    "Mobile workspace must not overflow horizontally.",
  );
  if (screenshotDir)
    await page.screenshot({
      path: path.join(screenshotDir, "workspace-mobile.png"),
      fullPage: true,
    });
  console.log(
    "Search, five tools, URL history/deep links and populated mobile layout passed: " +
      screenshotDir,
  );
  await page.setViewport({ width: 1600, height: 1000, deviceScaleFactor: 1 });
  const graphClickUrl = new URL(explorerUrl);
  graphClickUrl.searchParams.delete("selected");
  graphClickUrl.searchParams.delete("mode");
  await page.goto(graphClickUrl.href, { waitUntil: "networkidle0" });
  await page.waitForSelector('[data-testid="graph-canvas"] canvas');
  await page.waitForSelector('[data-testid="graph-canvas"][aria-busy="false"]');
  await delay(700);
  await clickVisibleGraphNode(page);
  await page.waitForFunction(() =>
    new URL(window.location.href).searchParams.has("selected"),
  );
  await delay(700);
  await page.screenshot({
    path: path.join(screenshotDir, "workspace-node-focus.png"),
    fullPage: true,
  });
  console.log(
    "Settled graph and actual canvas-node click captured: " + screenshotDir,
  );
  await page.goto(workspaceUrl(baseUrl, "/explorer"), {
    waitUntil: "networkidle0",
  });
  await page.waitForSelector('[data-tree-checkbox-trigger="/"] input');
  assert.equal(
    await page.$eval(
      '[data-tree-checkbox-trigger="/"] input',
      (input) => (input as HTMLInputElement).disabled,
    ),
    false,
    "Entire workspace must be selectable for root-level source files.",
  );
  await page.waitForSelector('[data-tree-checkbox-trigger=".agents"] input');
  const agentGraphResponse = page.waitForResponse(
    (response) =>
      response.url().includes("/api/graph/nodes/page") && response.ok(),
  );
  await page.click('[data-tree-checkbox-trigger=".agents"]');
  await page.waitForFunction(
    () =>
      new URL(window.location.href).searchParams.get("scopes") ===
      '[".agents"]',
  );
  const agentNodes = (
    (await (await agentGraphResponse).json()) as GraphNodesPage
  ).items;
  assert.ok(
    !agentNodes.some((node) => node.label.endsWith("RootWorkspaceSymbol")),
    "Selecting .agents must remain scoped before selecting Entire workspace.",
  );
  const rootGraphResponse = page.waitForResponse(
    (response) =>
      response.url().includes("/api/graph/nodes/page") && response.ok(),
  );
  await page.click('[data-tree-checkbox-trigger="/"]');
  const rootNodes = ((await (await rootGraphResponse).json()) as GraphNodesPage)
    .items;
  assert.ok(
    rootNodes.some((node) => node.label.endsWith("RootWorkspaceSymbol")),
    "Entire workspace must include symbols declared beside the root project.",
  );
  await page.waitForFunction(() => {
    const child = document.querySelector<HTMLInputElement>(
      '[data-tree-checkbox-trigger="src"] input',
    );
    return child?.checked && child.disabled;
  });
  assert.deepEqual(
    JSON.parse(new URL(page.url()).searchParams.get("scopes") ?? "[]"),
    ["/"],
  );
  const reloadedRootGraph = page.waitForResponse(
    (response) =>
      response.url().includes("/api/graph/nodes/page") && response.ok(),
  );
  await page.reload({ waitUntil: "networkidle0" });
  assert.ok(
    ((await (await reloadedRootGraph).json()) as GraphNodesPage).items.some(
      (node) => node.label.endsWith("RootWorkspaceSymbol"),
    ),
    "Entire workspace graph scope must survive reload.",
  );
  await page.waitForFunction(() => {
    const root = document.querySelector<HTMLInputElement>(
      '[data-tree-checkbox-trigger="/"] input',
    );
    const child = document.querySelector<HTMLInputElement>(
      '[data-tree-checkbox-trigger="src"] input',
    );
    return root?.checked && child?.checked && child.disabled;
  });
  await page.click('[data-tree-checkbox-trigger="/"]');
  await page.waitForFunction(() => {
    const child = document.querySelector<HTMLInputElement>(
      '[data-tree-checkbox-trigger=".agents"] input',
    );
    const scopes = JSON.parse(
      new URL(window.location.href).searchParams.get("scopes") ?? "[]",
    ) as string[];
    return child && !child.checked && !child.disabled && scopes.length === 0;
  });
  console.log("Root-level symbols and Entire workspace scope/reload passed.");
  assert.deepEqual(pageErrors, [], "Browser must not emit uncaught errors.");
  assert.deepEqual(
    failedApiResponses,
    [],
    "All core workflow API responses must succeed.",
  );
  assert.equal(
    requests.some((url) => /\/api\/graph\/(nodes|edges)(?:\?|$)/.test(url)),
    false,
    "Explorer must load graph pages instead of full graph arrays.",
  );
  await verifyProgressiveGraph(page, baseUrl, pageErrors);
  console.log(
    "Workspace smoke passed: lazy graph, independent inspector, numeric selection, real memory CRUD, safe labels, search, five tools, URL reload/history, mobile layout.",
  );
  console.log("Browser screenshots: " + screenshotDir);
} catch (error) {
  console.error("Workspace smoke failed:", error);
  const failedPage = (await browser?.pages())?.at(-1);
  if (failedPage) console.error("Browser URL: " + failedPage.url());
  if (failedPage)
    await failedPage
      .screenshot({
        path: path.join(screenshotDir, "failure.png"),
        fullPage: true,
      })
      .catch(() => undefined);
  if (failedPage) {
    const details = await failedPage
      .evaluate(() => ({
        url: window.location.href,
        body: document.body.innerText.slice(0, 24_000),
      }))
      .catch(() => undefined);
    await writeFile(
      path.join(screenshotDir, "failure.json"),
      JSON.stringify(details, null, 2),
    );
  }
  await writeFile(path.join(screenshotDir, "server.log"), serverOutput);
  console.error("Browser artifacts: " + screenshotDir);
  if (pageErrors.length) console.error("Browser errors:", pageErrors);
  if (failedApiResponses.length)
    console.error("API failures:", failedApiResponses);
  if (serverOutput) console.error("UI host output:", serverOutput.slice(-8000));
  process.exitCode = 1;
} finally {
  await browser?.close();
  await stopChildProcess(server);
  await rm(fixture, { recursive: true, force: true });
  await rm(workspaceHome, { recursive: true, force: true });
}

async function createFixture() {
  await mkdir(path.join(fixture, ".git"), { recursive: true });
  await mkdir(path.join(fixture, "src/Fixture"), { recursive: true });
  await mkdir(path.join(fixture, "docs"), { recursive: true });
  await mkdir(path.join(fixture, ".agents"), { recursive: true });
  await writeFile(path.join(fixture, ".git/HEAD"), "ref: refs/heads/main\n");
  await writeFile(
    path.join(fixture, "NuGet.Config"),
    "<configuration><packageSources><clear /></packageSources></configuration>",
  );
  await writeFile(
    path.join(fixture, ".agents/Guide.md"),
    "# Workspace agent guidance\n\nKeep indexed scope intentional.\n",
  );
  await writeFile(
    path.join(fixture, "App.csproj"),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="RootSymbol.cs" /><ProjectReference Include="src/Fixture/Fixture.csproj" /></ItemGroup></Project>',
  );
  await writeFile(
    path.join(fixture, "RootSymbol.cs"),
    "namespace WorkspaceFixture; public sealed class RootWorkspaceSymbol {}\n",
  );
  await writeFile(
    path.join(fixture, "src/Fixture/Fixture.csproj"),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>',
  );
  await writeFile(
    path.join(fixture, "src/Fixture/Messages.cs"),
    `namespace WorkspaceFixture;
public interface IMessageProvider { string GetMessage(); }
public class MessageProvider : IMessageProvider { public virtual string GetMessage() => "Hello"; }
public class DerivedMessageProvider : MessageProvider { public override string GetMessage() => base.GetMessage() + "!"; }
public sealed class MessageConsumer(IMessageProvider provider) { public string Consume() => provider.GetMessage(); }
public sealed class Envelope<T> { public T? Value { get; set; } }
`,
  );
  const name =
    process.platform === "win32"
      ? "Guide.md"
      : "<img src=x onerror=window.__graphLabelExecuted=true>.md";
  await writeFile(
    path.join(fixture, "docs", name),
    "# Message pipeline\n\nThe consumer calls the provider. Keep generic names such as Envelope<T> intact.\n",
  );
}

async function verifyProgressiveGraph(
  page: Page,
  baseUrl: string,
  browserErrors: string[],
) {
  const nodes: GraphPageNode[] = Array.from({ length: 1251 }, (_, index) => ({
    id: 100_000 + index,
    codeNodeId: 100_000 + index,
    label:
      index === 1250
        ? "BeyondLegacyCap"
        : `PagedNode${String(index).padStart(5, "0")}`,
    type: index % 2 === 0 && index < 1250 ? "class" : "method",
    relativePath: "Synthetic.cs",
    projectId: null,
    scope: "selected",
    isClickable: true,
  }));
  const edges = Array.from({ length: 2502 }, (_, index) => ({
    source: nodes[index % nodes.length]!.id,
    target:
      nodes[(index + 1 + Math.floor(index / nodes.length)) % nodes.length]!.id,
    type: "Calls",
    scope: "internal",
    metadata: null,
  }));
  type Scenario = "pause" | "conflict" | "fresh" | "cancel";
  let scenario: Scenario = "pause";
  let holdNextPage = true;
  let heldRequest: HTTPRequest | undefined;
  let releaseHeld: (() => void) | undefined;
  const cancelled: string[] = [];
  const graphRequests: { scenario: Scenario; url: URL }[] = [];
  const onFailed = (request: HTTPRequest) => {
    if (request.url().includes("/api/graph/")) cancelled.push(request.url());
  };
  const intercept = async (request: HTTPRequest) => {
    const url = new URL(request.url());
    const isNodes = url.pathname === "/api/graph/nodes/page";
    if (!isNodes && url.pathname !== "/api/graph/edges/page") {
      await request.continue();
      return;
    }
    const requestScenario = scenario;
    graphRequests.push({ scenario: requestScenario, url });
    assert.equal(request.headers()["x-sharpsense-workspace"], workspaceId);
    const cursor = Number(url.searchParams.get("cursor") ?? "0");
    const revision = `synthetic-${requestScenario}`;
    if (cursor > 0 || !isNodes) {
      assert.equal(url.searchParams.get("revision"), revision);
    }
    if (isNodes && cursor === 500 && holdNextPage) {
      holdNextPage = false;
      heldRequest = request;
      await new Promise<void>((resolve) => {
        releaseHeld = resolve;
      });
      if (request.failure()) return;
    }
    if (isNodes && cursor > 0 && requestScenario === "conflict") {
      await request.respond({
        status: 409,
        contentType: "application/problem+json",
        body: JSON.stringify({
          title: "Graph changed",
          detail:
            "Graph changed during paging. Retry to load its new revision.",
          status: 409,
        }),
      });
      return;
    }
    const items = isNodes ? nodes : edges;
    const pageSize = isNodes ? 500 : 700;
    const next = cursor + pageSize;
    await request.respond({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        revision,
        items: items.slice(cursor, next),
        nextCursor: next < items.length ? String(next) : null,
        totalCount: cursor === 0 ? items.length : null,
      }),
    });
  };
  const onRequest = (request: HTTPRequest) => {
    void intercept(request).catch((error: unknown) => {
      if (!request.failure()) browserErrors.push(String(error));
    });
  };
  const route = (includeEdges: boolean) => {
    const url = new URL(workspaceUrl(baseUrl, "/explorer"));
    url.searchParams.set("scopes", JSON.stringify(["/"]));
    url.searchParams.set("mode", "list");
    if (includeEdges) url.searchParams.set("edges", "true");
    return url.href;
  };
  const waitForHeldPage = async () => {
    const deadline = Date.now() + timeout;
    while (!heldRequest && Date.now() < deadline) await delay(25);
    assert.ok(heldRequest, "Next graph page must start before cancellation.");
    await page.waitForSelector(
      '[data-testid="graph-loading-state"][data-node-count="500"]',
    );
    return heldRequest;
  };
  const waitForCancellation = async (request: HTTPRequest) => {
    const deadline = Date.now() + 5000;
    while (!cancelled.includes(request.url()) && Date.now() < deadline)
      await delay(25);
    assert.ok(
      cancelled.includes(request.url()),
      "Query cancellation must abort the actual page request.",
    );
    releaseHeld?.();
    releaseHeld = undefined;
    heldRequest = undefined;
  };

  page.on("requestfailed", onFailed);
  await page.setRequestInterception(true);
  page.on("request", onRequest);
  try {
    await page.goto(route(true), { waitUntil: "domcontentloaded" });
    const pending = await waitForHeldPage();
    await page.locator('::-p-aria([name="Pause"][role="button"])').click();
    await waitForCancellation(pending);
    await page.waitForFunction(() =>
      document
        .querySelector('[data-testid="graph-loading-state"]')
        ?.textContent?.includes("Paused"),
    );
    const pausedRequestCount = graphRequests.length;
    await delay(200);
    assert.equal(
      graphRequests.length,
      pausedRequestCount,
      "Paused graphs must not request further pages.",
    );
    await page.locator('::-p-aria([name="Resume"][role="button"])').click();
    await page.waitForSelector(
      '[data-testid="graph-loading-state"][data-complete="true"][data-node-count="1251"][data-edge-count="2502"]',
    );
    assert.ok(
      graphRequests.filter((item) => item.url.pathname.endsWith("/nodes/page"))
        .length >= 3,
    );
    assert.ok(
      graphRequests.filter((item) => item.url.pathname.endsWith("/edges/page"))
        .length >= 4,
    );
    assert.match(
      await page.$eval(
        '[data-testid="graph-counts"]',
        (element) => element.textContent ?? "",
      ),
      /625 visible/,
    );
    await page.click('[data-testid="graph-type-filter"]');
    await page
      .locator('::-p-aria([name="All types"][role="menuitem"])')
      .click();
    await page.keyboard.press("Escape");
    await page.waitForFunction(() =>
      document
        .querySelector('[data-testid="graph-counts"]')
        ?.textContent?.includes("1,251 visible"),
    );
    await page
      .locator('input[placeholder="Filter symbols, types, or paths"]')
      .fill("BeyondLegacyCap");
    await page.waitForSelector('[data-node-id="101250"]');
    await page.screenshot({
      path: path.join(screenshotDir, "workspace-progressive-graph.png"),
      fullPage: true,
    });

    scenario = "conflict";
    await page.goto(route(false), { waitUntil: "networkidle0" });
    await page.waitForFunction(() =>
      document
        .querySelector('[data-testid="graph-loading-state"]')
        ?.textContent?.includes("Graph changed during paging"),
    );
    await page.waitForSelector(
      '[data-testid="graph-loading-state"][data-complete="false"][data-node-count="500"]',
    );
    assert.equal(
      graphRequests.filter(
        (item) =>
          item.scenario === "conflict" && item.url.searchParams.has("cursor"),
      ).length,
      1,
      "Revision conflicts must not retry the stale cursor automatically.",
    );
    scenario = "fresh";
    await page.locator('::-p-aria([name="Retry"][role="button"])').click();
    await page.waitForSelector(
      '[data-testid="graph-loading-state"][data-complete="true"][data-node-count="1251"]',
    );
    const firstRetry = graphRequests.find((item) => item.scenario === "fresh");
    assert.ok(firstRetry);
    assert.equal(firstRetry.url.searchParams.has("cursor"), false);
    assert.equal(firstRetry.url.searchParams.has("revision"), false);

    scenario = "cancel";
    holdNextPage = true;
    await page.goto(route(false), { waitUntil: "domcontentloaded" });
    const abandoned = await waitForHeldPage();
    await page.click('button[aria-label="Clear selected scopes"]');
    await waitForCancellation(abandoned);
    await page.waitForSelector('[data-testid="graph-empty-state"]');
    await delay(100);
    assert.equal(await page.$('[data-testid="graph-loading-state"]'), null);
    assert.deepEqual(
      browserErrors,
      [],
      "Progressive paging must not emit browser errors.",
    );
    console.log(
      "Progressive graph passed: 1,251 nodes, 2,502 edges, all types, pause/resume, 409 retry, and scope cancellation.",
    );
  } finally {
    releaseHeld?.();
    page.off("request", onRequest);
    page.off("requestfailed", onFailed);
    await page.setRequestInterception(false);
  }
}

async function runContext(page: Page): Promise<{
  parents: { id: number; name: string; codeNodeId: number | null }[];
  children: { id: number; name: string; codeNodeId: number | null }[];
}> {
  await page.waitForSelector('[data-testid="run-tool"]:not([disabled])');
  const response = page.waitForResponse(
    (candidate) =>
      candidate.url().includes("/api/tools/context") &&
      candidate.request().method() === "POST",
  );
  await page.click('[data-testid="run-tool"]');
  const result = await response;
  assert.equal(result.status(), 200);
  await page.waitForFunction(() =>
    document
      .querySelector('[data-testid="run-tool"]')
      ?.textContent?.startsWith("Run "),
  );
  return await result.json();
}
async function field(page: Page, label: string, value: string) {
  await page.waitForFunction(
    (text) =>
      [...document.querySelectorAll("label")].some(
        (element) => element.textContent === text && element.htmlFor,
      ),
    {},
    label,
  );
  const selector = await page.evaluate((text) => {
    const labelElement = [...document.querySelectorAll("label")].find(
      (element) => element.textContent === text,
    );
    if (!labelElement?.htmlFor)
      throw new Error("Input label not found: " + text);
    return "#" + CSS.escape(labelElement.htmlFor);
  }, label);
  // Focus first so MUI's focus render cannot restore the previous controlled value
  // between the locator clearing the field and typing its replacement.
  await page.click(selector);
  await page.locator(selector).fill(value);
  await page.waitForFunction(
    (inputSelector, expected) =>
      (document.querySelector(inputSelector) as HTMLInputElement | null)
        ?.value === expected,
    { timeout: 5000 },
    selector,
    value,
  );
}
async function command(file: string, args: string[]) {
  const child = spawn(file, args, {
    cwd: fixture,
    env: {
      ...process.env,
      SHARPSENSE_HOME: workspaceHome,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
    },
    stdio: ["ignore", "pipe", "pipe"],
  });
  let output = "";
  const capture = (chunk: Buffer) => {
    output = (output + chunk.toString()).slice(-32_000);
  };
  child.stdout?.on("data", capture);
  child.stderr?.on("data", capture);
  const timer = setTimeout(() => child.kill("SIGKILL"), 180_000);
  try {
    const code = await new Promise<number | null>((resolve, reject) => {
      child.once("error", reject);
      child.once("close", resolve);
    });
    assert.equal(code, 0, file + " failed: " + output);
  } finally {
    clearTimeout(timer);
  }
}
async function availablePort() {
  const socket = createServer();
  await new Promise<void>((resolve, reject) => {
    socket.once("error", reject);
    socket.listen(0, "127.0.0.1", resolve);
  });
  const address = socket.address();
  assert.ok(address && typeof address !== "string");
  await new Promise<void>((resolve, reject) =>
    socket.close((error) => (error ? reject(error) : resolve())),
  );
  return address.port;
}
async function ready(url: string, child: ChildProcess) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (serverError) throw serverError;
    assert.equal(
      child.exitCode === null && child.signalCode === null,
      true,
      "UI host exited during startup: " + serverOutput,
    );
    try {
      const response = await fetch(url + "/api/workspaces", {
        signal: AbortSignal.timeout(2000),
      });
      if (response.ok) return;
    } catch {
      /* Wait for host binding. */
    }
    await delay(200);
  }
  throw new Error("UI host did not become ready: " + serverOutput);
}
function workspaceUrl(baseUrl: string, route: string) {
  assert.ok(workspaceId, "Workspace must be resolved before navigation.");
  const url = new URL(route, baseUrl);
  url.searchParams.set("workspace", workspaceId);
  return url.href;
}
function executable() {
  if (process.env.PUPPETEER_EXECUTABLE_PATH)
    return process.env.PUPPETEER_EXECUTABLE_PATH;
  if (process.platform !== "darwin") return undefined;
  return [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
  ].find(existsSync);
}

async function clickVisibleGraphNode(page: Page) {
  const canvas = await page.$('[data-testid="graph-canvas"] canvas');
  assert.ok(canvas, "Graph canvas must be visible.");
  const bounds = await canvas.boundingBox();
  assert.ok(bounds);
  const screenshot = await canvas.screenshot({ encoding: "base64" });
  const point = await page.evaluate(async (png) => {
    const image = new Image();
    image.src = "data:image/png;base64," + png;
    await image.decode();
    const surface = document.createElement("canvas");
    surface.width = image.width;
    surface.height = image.height;
    const context = surface.getContext("2d")!;
    context.drawImage(image, 0, 0);
    const { data } = context.getImageData(0, 0, image.width, image.height);
    // Solid colored node interiors exceed the thin, dimmer edges. Work from
    // rendered pixels so this exercises real pointer picking without test hooks.
    for (let y = 12; y < image.height - 60; y += 3) {
      for (let x = 12; x < image.width - 12; x += 3) {
        const offsets = [
          [0, 0],
          [-3, 0],
          [3, 0],
          [0, -3],
          [0, 3],
        ];
        if (
          offsets.every(([dx, dy]) => {
            const index = ((y + dy) * image.width + x + dx) * 4;
            return (
              data[index] > 75 && data[index + 1] > 75 && data[index + 2] > 90
            );
          })
        )
          return { x, y };
      }
    }
    return null;
  }, screenshot);
  assert.ok(point, "Settled graph must contain readable, clickable nodes.");
  await page.mouse.move(bounds.x + point.x, bounds.y + point.y);
  await delay(150);
  await page.mouse.click(bounds.x + point.x, bounds.y + point.y);
}
