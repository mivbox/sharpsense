import assert from "node:assert/strict";
import { spawn, type ChildProcess } from "node:child_process";
import { existsSync } from "node:fs";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
import puppeteer, { type Browser, type Page } from "puppeteer";
import { stopChildProcess } from "./stopChildProcess";

const here = path.dirname(fileURLToPath(import.meta.url));
const cliDll =
  process.env.SHARPSENSE_E2E_CLI_DLL ??
  path.resolve(
    here,
    "../../SharpSense.Cli/bin/Release/net10.0/SharpSense.Cli.dll",
  );
const fixture = await mkdtemp(path.join(tmpdir(), "sharpsense-global-ui-"));
const repository = path.join(fixture, "repository");
const home = path.join(fixture, "home");
const artifacts =
  process.env.SHARPSENSE_E2E_ARTIFACTS ??
  (await mkdtemp(path.join(tmpdir(), "sharpsense-global-ui-artifacts-")));
const port = await freePort();
const baseUrl = `http://127.0.0.1:${port}`;
let server: ChildProcess | undefined;
let browser: Browser | undefined;
let serverOutput = "";
const pageErrors: string[] = [];

type Workspace = {
  id: string;
  name: string;
  sources: { kind: string; path: string }[];
};
type Status = { state: string; revision: number };

