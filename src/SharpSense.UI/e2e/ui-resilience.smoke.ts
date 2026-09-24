import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import puppeteer, {
  type Browser,
  type HTTPRequest,
  type Page,
} from "puppeteer";
import { preview } from "vite";
import { idleIndexingStatus } from "./indexingFixture";

const uiRoot = fileURLToPath(new URL("../", import.meta.url));
assert.ok(
  existsSync(path.join(uiRoot, "dist/index.html")),
  "Build the UI first.",
);
const workspace = {
  id: "87b3339a-c768-4026-8e31-b99132966a90",
  name: "Resilience fixture",
  repositoryRoot: "/fixture",
  sources: [{ kind: "CSharp", path: "Fixture.csproj" }],
};
const workspaces = [workspace];
const errors: string[] = [];
const impactTargets: number[] = [];
const discoveries: string[] = [];
const creates: unknown[] = [];
let catalogUnavailable = false;
let holdDiscovery = false;
let releaseDiscovery: (() => void) | undefined;
let cancelledDiscoveries = 0;
const server = await preview({
  configFile: false,
  root: uiRoot,
  preview: { host: "127.0.0.1", port: 0, strictPort: true },
});
let browser: Browser | undefined;
let browserPage: Page | undefined;

try {
  const address = server.httpServer.address();
  assert.ok(address && typeof address === "object");
  const baseUrl = "http://127.0.0.1:" + address.port;
  browser = await puppeteer.launch({
    headless: true,
    executablePath:
      process.env.PUPPETEER_EXECUTABLE_PATH ??
      (process.platform === "darwin"
        ? [
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            "/Applications/Chromium.app/Contents/MacOS/Chromium",
          ].find(existsSync)
        : undefined),
    args: process.env.CI ? ["--no-sandbox", "--disable-dev-shm-usage"] : [],
  });
  const page = await browser.newPage();
  browserPage = page;
  page.setDefaultTimeout(20_000);
  await page.setViewport({ width: 1500, height: 1000 });
  page.on("pageerror", (error) => errors.push(String(error)));
  page.on("requestfailed", (request) => {
    if (new URL(request.url()).pathname === "/api/workspaces/discover")
      cancelledDiscoveries++;
  });
  await page.setRequestInterception(true);
  page.on("request", (request) => {
    void respond(request).catch((error: unknown) => {
      if (!request.failure()) errors.push(String(error));
    });
  });

  await page.goto(
    `${baseUrl}/tools?workspace=${workspace.id}&tool=impact&nodeId=42`,
  );
  await page.locator('button[type="submit"]').click();
  await page.waitForSelector('[aria-label="Use Symbol50 as tool target"]');
  assert.equal(await resultCount(page), 50);
  await page
    .locator(
      '[aria-label="Impacted symbols pages"] [aria-label="Go to page 100"]',
    )
    .click();
  await page.waitForSelector('[aria-label="Use Symbol5000 as tool target"]');
  assert.equal(await resultCount(page), 50);
  assert.deepEqual(impactTargets, [42]);

  // A failed background poll must not remount the provider or lose a mutation's result/page.
  catalogUnavailable = true;
  await page.waitForFunction(() =>
    document.body.textContent?.includes("Workspace list could not refresh"),
  );
  assert.ok(await page.$('[aria-label="Use Symbol5000 as tool target"]'));
  assert.equal(await resultCount(page), 50);
  assert.deepEqual(impactTargets, [42]);
  catalogUnavailable = false;
  await clickButton(page, "Retry catalog");
  await page.waitForFunction(
    () =>
      !document.body.textContent?.includes("Workspace list could not refresh"),
  );
  assert.ok(await page.$('[aria-label="Use Symbol5000 as tool target"]'));

  await page.locator('[aria-label="Use Symbol5000 as tool target"]').click();
  assert.equal(await value(page, "Node ID"), "5000");
  await page.locator('button[type="submit"]').click();
  await page.waitForSelector('[aria-label="Use Selected5000 as tool target"]');
  assert.deepEqual(impactTargets, [42, 5000]);

  await page.goto(baseUrl + "/workspaces");
  await clickButton(page, "New workspace");
  await fill(page, "Workspace name", "Nested fixture");
  await fill(page, "Repository directory", "/fixture/apps/frontend");
  await fill(page, "Source path", "App.csproj");
  holdDiscovery = true;
  await clickButton(page, "Discover sources");
  await waitUntil(() => discoveries.length === 1);
  await fill(page, "Source path", "Edited.csproj");
  holdDiscovery = false;
  releaseDiscovery?.();
  await page.locator("::-p-aria(Discovered sources)").wait();
  assert.equal(
    await value(page, "Repository directory"),
    "/fixture/apps/frontend",
  );
  assert.equal(await value(page, "Source path"), "Edited.csproj");
  await page.locator("::-p-aria(Discovered sources)").fill("Other.csproj");
  await page.locator('::-p-aria(Other.csproj (C#)[role="option"])').click();
  await clickButton(page, "Add selected sources");
  assert.deepEqual(await values(page, "Source path"), [
    "Edited.csproj",
    "/fixture/Other.csproj",
  ]);
  await clickButton(page, "Create workspace");
  await waitUntil(() => creates.length === 1);
  assert.deepEqual(creates[0], {
    name: "Nested fixture",
    repositoryRoot: "/fixture/apps/frontend",
    sources: [
      { kind: "CSharp", path: "Edited.csproj" },
      { kind: "CSharp", path: "/fixture/Other.csproj" },
    ],
  });
  await page.waitForFunction(() => !document.querySelector('[role="dialog"]'));
  await page.goto(baseUrl + "/workspaces");
  await clickButton(page, "New workspace");
  await fill(page, "Repository directory", "/fixture/apps/old");
  await fill(page, "Source path", "Old.csproj");
  holdDiscovery = true;
  await clickButton(page, "Discover sources");
  await waitUntil(() => discoveries.length === 2);
  await fill(page, "Repository directory", "/fixture/apps/new");
  await fill(page, "Source path", "New.csproj");
  await waitUntil(() => cancelledDiscoveries === 1);
  holdDiscovery = false;
  releaseDiscovery?.();
  assert.equal(await value(page, "Repository directory"), "/fixture/apps/new");
  assert.equal(await value(page, "Source path"), "New.csproj");
  assert.deepEqual(await values(page, "Discovered sources"), []);
  await clickButton(page, "Cancel");
  assert.deepEqual(errors, []);
  console.log(
    "Catalog recovery preserves tool state; all 5,000 results remain navigable; nested discovery preserves paths and cancels stale requests.",
  );
} catch (error) {
  console.error({
    url: browserPage?.url(),
    body: await browserPage
      ?.evaluate(() => document.body.innerText.slice(0, 4_000))
      .catch(() => undefined),
    errors,
  });
  throw error;
} finally {
  releaseDiscovery?.();
  await browser?.close();
  await new Promise<void>((resolve, reject) =>
    server.httpServer.close((error) => (error ? reject(error) : resolve())),
  );
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
  let body: unknown;
  let status = 200;
  if (url.pathname === "/api/workspaces" && request.method() === "POST") {
    const input = JSON.parse(request.postData() ?? "null") as typeof workspace;
    creates.push(input);
    const created = {
      ...input,
      id: "8fe0f81a-c768-4026-8e31-b99132966a90",
      repositoryRoot: "/fixture",
    };
    workspaces.push(created);
    body = created;
    status = 201;
  } else if (url.pathname === "/api/workspaces") {
    status = catalogUnavailable ? 503 : 200;
    body = catalogUnavailable
      ? {
          status,
          title: "Catalog unavailable",
          detail: "Temporary fixture outage.",
        }
      : { workspaces, initialWorkspaceId: null };
  } else if (url.pathname.endsWith("/indexing")) {
    body = idleIndexingStatus(url.pathname.split("/")[3]!);
  } else if (url.pathname === "/api/workspaces/discover") {
    discoveries.push(
      (JSON.parse(request.postData() ?? "null") as { repositoryRoot: string })
        .repositoryRoot,
    );
    if (holdDiscovery)
      await new Promise<void>((resolve) => {
        releaseDiscovery = resolve;
      });
    if (request.failure()) return;
    body = {
      repositoryRoot: "/fixture",
      sources: [{ kind: "CSharp", path: "Other.csproj" }],
    };
  } else if (url.pathname === "/api/overview") {
    body = { name: workspace.name, indexed: true, nodeCount: 5_000 };
  } else if (url.pathname === "/api/tree") {
    body = { parentPath: "/", parentDirectoryId: null, nodes: [] };
  } else if (url.pathname === "/api/tools") {
    body = [{ id: "impact", name: "Impact analysis", description: "Fixture" }];
  } else if (url.pathname === "/api/tools/impact") {
    const nodeId = (
      JSON.parse(request.postData() ?? "null") as { nodeId: number }
    ).nodeId;
    impactTargets.push(nodeId);
    body = {
      targetSymbol: "Fixture.Target",
      impactedNodes:
        nodeId === 42
          ? Array.from({ length: 5_000 }, (_, index) => ({
              id: index + 1,
              displayName: `Symbol${index + 1}`,
              nodeType: "Class",
            }))
          : [
              {
                id: nodeId,
                displayName: `Selected${nodeId}`,
                nodeType: "Class",
              },
            ],
      dependencies: [],
    };
  } else throw new Error("Unexpected API request: " + url.pathname);
  if (
    !url.pathname.startsWith("/api/workspaces") &&
    url.pathname !== "/api/tools"
  )
    assert.ok(
      workspaces.some(
        (item) => item.id === request.headers()["x-sharpsense-workspace"],
      ),
    );
  await request.respond({
    status,
    contentType:
      status >= 400 ? "application/problem+json" : "application/json",
    body: JSON.stringify(body),
  });
}

