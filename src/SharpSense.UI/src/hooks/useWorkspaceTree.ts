import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ROOT_TREE_PATH,
  type WorkspaceTreeNode,
  type WorkspaceTreeResponse,
  type WorkspaceTreeRow
} from "../types/workspaceTree";

const TREE_ENDPOINT = "/api/tree";

type UseWorkspaceTreeResult = {
  errorMessage: string | null;
  hasRootNodes: boolean;
  isRootLoading: boolean;
  nodesByPath: Record<string, WorkspaceTreeNode>;
  rows: WorkspaceTreeRow[];
};

export function useWorkspaceTree(expandedPaths: Set<string>): UseWorkspaceTreeResult {
  const [errorByPath, setErrorByPath] = useState<Record<string, string>>({});
  const [loadedPaths, setLoadedPaths] = useState<Set<string>>(() => new Set());
  const [loadingPaths, setLoadingPaths] = useState<Set<string>>(() => new Set());
  const [nodesByParentPath, setNodesByParentPath] = useState<Record<string, WorkspaceTreeNode[]>>({});
  const [nodesByPath, setNodesByPath] = useState<Record<string, WorkspaceTreeNode>>({});
  const loadedPathsRef = useRef(new Set<string>());
  const loadingPathsRef = useRef(new Set<string>());

  const loadPath = useCallback(async (path: string) => {
    const normalizedPath = normalizeTreePath(path);
    if (
      loadingPathsRef.current.has(normalizedPath) ||
      loadedPathsRef.current.has(normalizedPath)
    ) {
      return;
    }

    loadingPathsRef.current = new Set(loadingPathsRef.current).add(normalizedPath);
    setLoadingPaths(new Set(loadingPathsRef.current));
    setErrorByPath((current) => {
      const next = { ...current };
      delete next[normalizedPath];
      return next;
    });

    try {
      const result = await fetchTree(normalizedPath);
      setNodesByParentPath((current) => ({
        ...current,
        [result.parentPath]: result.nodes
      }));
      setNodesByPath((current) => {
        const next = { ...current };
        result.nodes.forEach((node) => {
          next[node.path] = node;
        });
        return next;
      });
      loadedPathsRef.current = new Set(loadedPathsRef.current).add(normalizedPath);
      setLoadedPaths(new Set(loadedPathsRef.current));
    } catch (error) {
      setErrorByPath((current) => ({
        ...current,
        [normalizedPath]: getErrorMessage(error)
      }));
    } finally {
      const nextLoadingPaths = new Set(loadingPathsRef.current);
      nextLoadingPaths.delete(normalizedPath);
      loadingPathsRef.current = nextLoadingPaths;
      setLoadingPaths(new Set(nextLoadingPaths));
    }
  }, []);

  useEffect(() => {
    void loadPath(ROOT_TREE_PATH);
  }, [loadPath]);

  useEffect(() => {
    expandedPaths.forEach((path) => {
      const node = nodesByPath[path];
      if (node?.hasChildren) {
        void loadPath(path);
      }
    });
  }, [expandedPaths, loadPath, nodesByPath]);

  const rows = useMemo(
    () =>
      flattenTree(
        nodesByParentPath,
        expandedPaths,
        loadingPaths,
        ROOT_TREE_PATH,
        0
      ),
    [expandedPaths, loadingPaths, nodesByParentPath]
  );

  return {
    errorMessage: errorByPath[ROOT_TREE_PATH] ?? null,
    hasRootNodes: (nodesByParentPath[ROOT_TREE_PATH] ?? []).length > 0,
    isRootLoading:
      loadingPaths.has(ROOT_TREE_PATH) && (nodesByParentPath[ROOT_TREE_PATH] ?? []).length === 0,
    nodesByPath,
    rows
  };
}

async function fetchTree(path: string): Promise<WorkspaceTreeResponse> {
  const searchParams = new URLSearchParams();
  searchParams.set("path", path);

  const response = await fetch(`${TREE_ENDPOINT}?${searchParams.toString()}`);
  if (!response.ok) {
    throw new Error(`Tree request failed with ${response.status}.`);
  }

  return (await response.json()) as WorkspaceTreeResponse;
}

function flattenTree(
  nodesByParentPath: Record<string, WorkspaceTreeNode[]>,
  expandedPaths: Set<string>,
  loadingPaths: Set<string>,
  parentPath: string,
  depth: number
): WorkspaceTreeRow[] {
  const children = nodesByParentPath[parentPath] ?? [];

  return children.flatMap((node) => {
    const row: WorkspaceTreeRow = {
      depth,
      isExpanded: expandedPaths.has(node.path),
      isLoading: loadingPaths.has(node.path),
      node
    };

    if (!row.isExpanded || !node.hasChildren) {
      return [row];
    }

    return [
      row,
      ...flattenTree(nodesByParentPath, expandedPaths, loadingPaths, node.path, depth + 1)
    ];
  });
}

function normalizeTreePath(path: string): string {
  const trimmedPath = path.trim();

  return trimmedPath.length === 0 ? ROOT_TREE_PATH : trimmedPath;
}

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Unable to load tree data.";
}
