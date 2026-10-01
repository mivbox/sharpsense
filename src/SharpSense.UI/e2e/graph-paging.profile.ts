import assert from "node:assert/strict";
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { launchBrowser } from "./browserFixture";

const baseUrl = process.env.SHARPSENSE_PROFILE_URL;
const workspace = process.env.SHARPSENSE_PROFILE_WORKSPACE;
assert.ok(
  baseUrl && workspace,
  "Set SHARPSENSE_PROFILE_URL and SHARPSENSE_PROFILE_WORKSPACE to an existing UI host and workspace ID.",
);
const artifacts =
  process.env.SHARPSENSE_PROFILE_ARTIFACTS ??
  (await mkdtemp(path.join(tmpdir(), "sharpsense-graph-profile-")));
await mkdir(artifacts, { recursive: true });
const browser = await launchBrowser({
  args: [
    "--enable-unsafe-swiftshader",
    ...(process.env.CI ? ["--no-sandbox", "--disable-dev-shm-usage"] : []),
  ],
});
const page = await browser.newPage();
page.setDefaultTimeout(240_000);
await page.setViewport({ width: 1600, height: 1000, deviceScaleFactor: 1 });
const errors: string[] = [];
const responses = new Map<
  string,
  { kind: string; bytes: number; encoding: string; status: number }
>();
page.on("pageerror", (error) => errors.push(String(error)));
page.on("request", (request) => {
  if (new URL(request.url()).pathname.startsWith("/api/")) {
    assert.equal(
      request.method(),
      "GET",
      "Profiling must not mutate workspace content.",
    );
  }
});
const session = await page.createCDPSession();
await session.send("Network.enable");
session.on("Network.responseReceived", ({ requestId, response }) => {
  const pathname = new URL(response.url).pathname;
  if (!/\/api\/graph\/(nodes|edges)\/page$/.test(pathname)) return;
  responses.set(requestId, {
    kind: pathname.includes("/nodes/") ? "nodes" : "edges",
    bytes: 0,
    encoding: String(
      response.headers["Content-Encoding"] ??
        response.headers["content-encoding"] ??
        "identity",
    ),
    status: response.status,
  });
});
session.on("Network.loadingFinished", ({ requestId, encodedDataLength }) => {
  const response = responses.get(requestId);
  if (response) response.bytes = encodedDataLength;
});
await page.evaluateOnNewDocument(() => {
  const metrics = {
    firstNodesMs: null as number | null,
    firstSettledCanvasMs: null as number | null,
    completeMs: null as number | null,
  };
  Object.assign(window, { graphProfileMetrics: metrics });
  new MutationObserver(() => {
    const status = document.querySelector(
      '[data-testid="graph-loading-state"]',
    );
    if (Number(status?.getAttribute("data-node-count")) > 0) {
      metrics.firstNodesMs ??= performance.now();
      if (
        document.querySelector(
          '[data-testid="graph-canvas"][aria-busy="false"]',
        )
      )
        metrics.firstSettledCanvasMs ??= performance.now();
    }
    if (status?.getAttribute("data-complete") === "true")
      metrics.completeMs ??= performance.now();
  }).observe(document, {
    subtree: true,
    childList: true,
    attributes: true,
    attributeFilter: ["data-node-count", "data-complete", "aria-busy"],
  });
});