async function fill(page: Page, label: string, value: string) {
  const handle = await page.waitForFunction(
    (name) => {
      const input = [...document.querySelectorAll("input")].find((element) =>
        [...(element.labels ?? [])].some(
          (label) => label.textContent?.replaceAll("*", "").trim() === name,
        ),
      );
      return input?.id ? `#${CSS.escape(input.id)}` : null;
    },
    {},
    label,
  );
  const selector = await handle.jsonValue();
  await handle.dispose();
  assert.ok(selector, `Missing input: ${label}`);
  await page.click(selector);
  await page.$eval(selector, (input) => (input as HTMLInputElement).select());
  await page.keyboard.press("Backspace");
  await page.keyboard.type(value);
  await page.waitForFunction(
    (inputSelector, expected) =>
      (document.querySelector(inputSelector) as HTMLInputElement | null)
        ?.value === expected,
    {},
    selector,
    value,
  );
}

async function clickButton(page: Page, text: string) {
  const handle = await page.waitForFunction(
    (label) =>
      [
        ...(
          document.querySelector('[role="dialog"]') ?? document
        ).querySelectorAll("button"),
      ].find(
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

async function values(page: Page, label: string) {
  return page.$$eval(
    "input",
    (inputs, name) =>
      inputs
        .filter((input) =>
          [...(input.labels ?? [])].some(
            (element) =>
              element.textContent?.replaceAll("*", "").trim() === name,
          ),
        )
        .map((input) => input.value),
    label,
  );
}

async function value(page: Page, label: string) {
  return (await values(page, label))[0];
}

async function resultCount(page: Page) {
  return page.$$eval(
    '[aria-label="Impacted symbols"] [aria-label$="as tool target"]',
    (items) => items.length,
  );
}

async function waitUntil(condition: () => boolean) {
  const deadline = Date.now() + 20_000;
  while (!condition()) {
    assert.ok(Date.now() < deadline, "Fixture condition timed out.");
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
}
