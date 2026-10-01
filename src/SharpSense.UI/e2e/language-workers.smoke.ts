import assert from "node:assert/strict";
import { spawn, type ChildProcess } from "node:child_process";
import { existsSync } from "node:fs";
import {
  mkdir,
  mkdtemp,
  readFile,
  realpath,
  rename,
  rm,
  writeFile,
} from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
import { stopChildProcess } from "./stopChildProcess";
import { availablePort } from "./availablePort";

type Node = {
  id: number;
  label: string;
  type: string;
  relativePath?: string | null;
};
type Edge = { source: number; target: number; type: string };
type Graph = { nodes: Node[]; edges: Edge[]; revision: string };
type Page<T> = { items: T[]; revision: string; nextCursor?: string | null };
type Status = {
  state: string;
  revision: number;
  diagnostics?: { message?: string }[];
};
type SourceContribution = { language: string; action: string };
class ApiFailure extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

const here = path.dirname(fileURLToPath(import.meta.url));
const cli =
  process.env.SHARPSENSE_E2E_CLI_DLL ??
  path.resolve(
    here,
    "../../SharpSense.Cli/bin/Release/net10.0/SharpSense.Cli.dll",
  );
assert.ok(
  existsSync(cli),
  "Build the Release CLI before the language worker smoke.",
);
const repository = await realpath(
  await mkdtemp(path.join(tmpdir(), "sharpsense-language-fixture-")),
);
const home = await mkdtemp(path.join(tmpdir(), "sharpsense-language-home-"));
const artifacts =
  process.env.SHARPSENSE_E2E_ARTIFACTS ??
  (await mkdtemp(path.join(tmpdir(), "sharpsense-language-artifacts-")));
await mkdir(artifacts, { recursive: true });
let server: ChildProcess | undefined;
let serverOutput = "";
let serverError: Error | undefined;
let baseUrl = "";
let workspaceId = "";
let directoryId = 0;
const sourceContributions: SourceContribution[] = [];
let pendingLogLine = "";
const snapshots: {
  stage: string;
  revision: string;
  nodeCount: number;
  edgeCount: number;
  contributions: SourceContribution[];
}[] = [];

