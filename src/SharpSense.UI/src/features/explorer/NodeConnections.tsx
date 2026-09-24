import {
  Box,
  Button,
  LinearProgress,
  List,
  ListItemButton,
  ListItemText,
  Stack,
  Typography,
} from "@mui/material";
import type {
  GraphNodeSummary,
  GraphRelationship,
} from "../../shared/api/models";
import { ErrorState, LoadingRows } from "../../shared/ui/States";
import { edgeLabel } from "./graphLabels";
import type { useNodeConnections } from "./useNodeConnections";

function relationshipLabel(relationship: GraphRelationship) {
  const direction =
    relationship.direction === "incoming"
      ? "Incoming"
      : relationship.direction === "outgoing"
        ? "Outgoing"
        : "Self";
  return `${direction}: ${edgeLabel(relationship)}`;
}

export function NodeConnections({
  query,
  onSelect,
}: {
  query: ReturnType<typeof useNodeConnections>;
  onSelect: (node: GraphNodeSummary) => void;
}) {
  return (
    <Box data-testid="node-connections">
      <Typography variant="overline" color="text.secondary">
        Connected symbols
      </Typography>
      <Typography
        variant="caption"
        color="text.secondary"
        display="block"
        data-testid="node-connections-state"
        data-loaded-count={query.connections.length}
        data-total-count={query.totalCount ?? undefined}
        data-complete={query.complete}
        aria-live="polite"
      >
        {query.pending
          ? "Loading connections…"
          : `${query.connections.length.toLocaleString()}${query.totalCount === null ? "" : ` of ${query.totalCount.toLocaleString()}`} connected symbols loaded`}
      </Typography>
      {query.pending && <LoadingRows count={3} />}
      {query.fetching && !query.pending && <LinearProgress sx={{ mt: 1 }} />}
      {query.connections.length > 0 && (
        <List dense disablePadding aria-label="Connected symbols">
          {query.connections.map(({ node, relationships }) => (
            <ListItemButton
              key={node.id}
              onClick={() => onSelect(node)}
              data-graph-node-id={node.id}
            >
              <ListItemText
                primary={node.label}
                secondary={[
                  node.type,
                  ...relationships.map(relationshipLabel),
                ].join(" · ")}
                slotProps={{
                  primary: {
                    variant: "body2",
                    sx: { overflowWrap: "anywhere" },
                  },
                  secondary: { sx: { overflowWrap: "anywhere" } },
                }}
              />
            </ListItemButton>
          ))}
        </List>
      )}
      {query.complete && query.connections.length === 0 && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          This symbol has no connections in the current workspace index.
        </Typography>
      )}
      {query.error && (
        <Box sx={{ mt: 1 }}>
          <ErrorState error={query.error} retry={query.retry} />
        </Box>
      )}
      {query.hasMore && !query.error && (
        <Stack sx={{ mt: 1 }}>
          <Button
            size="small"
            disabled={query.fetching}
            onClick={() => void query.loadMore()}
          >
            {query.fetching ? "Loading connections…" : "Load more connections"}
          </Button>
        </Stack>
      )}
    </Box>
  );
}
