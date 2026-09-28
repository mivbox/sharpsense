import type { WorkspaceIndexingStatus } from "../../shared/api/generated/models";

export const activeIndexingStates = new Set([
  "indexing",
  "watching",
  "stopping",
]);

/** Orders all transports together; graph revision changes only after a commit. */
export class IndexingStatusOrder {
  private current: WorkspaceIndexingStatus | undefined;
  private readonly retiredStreams = new Set<string>();

  constructor(private readonly workspaceId: string) {}

  accept(
    incoming: WorkspaceIndexingStatus | undefined,
    authoritative = false,
  ): WorkspaceIndexingStatus | undefined {
    if (!incoming || incoming.workspaceId !== this.workspaceId)
      return this.current;

    const current = this.current;
    const stream = incoming.streamId;
    if (stream && this.retiredStreams.has(stream)) return current;

    if (current) {
      if (stream === current.streamId) {
        if ((incoming.sequence ?? 0) <= (current.sequence ?? 0)) return current;
      } else {
        // A reconnect snapshot establishes a new server lifetime, even if its
        // clock moved backwards. HTTP responses may arrive from an old server.
        if (
          !authoritative &&
          (incoming.updatedAt?.getTime() ?? 0) <=
            (current.updatedAt?.getTime() ?? 0)
        )
          return current;
        if (current.streamId) this.retiredStreams.add(current.streamId);
      }
    }

    this.current = incoming;
    return incoming;
  }
}

export function indexingProgress(
  completed: number | null | undefined,
  total: number | null | undefined,
): number | undefined {
  if (
    completed == null ||
    total == null ||
    !Number.isFinite(completed) ||
    !Number.isFinite(total) ||
    total <= 0
  )
    return undefined;
  return Math.max(0, Math.min(100, (completed / total) * 100));
}

export function elapsedTime(start: Date, end: Date): string {
  const seconds = Math.max(
    0,
    Math.floor((end.getTime() - start.getTime()) / 1000),
  );
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ${seconds % 60}s`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}
