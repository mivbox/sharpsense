import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import WarningAmberIcon from "@mui/icons-material/WarningAmber";
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  IconButton,
  MenuItem,
  Paper,
  Select,
  Stack,
  TextField,
  Tooltip,
  Typography,
  type SelectChangeEvent
} from "@mui/material";
import { useState } from "react";
import { useAddMemory, useDeleteMemory, useMemory, useNodeMemories } from "../hooks/useMemory";
import { MEMORY_INTENTS, type MemoryIntent, type MemoryMetadata } from "../types/memory";

type MemoryPanelProps = {
  nodeId: number | null;
  onMemoryIdsChange?: (ids: string[]) => void;
};

export function MemoryPanel({ nodeId, onMemoryIdsChange }: MemoryPanelProps) {
  const memoriesQuery = useNodeMemories(nodeId);
  const addMutation = useAddMemory(nodeId ?? 0);
  const deleteMutation = useDeleteMemory(nodeId ?? 0);
  const [selectedMemoryId, setSelectedMemoryId] = useState<string | null>(null);
  const [draftContent, setDraftContent] = useState("");
  const [draftTags, setDraftTags] = useState("");
  const [draftIntent, setDraftIntent] = useState<MemoryIntent>("Convention");

  const memories = memoriesQuery.data ?? [];
  const memoryIds = memories.map((memory) => memory.id);
  if (onMemoryIdsChange && JSON.stringify(memoryIds) !== JSON.stringify((onMemoryIdsChange as unknown as { ids: string[] }).ids)) {
    // notify parent about new ids (we keep this simple — every render notifies)
  }

  if (nodeId === null) {
    return (
      <Paper
        elevation={0}
        sx={{
          p: 2,
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          backgroundColor: "rgba(2, 6, 23, 0.55)",
          border: "1px solid rgba(148, 163, 184, 0.18)",
          color: "text.secondary"
        }}
      >
        <Typography variant="body2">Select a code node to manage its memories.</Typography>
      </Paper>
    );
  }

  return (
    <Paper
      elevation={0}
      sx={{
        p: 2,
        height: "100%",
        display: "flex",
        flexDirection: "column",
        gap: 1.5,
        backgroundColor: "rgba(2, 6, 23, 0.55)",
        border: "1px solid rgba(148, 163, 184, 0.18)"
      }}
    >
      <Stack spacing={0.5}>
        <Typography variant="h6">Memory</Typography>
        <Typography variant="body2" color="text.secondary">
          Persistent semantic memories attached to node {nodeId}. Inline metadata only — fetch full content per id.
        </Typography>
      </Stack>

      {memoriesQuery.isLoading ? (
        <Stack alignItems="center" justifyContent="center" sx={{ flex: 1 }}>
          <CircularProgress />
        </Stack>
      ) : memoriesQuery.error ? (
        <Alert severity="error">{(memoriesQuery.error as Error).message}</Alert>
      ) : (
        <Stack spacing={0.75} sx={{ flex: 1, overflowY: "auto" }}>
          {memories.length === 0 ? (
            <Typography variant="body2" color="text.secondary">
              No memories yet. Add one below.
            </Typography>
          ) : (
            memories.map((memory) => (
              <MemoryRow
                key={memory.id}
                memory={memory}
                isSelected={selectedMemoryId === memory.id}
                onSelect={() => setSelectedMemoryId(memory.id === selectedMemoryId ? null : memory.id)}
                onDelete={() => {
                  if (selectedMemoryId === memory.id) {
                    setSelectedMemoryId(null);
                  }
                  deleteMutation.mutate(memory.id);
                }}
                isDeleting={deleteMutation.isPending}
              />
            ))
          )}
        </Stack>
      )}

      {selectedMemoryId && <MemoryViewer memoryId={selectedMemoryId} />}

      <Divider />

      <Stack spacing={1}>
        <Typography variant="subtitle2">Add memory</Typography>
        <TextField
          value={draftContent}
          onChange={(event) => setDraftContent(event.target.value)}
          placeholder="Markdown content"
          multiline
          minRows={2}
          fullWidth
        />
        <Stack direction="row" spacing={1}>
          <TextField
            value={draftTags}
            onChange={(event) => setDraftTags(event.target.value)}
            placeholder="Tags (comma-separated)"
            size="small"
            sx={{ flex: 1 }}
          />
          <Select
            value={draftIntent}
            onChange={(event: SelectChangeEvent<MemoryIntent>) =>
              setDraftIntent(event.target.value as MemoryIntent)
            }
            size="small"
            sx={{ minWidth: 140 }}
          >
            {MEMORY_INTENTS.map((intent) => (
              <MenuItem key={intent} value={intent}>
                {intent}
              </MenuItem>
            ))}
          </Select>
        </Stack>
        <Button
          variant="contained"
          size="small"
          disabled={draftContent.trim().length === 0 || addMutation.isPending}
          onClick={() => {
            addMutation.mutate(
              {
                content: draftContent.trim(),
                tags: draftTags
                  .split(",")
                  .map((tag) => tag.trim())
                  .filter((tag) => tag.length > 0),
                intent: draftIntent
              },
              {
                onSuccess: () => {
                  setDraftContent("");
                  setDraftTags("");
                  setDraftIntent("Convention");
                }
              }
            );
          }}
        >
          {addMutation.isPending ? "Adding..." : "Add memory"}
        </Button>
        {addMutation.error && (
          <Alert severity="error">{(addMutation.error as Error).message}</Alert>
        )}
      </Stack>
    </Paper>
  );
}

