import assert from "node:assert/strict";
import test from "node:test";
import { absoluteDiscoveredSource } from "../src/features/workspaces/sourceDiscovery";

test("discovered sources retain canonical repository location and glob syntax", () => {
  const source = { kind: "Markdown" as const, path: "docs/**/*.{md,mdx}" };
  assert.deepEqual(
    absoluteDiscoveredSource("/repos/Example project/", source),
    {
      kind: "Markdown",
      path: "/repos/Example project/docs/**/*.{md,mdx}",
    },
  );
  assert.equal(source.path, "docs/**/*.{md,mdx}");
  assert.equal(
    absoluteDiscoveredSource("/", source).path,
    "/docs/**/*.{md,mdx}",
  );
});

test("Windows and UNC roots preserve server filesystem paths without browser URL resolution", () => {
  assert.equal(
    absoluteDiscoveredSource("C:\\repos\\Example", {
      kind: "CSharp",
      path: "src\\App.csproj",
    }).path,
    "C:/repos/Example/src/App.csproj",
  );
  assert.equal(
    absoluteDiscoveredSource("\\\\server\\share\\repo", {
      kind: "TypeScript",
      path: "app/tsconfig.json",
    }).path,
    "//server/share/repo/app/tsconfig.json",
  );
});