try {
  await mkdir(path.join(repository, ".git"), { recursive: true });
  await mkdir(path.join(repository, "docs", "alpha"), { recursive: true });
  await mkdir(path.join(repository, "docs", "beta"), { recursive: true });
  await mkdir(artifacts, { recursive: true });
  await writeFile(
    path.join(repository, "docs", "alpha", "guide.md"),
    "# Alpha guide\n\nWorkspace alpha.\n",
  );
  await writeFile(
    path.join(repository, "docs", "beta", "guide.md"),
    "# Beta guide\n\nWorkspace beta.\n",
  );
  server = spawn("dotnet", [cliDll, "ui", "--url", baseUrl], {
    env: { ...process.env, SHARPSENSE_HOME: home },
    stdio: ["ignore", "pipe", "pipe"],
  });
  const capture = (chunk: Buffer) => {
    serverOutput = (serverOutput + chunk.toString()).slice(-32_000);
  };
  server.stdout?.on("data", capture);
  server.stderr?.on("data", capture);
  await waitUntil(async () => {
    if (server?.exitCode !== null)
      throw new Error(`UI exited: ${serverOutput}`);
    try {
      return (await fetch(`${baseUrl}/api/workspaces`)).ok;
    } catch {
      return false;
    }
  });
  assert.deepEqual((await catalog()).workspaces, []);
  browser = await puppeteer.launch({
    headless: true,
    executablePath: executable(),
    args: ["--no-sandbox", "--enable-unsafe-swiftshader"],
  });
  const page = await browser.newPage();
  page.setDefaultTimeout(30_000);
  await page.setViewport({ width: 1440, height: 1000 });
  page.on("pageerror", (error) => pageErrors.push(String(error)));
  await page.goto(baseUrl);
  await page.locator("::-p-text(Create your first workspace)").wait();

  await createWorkspace(page, "alpha", "docs/alpha/**/*.md");
  const alpha = (await catalog()).workspaces.find(
    (workspace) => workspace.name === "alpha",
  )!;
  assert.ok(alpha?.id);
  await setKeywordOnly(page);
  await clickButton(page, "Analyze & watch");
  await waitUntil(async () => (await status(alpha.id)).state === "watching");
  const originalAlpha = await stats(alpha.id);
  assert.ok(originalAlpha.codeNodeCount > 0);
  await waitForIndexedUi(page, originalAlpha.codeNodeCount, true);

  await clickButton(page, "Manage workspaces");
  await createWorkspace(page, "beta", "docs/beta/**/*.md");
  const beta = (await catalog()).workspaces.find(
    (workspace) => workspace.name === "beta",
  )!;
  await setKeywordOnly(page);
  await clickButton(page, "Analyze");
  await waitUntil(async () => (await status(beta.id)).state === "completed");
  const originalBeta = await stats(beta.id);
  await waitForIndexedUi(page, originalBeta.codeNodeCount);
  assert.equal(
    (await status(alpha.id)).state,
    "watching",
    "Switching must not stop another workspace job.",
  );

  await page.locator('::-p-aria(Selected workspace[role="combobox"])').click();
  await page.locator('::-p-aria(alpha[role="option"])').click();
  await page.waitForFunction(
    (id) => new URL(location.href).searchParams.get("workspace") === id,
    {},
    alpha.id,
  );
  await clickButton(page, "Stop");
  await waitUntil(async () => (await status(alpha.id)).state === "stopped");

  await clickButton(page, "Manage workspaces");
  await page.screenshot({
    path: path.join(artifacts, "workspace-dashboard-desktop.png"),
    fullPage: true,
  });
  await clickButton(page, "Merge");
  await fillField(page, "New workspace name", "combined");
  await fillField(page, "Source workspaces", "alpha");
  await page.locator('::-p-aria(alpha[role="option"])').click();
  await fillField(page, "Source workspaces", "beta");
  await page.locator('::-p-aria(beta[role="option"])').click();
  await clickButton(page, "Create merged workspace");
  const combined = await waitForWorkspace("combined");
  assert.equal(combined.sources.length, 2);
  await page.waitForFunction(
    (id) => new URL(location.href).searchParams.get("workspace") === id,
    {},
    combined.id,
  );
  await setKeywordOnly(page);
  await clickButton(page, "Analyze & watch");
  await waitUntil(async () => (await status(combined.id)).state === "watching");
  const before = await stats(combined.id);
  assert.equal(
    before.codeNodeCount,
    originalAlpha.codeNodeCount + originalBeta.codeNodeCount,
  );
  await waitForIndexedUi(page, before.codeNodeCount, true);
  await page.locator('[data-tree-checkbox-trigger="/"]').click();
  await page.locator('[data-testid="node-list-toggle"]').click();
  await page.waitForFunction(() => {
    const list = document.querySelector('[aria-label="Graph nodes"]');
    return (
      list?.textContent?.includes("docs/alpha/guide.md#alpha-guide") &&
      list.textContent.includes("docs/beta/guide.md#beta-guide")
    );
  });
  const revision = (await status(combined.id)).revision;
  await writeFile(
    path.join(repository, "docs", "alpha", "guide.md"),
    "# Alpha guide\n\n## New alpha section\n",
  );
  await writeFile(
    path.join(repository, "docs", "beta", "guide.md"),
    "# Beta guide\n\n## New beta section\n",
  );
  await waitUntil(
    async () =>
      (await status(combined.id)).revision > revision &&
      (await stats(combined.id)).codeNodeCount >= before.codeNodeCount + 2,
  );
  assert.equal(
    (await stats(alpha.id)).codeNodeCount,
    originalAlpha.codeNodeCount,
  );
  assert.equal(
    (await stats(beta.id)).codeNodeCount,
    originalBeta.codeNodeCount,
  );
  const after = await stats(combined.id);
  await waitForIndexedUi(page, after.codeNodeCount, true);
  await page.waitForFunction(() => {
    const list = document.querySelector('[aria-label="Graph nodes"]');
    return (
      list?.textContent?.includes("docs/alpha/guide.md#new-alpha-section") &&
      list.textContent.includes("docs/beta/guide.md#new-beta-section")
    );
  });
  await page.screenshot({
    path: path.join(artifacts, "combined-workspace-watching.png"),
    fullPage: true,
  });
  await clickButton(page, "Stop");
  await waitUntil(async () => (await status(combined.id)).state === "stopped");
  await clickButton(page, "Manage workspaces");
  await page.setViewport({ width: 390, height: 844 });
  await page.waitForFunction(
    () => document.documentElement.scrollWidth <= window.innerWidth,
  );
  await page.screenshot({
    path: path.join(artifacts, "workspace-dashboard-mobile.png"),
    fullPage: true,
  });
  assert.deepEqual(pageErrors, []);
  const definition = await readFile(
    path.join(home, "workspaces", combined.id, "workspace.yaml"),
    "utf8",
  );
  assert.match(definition, /docs\/alpha/);
  assert.match(definition, /docs\/beta/);
  console.log(
    JSON.stringify({
      result: "passed",
      scenarios: [
        "empty global UI",
        "create",
        "independent jobs",
        "switch",
        "merge",
        "watch multiple selections",
        "automatic UI graph refresh",
        "graph isolation",
        "stop",
        "mobile",
      ],
      artifacts,
    }),
  );
} catch (error) {
  const page = (await browser?.pages())?.at(-1);
  if (page) {
    await page
      .screenshot({ path: path.join(artifacts, "failure.png"), fullPage: true })
      .catch(() => undefined);
    const pageState = await page.evaluate(() => ({
      url: location.href,
      text: document.body.innerText,
      buttons: Array.from(document.querySelectorAll("button")).map(
        (button) => ({
          text: button.innerText,
          label: button.getAttribute("aria-label"),
          disabled: button.disabled,
          bounds: button.getBoundingClientRect().toJSON(),
        }),
      ),
    }));
    await writeFile(
      path.join(artifacts, "failure-page.json"),
      JSON.stringify(pageState, null, 2),
    );
    const session = await page.createCDPSession();
    const accessibility = await session.send("Accessibility.getFullAXTree");
    await writeFile(
      path.join(artifacts, "failure-accessibility.json"),
      JSON.stringify(accessibility, null, 2),
    );
    await session.detach();
  }
  console.error("Workspace browser artifacts:", artifacts);
  if (pageErrors.length) console.error("Browser errors:", pageErrors);
  throw error;
} finally {
  if (serverOutput)
    await writeFile(path.join(artifacts, "server.log"), serverOutput);
  await browser?.close();
  await stopChildProcess(server);
  await rm(fixture, { recursive: true, force: true });
}