try {
  const url = new URL("/explorer", baseUrl);
  url.searchParams.set("workspace", workspace);
  url.searchParams.set("scopes", JSON.stringify(["/"]));
  url.searchParams.set("edges", "true");
  await page.goto(url.href, { waitUntil: "domcontentloaded" });
  await page.waitForSelector(
    '[data-testid="graph-loading-state"][data-complete="true"]',
  );
  const counts = await page.$eval(
    '[data-testid="graph-loading-state"]',
    (element) => ({
      nodes: Number(element.getAttribute("data-node-count")),
      edges: Number(element.getAttribute("data-edge-count")),
    }),
  );
  assert.ok(
    counts.nodes > 1000 && counts.edges > 2000,
    "Profile must exercise graph sizes above former limits.",
  );
  await page.waitForSelector('[data-testid="graph-canvas"][aria-busy="false"]');
  const defaultVisible = await page.$eval(
    '[data-testid="graph-counts"]',
    (element) => element.textContent,
  );
  await page.screenshot({
    path: path.join(artifacts, "graph-default-types.png"),
    fullPage: true,
  });
  const filterStarted = Date.now();
  await page.click('[data-testid="graph-type-filter"]');
  await page.locator('::-p-aria([name="All types"][role="menuitem"])').click();
  await page.keyboard.press("Escape");
  await page.waitForFunction(
    (expected) =>
      document
        .querySelector('[data-testid="graph-counts"]')
        ?.textContent?.includes(`${expected.toLocaleString()} visible`),
    {},
    counts.nodes,
  );
  const allTypesFilterMs = Date.now() - filterStarted;
  await page.waitForSelector('[data-testid="graph-canvas"][aria-busy="false"]');
  await page.screenshot({
    path: path.join(artifacts, "graph-all-types.png"),
    fullPage: true,
  });
  const canvas = await page.$('[data-testid="graph-canvas"] canvas');
  assert.ok(canvas);
  const bounds = await canvas.boundingBox();
  assert.ok(bounds);
  // Keep the recursive browser callback outside tsx's function-name transform.
  const frames = page
    .evaluate(
      `new Promise((resolve) => {
    const samples = [];
    const start = performance.now();
    let previous = start;
    function sample(time) {
      samples.push(time - previous);
      previous = time;
      if (time - start < 2500) requestAnimationFrame(sample);
      else {
        samples.sort((left, right) => left - right);
        resolve({
          count: samples.length,
          medianMs: samples[Math.floor(samples.length / 2)],
          p95Ms: samples[Math.floor(samples.length * 0.95)],
          maxMs: samples.at(-1)
        });
      }
    }
    requestAnimationFrame(sample);
  })`,
    )
    .then(
      (value: unknown) => ({ value }),
      (error: unknown) => ({ error }),
    );
  await page.mouse.move(
    bounds.x + bounds.width * 0.4,
    bounds.y + bounds.height * 0.5,
  );
  await page.mouse.down();
  for (let index = 1; index <= 24; index++) {
    await page.mouse.move(
      bounds.x + bounds.width * (0.4 + index / 120),
      bounds.y + bounds.height * (0.5 + Math.sin(index / 4) * 0.08),
    );
    await delay(45);
  }
  await page.mouse.up();
  const frameResult = await frames;
  if ("error" in frameResult) throw frameResult.error;
  const orbitFrames = frameResult.value;
  assert.deepEqual(
    errors,
    [],
    "Real graph profile must not emit browser errors.",
  );
  assert.ok(
    [...responses.values()].some((response) => response.kind === "nodes"),
    "The profile must fetch node pages.",
  );
  assert.ok(
    [...responses.values()].some((response) => response.kind === "edges"),
    "The profile must fetch edge pages.",
  );
  assert.ok(
    [...responses.values()].every((response) => response.status === 200),
  );
  const timings = await page.evaluate(
    () =>
      (window as unknown as { graphProfileMetrics: unknown })
        .graphProfileMetrics,
  );
  assert.ok(timings && typeof timings === "object");
  for (const key of ["firstNodesMs", "firstSettledCanvasMs", "completeMs"]) {
    assert.ok(key in timings, "Missing timing: " + key);
    const value: unknown = (timings as Record<string, unknown>)[key];
    assert.ok(
      typeof value === "number" && Number.isFinite(value) && value >= 0,
    );
  }
  const report = {
    workspace,
    counts,
    timings,
    defaultVisible,
    allTypesFilterMs,
    orbitFrames,
    pages: ["nodes", "edges"].map((kind) => {
      const entries = [...responses.values()].filter(
        (response) => response.kind === kind,
      );
      return {
        kind,
        count: entries.length,
        transferredBytes: entries.reduce((sum, entry) => sum + entry.bytes, 0),
        encodings: [...new Set(entries.map((entry) => entry.encoding))],
      };
    }),
    errors,
    artifacts,
  };
  await writeFile(
    path.join(artifacts, "profile.json"),
    JSON.stringify(report, null, 2),
  );
  console.log(JSON.stringify(report));
} catch (error) {
  await page
    .screenshot({ path: path.join(artifacts, "failure.png"), fullPage: true })
    .catch(() => undefined);
  await writeFile(
    path.join(artifacts, "failure.json"),
    JSON.stringify(
      {
        error: String(error),
        errors,
        url: page.url(),
        body: await page
          .evaluate(() => document.body.innerText.slice(0, 24_000))
          .catch(() => ""),
      },
      null,
      2,
    ),
  );
  throw error;
} finally {
  await browser.close();
}
