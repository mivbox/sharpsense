import { lazy } from "react";
import {
  useNavigate,
  useSearch,
  type ErrorComponentProps,
} from "@tanstack/react-router";
import { Alert, Box, Button } from "@mui/material";
import { searchDefaults, toolsDefaults } from "./searchState";
import { useWorkspaceOverview } from "../shared/api/queries";
import type { ToolSelection } from "../shared/api/models";

const ExplorerPage = lazy(() => import("../features/explorer/ExplorerPage"));
const SearchPage = lazy(() => import("../features/search/SearchPage"));
const ToolsPage = lazy(() => import("../features/tools/ToolsPage"));
function useOpenTool() {
  const navigate = useNavigate();
  return (selection: ToolSelection) => {
    void navigate({
      to: "/tools",
      search: {
        ...toolsDefaults,
        tool: selection.tool,
        nodeId: selection.nodeId ?? undefined,
        label: selection.label,
      },
    });
  };
}
export function ExplorerRoute() {
  const state = useSearch({ from: "/explorer" });
  const navigate = useNavigate({ from: "/explorer" });
  const overview = useWorkspaceOverview();
  const openTool = useOpenTool();
  return (
    <ExplorerPage
      state={state}
      onStateChange={(patch, replace = false) => {
        void navigate({
          search: (previous) => ({ ...previous, ...patch }),
          replace,
        });
      }}
      overview={overview}
      onOpenTool={openTool}
    />
  );
}
export function SearchRoute() {
  const state = useSearch({ from: "/search" });
  const navigate = useNavigate({ from: "/search" });
  const openTool = useOpenTool();
  return (
    <SearchPage
      key={JSON.stringify([state.q, state.limit])}
      state={state}
      onStateChange={(next) => {
        void navigate({ search: next });
      }}
      onOpenTool={openTool}
    />
  );
}
export function ToolsRoute() {
  const state = useSearch({ from: "/tools" });
  const navigate = useNavigate({ from: "/tools" });
  return (
    <ToolsPage
      state={state}
      onStateChange={(patch, replace = false) => {
        void navigate({
          search: (previous) => ({ ...previous, ...patch }),
          replace,
        });
      }}
      onSearch={() => {
        void navigate({ to: "/search", search: searchDefaults });
      }}
    />
  );
}
export function RouteNotFound() {
  return (
    <Alert severity="info">
      This page does not exist. <Button href="/explorer">Open Explorer</Button>
    </Alert>
  );
}
export function RouteError({ error, reset }: ErrorComponentProps) {
  return (
    <Box sx={{ p: 3 }}>
      <Alert
        severity="error"
        action={
          <Button color="inherit" onClick={reset}>
            Retry
          </Button>
        }
      >
        {error instanceof Error
          ? error.message
          : "This view couldn’t be loaded. Please retry."}
      </Alert>
    </Box>
  );
}