async function createWorkspace(page: Page, name: string, sourcePath: string) {
  await clickButton(page, "New workspace");
  await fillField(page, "Workspace name", name);
  await fillField(page, "Repository directory", repository);
  await page.locator('::-p-aria(Source type[role="combobox"])').click();
  await page.locator('::-p-aria(Documentation glob[role="option"])').click();
  await fillField(page, "Source path", sourcePath);
  await clickButton(page, "Create workspace");
  await page.locator('::-p-aria(Workspace explorer[role="heading"])').wait();
}

async function setKeywordOnly(page: Page) {
  const input = await page.waitForSelector('input[type="checkbox"]');
  assert.ok(input);
  if (await input?.evaluate((element) => (element as HTMLInputElement).checked))
    await input.click();
}

async function waitForIndexedUi(page: Page, nodes: number, watching = false) {
  await page.waitForFunction(
    (expectedNodes, expectWatching) => {
      const chips = Array.from(document.querySelectorAll(".MuiChip-label"));
      const available = chips.some(
        (chip) => chip.textContent === "Index available",
      );
      const watching = chips.some((chip) => chip.textContent === "Watching");
      const treeReady = document.querySelector(
        '[data-tree-checkbox-trigger="/"] input:not(:disabled)',
      );
      return (
        available &&
        (!expectWatching || watching) &&
        treeReady &&
        document.body.innerText.includes(
          `· ${expectedNodes.toLocaleString()} symbols`,
        )
      );
    },
    {},
    nodes,
    watching,
  );
}

async function clickButton(page: Page, text: string) {
  const handle = await page.waitForFunction(
    (label) =>
      Array.from(
        (
          document.querySelector('[role="dialog"]') ?? document
        ).querySelectorAll("button"),
      ).find(
        (button) =>
          !button.disabled &&
          button.getClientRects().length > 0 &&
          button.innerText.replace(/\u200b/g, "").trim() === label,
      ),
    {},
    text,
  );
  const element = handle.asElement();
  assert.ok(element, `Button not found: ${text}`);
  const button = await element.toElement("button");
  await button.scrollIntoView();
  await page.waitForFunction(
    (target) => {
      const bounds = target.getBoundingClientRect();
      const hit = document.elementFromPoint(
        bounds.x + bounds.width / 2,
        bounds.y + bounds.height / 2,
      );
      return hit !== null && target.contains(hit);
    },
    {},
    button,
  );
  await button.asLocator().click();
  await handle.dispose();
}

async function fieldSelector(page: Page, text: string) {
  await page.waitForFunction(
    (name) =>
      Array.from(document.querySelectorAll("label")).some(
        (label) =>
          label.textContent
            ?.replace(/\u200b/g, "")
            .replace(/\s*\*$/, "")
            .trim() === name && label.htmlFor,
      ),
    {},
    text,
  );
  return page.evaluate((name) => {
    const label = Array.from(document.querySelectorAll("label")).find(
      (element) =>
        element.textContent
          ?.replace(/\u200b/g, "")
          .replace(/\s*\*$/, "")
          .trim() === name,
    );
    if (!label?.htmlFor) throw new Error(`Input label not found: ${name}`);
    return "#" + CSS.escape(label.htmlFor);
  }, text);
}

async function fillField(page: Page, text: string, value: string) {
  const selector = await fieldSelector(page, text);
  await page.click(selector);
  await page.locator(selector).fill(value);
  await page.waitForFunction(
    (inputSelector, expected) =>
      (document.querySelector(inputSelector) as HTMLInputElement | null)
        ?.value === expected,
    {},
    selector,
    value,
  );
}

async function catalog(): Promise<{ workspaces: Workspace[] }> {
  return (await fetch(`${baseUrl}/api/workspaces`)).json();
}
async function status(id: string): Promise<Status> {
  return (await fetch(`${baseUrl}/api/workspaces/${id}/indexing`)).json();
}
async function stats(id: string): Promise<{ codeNodeCount: number }> {
  const response = await fetch(`${baseUrl}/api/tools/graph-stats`, {
    headers: { "X-SharpSense-Workspace": id },
  });
  assert.equal(response.status, 200);
  return response.json();
}
async function waitForWorkspace(name: string) {
  let workspace: Workspace | undefined;
  await waitUntil(async () => {
    workspace = (await catalog()).workspaces.find(
      (value) => value.name === name,
    );
    return Boolean(workspace);
  });
  return workspace!;
}
async function waitUntil(predicate: () => Promise<boolean>) {
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    if (await predicate()) return;
    await delay(150);
  }
  throw new Error(
    `Timed out waiting for workspace state. ${serverOutput.slice(-4000)}`,
  );
}
async function freePort(): Promise<number> {
  const socket = createServer();
  await new Promise<void>((resolve) => socket.listen(0, "127.0.0.1", resolve));
  const address = socket.address();
  assert.ok(address && typeof address !== "string");
  const port = address.port;
  await new Promise<void>((resolve, reject) =>
    socket.close((error) => (error ? reject(error) : resolve())),
  );
  return port;
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
