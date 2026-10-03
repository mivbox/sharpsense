import { Suspense, useCallback, useEffect, useState } from "react";
import { Outlet, useNavigate, useRouterState } from "@tanstack/react-router";
import { explorerDefaults, searchDefaults, toolsDefaults } from "./searchState";
import { useWorkspaceOverview } from "../shared/api/queries";
import {
  AppBar,
  BottomNavigation,
  BottomNavigationAction,
  Box,
  Button,
  Chip,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  MenuItem,
  TextField,
  Skeleton,
  Stack,
  Toolbar,
  Tooltip,
  Typography,
} from "@mui/material";
import AccountTreeOutlinedIcon from "@mui/icons-material/AccountTreeOutlined";
import SearchRoundedIcon from "@mui/icons-material/SearchRounded";
import TerminalRoundedIcon from "@mui/icons-material/TerminalRounded";
import RefreshRoundedIcon from "@mui/icons-material/RefreshRounded";
import { SharpSenseMark } from "../shared/ui/SharpSenseMark";
import { ThemeToggle } from "../shared/ui/ThemeToggle";
import FolderOutlinedIcon from "@mui/icons-material/FolderOutlined";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import { useQueryClient } from "@tanstack/react-query";
import { LoadingRows } from "../shared/ui/States";
import { IndexStatusDialog } from "../features/diagnostics/IndexStatusDialog";

import type { WorkspaceSummary } from "../shared/workspace/models";
import { useWorkspace } from "../shared/workspace/context";
import { IndexingControls } from "../features/workspaces/IndexingControls";

type View = "explorer" | "search" | "tools";
const navigation = [
  {
    id: "explorer" as const,
    label: "Explorer",
    icon: AccountTreeOutlinedIcon,
    subtitle: "Understand the connections in your codebase.",
  },
  {
    id: "search" as const,
    label: "Search",
    icon: SearchRoundedIcon,
    subtitle: "Find the code behind your question.",
  },
  {
    id: "tools" as const,
    label: "Tool playground",
    icon: TerminalRoundedIcon,
    subtitle: "Ask your codebase. Inspect the answer.",
  },
];
const drawerWidth = 208;

