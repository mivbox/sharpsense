import AccountTreeOutlinedIcon from "@mui/icons-material/AccountTreeOutlined";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import DescriptionOutlinedIcon from "@mui/icons-material/DescriptionOutlined";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import FolderOutlinedIcon from "@mui/icons-material/FolderOutlined";
import {
  Box,
  Checkbox,
  CircularProgress,
  IconButton,
  Stack,
  Typography
} from "@mui/material";
import { Virtuoso } from "react-virtuoso";
import type { WorkspaceTreeRow } from "../types/workspaceTree";

type WorkspaceTreeProps = {
  rows: WorkspaceTreeRow[];
  selectedPaths: string[];
  onToggleExpandedPath: (path: string) => void;
  onToggleSelectedPath: (path: string) => void;
};

export function WorkspaceTree({
  rows,
  selectedPaths,
  onToggleExpandedPath,
  onToggleSelectedPath
}: WorkspaceTreeProps) {
  if (rows.length === 0) {
    return (
      <Stack
        alignItems="center"
        justifyContent="center"
        sx={{ height: "100%", px: 2, textAlign: "center" }}
      >
        <Typography variant="body2" color="text.secondary">
          No analyzed folders or files are available yet.
        </Typography>
      </Stack>
    );
  }

  return (
    <Virtuoso
      data={rows}
      style={{ height: "100%", width: "100%" }}
      itemContent={(_, row) => {
        const isChecked = selectedPaths.includes(row.node.path);
        const isIndeterminate =
          !isChecked &&
          selectedPaths.some((selectedPath) =>
            selectedPath.startsWith(`${row.node.path}/`)
          );

        return (
          <Box
            key={row.node.path}
            data-tree-path={row.node.path}
            sx={{
              px: 1,
              py: 0.25
            }}
          >
            <Stack
              direction="row"
              alignItems="center"
              spacing={0.5}
              sx={{
                minHeight: 40,
                pl: row.depth * 2
              }}
            >
              {row.node.hasChildren ? (
                <IconButton
                  size="small"
                  data-expand-path={row.node.path}
                  onClick={() => {
                    onToggleExpandedPath(row.node.path);
                  }}
                  sx={{ color: "text.secondary" }}
                >
                  {row.isExpanded ? (
                    <ExpandMoreIcon fontSize="small" />
                  ) : (
                    <ChevronRightIcon fontSize="small" />
                  )}
                </IconButton>
              ) : (
                <Box sx={{ width: 32 }} />
              )}

              <Checkbox
                data-tree-checkbox-trigger={row.node.path}
                size="small"
                checked={isChecked}
                indeterminate={isIndeterminate}
                disabled={!row.node.isSelectable}
                inputProps={{
                  "data-tree-checkbox": row.node.path
                }}
                onChange={() => {
                  onToggleSelectedPath(row.node.path);
                }}
              />

              <Box sx={{ color: "text.secondary", display: "flex", alignItems: "center" }}>
                {renderNodeIcon(row.node.kind)}
              </Box>

              <Stack
                direction="row"
                spacing={1}
                alignItems="center"
                justifyContent="space-between"
                sx={{ flex: 1, minWidth: 0 }}
              >
                <Typography
                  variant="body2"
                  sx={{
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap"
                  }}
                >
                  {row.node.label}
                </Typography>

                <Stack direction="row" spacing={0.75} alignItems="center">
                  {row.node.childCount !== null && (
                    <Typography variant="caption" color="text.secondary">
                      {row.node.childCount.toLocaleString()}
                    </Typography>
                  )}
                  {row.isLoading && <CircularProgress size={12} />}
                </Stack>
              </Stack>
            </Stack>
          </Box>
        );
      }}
    />
  );
}

function renderNodeIcon(kind: string) {
  switch (kind) {
    case "folder":
      return <FolderOutlinedIcon fontSize="small" />;
    case "project":
      return <AccountTreeOutlinedIcon fontSize="small" />;
    default:
      return <DescriptionOutlinedIcon fontSize="small" />;
  }
}
