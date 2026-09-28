import type { GraphStatsSnapshot } from "../../shared/api/generated/models";

export function indexState(stats: GraphStatsSnapshot): {
  label: string;
  severity: "success" | "info" | "warning" | "error";
  detail: string;
} {
  if (stats.databaseState !== "ready") {
    return {
      label:
        stats.databaseState === "missing"
          ? "No index yet"
          : "Index needs attention",
      severity: stats.databaseState === "missing" ? "info" : "warning",
      detail:
        stats.databaseState === "missing"
          ? "Analyze this workspace to create its first index."
          : "Review the diagnostics below before querying this index.",
    };
  }

  if (stats.lastAttempt?.outcome === "failed") {
    return {
      label: "Latest indexing attempt failed",
      severity: "error",
      detail: stats.isIndexed
        ? "The existing index is available, but it may not reflect your latest changes."
        : "Resolve the reported errors, then analyze the workspace again.",
    };
  }

  if (stats.lastAttempt?.outcome === "cancelled") {
    return {
      label: "Latest indexing attempt cancelled",
      severity: "warning",
      detail: "Run indexing again to include your latest changes.",
    };
  }

  if (!stats.isIndexed) {
    return {
      label: "No indexed symbols",
      severity: "info",
      detail: "Check the configured source paths and analyze this workspace.",
    };
  }

  return {
    label: "Index available",
    severity: stats.lastSuccessfulIndex ? "success" : "info",
    detail: stats.lastSuccessfulIndex
      ? "The last successful indexing time is recorded below. Files may have changed since then."
      : "This index has no recorded indexing history. Its last successful indexing time is unknown.",
  };
}

export function formatDuration(value?: number | null): string {
  if (value == null || !Number.isFinite(value) || value < 0) return "Unknown";
  if (value < 1000) return `${Math.round(value).toLocaleString()} ms`;
  if (value < 60_000) return `${(value / 1000).toFixed(1)} s`;
  const seconds = Math.round(value / 1000);
  return `${Math.floor(seconds / 60)} min ${seconds % 60} s`;
}

export function formatTimestamp(value?: Date | null): string {
  return value && Number.isFinite(value.getTime())
    ? value.toLocaleString()
    : "Unknown";
}
