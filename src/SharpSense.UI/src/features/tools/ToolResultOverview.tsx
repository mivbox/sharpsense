import { useMemo, useState } from "react";
import {
  Alert,
  Box,
  Chip,
  List,
  ListItemButton,
  ListItemText,
  Pagination,
  Stack,
  Typography,
} from "@mui/material";
import { GraphStatsView } from "../diagnostics/GraphStatsView";
import type { ToolResult } from "./models";
import {
  contextOverview,
  impactOverview,
  inheritorsOverview,
  traceOverview,
  type SymbolOverview,
} from "./resultPresentation";

export function ToolResultOverview({
  result,
  onInspect,
}: {
  result: ToolResult;
  onInspect: (nodeId: number) => void;
}) {
  const overview = useMemo(() => {
    switch (result.tool) {
      case "graph_stats":
        return null;
      case "context":
        return contextOverview(result.data);
      case "trace":
        return traceOverview(result.data);
      case "inheritors":
        return inheritorsOverview(result.data);
      case "impact":
        return impactOverview(result.data);
    }
  }, [result]);
  if (result.tool === "graph_stats")
    return <GraphStatsView stats={result.data} />;
  return (
    overview && (
      <SymbolResultOverview overview={overview} onInspect={onInspect} />
    )
  );
}

function SymbolResultOverview({
  overview,
  onInspect,
}: {
  overview: SymbolOverview;
  onInspect: (nodeId: number) => void;
}) {
  const count = overview.groups.reduce(
    (sum, group) => sum + group.items.length,
    0,
  );

  return (
    <Stack spacing={2}>
      {overview.targetName && (
        <Box>
          <Typography variant="overline" color="text.secondary">
            Target symbol
          </Typography>
          <Typography variant="subtitle2" sx={{ overflowWrap: "anywhere" }}>
            {overview.targetName}
          </Typography>
        </Box>
      )}
      {overview.truncated && (
        <Alert severity="info">
          The trace reached its result limit. Use a smaller depth to inspect a
          narrower path.
        </Alert>
      )}
      {count === 0 && (
        <Typography variant="body2" color="text.secondary">
          {overview.emptyMessage}
        </Typography>
      )}
      {overview.groups.map((group) => (
        <SymbolResultGroup
          key={group.title}
          group={group}
          onInspect={onInspect}
        />
      ))}
      {overview.dependencyCount !== undefined && (
        <Typography variant="caption" color="text.secondary">
          {overview.dependencyCount} dependency relationships returned. Inspect
          JSON for edge details.
        </Typography>
      )}
      {count > 0 && (
        <Typography variant="caption" color="text.secondary">
          Choose a result to use it as the next query’s target. Run the tool
          again to continue exploring.
        </Typography>
      )}
    </Stack>
  );
}

const RESULT_PAGE_SIZE = 50;

function SymbolResultGroup({
  group,
  onInspect,
}: {
  group: SymbolOverview["groups"][number];
  onInspect: (nodeId: number) => void;
}) {
  const [pagination, setPagination] = useState({ items: group.items, page: 1 });
  const requestedPage = pagination.items === group.items ? pagination.page : 1;
  const pageCount = Math.max(
    1,
    Math.ceil(group.items.length / RESULT_PAGE_SIZE),
  );
  const page = Math.min(requestedPage, pageCount);
  const start = (page - 1) * RESULT_PAGE_SIZE;
  const items = group.items.slice(start, start + RESULT_PAGE_SIZE);

  return (
    <Box>
      <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
        <Typography variant="subtitle2">{group.title}</Typography>
        <Chip size="small" label={group.items.length} />
      </Stack>
      {items.length === 0 ? (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mt: 1 }}
        >
          None in the current index
        </Typography>
      ) : (
        <List dense aria-label={group.title}>
          {items.map((item, index) => (
            <ListItemButton
              key={item.id ?? start + index}
              disabled={item.targetId === undefined}
              onClick={() =>
                item.targetId !== undefined && onInspect(item.targetId)
              }
              aria-label={`Use ${item.name} as tool target`}
            >
              <ListItemText
                primary={item.name}
                secondary={[item.path, item.id ? "#" + item.id : ""]
                  .filter(Boolean)
                  .join(" · ")}
                slotProps={{
                  primary: {
                    variant: "body2",
                    sx: { overflowWrap: "anywhere" },
                  },
                }}
              />
              {item.kind && (
                <Chip
                  label={item.kind}
                  size="small"
                  variant="outlined"
                  sx={{ ml: 1 }}
                />
              )}
            </ListItemButton>
          ))}
        </List>
      )}
      {pageCount > 1 && (
        <Stack spacing={1}>
          <Typography variant="caption" color="text.secondary">
            {start + 1}–{Math.min(start + RESULT_PAGE_SIZE, group.items.length)}{" "}
            of {group.items.length.toLocaleString()} symbols
          </Typography>
          <Pagination
            count={pageCount}
            page={page}
            onChange={(_, value) =>
              setPagination({ items: group.items, page: value })
            }
            size="small"
            siblingCount={0}
            boundaryCount={1}
            aria-label={`${group.title} pages`}
          />
        </Stack>
      )}
    </Box>
  );
}
