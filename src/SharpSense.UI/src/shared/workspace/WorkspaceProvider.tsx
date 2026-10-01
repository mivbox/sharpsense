import { useEffect, useState, type ReactNode } from "react";
import { QueryClientProvider } from "@tanstack/react-query";
import { createWorkspaceApi } from "../api/workspaceApi";
import { createQueryClient } from "../api/queryClient";
import { WorkspaceContext } from "./context";
import type { WorkspaceSummary } from "./models";

export function WorkspaceProvider({
  workspace,
  children,
}: {
  workspace: WorkspaceSummary;
  children: ReactNode;
}) {
  const [api] = useState(() => createWorkspaceApi(workspace.id));
  const [client] = useState(createQueryClient);
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
