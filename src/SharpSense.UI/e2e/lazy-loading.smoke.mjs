import { spawn } from "node:child_process";
import process from "node:process";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import fs from "node:fs";
import puppeteer from "puppeteer";

const baseUrl = process.env.SHARPSENSE_E2E_BASE_URL ?? "http://127.0.0.1:50731";
const rootTreePath = encodeURIComponent("/");
const repoRoot = process.env.SHARPSENSE_E2E_REPO_ROOT ?? path.resolve(process.cwd(), "../..");
const cliProjectPath = path.resolve(process.cwd(), "../SharpSense.Cli/SharpSense.Cli.csproj");
const targetPath =
  process.env.SHARPSENSE_E2E_TARGET_PATH ?? path.resolve(repoRoot, "SharpSense.sln");
const timeoutMs = Number.parseInt(process.env.SHARPSENSE_E2E_TIMEOUT_MS ?? "30000", 10);
const preferredExpandablePaths = ["src", "tests", "docs"];
const preferredSelectionPaths = [
  "src/SharpSense.Domain",
  "src/SharpSense.Cli",
  "src/SharpSense.Application",
  "tests/SharpSense.Application.Tests",
  "tests/SharpSense.Infrastructure.Tests"
];

let serverProcess;
let browser;

try {
  await runAnalyze();
  serverProcess = startUiHost();
  await waitForServer(baseUrl, timeoutMs);

  const browserConsoleMessages = [];
  const pageErrors = [];
  const requests = [];
  const graphNodeRequests = [];
  const graphEdgeRequests = [];
  const graphNodeResponses = [];
  const graphEdgeResponses = [];
  browser = await puppeteer.launch({
    headless: true,
    executablePath: resolveExecutablePath()
  });
  const page = await browser.newPage();
  page.on("console", (message) => {
    browserConsoleMessages.push(`${message.type()}: ${message.text()}`);
  });
  page.on("pageerror", (error) => {
    pageErrors.push(error.stack ?? error.message);
  });
  page.on("request", (request) => {
    const url = request.url();
    requests.push(url);

    if (url.includes("/api/graph/nodes?")) {
      graphNodeRequests.push(url);
    }

    if (url.includes("/api/graph/edges?")) {
      graphEdgeRequests.push(url);
    }
  });
  page.on("response", async (response) => {
    const url = response.url();

    if (url.includes("/api/graph/nodes?")) {
      try {
        graphNodeResponses.push(await response.json());
      } catch {
        // Ignore non-JSON responses from failed requests; later assertions catch behavior regressions.
      }

      return;
    }

    if (url.includes("/api/graph/edges?")) {
      try {
        graphEdgeResponses.push(await response.json());
      } catch {
        // Ignore non-JSON responses from failed requests; later assertions catch behavior regressions.
      }
    }
  });

  await page.goto(baseUrl, { waitUntil: "networkidle0" });
  await page.waitForSelector('[data-testid="workspace-explorer-panel"]', { timeout: timeoutMs });
  await page.waitForSelector('[data-tree-path]', { timeout: timeoutMs });
  await page.waitForSelector('[data-testid="graph-empty-state"]', { timeout: timeoutMs });

  assert(
    requests.some((url) => url.includes(`/api/tree?path=${rootTreePath}`)),
    "Expected the UI to fetch only the root tree on first load."
  );
  assert(
    requests.every((url) => !url.includes("/api/graph")),
    "Expected the graph to stay unloaded until the user opts into scope."
  );

  const initialTreeRequests = requests.filter((url) => url.includes("/api/tree?")).length;
  const expandablePath = await page.$$eval(
    "[data-expand-path]",
    (elements, preferredPaths) => {
      const paths = elements
        .map((element) => element.getAttribute("data-expand-path"))
        .filter((path) => typeof path === "string");

      return preferredPaths.find((path) => paths.includes(path)) ?? paths[0] ?? null;
    },
    preferredExpandablePaths
  );
  assert(expandablePath, "Expected at least one expandable tree row.");

  await page.click(`[data-expand-path="${cssEscape(expandablePath)}"]`);
  await waitForCondition(
    () => requests.filter((url) => url.includes("/api/tree?")).length > initialTreeRequests,
    timeoutMs,
    "Expected expanding the tree to trigger a lazy child fetch."
  );

  const preferredChildPath =
    preferredSelectionPaths.find((path) => path.startsWith(`${expandablePath}/`)) ?? null;
  if (preferredChildPath) {
    await page.waitForSelector(
      `[data-tree-checkbox-trigger="${cssEscape(preferredChildPath)}"]`,
      { timeout: timeoutMs }
    );
  } else {
    await page.waitForFunction(
      (parentPath) =>
        Array.from(document.querySelectorAll("[data-tree-checkbox-trigger]")).some((element) => {
          const candidatePath = element.getAttribute("data-tree-checkbox-trigger");
          return Boolean(candidatePath && candidatePath.startsWith(`${parentPath}/`));
        }),
      { timeout: timeoutMs },
      expandablePath
    );
  }

  const selectionPath =
    preferredChildPath ??
    (await page.$$eval(
      "[data-tree-checkbox-trigger]",
      (elements, parentPath) => {
        const paths = elements
          .map((element) => element.getAttribute("data-tree-checkbox-trigger"))
          .filter((path) => typeof path === "string");

        return paths.find((path) => path.startsWith(`${parentPath}/`)) ?? null;
      },
      expandablePath
    ));
  assert(selectionPath, "Expected at least one selectable tree path.");

  const graphNodeRequestCountBeforeSelection = graphNodeRequests.length;
  const graphEdgeRequestCountBeforeSelection = graphEdgeRequests.length;
  await page.click(`[data-tree-checkbox-trigger="${cssEscape(selectionPath)}"]`);
  await waitForCondition(
    () => graphNodeRequests.length > graphNodeRequestCountBeforeSelection,
    timeoutMs,
    "Expected selecting a tree path to trigger a scoped graph node request."
  );
  assert(
    graphEdgeRequests.length === graphEdgeRequestCountBeforeSelection,
    "Expected selecting a tree path to defer graph edge loading until the user opts in."
  );
  try {
    await page.waitForSelector('[data-testid="toggle-edges-checkbox"]', { timeout: timeoutMs });
  } catch (error) {
    const debugState = await capturePageState(page);
    throw new Error(
      [
        `Expected the edge toggle to appear after selecting ${selectionPath}.`,
        `Latest node request: ${graphNodeRequests.at(-1) ?? "none"}`,
        `Latest edge request: ${graphEdgeRequests.at(-1) ?? "none"}`,
        `Page errors: ${pageErrors.join(" | ") || "none"}`,
        `Browser console: ${browserConsoleMessages.join(" | ") || "none"}`,
        `Visible state: ${JSON.stringify(debugState)}`
      ].join("\n"),
      { cause: error }
    );
  }

  const graphEdgeRequestCountBeforeToggle = graphEdgeRequests.length;
  await page.click('[data-testid="toggle-edges-checkbox"]');
  await waitForCondition(
    () => graphEdgeRequests.length > graphEdgeRequestCountBeforeToggle,
    timeoutMs,
    "Expected enabling edges to trigger a scoped graph edge request."
  );

  const externalNodeCount = graphNodeResponses
    .flatMap((response) => response ?? [])
    .filter((node) => node?.scope === "external").length;

  if (externalNodeCount === 0) {
    console.warn("Smoke warning: selected scope did not expose any ghost nodes in this dataset.");
  }

  if (graphEdgeResponses.length === 0) {
    console.warn("Smoke warning: edge request fired but no edge payloads were captured.");
  }

  console.log("Lazy-loading smoke passed.");
} finally {
  await browser?.close();
  stopUiHost(serverProcess);
}

