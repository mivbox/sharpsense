import type {
  WorkspaceSummary,
  SourceKind,
} from "../../shared/workspace/models";
import { apiClient } from "../../shared/api/client";
import {
  requestConfiguration,
  workspaceRequest,
} from "../../shared/api/transport";
import type { WorkspaceSummary as ApiWorkspaceSummary } from "../../shared/api/generated/models";
import { sourceKinds, type WorkspaceInput } from "./models";

function workspace(value: ApiWorkspaceSummary | undefined): WorkspaceSummary {
  if (!value?.id || !value.name || !value.repositoryRoot)
    throw new Error("Invalid workspace response.");
  return {
    id: value.id,
    name: value.name,
    repositoryRoot: value.repositoryRoot,
    sources: (value.sources ?? []).map((source) => {
      if (!source.path || !sourceKinds.includes(source.kind as SourceKind))
        throw new Error("Invalid workspace source.");
      return { kind: source.kind as SourceKind, path: source.path };
    }),
  };
}

export async function listWorkspaces(signal?: AbortSignal) {
  const value = await workspaceRequest(
    () => apiClient.api.workspaces.get(requestConfiguration(signal)),
    signal,
  );
  if (!value)
    throw new Error("The workspace catalog returned an empty response.");
  return {
    workspaces: (value.workspaces ?? []).map(workspace),
    initialWorkspaceId: value.initialWorkspaceId ?? null,
  };
}

export async function saveWorkspace(input: WorkspaceInput, id?: string) {
  const value = await workspaceRequest(() =>
    id
      ? apiClient.api.workspaces
          .byWorkspaceId(id)
          .put({ name: input.name, sources: input.sources })
      : apiClient.api.workspaces.post(input),
  );
  return workspace(value);
}

export async function mergeWorkspaces(name: string, workspaceIds: string[]) {
  return workspace(
    await workspaceRequest(() =>
      apiClient.api.workspaces.merge.post({ name, workspaceIds }),
    ),
  );
}

export async function getIndexingStatus(
  workspaceId: string,
  signal?: AbortSignal,
) {
  return workspaceRequest(
    () =>
      apiClient.api.workspaces
        .byWorkspaceId(workspaceId)
        .indexing.get(requestConfiguration(signal)),
    signal,
  );
}

export async function startIndexing(
  workspaceId: string,
  watch: boolean,
  skipEmbeddings: boolean,
) {
  return workspaceRequest(() =>
    apiClient.api.workspaces
      .byWorkspaceId(workspaceId)
      .indexing.post({ watch, skipEmbeddings }),
  );
}

export async function stopIndexing(workspaceId: string) {
  return workspaceRequest(() =>
    apiClient.api.workspaces.byWorkspaceId(workspaceId).indexing.delete(),
  );
}

export async function discoverSources(
  repositoryRoot: string,
  signal?: AbortSignal,
) {
  const result = await workspaceRequest(
    () =>
      apiClient.api.workspaces.discover.post(
        { repositoryRoot },
        requestConfiguration(signal),
      ),
    signal,
  );
  if (!result?.repositoryRoot)
    throw new Error("The repository could not be discovered.");
  return {
    repositoryRoot: result.repositoryRoot,
    sources: (result.sources ?? []).map((source) => {
      if (!source.path || !sourceKinds.includes(source.kind as SourceKind))
        throw new Error("Invalid discovered source.");
      return { kind: source.kind as SourceKind, path: source.path };
    }),
  };
}
