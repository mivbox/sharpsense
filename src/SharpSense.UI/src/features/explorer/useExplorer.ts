import { useQueries, useQuery } from "@tanstack/react-query";
import { useWorkspaceApi } from "../../shared/workspace/context";
import type { TreeNode } from "../../shared/api/models";
import { ancestorPaths } from "./workspacePaths";

export { useScopedGraph } from "./useGraphPages";

export function useWorkspaceTree(
  expandedPaths: Set<string>,
  selectedPaths: string[] = [],
) {
  const { getTree } = useWorkspaceApi();
  const root = useQuery({
    queryKey: ["tree", "/"],
    queryFn: ({ signal }) => getTree("/", signal),
  });
  const requiredParents = selectedPaths.flatMap(ancestorPaths);
  const paths = [...new Set([...expandedPaths, ...requiredParents])]
    .filter((path) => path !== "/" && path !== "")
    .sort();
  const children = useQueries({
    queries: paths.map((path) => ({
      queryKey: ["tree", path],
      queryFn: ({ signal }: { signal: AbortSignal }) => getTree(path, signal),
    })),
  });
  const byParent = new Map<string, TreeNode[]>();
  byParent.set("/", root.data?.nodes ?? []);
  const loadingPaths = new Set<string>();
  const errors = new Map<string, unknown>();
  children.forEach((query, index) => {
    byParent.set(paths[index], query.data?.nodes ?? []);
    if (query.isPending) loadingPaths.add(paths[index]);
    if (query.error) errors.set(paths[index], query.error);
  });
  const nodesByPath = new Map<string, TreeNode>();
  for (const nodes of byParent.values())
    for (const node of nodes) nodesByPath.set(node.path, node);
  const rootNode: TreeNode | null = root.data?.parentDirectoryId
    ? {
        id: root.data.parentDirectoryId,
        parentId: null,
        path: "/",
        label: "Entire workspace",
        kind: "folder",
        hasChildren: root.data.nodes.length > 0,
        childCount: root.data.nodes.length,
        isSelectable: true,
      }
    : null;
  if (rootNode) nodesByPath.set(rootNode.path, rootNode);
  const rows: {
    node: TreeNode;
    depth: number;
    expanded: boolean;
    loading: boolean;
    error: unknown;
  }[] = [];
  const append = (path: string, depth: number) => {
    for (const node of byParent.get(path) ?? []) {
      rows.push({
        node,
        depth,
        expanded: expandedPaths.has(node.path),
        loading: loadingPaths.has(node.path),
        error: errors.get(node.path),
      });
      if (expandedPaths.has(node.path) && depth < 60)
        append(node.path, depth + 1);
    }
  };
  if (rootNode) {
    rows.push({
      node: rootNode,
      depth: 0,
      expanded: expandedPaths.has("/"),
      loading: root.isPending,
      error: root.error,
    });
    if (expandedPaths.has("/")) append("/", 1);
  } else {
    append("/", 0);
  }
  return { root, rows, nodesByPath };
}
export function normalizePaths(paths: string[]) {
  if (paths.includes("/")) return ["/"];

  return [...new Set(paths)]
    .sort()
    .reduce<string[]>(
      (result, path) =>
        result.some(
          (parent) =>
            parent === "/" || path === parent || path.startsWith(parent + "/"),
        )
          ? result
          : [...result, path],
      [],
    );
}