try {
  await mkdir(path.join(repository, ".git"));
  await mkdir(path.join(repository, "frontend/src"), { recursive: true });
  await mkdir(path.join(repository, "docs"));
  await writeFile(path.join(repository, ".git/HEAD"), "ref: refs/heads/main\n");
  await writeFile(
    path.join(repository, "NuGet.Config"),
    "<configuration><packageSources><clear /></packageSources></configuration>",
  );
  await writeFile(
    path.join(repository, "Fixture.csproj"),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="App.cs" /></ItemGroup></Project>',
  );
  await writeFile(path.join(repository, "App.cs"), csharp("CodeBefore"));
  await writeFile(
    path.join(repository, "frontend/tsconfig.json"),
    JSON.stringify({
      compilerOptions: { target: "ES2022", module: "ESNext", strict: true },
      include: ["src/**/*.ts"],
    }),
  );
  await writeFile(
    path.join(repository, "frontend/src/index.ts"),
    typescript("tsBefore"),
  );
  await writeFile(
    path.join(repository, "docs/guide.md"),
    "# Guide\n\n[Extra](extra.md)\n",
  );
  await command("dotnet", [
    "restore",
    path.join(repository, "Fixture.csproj"),
    "--ignore-failed-sources",
    "--verbosity",
    "quiet",
  ]);
  await command("dotnet", [
    cli,
    "workspace",
    "create",
    "language-workers",
    "--repo-root",
    repository,
    "--csharp",
    "Fixture.csproj",
    "--typescript",
    "frontend/tsconfig.json",
    "--markdown",
    "docs/**/*.md",
  ]);
  baseUrl = "http://127.0.0.1:" + (await availablePort());
  server = spawn("dotnet", [cli, "ui", "--verbose", "--url", baseUrl], {
    cwd: repository,
    env: environment(),
    stdio: ["ignore", "pipe", "pipe"],
  });
  const capture = (chunk: Buffer) => {
    const text = chunk.toString();
    serverOutput = (serverOutput + text).slice(-2_000_000);
    const lines = (pendingLogLine + text).split("\n");
    pendingLogLine = lines.pop() ?? "";
    for (const line of lines) {
      const match =
        /Workspace source (CSharp|TypeScript|Markdown) '[^']*': (reused|extracted)\./.exec(
          line,
        );
      if (match)
        sourceContributions.push({ language: match[1]!, action: match[2]! });
    }
  };
  server.stdout?.on("data", capture);
  server.stderr?.on("data", capture);
  server.on("error", (error) => {
    serverError = error;
  });
  await waitUntil(async () => {
    try {
      return Boolean(await api("/api/workspaces"));
    } catch (error) {
      if (error instanceof TypeError) return false;
      throw error;
    }
  });
  const catalog = await api<{ workspaces: { id: string }[] }>(
    "/api/workspaces",
  );
  assert.equal(catalog.workspaces.length, 1);
  workspaceId = catalog.workspaces[0]!.id;
  const definitionPath = path.join(
    home,
    "workspaces",
    workspaceId,
    "workspace.yaml",
  );
  const definition = await readFile(definitionPath, "utf8");
  await api(`/api/workspaces/${workspaceId}/indexing`, "POST", {
    watch: true,
    skipEmbeddings: true,
  });
  await waitUntil(async () => (await status()).state === "watching");
  directoryId = (await api<{ parentDirectoryId: number }>("/api/tree?path=%2F"))
    .parentDirectoryId;
  const baseline = await graph();
  assert.ok(
    has(baseline, "CodeBefore") &&
      has(baseline, "tsBefore") &&
      has(baseline, "guide.md#guide"),
    "Initial graph must contain all three selected languages.",
  );
  await waitUntil(
    async () =>
      new Set(sourceContributions.map((item) => item.language)).size === 3,
  );
  record("initial", baseline, [...sourceContributions]);
  const originalCode = identities(baseline, isCode);

  const modified = await change(
    "docs modified",
    () =>
      writeFile(
        path.join(repository, "docs/guide.md"),
        "# Guide\n\n## Doc added\n\n[Extra](extra.md)\n",
      ),
    (value) => has(value, "doc-added"),
  );
  assert.deepEqual(
    identities(modified, isCode),
    originalCode,
    "Existing documentation edits must preserve C# and TypeScript identities.",
  );
  const modifiedActions = snapshots.at(-1)!.contributions;
  for (const language of ["CSharp", "TypeScript"]) {
    assert.ok(
      modifiedActions.some(
        (item) => item.language === language && item.action === "reused",
      ),
      `${language} extraction must actually be skipped for ordinary existing documentation edits.`,
    );
    assert.equal(
      modifiedActions.some(
        (item) => item.language === language && item.action === "extracted",
      ),
      false,
      `${language} must not be re-extracted merely because standard SDK analyzers are present.`,
    );
  }
  assert.ok(
    modifiedActions.some(
      (item) => item.language === "Markdown" && item.action === "extracted",
    ),
  );
  const added = await change(
    "docs added",
    () => writeFile(path.join(repository, "docs/extra.md"), "# Extra\n"),
    (value) => has(value, "extra.md#extra"),
  );
  assert.deepEqual(identities(added, isCode), originalCode);
  const extraIds = new Set(
    added.nodes
      .filter((node) => node.relativePath === "docs/extra.md")
      .map((node) => node.id),
  );
  const guideIds = new Set(
    added.nodes
      .filter((node) => node.relativePath === "docs/guide.md")
      .map((node) => node.id),
  );
  assert.ok(
    added.edges.some(
      (edge) =>
        edge.type === "documentlink" &&
        extraIds.has(edge.target) &&
        guideIds.has(edge.source),
    ),
    "Creating a previously missing document must resolve its incoming link.",
  );
  const renamed = await change(
    "docs renamed",
    () =>
      rename(
        path.join(repository, "docs/extra.md"),
        path.join(repository, "docs/renamed.md"),
      ),
    (value) =>
      has(value, "renamed.md#extra") &&
      !value.nodes.some((node) => node.relativePath === "docs/extra.md"),
  );
  assert.deepEqual(identities(renamed, isCode), originalCode);
  const deleted = await change(
    "docs deleted",
    () => rm(path.join(repository, "docs/renamed.md")),
    (value) =>
      !value.nodes.some((node) => node.relativePath === "docs/renamed.md"),
  );
  assert.deepEqual(identities(deleted, isCode), originalCode);

  const csharpChanged = await change(
    "C# changed",
    () => writeFile(path.join(repository, "App.cs"), csharp("CodeAfter")),
    (value) => has(value, "CodeAfter") && !has(value, "CodeBefore"),
  );
  assert.deepEqual(
    identities(csharpChanged, isTypeScript),
    identities(baseline, isTypeScript),
  );
  assertCall(csharpChanged, "CodeAfter", "CodeAnchor");
  const typescriptChanged = await change(
    "TypeScript changed",
    () =>
      writeFile(
        path.join(repository, "frontend/src/index.ts"),
        typescript("tsAfter"),
      ),
    (value) => has(value, "tsAfter") && !has(value, "tsBefore"),
  );
  assert.deepEqual(
    identities(typescriptChanged, isCSharp),
    identities(csharpChanged, isCSharp),
  );
  assertHttpRequest(typescriptChanged, "tsAfter");
  const mixed = await change(
    "mixed changed",
    async () => {
      await Promise.all([
        writeFile(path.join(repository, "App.cs"), csharp("CodeMixed")),
        writeFile(
          path.join(repository, "frontend/src/index.ts"),
          typescript("tsMixed"),
        ),
        writeFile(
          path.join(repository, "docs/guide.md"),
          "# Guide\n\n## Mixed docs\n",
        ),
      ]);
    },
    (value) =>
      has(value, "CodeMixed") &&
      has(value, "tsMixed") &&
      has(value, "mixed-docs") &&
      !has(value, "CodeAfter") &&
      !has(value, "tsAfter"),
  );
  assertCall(mixed, "CodeMixed", "CodeAnchor");
  assertHttpRequest(mixed, "tsMixed");
  assert.deepEqual(
    identities(
      mixed,
      (node) =>
        node.label.includes("CodeAnchor") || node.label.includes("tsAnchor"),
    ),
    identities(
      baseline,
      (node) =>
        node.label.includes("CodeAnchor") || node.label.includes("tsAnchor"),
    ),
    "Unchanged declarations must retain identities through mixed edits.",
  );
  assert.equal(
    await readFile(definitionPath, "utf8"),
    definition,
    "Watch must not rewrite workspace sources.",
  );
  await api(`/api/workspaces/${workspaceId}/indexing`, "DELETE");
  await waitUntil(async () => (await status()).state === "stopped");
  await writeFile(
    path.join(artifacts, "snapshots.json"),
    JSON.stringify(snapshots, null, 2),
  );
  console.log(
    "Language worker smoke passed: mixed-language initial index; Markdown modify/add/rename/delete; C#, TypeScript and mixed watch edits; stable identities and updated relationships.",
  );
  console.log("Artifacts: " + artifacts);
} catch (error) {
  await writeFile(
    path.join(artifacts, "snapshots.json"),
    JSON.stringify(snapshots, null, 2),
  );
  console.error("Language worker artifacts: " + artifacts);
  throw error;
} finally {
  await stopChildProcess(server);
  await writeFile(path.join(artifacts, "server.log"), serverOutput);
  await rm(repository, { recursive: true, force: true });
  await rm(home, { recursive: true, force: true });
}

