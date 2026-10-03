import assert from "node:assert/strict";
import type { Browser, HTTPRequest } from "puppeteer";
import { idleIndexingStatus } from "./indexingFixture";
import { launchBrowser, startUiPreview } from "./browserFixture";

const original = {
  id: 42,
  fullyQualifiedName: "Fixture.OriginalSymbol",
  displayName: "OriginalSymbol",
  nodeType: "Class",
  relativeFilePath: "src/Original.cs",
  startLine: 10,
  endLine: 15,
  summary: "Original search result.",
};
const workspace = {
  id: "5d9c4f64-9f4f-4fb2-9247-0f4b99b388b4",
  name: "Search fixture",
  repositoryRoot: "/fixture",
  sources: [{ kind: "CSharp", path: "Fixture.csproj" }],
};
let hits = [original];
const errors: string[] = [];
const server = await startUiPreview();
let browser: Browser | undefined;

try {
  const { baseUrl } = server;
  browser = await launchBrowser();
  const page = await browser.newPage();
  page.setDefaultTimeout(10_000);
  await page.setViewport({ width: 1440, height: 1000 });
  page.on("pageerror", (error) => errors.push(String(error)));
  await page.setRequestInterception(true);
  page.on("request", (request) => {
    void respond(request).catch((error: unknown) => errors.push(String(error)));
  });

  await page.goto(baseUrl + "/search?q=Fixture&workspace=" + workspace.id);
  await page.waitForSelector('[data-search-node-id="42"]');
  await page.click('[data-search-node-id="42"]');
  await page.waitForSelector('[data-testid="search-inspect-context"]');
  const searchUrl = page.url();

  // Index refresh can change metadata without changing persisted symbol ID.
  hits = [
    {
      ...original,
      fullyQualifiedName: "Fixture.UpdatedSymbol",
      displayName: "UpdatedSymbol",
      relativeFilePath: "src/Updated.cs",
      startLine: 25,
      summary: "Updated indexed excerpt.",
    },
  ];
  await page.click('[aria-label="Refresh workspace"]');
  await page.waitForFunction(() => {
    const button = document.querySelector(
      '[data-testid="search-inspect-context"]',
    );
    const details = button?.parentElement?.textContent ?? "";
    return (
      details.includes("Fixture.UpdatedSymbol") &&
      details.includes("src/Updated.cs:25") &&
      details.includes("Updated indexed excerpt.") &&
      !details.includes("Fixture.OriginalSymbol")
    );
  });
  assert.equal(page.url(), searchUrl, "Refresh must preserve search URL.");

  hits = [
    hits[0]!,
    ...Array.from({ length: 19 }, (_, index) => ({
      ...original,
      id: 100 + index,
      fullyQualifiedName: "Fixture.OtherSymbol" + index,
    })),
  ];
  await page.click('[aria-label="Refresh workspace"]');
  await page.waitForSelector('[data-search-node-id="118"]');
  await page.setViewport({ width: 390, height: 844 });
  await page.waitForFunction(
    () => {
      const action = document.querySelector(
        '[data-testid="search-inspect-context"]',
      );
      const bounds = action?.getBoundingClientRect();
      return bounds && bounds.top >= 0 && bounds.bottom <= window.innerHeight;
    },
    { timeout: 3000 },
  );
  await page.keyboard.press("Escape");
  await page.waitForSelector('[data-testid="search-inspect-context"]', {
    hidden: true,
  });
  await page.click('[data-search-node-id="42"]');
  await page.waitForFunction(() => {
    const action = document.querySelector(
      '[data-testid="search-inspect-context"]',
    );
    const bounds = action?.getBoundingClientRect();
    return bounds && bounds.top >= 0 && bounds.bottom <= window.innerHeight;
  });
  await page.keyboard.press("Escape");
  await page.waitForFunction(
    () => document.activeElement?.getAttribute("data-search-node-id") === "42",
  );
  assert.equal(
    page.url(),
    searchUrl,
    "Closing details must preserve the search.",
  );
  await page.click('[data-search-node-id="42"]');
  await page.waitForSelector('[data-testid="search-inspect-context"]');
  await page.setViewport({ width: 1440, height: 1000 });
  await page.waitForSelector('[role="dialog"]', { hidden: true });

  // Removed result must not retain actions that target missing symbol ID.
  hits = [];
  await page.click('[aria-label="Refresh workspace"]');
  await page.waitForFunction(() =>
    document.body.textContent?.includes("No results for this search"),
  );
  assert.equal(
    await page.$('[data-testid="search-inspect-context"]'),
    null,
    "Removed selected result must not retain tool actions.",
  );
  assert.equal(
    await page.evaluate(() =>
      document.body.textContent?.includes("Fixture.UpdatedSymbol"),
    ),
    false,
    "Removed selected result must not retain detail card.",
  );
  assert.equal(page.url(), searchUrl, "Refresh must preserve search URL.");
  assert.deepEqual(errors, [], "Browser must not emit uncaught errors.");
  console.log(
    "Search selection follows refreshed metadata and removed results.",
  );
} finally {
  await browser?.close();
  await server.close();
}

async function respond(request: HTTPRequest) {
  const pathname = new URL(request.url()).pathname;
  if (pathname.endsWith("/indexing/events")) {
    await request.respond({ status: 204 });
    return;
  }
  if (!pathname.startsWith("/api/")) {
    await request.continue();
    return;
  }
  const body =
    pathname === "/api/workspaces"
      ? { workspaces: [workspace], initialWorkspaceId: null }
      : pathname === "/api/workspaces/" + workspace.id + "/indexing"
        ? idleIndexingStatus(workspace.id)
        : pathname === "/api/overview"
          ? {
              name: workspace.name,
              workspaceId: workspace.id,
              indexed: true,
              nodeCount: hits.length,
            }
          : pathname === "/api/tools/search"
            ? { hits }
            : undefined;
  if (!pathname.startsWith("/api/workspaces")) {
    assert.equal(
      request.headers()["x-sharpsense-workspace"],
      workspace.id,
      "Workspace requests must carry their selected identity.",
    );
  }
  assert.ok(body, "Unexpected API request: " + pathname);
  await request.respond({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}