function startUiHost() {
  return spawn(
    "dotnet",
    [
      "run",
      "--project",
      cliProjectPath,
      "--",
      "ui",
      "--url",
      baseUrl,
      "--repo-root",
      repoRoot
    ],
    {
      cwd: repoRoot,
      env: {
        ...process.env,
        DOTNET_CLI_TELEMETRY_OPTOUT: "1"
      },
      stdio: "ignore"
    }
  );
}

function stopUiHost(serverProcess) {
  if (!serverProcess || serverProcess.exitCode !== null) {
    return;
  }

  serverProcess.kill("SIGTERM");
}

async function runAnalyze() {
  if (process.env.SHARPSENSE_E2E_SKIP_ANALYZE === "1") {
    return;
  }

  assert(
    fs.existsSync(targetPath),
    `Expected an analyze target at ${targetPath}. Set SHARPSENSE_E2E_TARGET_PATH to override it.`
  );

  await runCommand("dotnet", [
    "run",
    "--project",
    cliProjectPath,
    "--",
    "analyze",
    targetPath,
    "--repo-root",
    repoRoot,
    "--no-embeddings"
  ]);
}

function resolveExecutablePath() {
  if (process.env.PUPPETEER_EXECUTABLE_PATH) {
    return process.env.PUPPETEER_EXECUTABLE_PATH;
  }

  if (process.platform !== "darwin") {
    return undefined;
  }

  const macBrowserCandidates = [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Google Chrome Canary.app/Contents/MacOS/Google Chrome Canary",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
  ];

  return macBrowserCandidates.find((candidate) => fs.existsSync(candidate));
}

async function runCommand(command, args) {
  const child = spawn(command, args, {
    cwd: repoRoot,
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
    },
    stdio: "inherit"
  });

  const exitCode = await new Promise((resolve, reject) => {
    child.once("error", reject);
    child.once("close", resolve);
  });

  if (exitCode !== 0) {
    throw new Error(`${command} ${args.join(" ")} exited with code ${String(exitCode)}.`);
  }
}

async function waitForServer(url, timeout) {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeout) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
      // Ignore startup connection failures until the host is ready.
    }

    await delay(250);
  }

  throw new Error(`The UI host did not become ready at ${url}.`);
}

async function waitForCondition(predicate, timeout, message) {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeout) {
    if (predicate()) {
      return;
    }

    await delay(100);
  }

  throw new Error(message);
}

function cssEscape(value) {
  return value.replaceAll("\\", "\\\\").replaceAll('"', '\\"');
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

async function capturePageState(page) {
  return await page.evaluate(() => ({
    canvasVisible: document.querySelector("canvas") !== null,
    emptyStateVisible: document.querySelector('[data-testid="graph-empty-state"]') !== null,
    loadingStateVisible: document.querySelector('[data-testid="graph-loading-state"]') !== null,
    noResultsStateVisible: document.querySelector('[data-testid="graph-no-results-state"]') !== null,
    toggleVisible: document.querySelector('[data-testid="toggle-edges-checkbox"]') !== null,
    text: document.body.innerText.slice(0, 2000)
  }));
}
