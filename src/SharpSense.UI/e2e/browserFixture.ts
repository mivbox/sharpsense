import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import puppeteer from "puppeteer";
import { preview, type ProxyOptions } from "vite";

export function launchBrowser(
  options: {
    args?: string[];
    includeEdge?: boolean;
  } = {},
) {
  const candidates = [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
    ...(options.includeEdge
      ? ["/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"]
      : []),
  ];
  return puppeteer.launch({
    headless: true,
    executablePath:
      process.env.PUPPETEER_EXECUTABLE_PATH ??
      (process.platform === "darwin" ? candidates.find(existsSync) : undefined),
    args:
      options.args ??
      (process.env.CI ? ["--no-sandbox", "--disable-dev-shm-usage"] : []),
  });
}

export async function startUiPreview(
  options: {
    proxy?: Record<string, string | ProxyOptions>;
  } = {},
) {
  const root = fileURLToPath(new URL("../", import.meta.url));
  assert.ok(
    existsSync(path.join(root, "dist/index.html")),
    "Build the UI before browser tests.",
  );
  const server = await preview({
    configFile: false,
    root,
    preview: {
      host: "127.0.0.1",
      port: 0,
      strictPort: true,
      proxy: options.proxy,
    },
  });
  const close = () =>
    new Promise<void>((resolve, reject) =>
      server.httpServer.close((error) => (error ? reject(error) : resolve())),
    );
  try {
    const address = server.httpServer.address();
    assert.ok(address && typeof address === "object");
    return { baseUrl: "http://127.0.0.1:" + address.port, close };
  } catch (error) {
    await close();
    throw error;
  }
}
