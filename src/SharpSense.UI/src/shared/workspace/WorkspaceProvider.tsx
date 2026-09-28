import { useEffect, useState, type ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createWorkspaceApi } from "../api/workspaceApi";
import { shouldRetryWorkspaceRequest } from "../api/transport";
import { WorkspaceContext } from "./context";
import type { WorkspaceSummary } from "../../features/workspaces/models";

export function WorkspaceProvider({
  workspace,
  children,
}: {
  workspace: WorkspaceSummary;
  children: ReactNode;
}) {
  const [api] = useState(() => createWorkspaceApi(workspace.id));
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            retry: shouldRetryWorkspaceRequest,
            retryDelay: 1000,
            refetchOnWindowFocus: false,
          },
          mutations: { retry: false },
        },
      }),
  );
  useEffect(
    () => () => {
      void client.cancelQueries();
    },
    [client],
  );

  return (
    <WorkspaceContext value={{ workspace, api }}>
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    </WorkspaceContext>
  );
}
