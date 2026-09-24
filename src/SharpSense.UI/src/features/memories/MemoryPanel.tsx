import { useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import AddRoundedIcon from "@mui/icons-material/AddRounded";
import DeleteOutlineRoundedIcon from "@mui/icons-material/DeleteOutlineRounded";
import ExpandLessRoundedIcon from "@mui/icons-material/ExpandLessRounded";
import ExpandMoreRoundedIcon from "@mui/icons-material/ExpandMoreRounded";
import StickyNote2OutlinedIcon from "@mui/icons-material/StickyNote2Outlined";
import type { MemoryIntent } from "../../shared/api/models";
import { memoryIntents } from "../../shared/api/models";
import { EmptyState, ErrorState, LoadingRows } from "../../shared/ui/States";
import { useMemory, useMemoryActions, useNodeMemories } from "./useMemories";

export function MemoryPanel({ nodeId }: { nodeId: number }) {
  const memories = useNodeMemories(nodeId);
  const actions = useMemoryActions(nodeId);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<string | null>(null);
  const [content, setContent] = useState("");
  const [tags, setTags] = useState("");
  const [intent, setIntent] = useState<MemoryIntent>("Convention");
  const detail = useMemory(expandedId);
  const submit = async () => {
    try {
      await actions.add.mutateAsync({
        content: content.trim(),
        tags: tags
          .split(",")
          .map((tag) => tag.trim())
          .filter(Boolean),
        intent,
      });
      setAdding(false);
      setContent("");
      setTags("");
      setIntent("Convention");
    } catch {
      /* Mutation error rendered in dialog. */
    }
  };
  const remove = async () => {
    if (!removing) return;
    if (expandedId === removing) setExpandedId(null);
    try {
      await actions.remove.mutateAsync(removing);
      setRemoving(null);
    } catch {
      /* Mutation error rendered in dialog. */
    }
  };
  return (
    <Stack spacing={2} sx={{ p: 2 }} data-testid="memory-panel">
      <Stack direction="row" alignItems="center" justifyContent="space-between">
        <Typography variant="subtitle2">
          Team knowledge{" "}
          {memories.data?.length ? `(${memories.data.length})` : ""}
        </Typography>
        <Button
          size="small"
          startIcon={<AddRoundedIcon />}
          onClick={() => {
            actions.add.reset();
            setAdding(true);
          }}
          data-testid="add-memory-button"
        >
          Add
        </Button>
      </Stack>
      <Typography variant="caption" color="text.secondary">
        Keep decisions, conventions, and warnings alongside this symbol.
      </Typography>
      {memories.isPending ? (
        <LoadingRows count={3} />
      ) : memories.error ? (
        <ErrorState
          error={memories.error}
          retry={() => {
            void memories.refetch();
          }}
        />
      ) : memories.data.length === 0 ? (
        <EmptyState
          compact
          icon={<StickyNote2OutlinedIcon />}
          title="No memories yet"
          description="Capture what the next person should know about this code."
        />
      ) : (
        memories.data.map((memory) => (
          <Paper
            key={memory.id}
            variant="outlined"
            sx={{ p: 1.5 }}
            data-memory-id={memory.id}
          >
            <Stack
              direction="row"
              alignItems="center"
              justifyContent="space-between"
              spacing={1}
            >
              <Chip
                size="small"
                label={memory.intent}
                color={memory.intent === "Warning" ? "warning" : "default"}
              />
              <Stack direction="row">
                <Tooltip
                  title={
                    expandedId === memory.id ? "Hide memory" : "Read memory"
                  }
                >
                  <IconButton
                    aria-label={
                      expandedId === memory.id ? "Hide memory" : "Read memory"
                    }
                    size="small"
                    onClick={() =>
                      setExpandedId(expandedId === memory.id ? null : memory.id)
                    }
                  >
                    {expandedId === memory.id ? (
                      <ExpandLessRoundedIcon fontSize="small" />
                    ) : (
                      <ExpandMoreRoundedIcon fontSize="small" />
                    )}
                  </IconButton>
                </Tooltip>
                <Tooltip title="Delete memory">
                  <IconButton
                    size="small"
                    aria-label="Delete memory"
                    onClick={() => {
                      actions.remove.reset();
                      setRemoving(memory.id);
                    }}
                  >
                    <DeleteOutlineRoundedIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </Stack>
            </Stack>
            {memory.isStale && (
              <Alert severity="warning" sx={{ mt: 1 }}>
                The code has changed. Review this memory before relying on it.
              </Alert>
            )}
            {memory.tags.length > 0 && (
              <Stack direction="row" flexWrap="wrap" gap={0.5} sx={{ mt: 1 }}>
                {memory.tags.map((tag) => (
                  <Chip size="small" variant="outlined" label={tag} key={tag} />
                ))}
              </Stack>
            )}
            {expandedId === memory.id && (
              <Box sx={{ mt: 1.5 }}>
                {detail.isPending ? (
                  <CircularProgress size={18} />
                ) : detail.error ? (
                  <ErrorState
                    error={detail.error}
                    retry={() => {
                      void detail.refetch();
                    }}
                  />
                ) : (
                  <Typography
                    variant="body2"
                    sx={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}
                    data-testid="memory-content"
                  >
                    {detail.data?.content}
                  </Typography>
                )}
              </Box>
            )}
            {memory.createdAt && (
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ mt: 1, display: "block" }}
              >
                {new Date(memory.createdAt).toLocaleDateString(undefined, {
                  day: "numeric",
                  month: "short",
                  year: "numeric",
                })}
              </Typography>
            )}
          </Paper>
        ))
      )}
      <Dialog
        open={adding}
        onClose={() => {
          if (!actions.add.isPending) setAdding(false);
        }}
        fullWidth
        maxWidth="sm"
      >
        <DialogTitle>Add a memory</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            Attach useful knowledge to symbol #{nodeId}. Memories stay with your
            local workspace.
          </DialogContentText>
          <Stack spacing={2}>
            <TextField
              autoFocus
              multiline
              minRows={4}
              fullWidth
              label="Memory content"
              disabled={actions.add.isPending}
              value={content}
              onChange={(event) => setContent(event.target.value)}
              placeholder="What should someone working on this code know?"
            />
            <TextField
              select
              label="Intent"
              disabled={actions.add.isPending}
              value={intent}
              onChange={(event) =>
                setIntent(event.target.value as MemoryIntent)
              }
            >
              {memoryIntents.map((value) => (
                <MenuItem key={value} value={value}>
                  {value}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Tags"
              disabled={actions.add.isPending}
              value={tags}
              onChange={(event) => setTags(event.target.value)}
              helperText="Optional, separated by commas"
            />
            {actions.add.error && <ErrorState error={actions.add.error} />}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button
            onClick={() => setAdding(false)}
            disabled={actions.add.isPending}
          >
            Cancel
          </Button>
          <Button
            variant="contained"
            disabled={!content.trim() || actions.add.isPending}
            onClick={() => {
              void submit();
            }}
            data-testid="save-memory-button"
          >
            {actions.add.isPending ? "Saving…" : "Save memory"}
          </Button>
        </DialogActions>
      </Dialog>
      <Dialog
        open={Boolean(removing)}
        onClose={() => {
          if (!actions.remove.isPending) setRemoving(null);
        }}
        fullWidth
        maxWidth="xs"
      >
        <DialogTitle>Delete this memory?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            This removes the memory from your workspace. This action cannot be
            undone.
          </DialogContentText>
          {actions.remove.error && (
            <Box sx={{ mt: 2 }}>
              <ErrorState error={actions.remove.error} />
            </Box>
          )}
        </DialogContent>
        <DialogActions>
          <Button
            onClick={() => setRemoving(null)}
            disabled={actions.remove.isPending}
          >
            Cancel
          </Button>
          <Button
            color="error"
            variant="contained"
            disabled={actions.remove.isPending}
            onClick={() => {
              void remove();
            }}
            data-testid="confirm-delete-memory"
          >
            {actions.remove.isPending ? "Deleting…" : "Delete memory"}
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}
