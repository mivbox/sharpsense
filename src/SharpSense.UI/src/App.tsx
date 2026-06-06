import { Box, TextField, Stack } from "@mui/material";
import { useCallback, useState } from "react";
import { GraphViewport } from "./components/GraphViewport";
import { MemoryPanel } from "./components/MemoryPanel";
import { WorkspaceExplorerPanel } from "./components/WorkspaceExplorerPanel";
import { normalizeSelectedPaths, useScopedGraph } from "./hooks/useScopedGraph";
import { useWorkspaceTree } from "./hooks/useWorkspaceTree";

const GRAPH_BACKGROUND =
  "radial-gradient(circle at 16% 16%, rgba(6, 182, 212, 0.18) 0%, rgba(2, 6, 23, 0) 30%), " +
  "radial-gradient(circle at 82% 12%, rgba(168, 85, 247, 0.2) 0%, rgba(2, 6, 23, 0) 32%), " +
  "radial-gradient(circle at 54% 74%, rgba(59, 130, 246, 0.14) 0%, rgba(2, 6, 23, 0) 28%), " +
  "linear-gradient(180deg, #020617 0%, #0b1120 56%, #111827 100%)";

export default function App() {
  const [expandedPaths, setExpandedPaths] = useState<Set<string>>(() => new Set());
  const [searchText, setSearchText] = useState("");
  const [selectedPaths, setSelectedPaths] = useState<string[]>([]);
  const [showEdges, setShowEdges] = useState(false);
  const [selectedNodeId, setSelectedNodeId] = useState<number | null>(null);
  const tree = useWorkspaceTree(expandedPaths);
  const graphQuery = useScopedGraph(selectedPaths, tree.nodesByPath, showEdges);

  const handleToggleExpandedPath = useCallback((path: string) => {
    setExpandedPaths((current) => {
      const next = new Set(current);

      if (next.has(path)) {
        next.delete(path);
      } else {
        next.add(path);
      }

      return next;
    });
  }, []);

  const handleToggleSelectedPath = useCallback((path: string) => {
    setSelectedPaths((current) => toggleSelectedPath(current, path));
  }, []);

  return (
    <Box
      sx={{
        height: "100vh",
        width: "100vw",
        position: "relative",
        overflow: "hidden",
        backgroundColor: "#020617",
        backgroundImage: GRAPH_BACKGROUND,
        "&::before": {
          content: '""',
          position: "absolute",
          inset: 0,
          pointerEvents: "none",
          opacity: 0.2,
          backgroundImage:
            "linear-gradient(rgba(148, 163, 184, 0.08) 1px, transparent 1px), " +
            "linear-gradient(90deg, rgba(148, 163, 184, 0.08) 1px, transparent 1px)",
          backgroundSize: "48px 48px"
        }
      }}
    >
      <WorkspaceExplorerPanel
        graphData={graphQuery.data}
        graphErrorMessage={getErrorMessage(graphQuery.error)}
        isGraphLoading={graphQuery.isLoading || graphQuery.isFetching}
        isRootLoading={tree.isRootLoading}
        rows={tree.rows}
        searchText={searchText}
        selectedPaths={graphQuery.normalizedSelectedPaths}
        treeErrorMessage={tree.errorMessage}
        onSearchTextChange={setSearchText}
        onToggleExpandedPath={handleToggleExpandedPath}
        onToggleSelectedPath={handleToggleSelectedPath}
      />

      <GraphViewport
        errorMessage={getErrorMessage(graphQuery.error)}
        graphData={graphQuery.data}
        hasSelection={graphQuery.selectedDirectoryIds.length > 0}
        isLoading={graphQuery.isLoading}
        searchText={searchText}
        showEdges={showEdges}
        onToggleShowEdges={setShowEdges}
        onSelectNode={setSelectedNodeId}
      />

      <Box
        sx={{
          position: "absolute",
          right: 16,
          top: 16,
          bottom: 16,
          width: 360,
          display: "flex",
          flexDirection: "column",
          gap: 1
        }}
      >
        <Stack direction="row" spacing={1} alignItems="center">
          <TextField
            type="number"
            size="small"
            label="Node ID"
            value={selectedNodeId ?? ""}
            onChange={(event) => {
              const raw = event.target.value.trim();
              if (raw === "") {
                setSelectedNodeId(null);
                return;
              }
              const parsed = Number(raw);
              setSelectedNodeId(Number.isFinite(parsed) && parsed > 0 ? parsed : null);
            }}
            sx={{ flex: 1, backgroundColor: "rgba(2, 6, 23, 0.7)", borderRadius: 1 }}
          />
        </Stack>
        <Box sx={{ flex: 1, minHeight: 0 }}>
          <MemoryPanel nodeId={selectedNodeId} />
        </Box>
      </Box>
    </Box>
  );
}

function toggleSelectedPath(selectedPaths: string[], path: string): string[] {
  const normalizedPath = normalizePath(path);
  const normalizedSelectedPaths = normalizeSelectedPaths(selectedPaths);

  if (normalizedSelectedPaths.includes(normalizedPath)) {
    return normalizedSelectedPaths.filter((selectedPath) => selectedPath !== normalizedPath);
  }

  const hasSelectedAncestor = normalizedSelectedPaths.some(
    (selectedPath) =>
      selectedPath === normalizedPath || normalizedPath.startsWith(`${selectedPath}/`)
  );
  if (hasSelectedAncestor) {
    return normalizedSelectedPaths;
  }

  return normalizeSelectedPaths([...normalizedSelectedPaths, normalizedPath]);
}

function normalizePath(path: string): string {
  return path.trim().replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
}

function getErrorMessage(error: unknown): string | null {
  return error instanceof Error ? error.message : null;
}
