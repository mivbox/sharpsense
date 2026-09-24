import { useEffect, useState } from "react";
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Box,
  Chip,
  LinearProgress,
  Stack,
  Typography,
} from "@mui/material";
import ExpandMoreRoundedIcon from "@mui/icons-material/ExpandMoreRounded";
import type {
  AnalysisSnapshot,
  AnalysisSourceStatus,
} from "../../shared/api/generated/models";
import { elapsedTime, indexingProgress } from "./indexingStatus";

const phaseLabels: Record<string, string> = {
  Discovery: "Discovering sources",
  Extraction: "Reading source relationships",
  Embeddings: "Preparing semantic search",
  Persistence: "Saving graph",
};

export function AnalysisProgress({ analysis }: { analysis: AnalysisSnapshot }) {
  const [now, setNow] = useState(() => new Date());
  const running = analysis.state === "running";
  useEffect(() => {
    if (!running) return;
    const timer = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(timer);
  }, [running]);

  const sources = analysis.sources ?? [];
  const activeSources = sources.filter((source) => source.state === "running");
  const visibleSources = activeSources.slice(0, 6);
  const summary = analysis.summary ?? analysis.lastCommittedSummary;
  const progress = indexingProgress(
    analysis.completedItems,
    analysis.totalItems,
  );

  return (
    <Stack spacing={1.5} aria-label="Analysis progress">
      <Stack
        direction="row"
        spacing={1}
        alignItems="center"
        flexWrap="wrap"
        useFlexGap
      >
        {running && analysis.phase && (
          <Typography variant="body2" fontWeight={600}>
            {phaseLabels[analysis.phase] ?? analysis.phase}
          </Typography>
        )}
        {analysis.operationKind === "Incremental" && (
          <Chip
            size="small"
            variant="outlined"
            label={running ? "Updating changed sources" : "Last source update"}
          />
        )}
        {analysis.startedAt && (
          <Typography variant="caption" color="text.secondary">
            {elapsedTime(analysis.startedAt, analysis.completedAt ?? now)}{" "}
            elapsed
          </Typography>
        )}
        {analysis.totalItems != null &&
          analysis.completedItems != null &&
          running && (
            <Typography variant="caption" color="text.secondary">
              {analysis.completedItems.toLocaleString()} of{" "}
              {analysis.totalItems.toLocaleString()}
            </Typography>
          )}
      </Stack>
      {running && progress != null && (
        <LinearProgress
          variant="determinate"
          value={progress}
          aria-label={
            phaseLabels[analysis.phase ?? ""] ?? "Current phase progress"
          }
        />
      )}
      {visibleSources.map((source) => (
        <SourceProgress key={`${source.kind}:${source.path}`} source={source} />
      ))}
      {sources.length > visibleSources.length && (
        <Accordion disableGutters elevation={0} variant="outlined">
          <AccordionSummary expandIcon={<ExpandMoreRoundedIcon />}>
            <Typography variant="body2">Latest activity by language</Typography>
          </AccordionSummary>
          <AccordionDetails>
            <Stack spacing={1.5} sx={{ maxHeight: 280, overflow: "auto" }}>
              {sources.map((source) => (
                <SourceProgress
                  key={`${source.kind}:${source.path}`}
                  source={source}
                />
              ))}
            </Stack>
          </AccordionDetails>
        </Accordion>
      )}
      {summary && (
        <Typography variant="caption" color="text.secondary">
          {running && "Previous graph: "}
          {(summary.nodes ?? 0).toLocaleString()} nodes ·{" "}
          {(summary.edges ?? 0).toLocaleString()} relationships ·{" "}
          {(summary.documents ?? 0).toLocaleString()} documents
          {Boolean(summary.reusedSources) &&
            ` · ${summary.reusedSources} sources reused`}
          {Boolean(summary.generatedEmbeddings) &&
            ` · ${summary.generatedEmbeddings} embeddings generated`}
          {Boolean(summary.reusedEmbeddings) &&
            ` · ${summary.reusedEmbeddings} embeddings reused`}
        </Typography>
      )}
    </Stack>
  );
}

function SourceProgress({ source }: { source: AnalysisSourceStatus }) {
  const progress = indexingProgress(source.completedItems, source.totalItems);
  return (
    <Box>
      <Stack direction="row" spacing={1} alignItems="center">
        <Chip size="small" variant="outlined" label={source.kind ?? "Source"} />
        <Typography variant="body2" sx={{ flex: 1, overflowWrap: "anywhere" }}>
          {source.path}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {source.state === "reused" ? "Reused" : source.state}
        </Typography>
      </Stack>
      {source.message && (
        <Typography variant="caption" color="text.secondary">
          {source.message}
        </Typography>
      )}
      {source.state === "running" && (
        <LinearProgress
          sx={{ mt: 0.5 }}
          variant={progress == null ? "indeterminate" : "determinate"}
          value={progress}
          aria-label={`${source.path ?? "Source"} progress`}
        />
      )}
    </Box>
  );
}
