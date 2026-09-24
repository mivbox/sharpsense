import type { WorkspaceIndexingStatus } from "../src/shared/api/generated/models";

export function idleIndexingStatus(
  workspaceId: string,
): WorkspaceIndexingStatus {
  return {
    workspaceId,
    streamId: "b91eff24-a8df-45d1-9baf-f8861c854485",
    sequence: 0,
    state: "idle",
    watch: false,
    revision: 0,
    updatedAt: new Date("2026-09-24T00:00:00Z"),
    diagnostics: [],
  };
}
