import { useEffect, useRef } from "react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useRouterState, useSearch } from "@tanstack/react-router";
import { Alert, Box, Button, Snackbar } from "@mui/material";
import WorkspaceApp from "./WorkspaceApp";
import { explorerDefaults } from "./searchState";
import { WorkspacesPage } from "../features/workspaces/WorkspacesPage";
import { workspaceCatalogOptions } from "../features/workspaces/queries";
import type { WorkspaceSummary } from "../shared/workspace/models";
import { WorkspaceProvider } from "../shared/workspace/WorkspaceProvider";
import { ErrorState, LoadingRows } from "../shared/ui/States";

export default function WorkspaceRoot() {
  const catalog = useQuery(workspaceCatalogOptions);
  const { workspace: workspaceId } = useSearch({ from: "__root__" });
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });
  const navigate = useNavigate();
  const initialHandled = useRef(false);
  useEffect(() => {
    if (!catalog.data || initialHandled.current) return;
    initialHandled.current = true;
    if (!workspaceId && catalog.data.initialWorkspaceId) {
      void navigate({
        to: "/explorer",
        search: {
          ...explorerDefaults,
          workspace: catalog.data.initialWorkspaceId,
        },
        replace: true,
      });
    }
  }, [catalog.data, navigate, workspaceId]);
  const openWorkspace = (workspace: WorkspaceSummary) => {
    void navigate({
      to: "/explorer",
      search: { ...explorerDefaults, workspace: workspace.id },
    });
  };
  const manageWorkspaces = () => {
    void navigate({ to: "/workspaces", search: { workspace: undefined } });
  };

  if (catalog.isPending)
    return (
      <Box sx={{ p: 3 }}>
        <LoadingRows count={5} />
      </Box>
    );
  if (catalog.error && !catalog.data)
    return (
      <Box sx={{ p: 3 }}>
        <ErrorState
          error={catalog.error}
          retry={() => void catalog.refetch()}
        />
      </Box>
    );
  const selected = catalog.data?.workspaces.find(
    (workspace) => workspace.id === workspaceId,
  );
  if (workspaceId && !selected)
    return (
      <Box sx={{ p: 3 }}>
        <Alert
          severity="warning"
          action={<Button onClick={manageWorkspaces}>Workspaces</Button>}
        >
          This workspace is unavailable. Choose a registered workspace to
          continue.
        </Alert>
      </Box>
    );
  if (!selected || pathname === "/workspaces")
    return <WorkspacesPage onOpen={openWorkspace} />;
  return (
    <WorkspaceProvider key={selected.id} workspace={selected}>
      <WorkspaceApp
        workspaces={catalog.data?.workspaces ?? []}
        onSwitch={openWorkspace}
        onManage={manageWorkspaces}
      />
      <Snackbar
        open={Boolean(catalog.error)}
        anchorOrigin={{ vertical: "top", horizontal: "center" }}
      >
        <Alert
          severity="warning"
          action={
            <Button
              color="inherit"
              disabled={catalog.isFetching}
              onClick={() => void catalog.refetch()}
            >
              Retry catalog
            </Button>
          }
        >
          Workspace list could not refresh. Your current workspace remains
          available.
        </Alert>
      </Snackbar>
    </WorkspaceProvider>
  );
}
