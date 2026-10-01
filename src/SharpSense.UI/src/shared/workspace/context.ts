import { createContext, useContext } from "react";
import type { WorkspaceApi } from "../api/workspaceApi";
import type { WorkspaceSummary } from "./models";

export const WorkspaceContext = createContext<{
  workspace: WorkspaceSummary;
  api: WorkspaceApi;
} | null>(null);

export function useWorkspace() {
  const context = useContext(WorkspaceContext);
  if (!context) throw new Error("Select a workspace to use this feature.");
  return context;
}

export function useWorkspaceApi() {
  return useWorkspace().api;
}