export default function WorkspaceApp({
  workspaces,
  onSwitch,
  onManage,
}: {
  workspaces: WorkspaceSummary[];
  onSwitch: (workspace: WorkspaceSummary) => void;
  onManage: () => void;
}) {
  const { workspace } = useWorkspace();
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });
  const view: View =
    pathname === "/search"
      ? "search"
      : pathname === "/tools"
        ? "tools"
        : "explorer";
  const navigateRoute = useNavigate();
  const overview = useWorkspaceOverview();
  const [viewRevision, setViewRevision] = useState(0);
  const onIndexed = useCallback(
    () => setViewRevision((previous) => previous + 1),
    [],
  );
  const [indexStatusOpen, setIndexStatusOpen] = useState(false);
  const queryClient = useQueryClient();
  const navigate = useCallback(
    (next: View) => {
      if (next === "explorer")
        void navigateRoute({ to: "/explorer", search: explorerDefaults });
      else if (next === "search")
        void navigateRoute({ to: "/search", search: searchDefaults });
      else void navigateRoute({ to: "/tools", search: toolsDefaults });
    },
    [navigateRoute],
  );
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        navigate("search");
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [navigate]);
  const current = navigation.find((item) => item.id === view)!;
  return (
    <Box sx={{ display: "flex", minHeight: "100dvh" }}>
      <Drawer
        variant="permanent"
        sx={{
          display: { xs: "none", md: "block" },
          width: drawerWidth,
          flexShrink: 0,
          "& .MuiDrawer-paper": { width: drawerWidth, boxSizing: "border-box" },
        }}
      >
        <Toolbar>
          <SharpSenseMark color="primary" sx={{ mr: 1 }} />
          <Typography variant="h6">SharpSense</Typography>
        </Toolbar>
        <Divider />
        <Box sx={{ px: 2, py: 2.5 }}>
          <Typography variant="overline" color="text.secondary">
            Workspace
          </Typography>
          <TextField
            select
            fullWidth
            size="small"
            label="Selected workspace"
            value={workspace.id}
            onChange={(event) => {
              const next = workspaces.find(
                (item) => item.id === event.target.value,
              );
              if (next) onSwitch(next);
            }}
          >
            {workspaces.map((item) => (
              <MenuItem key={item.id} value={item.id}>
                {item.name}
              </MenuItem>
            ))}
          </TextField>
          <Button
            fullWidth
            size="small"
            startIcon={<FolderOutlinedIcon />}
            onClick={onManage}
            sx={{ mt: 1 }}
          >
            Manage workspaces
          </Button>
        </Box>
        <List component="nav" aria-label="Main navigation" sx={{ px: 1 }}>
          {navigation.map((item) => (
            <ListItemButton
              key={item.id}
              selected={view === item.id}
              onClick={() => navigate(item.id)}
              aria-current={view === item.id ? "page" : undefined}
              data-testid={"nav-" + item.id}
            >
              <ListItemIcon sx={{ minWidth: 36 }}>
                <item.icon fontSize="small" />
              </ListItemIcon>
              <ListItemText
                primary={item.label}
                slotProps={{ primary: { variant: "body2" } }}
              />
            </ListItemButton>
          ))}
        </List>
        <Box sx={{ mt: "auto", p: 2 }}>
          <Divider sx={{ mb: 2 }} />
          <Chip
            size="small"
            onClick={() => setIndexStatusOpen(true)}
            aria-label="View index status"
            color={
              overview.isError
                ? "error"
                : overview.data?.isIndexed
                  ? "success"
                  : "default"
            }
            variant="outlined"
            label={
              overview.isError
                ? "Connection unavailable"
                : overview.data?.isIndexed
                  ? "Index available"
                  : "Awaiting index"
            }
          />
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: "block", mt: 1.5 }}
          >
            Your code. Your machine.
          </Typography>
        </Box>
      </Drawer>
      <Box sx={{ flex: 1, minWidth: 0, pb: { xs: 8, md: 0 } }}>
        <AppBar position="static" color="transparent" elevation={0}>
          <Toolbar sx={{ justifyContent: "space-between", gap: 1 }}>
            <Stack
              direction="row"
              spacing={1.5}
              useFlexGap
              sx={{ alignItems: "center", minWidth: 0 }}
            >
              <SharpSenseMark
                color="primary"
                sx={{ display: { xs: "block", md: "none" } }}
              />
              <Typography
                variant="body2"
                color="text.secondary"
                noWrap
                sx={{ maxWidth: 180, display: { xs: "none", sm: "block" } }}
              >
                {overview.data?.name ?? "Workspace"}
              </Typography>
              <Typography
                color="text.secondary"
                sx={{ display: { xs: "none", sm: "block" } }}
              >
                /
              </Typography>
              <Typography
                variant="body2"
                noWrap
                sx={{ display: { xs: "none", sm: "block" } }}
              >
                {current.label}
              </Typography>
            </Stack>
            <Stack
              direction="row"
              spacing={{ xs: 0.5, sm: 1 }}
              sx={{ alignItems: "center", flexShrink: 0 }}
            >
              <Button
                size="small"
                onClick={onManage}
                sx={{ display: { xs: "inline-flex", md: "none" } }}
              >
                Workspaces
              </Button>
              <ThemeToggle />
              <Chip
                size="small"
                label={`${workspace.sources.length} ${workspace.sources.length === 1 ? "source" : "sources"}`}
                variant="outlined"
                sx={{ display: { xs: "none", sm: "flex" } }}
              />
              <Tooltip title="Index status and diagnostics">
                <IconButton
                  aria-label="Index status and diagnostics"
                  onClick={() => setIndexStatusOpen(true)}
                >
                  <InfoOutlinedIcon />
                </IconButton>
              </Tooltip>
              <Tooltip title="Search workspace (⌘/Ctrl K)">
                <IconButton
                  aria-label="Search workspace"
                  sx={{ display: { xs: "none", sm: "inline-flex" } }}
                  onClick={() => navigate("search")}
                >
                  <SearchRoundedIcon />
                </IconButton>
              </Tooltip>
              <Tooltip title="Refresh workspace">
                <IconButton
                  aria-label="Refresh workspace"
                  onClick={() => {
                    void queryClient.invalidateQueries();
                    onIndexed();
                  }}
                >
                  <RefreshRoundedIcon />
                </IconButton>
              </Tooltip>
            </Stack>
          </Toolbar>
          <Divider />
        </AppBar>
        <Box
          component="main"
          sx={{ p: { xs: 2, lg: 3 }, maxWidth: 2200, mx: "auto" }}
        >
          <IndexingControls onIndexed={onIndexed} />
          <Stack
            direction="row"
            sx={{
              justifyContent: "space-between",
              alignItems: "center",
              mb: 3,
            }}
            spacing={2}
          >
            <Box>
              <Typography component="h1" variant="h4">
                {view === "explorer"
                  ? "Workspace explorer"
                  : view === "search"
                    ? "Search your workspace"
                    : current.label}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                {current.subtitle}
              </Typography>
            </Box>
            {view === "explorer" && (
              <Button
                variant="outlined"
                startIcon={<SearchRoundedIcon />}
                onClick={() => navigate("search")}
                sx={{ display: { xs: "none", lg: "flex" }, flexShrink: 0 }}
              >
                Find a symbol
              </Button>
            )}
          </Stack>
          <Suspense
            key={`${view}:${view === "tools" ? viewRevision : 0}`}
            fallback={<LoadingRows />}
          >
            <Outlet />
          </Suspense>
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: "block", mt: 2 }}
          >
            {overview.isPending ? (
              <Skeleton width={210} />
            ) : overview.data ? (
              `${overview.data.projects.toLocaleString()} projects · ${overview.data.files.toLocaleString()} files · ${overview.data.nodes.toLocaleString()} symbols`
            ) : (
              "Workspace statistics unavailable"
            )}
          </Typography>
        </Box>
      </Box>
      <BottomNavigation
        value={view}
        onChange={(_, value: View) => navigate(value)}
        showLabels
        sx={{
          display: { md: "none" },
          position: "fixed",
          bottom: 0,
          left: 0,
          right: 0,
          zIndex: 1200,
        }}
      >
        {navigation.map((item) => (
          <BottomNavigationAction
            key={item.id}
            label={item.label}
            value={item.id}
            icon={<item.icon />}
            data-testid={"mobile-nav-" + item.id}
          />
        ))}
      </BottomNavigation>
      <IndexStatusDialog
        open={indexStatusOpen}
        onClose={() => setIndexStatusOpen(false)}
      />
    </Box>
  );
}
