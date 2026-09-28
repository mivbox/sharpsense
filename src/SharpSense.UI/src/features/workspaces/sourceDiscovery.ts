import type { WorkspaceSource } from "./models";

/** Discovery paths belong to its canonical root, not necessarily the entered directory. */
export function absoluteDiscoveredSource(
  repositoryRoot: string,
  source: WorkspaceSource,
): WorkspaceSource {
  const root = repositoryRoot.replaceAll("\\", "/").replace(/\/+$/, "");
  const path = source.path.replaceAll("\\", "/").replace(/^\/+/, "");
  return { ...source, path: `${root}/${path}` };
}