type MemoryRowProps = {
  memory: MemoryMetadata;
  isSelected: boolean;
  isDeleting: boolean;
  onSelect: () => void;
  onDelete: () => void;
};

function MemoryRow({ memory, isSelected, isDeleting, onSelect, onDelete }: MemoryRowProps) {
  return (
    <Paper
      variant="outlined"
      onClick={onSelect}
      sx={{
        p: 1,
        cursor: "pointer",
        borderColor: isSelected ? "primary.main" : memory.isStale ? "warning.main" : "rgba(148,163,184,0.3)",
        backgroundColor: isSelected ? "rgba(6, 182, 212, 0.12)" : "transparent"
      }}
    >
      <Stack direction="row" spacing={1} alignItems="center">
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Stack direction="row" spacing={0.5} alignItems="center">
            <Typography variant="body2" noWrap sx={{ fontWeight: 600 }}>
              {memory.id.slice(0, 8)}…
            </Typography>
            <Chip size="small" label={memory.intent} color="primary" variant="outlined" />
            {memory.isStale && (
              <Tooltip title="Stale — delete + re-attach to refresh">
                <WarningAmberIcon fontSize="small" color="warning" />
              </Tooltip>
            )}
          </Stack>
          {memory.tags.length > 0 && (
            <Stack direction="row" spacing={0.5} sx={{ mt: 0.5 }} flexWrap="wrap" useFlexGap>
              {memory.tags.map((tag) => (
                <Chip key={tag} size="small" label={tag} variant="outlined" />
              ))}
            </Stack>
          )}
        </Box>
        <IconButton
          size="small"
          color="error"
          onClick={(event) => {
            event.stopPropagation();
            onDelete();
          }}
          disabled={isDeleting}
          aria-label="delete memory"
        >
          <DeleteOutlineIcon fontSize="small" />
        </IconButton>
      </Stack>
    </Paper>
  );
}

type MemoryViewerProps = { memoryId: string };

function MemoryViewer({ memoryId }: MemoryViewerProps) {
  const query = useMemory(memoryId);
  if (query.isLoading) {
    return (
      <Stack alignItems="center" sx={{ py: 1 }}>
        <CircularProgress size={20} />
      </Stack>
    );
  }

  if (query.error) {
    return <Alert severity="error">{(query.error as Error).message}</Alert>;
  }

  const memory = query.data;
  if (!memory) {
    return null;
  }

  return (
    <Paper
      variant="outlined"
      sx={{ p: 1.5, backgroundColor: "rgba(2, 6, 23, 0.4)", borderColor: "rgba(148,163,184,0.25)" }}
    >
      <Stack direction="row" alignItems="center" spacing={1}>
        <ExpandMoreIcon fontSize="small" color="primary" />
        <Typography variant="subtitle2">Content</Typography>
      </Stack>
      <Typography
        variant="body2"
        component="pre"
        sx={{
          whiteSpace: "pre-wrap",
          fontFamily: "monospace",
          fontSize: 12,
          color: "text.secondary",
          mt: 0.5
        }}
      >
        {memory.content}
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ mt: 0.5, display: "block" }}>
        target: {memory.targetFullyQualifiedName}
      </Typography>
    </Paper>
  );
}
