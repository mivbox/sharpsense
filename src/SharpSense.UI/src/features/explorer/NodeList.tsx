import { useState } from "react";
import {
  Box,
  Chip,
  Divider,
  List,
  ListItemButton,
  ListItemText,
  Pagination,
  Stack,
  Typography,
} from "@mui/material";
import type { GraphNode } from "../../shared/api/models";
import { EmptyState } from "../../shared/ui/States";

const PAGE_SIZE = 50;

export function NodeList({
  nodes,
  selectedId,
  onSelect,
}: {
  nodes: readonly GraphNode[];
  selectedId?: string;
  onSelect: (node: GraphNode) => void;
}) {
  const [requestedPage, setRequestedPage] = useState(() => {
    const selectedIndex = nodes.findIndex((node) => node.id === selectedId);
    return Math.max(1, Math.floor(selectedIndex / PAGE_SIZE) + 1);
  });
  const pageCount = Math.max(1, Math.ceil(nodes.length / PAGE_SIZE));
  const page = Math.min(requestedPage, pageCount);
  const start = (page - 1) * PAGE_SIZE;
  const pageNodes = nodes.slice(start, start + PAGE_SIZE);
  if (nodes.length === 0) {
    return (
      <EmptyState
        compact
        title="No matching symbols"
        description="Try a different name, node ID, or type."
      />
    );
  }
  return (
    <Stack sx={{ height: "100%", minHeight: 0 }}>
      <List
        dense
        sx={{ flex: 1, overflow: "auto", minHeight: 0 }}
        aria-label="Graph nodes"
      >
        {pageNodes.map((node) => (
          <ListItemButton
            key={node.id}
            selected={selectedId === node.id}
            onClick={() => onSelect(node)}
            data-node-id={node.codeNodeId ?? undefined}
          >
            <ListItemText
              primary={node.label}
              secondary={node.relativePath ?? node.type}
              slotProps={{
                primary: { variant: "body2", sx: { overflowWrap: "anywhere" } },
                secondary: { sx: { overflowWrap: "anywhere" } },
              }}
            />
            <Chip
              size="small"
              variant="outlined"
              label={node.type}
              sx={{ ml: 1, flexShrink: 0 }}
            />
          </ListItemButton>
        ))}
      </List>
      <Divider />
      <Box sx={{ p: 1 }}>
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mb: 0.5 }}
        >
          {start + 1}–{Math.min(start + PAGE_SIZE, nodes.length)} of{" "}
          {nodes.length.toLocaleString()} symbols
        </Typography>
        <Pagination
          count={pageCount}
          page={page}
          onChange={(_, value) => setRequestedPage(value)}
          size="small"
          siblingCount={0}
          boundaryCount={1}
          aria-label="Symbol pages"
        />
      </Box>
    </Stack>
  );
}
