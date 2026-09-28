import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  AlertTitle,
  Box,
  Chip,
  Divider,
  LinearProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from "@mui/material";
import ExpandMoreRoundedIcon from "@mui/icons-material/ExpandMoreRounded";
import type {
  GraphStatsSnapshot,
  IndexDiagnostic,
  IndexRunSummary,
} from "../../shared/api/generated/models";
import { formatDuration, formatTimestamp, indexState } from "./presentation";

function count(value?: number | null): string {
  return value?.toLocaleString() ?? "Unknown";
}

function Diagnostics({ diagnostics }: { diagnostics: IndexDiagnostic[] }) {
  return (
    <Stack spacing={1.5}>
      {diagnostics.slice(0, 10).map((diagnostic, index) => (
        <Alert
          key={`${diagnostic.code}-${diagnostic.filePath}-${index}`}
          severity={
            diagnostic.severity === "error"
              ? "error"
              : diagnostic.severity === "warning"
                ? "warning"
                : "info"
          }
          sx={{ overflowWrap: "anywhere" }}
        >
          {diagnostic.filePath && (
            <AlertTitle>{diagnostic.filePath}</AlertTitle>
          )}
          {diagnostic.message}
          {diagnostic.suggestion && (
            <Typography variant="body2" sx={{ mt: 1 }}>
              {diagnostic.suggestion}
            </Typography>
          )}
        </Alert>
      ))}
      {diagnostics.length > 10 && (
        <Typography variant="caption" color="text.secondary">
          Showing 10 of {diagnostics.length} diagnostics. Run sharpsense doctor
          for the complete report.
        </Typography>
      )}
    </Stack>
  );
}

function IndexAttempt({ run }: { run: IndexRunSummary }) {
  const diagnostics = run.diagnostics ?? [];

  return (
    <Stack spacing={2}>
      <Box>
        <Typography variant="subtitle2">Latest indexing attempt</Typography>
        <Stack direction="row" spacing={1} sx={{ alignItems: "center", mt: 1 }}>
          <Chip
            size="small"
            variant="outlined"
            label={
              run.outcome === "succeeded"
                ? "Succeeded"
                : run.outcome === "failed"
                  ? "Failed"
                  : run.outcome === "cancelled"
                    ? "Cancelled"
                    : "Unknown outcome"
            }
            color={
              run.outcome === "succeeded"
                ? "success"
                : run.outcome === "failed"
                  ? "error"
                  : "default"
            }
          />
          <Typography variant="body2" color="text.secondary">
            {run.kind === "incremental"
              ? "Incremental update"
              : run.kind === "full"
                ? "Full index"
                : "Indexing"}
            {" · "}
            {formatDuration(run.durationMs)}
          </Typography>
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          Completed {formatTimestamp(run.completedAt)}
        </Typography>
        {run.scope && (
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ overflowWrap: "anywhere" }}
          >
            {run.scope}
          </Typography>
        )}
      </Box>
      {diagnostics.length > 0 && <Diagnostics diagnostics={diagnostics} />}
      <Accordion disableGutters variant="outlined">
        <AccordionSummary expandIcon={<ExpandMoreRoundedIcon />}>
          <Typography variant="body2">Indexing performance</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <Stack spacing={1.5}>
            <Typography variant="body2" color="text.secondary">
              {count(run.extractedNodeCount)} symbols extracted ·{" "}
              {count(run.reusedEmbeddingCount)} embeddings reused ·{" "}
              {count(run.generatedEmbeddingCount)} embeddings generated
            </Typography>
            {(run.phases ?? []).map((phase, index) => (
              <Stack
                key={`${phase.name}-${index}`}
                direction="row"
                spacing={2}
                sx={{ justifyContent: "space-between" }}
              >
                <Typography variant="body2">{phase.name}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {formatDuration(phase.durationMs)}
                </Typography>
              </Stack>
            ))}
            {!run.phases?.length && (
              <Typography variant="body2" color="text.secondary">
                Phase timings were not recorded for this attempt.
              </Typography>
            )}
          </Stack>
        </AccordionDetails>
      </Accordion>
    </Stack>
  );
}

