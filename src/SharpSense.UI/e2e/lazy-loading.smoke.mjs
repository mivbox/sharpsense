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

let serverProcess;
let browser;

try {
  await runAnalyze();
  serverProcess = startUiHost();
  await waitForServer(baseUrl, timeoutMs);

  const requests = [];
  const graphResponses = [];
  browser = await puppeteer.launch({
    headless: true,
    executablePath: resolveExecutablePath()
  });
  const page = await browser.newPage();
  page.on("request", (request) => {
    requests.push(request.url());
  });
  page.on("response", async (response) => {
    if (!response.url().includes("/api/graph?")) {
      return;
    }

    try {
      graphResponses.push(await response.json());
    } catch {
      // Ignore non-JSON responses from failed requests; later assertions catch behavior regressions.
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
  const expandablePath = await page.$eval("[data-expand-path]", (element) =>
    element.getAttribute("data-expand-path")
  );
  assert(expandablePath, "Expected at least one expandable tree row.");

  await page.click(`[data-expand-path="${cssEscape(expandablePath)}"]`);
  await waitForCondition(
    () => requests.filter((url) => url.includes("/api/tree?")).length > initialTreeRequests,
    timeoutMs,
    "Expected expanding the tree to trigger a lazy child fetch."
  );

  await page.click(`[data-tree-checkbox-trigger="${cssEscape(expandablePath)}"]`);
  await waitForCondition(
    () => requests.some((url) => url.includes("/api/graph?")),
    timeoutMs,
    "Expected selecting a tree path to trigger a scoped graph request."
  );
  await page.waitForFunction(
    () =>
      document.querySelector("canvas") !== null ||
      document.querySelector('[data-testid="graph-no-results-state"]') !== null,
    { timeout: timeoutMs }
  );

  const externalNodeCount = graphResponses
    .flatMap((response) => response?.nodes ?? [])
    .filter((node) => node?.scope === "external").length;

  if (externalNodeCount === 0) {
    console.warn("Smoke warning: selected scope did not expose any ghost nodes in this dataset.");
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
      stdio: ["ignore", "pipe", "pipe"]
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
