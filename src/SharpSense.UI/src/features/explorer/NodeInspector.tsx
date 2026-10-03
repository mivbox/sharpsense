import { useId, useState } from "react";
import {
  Box,
  Button,
  Chip,
  Divider,
  IconButton,
  Snackbar,
  Stack,
  Tab,
  Tabs,
  Tooltip,
  Typography,
} from "@mui/material";
import CloseRoundedIcon from "@mui/icons-material/CloseRounded";
import DataObjectRoundedIcon from "@mui/icons-material/DataObjectRounded";
import ContentCopyRoundedIcon from "@mui/icons-material/ContentCopyRounded";
import ArrowForwardRoundedIcon from "@mui/icons-material/ArrowForwardRounded";
import AccountTreeOutlinedIcon from "@mui/icons-material/AccountTreeOutlined";
import StickyNote2OutlinedIcon from "@mui/icons-material/StickyNote2Outlined";
import type {
  GraphNode,
  GraphNodeSummary,
  ToolSelection,
} from "../../shared/api/models";
import { EmptyState, ErrorState, LoadingRows } from "../../shared/ui/States";
import { MemoryPanel } from "../memories/MemoryPanel";
import { NodeConnections } from "./NodeConnections";
import { useNodeConnections } from "./useNodeConnections";