export function GraphStatsView({ stats }: { stats: GraphStatsSnapshot }) {
  const state = indexState(stats);
  const coverage =
    stats.codeNodeCount != null &&
    stats.codeNodeCount > 0 &&
    stats.embeddedNodeCount != null
      ? Math.min(100, (stats.embeddedNodeCount / stats.codeNodeCount) * 100)
      : null;
  const metrics = [
    ["Graph nodes", stats.graphNodeCount],
    ["Symbols", stats.codeNodeCount],
    ["Relationships", stats.edgeCount],
    ["Files", stats.fileCount],
    ["Projects", stats.projectCount],
    ["Memories", stats.memoryCount],
  ] as const;

  return (
    <Stack spacing={2.5}>
      <Alert severity={state.severity}>
        <AlertTitle>{state.label}</AlertTitle>
        {state.detail}
      </Alert>
      <Box>
        <Typography variant="overline" color="text.secondary">
          Last successful index
        </Typography>
        <Typography variant="body2">
          {formatTimestamp(stats.lastSuccessfulIndex?.completedAt)}
        </Typography>
      </Box>
      {(stats.diagnostics?.length ?? 0) > 0 && (
        <Diagnostics diagnostics={stats.diagnostics!} />
      )}
      {stats.databaseState === "ready" && (
        <>
          <Box
            sx={{
              display: "grid",
              gridTemplateColumns: {
                xs: "repeat(2, 1fr)",
                sm: "repeat(3, 1fr)",
              },
              gap: 1.5,
            }}
          >
            {metrics.map(([label, value]) => (
              <Paper key={label} variant="outlined" sx={{ p: 2 }}>
                <Typography variant="h6">{count(value)}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {label}
                </Typography>
              </Paper>
            ))}
          </Box>
          <Box>
            <Typography variant="subtitle2">Embedding coverage</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
              {count(stats.embeddedNodeCount)} of {count(stats.codeNodeCount)}{" "}
              symbols{coverage == null ? "" : ` · ${coverage.toFixed(1)}%`}
            </Typography>
            {coverage != null && (
              <LinearProgress
                variant="determinate"
                value={coverage}
                aria-label="Embedding coverage"
                sx={{ mt: 1 }}
              />
            )}
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{ display: "block", mt: 1 }}
            >
              Embeddings support semantic search. Indexing without embeddings
              keeps graph queries and text search available.
            </Typography>
          </Box>
          {(stats.languages?.length ?? 0) > 0 && (
            <Box>
              <Typography variant="subtitle2" sx={{ mb: 1 }}>
                Languages
              </Typography>
              <TableContainer>
                <Table size="small" aria-label="Indexed languages">
                  <TableHead>
                    <TableRow>
                      <TableCell>Language</TableCell>
                      <TableCell align="right">Files</TableCell>
                      <TableCell align="right">Symbols</TableCell>
                      <TableCell align="right">Embedded</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {stats.languages?.map((language) => (
                      <TableRow key={language.language}>
                        <TableCell>{language.language}</TableCell>
                        <TableCell align="right">
                          {count(language.fileCount)}
                        </TableCell>
                        <TableCell align="right">
                          {count(language.nodeCount)}
                        </TableCell>
                        <TableCell align="right">
                          {count(language.embeddedNodeCount)}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            </Box>
          )}
          {(stats.edgeTypes?.length ?? 0) > 0 && (
            <Accordion disableGutters variant="outlined">
              <AccordionSummary expandIcon={<ExpandMoreRoundedIcon />}>
                <Typography variant="body2">Relationship types</Typography>
              </AccordionSummary>
              <AccordionDetails>
                <Stack
                  direction="row"
                  useFlexGap
                  sx={{ flexWrap: "wrap", gap: 1 }}
                >
                  {stats.edgeTypes?.map((edge) => (
                    <Chip
                      key={edge.edgeType}
                      label={`${edge.edgeType}: ${count(edge.count)}`}
                      size="small"
                      variant="outlined"
                    />
                  ))}
                </Stack>
              </AccordionDetails>
            </Accordion>
          )}
        </>
      )}
      {stats.lastAttempt && (
        <>
          <Divider />
          <IndexAttempt run={stats.lastAttempt} />
        </>
      )}
      <Typography variant="caption" color="text.secondary">
        Run{" "}
        <Box component="code">sharpsense doctor --workspace &lt;name&gt;</Box>
        for the same diagnostics in your terminal.
      </Typography>
    </Stack>
  );
}
