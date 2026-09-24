import { useState } from "react";
import {
  Box,
  Divider,
  Paper,
  Stack,
  Tab,
  Tabs,
  Typography,
} from "@mui/material";
import TerminalRoundedIcon from "@mui/icons-material/TerminalRounded";
import CheckCircleOutlineRoundedIcon from "@mui/icons-material/CheckCircleOutlineRounded";
import type { ToolId } from "../../shared/api/models";
import { EmptyState, LoadingRows } from "../../shared/ui/States";
import type { ToolResult } from "./models";
import { ToolResultOverview } from "./ToolResultOverview";

export function ToolResultView({
  result,
  loading,
  tool,
  onInspect,
}: {
  result?: ToolResult;
  loading: boolean;
  tool: ToolId;
  onInspect: (nodeId: number) => void;
}) {
  const [tab, setTab] = useState(0);

  return (
    <Paper
      variant="outlined"
      sx={{ minHeight: 350, overflow: "hidden" }}
      data-testid="tool-results"
    >
      <Stack
        direction="row"
        alignItems="center"
        justifyContent="space-between"
        spacing={1}
        sx={{ px: 2.5, py: 2 }}
      >
        <Typography variant="subtitle1">Result</Typography>
        {result && !loading && (
          <Stack direction="row" spacing={1} alignItems="center">
            <CheckCircleOutlineRoundedIcon color="success" fontSize="small" />
            <Typography variant="caption" color="text.secondary">
              {Math.round(result.elapsedMs).toLocaleString()} ms
            </Typography>
          </Stack>
        )}
      </Stack>
      <Divider />
      {loading ? (
        <LoadingRows count={5} />
      ) : !result ? (
        <EmptyState
          icon={<TerminalRoundedIcon />}
          title="Ready when you are"
          description={
            tool === "graph_stats"
              ? "Run graph statistics to inspect this workspace’s index and latest indexing outcome."
              : "Choose a tool and a symbol, then run a query. Results will appear here without changing your code."
          }
        />
      ) : (
        <>
          <Tabs
            value={tab}
            onChange={(_, value: number) => setTab(value)}
            aria-label="Result format"
            sx={{ px: 1 }}
          >
            <Tab label="Overview" />
            <Tab label="JSON" />
          </Tabs>
          <Divider />
          {tab === 1 ? (
            <Box
              component="pre"
              sx={{
                p: 2.5,
                m: 0,
                maxHeight: 550,
                overflow: "auto",
                fontSize: 12,
                lineHeight: 1.7,
                whiteSpace: "pre-wrap",
                overflowWrap: "anywhere",
              }}
            >
              {JSON.stringify(result.data, null, 2)}
            </Box>
          ) : (
            <Box sx={{ p: 2.5 }}>
              <ToolResultOverview result={result} onInspect={onInspect} />
            </Box>
          )}
        </>
      )}
    </Paper>
  );
}
