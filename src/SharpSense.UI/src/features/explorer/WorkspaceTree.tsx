import { useState } from "react";
import {
  Box,
  Checkbox,
  CircularProgress,
  IconButton,
  InputAdornment,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import ChevronRightRoundedIcon from "@mui/icons-material/ChevronRightRounded";
import ExpandMoreRoundedIcon from "@mui/icons-material/ExpandMoreRounded";
import FolderOutlinedIcon from "@mui/icons-material/FolderOutlined";
import DescriptionOutlinedIcon from "@mui/icons-material/DescriptionOutlined";
import AccountTreeOutlinedIcon from "@mui/icons-material/AccountTreeOutlined";
import SearchRoundedIcon from "@mui/icons-material/SearchRounded";
import RefreshRoundedIcon from "@mui/icons-material/RefreshRounded";
import { useQueryClient } from "@tanstack/react-query";
import { useWorkspaceTree } from "./useExplorer";
import { EmptyState, ErrorState, LoadingRows } from "../../shared/ui/States";
type TreeState = ReturnType<typeof useWorkspaceTree>;
export function WorkspaceTree({
  tree,
  selectedPaths,
  onToggleExpanded,
  onToggleSelected,
}: {
  tree: TreeState;
  selectedPaths: string[];
  onToggleExpanded: (path: string) => void;
  onToggleSelected: (path: string) => void;
}) {
  const [filter, setFilter] = useState("");
  const queryClient = useQueryClient();
  const visible = tree.rows.filter(
    (row) =>
      !filter || row.node.path.toLowerCase().includes(filter.toLowerCase()),
  );
  return (
    <Box
      data-testid="workspace-explorer-panel"
      sx={{
        height: "100%",
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
      }}
    >
      <Stack
        direction="row"
        sx={{
          alignItems: "center",
          justifyContent: "space-between",
          p: 2,
          pb: 1,
        }}
      >
        <Typography variant="subtitle2">Workspace</Typography>
        <Tooltip title="Refresh workspace tree">
          <IconButton
            size="small"
            aria-label="Refresh workspace tree"
            onClick={() => {
              void queryClient.invalidateQueries({ queryKey: ["tree"] });
            }}
          >
            <RefreshRoundedIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </Stack>
      <Box sx={{ px: 2, pb: 2 }}>
        <TextField
          fullWidth
          size="small"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder="Filter loaded paths"
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchRoundedIcon fontSize="small" />
                </InputAdornment>
              ),
            },
            htmlInput: { "aria-label": "Filter loaded workspace paths" },
          }}
        />
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mt: 1 }}
        >
          Select folders to define your graph.
        </Typography>
      </Box>
      <Box sx={{ flex: 1, overflow: "auto", minHeight: 0 }}>
        {tree.root.isPending ? (
          <LoadingRows />
        ) : tree.root.error ? (
          <Box sx={{ px: 1 }}>
            <ErrorState
              error={tree.root.error}
              retry={() => {
                void tree.root.refetch();
              }}
            />
          </Box>
        ) : visible.length === 0 ? (
          <EmptyState
            compact
            icon={<FolderOutlinedIcon />}
            title={filter ? "No loaded paths match" : "No indexed files yet"}
            description={
              filter
                ? "Expand a folder or clear your filter to see more paths."
                : "Analyze this workspace to explore its selected code and documentation."
            }
          />
        ) : (
          <List dense disablePadding aria-label="Workspace folders">
            {visible.map((row) => {
              const includedByParent = selectedPaths.some(
                (path) =>
                  path !== row.node.path &&
                  (path === "/" || row.node.path.startsWith(path + "/")),
              );
              const checked = selectedPaths.includes(row.node.path);
              const indeterminate =
                !checked &&
                selectedPaths.some(
                  (path) =>
                    row.node.path === "/" ||
                    path.startsWith(row.node.path + "/"),
                );
              const Icon =
                row.node.kind === "project"
                  ? AccountTreeOutlinedIcon
                  : row.node.kind === "file"
                    ? DescriptionOutlinedIcon
                    : FolderOutlinedIcon;
              return (
                <Box key={row.node.path} data-tree-path={row.node.path}>
                  <ListItem
                    disablePadding
                    sx={{ pl: Math.min(row.depth, 5) * 1.5 }}
                  >
                    {row.node.hasChildren ? (
                      <IconButton
                        size="small"
                        aria-label={`${row.expanded ? "Collapse" : "Expand"} ${row.node.label}`}
                        aria-expanded={row.expanded}
                        data-expand-path={row.node.path}
                        onClick={() => onToggleExpanded(row.node.path)}
                      >
                        {row.loading ? (
                          <CircularProgress size={16} />
                        ) : row.expanded ? (
                          <ExpandMoreRoundedIcon fontSize="small" />
                        ) : (
                          <ChevronRightRoundedIcon fontSize="small" />
                        )}
                      </IconButton>
                    ) : (
                      <Box sx={{ width: 34, flexShrink: 0 }} />
                    )}
                    <ListItemButton
                      dense
                      selected={checked}
                      onClick={() => {
                        if (row.node.isSelectable && !includedByParent)
                          onToggleSelected(row.node.path);
                      }}
                      sx={{ pl: 0, pr: 1 }}
                    >
                      <Checkbox
                        size="small"
                        checked={checked || includedByParent}
                        indeterminate={indeterminate}
                        disabled={!row.node.isSelectable || includedByParent}
                        onClick={(event) => event.stopPropagation()}
                        onChange={() => onToggleSelected(row.node.path)}
                        data-tree-checkbox-trigger={row.node.path}
                        slotProps={{
                          input: { "aria-label": `Include ${row.node.path}` },
                        }}
                        sx={{ p: 0.5 }}
                      />
                      <ListItemIcon sx={{ minWidth: 25, ml: 0.5 }}>
                        <Icon
                          fontSize="small"
                          color={
                            row.node.kind === "project" ? "primary" : "inherit"
                          }
                        />
                      </ListItemIcon>
                      <ListItemText
                        primary={row.node.label}
                        title={row.node.path}
                        slotProps={{
                          primary: {
                            noWrap: true,
                            variant: "caption",
                          },
                        }}
                      />
                    </ListItemButton>
                  </ListItem>
                  {row.error != null && (
                    <Box sx={{ mx: 1, my: 0.5 }}>
                      <ErrorState
                        error={row.error}
                        retry={() => {
                          void queryClient.invalidateQueries({
                            queryKey: ["tree", row.node.path],
                          });
                        }}
                      />
                    </Box>
                  )}
                </Box>
              );
            })}
          </List>
        )}
      </Box>
      <Box sx={{ p: 2 }}>
        <Typography variant="caption" color="text.secondary">
          {selectedPaths.length
            ? `${selectedPaths.length} selected scope${selectedPaths.length === 1 ? "" : "s"}`
            : "No scopes selected"}
        </Typography>
      </Box>
    </Box>
  );
}
