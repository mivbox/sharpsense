import SearchIcon from "@mui/icons-material/Search";
import {
  Alert,
  Box,
  Chip,
  CircularProgress,
  Divider,
  InputAdornment,
  Paper,
  Stack,
  TextField,
  Typography
} from "@mui/material";
import { WorkspaceTree } from "./WorkspaceTree";
import type { GraphApiResponse } from "../types/graph";
import type { WorkspaceTreeRow } from "../types/workspaceTree";

type WorkspaceExplorerPanelProps = {
  graphData: GraphApiResponse;
  graphErrorMessage: string | null;
  isGraphLoading: boolean;
  isRootLoading: boolean;
  rows: WorkspaceTreeRow[];
  searchText: string;
  selectedPaths: string[];
  treeErrorMessage: string | null;
  onSearchTextChange: (value: string) => void;
  onToggleExpandedPath: (path: string) => void;
  onToggleSelectedPath: (path: string) => void;
};

export function WorkspaceExplorerPanel({
  graphData,
  graphErrorMessage,
  isGraphLoading,
  isRootLoading,
  rows,
  searchText,
  selectedPaths,
  treeErrorMessage,
  onSearchTextChange,
  onToggleExpandedPath,
  onToggleSelectedPath
}: WorkspaceExplorerPanelProps) {
  return (
    <Paper
      data-testid="workspace-explorer-panel"
      elevation={8}
      sx={{
        position: "absolute",
        top: 24,
        left: 24,
        bottom: 24,
        zIndex: 10,
        width: { xs: "calc(100% - 48px)", md: 420 },
        p: 2,
        display: "flex",
        flexDirection: "column",
        border: "1px solid rgba(148, 163, 184, 0.16)",
        backgroundColor: "rgba(2, 6, 23, 0.72)",
        backdropFilter: "blur(18px)",
        boxShadow: "0 24px 80px rgba(2, 6, 23, 0.45)"
      }}
    >
      <Stack spacing={1.5} sx={{ minHeight: 0, flex: 1 }}>
        <Stack spacing={0.5}>
          <Typography variant="h6">Workspace Explorer</Typography>
          <Typography variant="body2" color="text.secondary">
            Select analyzed folders or files to opt into graph scope. The canvas starts empty.
          </Typography>
        </Stack>

        <TextField
          fullWidth
          value={searchText}
          onChange={(event) => {
            onSearchTextChange(event.target.value);
          }}
          placeholder="Filter loaded graph nodes by label, node ID, or type"
          InputProps={{
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon color="primary" />
              </InputAdornment>
            )
          }}
        />

        <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
          <Chip size="small" color="primary" label={`${selectedPaths.length} selected`} />
          <Chip size="small" variant="outlined" label={`${graphData.nodes.length} nodes`} />
          <Chip size="small" variant="outlined" label={`${graphData.edges.length} edges`} />
          {isGraphLoading && <CircularProgress size={18} />}
        </Stack>

        {treeErrorMessage && <Alert severity="error">{treeErrorMessage}</Alert>}
        {graphErrorMessage && <Alert severity="error">{graphErrorMessage}</Alert>}

        <Divider />

        <Box sx={{ flex: 1, minHeight: 0 }}>
          {isRootLoading ? (
            <Stack alignItems="center" justifyContent="center" sx={{ height: "100%" }} spacing={1}>
              <CircularProgress />
              <Typography variant="body2" color="text.secondary">
                Loading analyzed tree...
              </Typography>
            </Stack>
          ) : (
            <WorkspaceTree
              rows={rows}
              selectedPaths={selectedPaths}
              onToggleExpandedPath={onToggleExpandedPath}
              onToggleSelectedPath={onToggleSelectedPath}
            />
          )}
        </Box>
      </Stack>
    </Paper>
  );
}