export function NodeInspector({
  nodeId,
  node: graphNode,
  onSelect,
  onOpenTool,
  onExploreProject,
  onClose,
}: {
  nodeId?: string;
  node: GraphNode | null;
  onSelect: (node: GraphNodeSummary) => void;
  onOpenTool: (selection: ToolSelection) => void;
  onExploreProject: (node: GraphNodeSummary) => void;
  onClose?: () => void;
}) {
  const [tab, setTab] = useState(0);
  const tabId = useId();
  const [notice, setNotice] = useState("");
  const connections = useNodeConnections(nodeId);
  const node = connections.missing ? null : (connections.node ?? graphNode);
  return (
    <Box data-testid="node-inspector" sx={{ height: "100%", minHeight: 340 }}>
      <Stack
        direction="row"
        spacing={1}
        sx={{ alignItems: "center", px: 2, py: 2 }}
      >
        <DataObjectRoundedIcon fontSize="small" color="primary" />
        <Typography variant="subtitle2" sx={{ flex: 1 }}>
          Symbol inspector
        </Typography>
        {onClose && (
          <IconButton aria-label="Close symbol inspector" onClick={onClose}>
            <CloseRoundedIcon />
          </IconButton>
        )}
      </Stack>
      <Divider />
      {!nodeId ? (
        <EmptyState
          icon={<DataObjectRoundedIcon />}
          title="A closer look"
          description="Select a symbol in the graph or list to inspect its connections, explore its context, and manage memories."
        />
      ) : !node ? (
        connections.error ? (
          <Box sx={{ p: 2 }}>
            <ErrorState error={connections.error} retry={connections.retry} />
          </Box>
        ) : (
          <LoadingRows count={4} />
        )
      ) : (
        <>
          <Box sx={{ p: 2 }}>
            <Stack
              direction="row"
              spacing={1}
              sx={{ alignItems: "center", justifyContent: "space-between" }}
            >
              <Stack direction="row" spacing={0.75} sx={{ flexWrap: "wrap" }}>
                <Chip
                  label={node.type}
                  size="small"
                  color="primary"
                  variant="outlined"
                />
                {node.codeNodeId && (
                  <Chip
                    label={"#" + node.codeNodeId}
                    size="small"
                    variant="outlined"
                    data-testid="selected-node-id"
                  />
                )}
              </Stack>
              <Tooltip title="Copy symbol name">
                <IconButton
                  size="small"
                  aria-label="Copy symbol name"
                  onClick={() => {
                    void navigator.clipboard
                      .writeText(node.label)
                      .then(() => setNotice("Symbol name copied"))
                      .catch(() => setNotice("Couldn’t access the clipboard"));
                  }}
                >
                  <ContentCopyRoundedIcon sx={{ fontSize: 16 }} />
                </IconButton>
              </Tooltip>
            </Stack>
            <Typography
              variant="subtitle1"
              sx={{ mt: 1.5, overflowWrap: "anywhere" }}
              data-testid="selected-node-name"
            >
              {symbolTitle(node.label)}
            </Typography>
            {symbolTitle(node.label) !== node.label && (
              <Tooltip title={node.label}>
                <Typography
                  variant="caption"
                  color="text.secondary"
                  noWrap
                  component="p"
                >
                  {node.label}
                </Typography>
              </Tooltip>
            )}
            {node.relativePath && (
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ mt: 1, display: "block", overflowWrap: "anywhere" }}
              >
                {node.relativePath}
              </Typography>
            )}
            {graphNode?.scope === "external" && (
              <Chip
                label="Boundary dependency"
                size="small"
                variant="outlined"
                sx={{ mt: 1.5 }}
              />
            )}
          </Box>
          <Tabs
            value={tab}
            onChange={(_, value: number) => setTab(value)}
            variant="fullWidth"
            aria-label="Symbol details"
          >
            <Tab
              label="Overview"
              id={`${tabId}-tab-0`}
              aria-controls={`${tabId}-panel-0`}
            />
            <Tab
              label="Memories"
              id={`${tabId}-tab-1`}
              aria-controls={`${tabId}-panel-1`}
              icon={<StickyNote2OutlinedIcon fontSize="small" />}
              iconPosition="start"
              data-testid="memories-tab"
              disabled={!node.codeNodeId}
            />
          </Tabs>
          <Divider />
          <Box
            role="tabpanel"
            id={`${tabId}-panel-1`}
            aria-labelledby={`${tabId}-tab-1`}
            hidden={tab !== 1}
          >
            {tab === 1 && node.codeNodeId && (
              <MemoryPanel nodeId={node.codeNodeId} />
            )}
          </Box>
          <Box
            role="tabpanel"
            id={`${tabId}-panel-0`}
            aria-labelledby={`${tabId}-tab-0`}
            hidden={tab !== 0}
          >
            <Stack spacing={2.5} sx={{ p: 2 }}>
              {node.codeNodeId ? (
                <Box>
                  <Typography variant="overline" color="text.secondary">
                    Explore this symbol
                  </Typography>
                  <Stack spacing={1} sx={{ mt: 1 }}>
                    <Button
                      variant="outlined"
                      fullWidth
                      startIcon={<AccountTreeOutlinedIcon />}
                      endIcon={<ArrowForwardRoundedIcon />}
                      onClick={() =>
                        onOpenTool({
                          tool: "context",
                          nodeId: node.codeNodeId,
                          label: node.label,
                        })
                      }
                    >
                      Inspect context
                    </Button>
                    <Stack direction="row" spacing={1}>
                      <Button
                        variant="outlined"
                        fullWidth
                        onClick={() =>
                          onOpenTool({
                            tool: "trace",
                            nodeId: node.codeNodeId,
                            label: node.label,
                          })
                        }
                      >
                        Trace calls
                      </Button>
                      <Button
                        variant="outlined"
                        fullWidth
                        onClick={() =>
                          onOpenTool({
                            tool: "impact",
                            nodeId: node.codeNodeId,
                            label: node.label,
                          })
                        }
                      >
                        Assess impact
                      </Button>
                    </Stack>
                    {["class", "interface"].includes(
                      node.type.toLowerCase(),
                    ) && (
                      <Button
                        variant="text"
                        onClick={() =>
                          onOpenTool({
                            tool: "inheritors",
                            nodeId: node.codeNodeId,
                            label: node.label,
                          })
                        }
                      >
                        Find inheritors
                      </Button>
                    )}
                  </Stack>
                </Box>
              ) : node.type === "project" && node.relativePath ? (
                <Button
                  variant="outlined"
                  fullWidth
                  endIcon={<ArrowForwardRoundedIcon />}
                  data-testid="explore-project-symbols"
                  onClick={() => onExploreProject(node)}
                >
                  Explore symbols
                </Button>
              ) : (
                <Typography variant="body2" color="text.secondary">
                  Context and memory tools are available for code symbols.
                  Explore this node’s connections below.
                </Typography>
              )}
              <Divider />
              <NodeConnections query={connections} onSelect={onSelect} />
            </Stack>
          </Box>
        </>
      )}
      <Snackbar
        open={Boolean(notice)}
        autoHideDuration={2500}
        onClose={() => setNotice("")}
        message={notice}
      />
    </Box>
  );
}

function symbolTitle(qualifiedName: string) {
  let lastSeparator = -1;
  let genericDepth = 0;
  for (let index = 0; index < qualifiedName.length; index++) {
    const character = qualifiedName[index];
    if (character === "(" && genericDepth === 0) break;
    if (character === "<") genericDepth++;
    else if (character === ">") genericDepth--;
    else if (character === "." && genericDepth === 0) lastSeparator = index;
  }
  return qualifiedName.slice(lastSeparator + 1);
}