function environment() {
  return {
    ...process.env,
    SHARPSENSE_HOME: home,
    DOTNET_CLI_TELEMETRY_OPTOUT: "1",
  };
}
function csharp(method: string) {
  return `namespace Fixture; public static class App { public static int ${method}() => CodeAnchor(); public static int CodeAnchor() => 1; }\n`;
}
function typescript(method: string) {
  return `export function tsAnchor() { return 1; }\nexport function ${method}() { return fetch("/api/${method}"); }\n`;
}
function isCSharp(node: Node) {
  return node.relativePath === "App.cs";
}
function isTypeScript(node: Node) {
  return node.relativePath === "frontend/src/index.ts";
}
function isCode(node: Node) {
  return isCSharp(node) || isTypeScript(node);
}
function has(value: Graph, label: string) {
  return value.nodes.some((node) => node.label.includes(label));
}
function identities(value: Graph, predicate: (node: Node) => boolean) {
  return value.nodes
    .filter(predicate)
    .map((node) => [node.type, node.label, node.relativePath, node.id])
    .sort((left, right) =>
      JSON.stringify(left).localeCompare(JSON.stringify(right)),
    );
}
function assertCall(value: Graph, caller: string, callee: string) {
  const source = value.nodes.find((node) => node.label.includes(caller));
  const target = value.nodes.find((node) => node.label.includes(callee));
  assert.ok(
    source &&
      target &&
      value.edges.some(
        (edge) =>
          edge.type === "methodcall" &&
          edge.source === source.id &&
          edge.target === target.id,
      ),
    `${caller} must reference ${callee} after reconciliation.`,
  );
}
function assertHttpRequest(value: Graph, caller: string) {
  const source = value.nodes.find(
    (node) => isTypeScript(node) && node.label.includes(caller),
  );
  const target = value.nodes.find(
    (node) => node.type === "http" && node.label.includes(`/api/${caller}`),
  );
  assert.ok(
    source &&
      target &&
      value.edges.some(
        (edge) =>
          edge.type === "http-request" &&
          edge.source === source.id &&
          edge.target === target.id,
      ),
    `${caller} must retain its extracted HTTP relationship after reconciliation.`,
  );
}
function record(
  stage: string,
  value: Graph,
  contributions: SourceContribution[],
) {
  snapshots.push({
    stage,
    revision: value.revision,
    nodeCount: value.nodes.length,
    edgeCount: value.edges.length,
    contributions,
  });
}
async function api<T = unknown>(
  route: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  const response = await fetch(baseUrl + route, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(workspaceId ? { "X-SharpSense-Workspace": workspaceId } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(15_000),
  });
  if (!response.ok)
    throw new ApiFailure(
      response.status,
      `API ${method} ${route}: ${await response.text()}`,
    );
  return response.status === 204
    ? (undefined as T)
    : ((await response.json()) as T);
}
async function status() {
  return api<Status>(`/api/workspaces/${workspaceId}/indexing`);
}
async function graph(): Promise<Graph> {
  const nodes = await api<Page<Node>>(
    `/api/graph/nodes/page?directoryIds=${directoryId}&pageSize=5000`,
  );
  const edges = await api<Page<Edge>>(
    `/api/graph/edges/page?directoryIds=${directoryId}&pageSize=5000&revision=${encodeURIComponent(nodes.revision)}`,
  );
  assert.equal(nodes.nextCursor ?? null, null);
  assert.equal(edges.nextCursor ?? null, null);
  assert.equal(edges.revision, nodes.revision);
  const ids = new Set(nodes.items.map((node) => node.id));
  assert.ok(
    edges.items.every((edge) => ids.has(edge.source) && ids.has(edge.target)),
    "Snapshot edges must reference current graph nodes.",
  );
  return { nodes: nodes.items, edges: edges.items, revision: nodes.revision };
}
async function change(
  stage: string,
  mutation: () => Promise<unknown>,
  predicate: (value: Graph) => boolean,
) {
  const revision = (await status()).revision;
  const contributionOffset = sourceContributions.length;
  await mutation();
  let result: Graph | undefined;
  await waitUntil(async () => {
    const current = await status();
    assert.notEqual(
      current.state,
      "failed",
      JSON.stringify(current.diagnostics),
    );
    if (current.state !== "watching" || current.revision <= revision)
      return false;
    try {
      const value = await graph();
      if (!predicate(value)) return false;
      result = value;
      return true;
    } catch (error) {
      if (error instanceof ApiFailure && error.status === 409) return false;
      throw error;
    }
  });
  assert.ok(result);
  await waitUntil(
    async () =>
      new Set(
        sourceContributions
          .slice(contributionOffset)
          .map((item) => item.language),
      ).size === 3,
  );
  record(stage, result, sourceContributions.slice(contributionOffset));
  return result;
}
async function waitUntil(predicate: () => Promise<boolean>) {
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    if (serverError) throw serverError;
    if (server)
      assert.equal(
        server.exitCode === null && server.signalCode === null,
        true,
        serverOutput.slice(-4000),
      );
    if (await predicate()) return;
    await delay(150);
  }
  throw new Error(
    "Timed out waiting for language worker fixture. " +
      serverOutput.slice(-4000),
  );
}
async function command(file: string, args: string[]) {
  const child = spawn(file, args, {
    cwd: repository,
    env: environment(),
    stdio: ["ignore", "pipe", "pipe"],
  });
  let output = "";
  const capture = (chunk: Buffer) => {
    output = (output + chunk.toString()).slice(-16000);
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
