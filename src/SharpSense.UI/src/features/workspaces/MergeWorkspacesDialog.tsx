import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Autocomplete,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { mergeWorkspaces } from "./api";
import type { WorkspaceSummary } from "../../shared/workspace/models";

export function MergeWorkspacesDialog({
  workspaces,
  onClose,
  onSaved,
}: {
  workspaces: WorkspaceSummary[];
  onClose: () => void;
  onSaved: (workspace: WorkspaceSummary) => void;
}) {
  const [name, setName] = useState("");
  const [selected, setSelected] = useState<WorkspaceSummary[]>([]);
  const client = useQueryClient();
  const merge = useMutation({
    mutationFn: () =>
      mergeWorkspaces(
        name.trim(),
        selected.map((workspace) => workspace.id),
      ),
    onSuccess: async (workspace) => {
      await client.invalidateQueries({ queryKey: ["workspace-catalog"] });
      onSaved(workspace);
    },
  });
  const sameRepository =
    new Set(selected.map((workspace) => workspace.repositoryRoot)).size <= 1;
  return (
    <Dialog
      open
      onClose={() => {
        if (!merge.isPending) onClose();
      }}
      fullWidth
      maxWidth="sm"
    >
      <DialogTitle>Merge workspaces</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={3}>
          {merge.error && <Alert severity="error">{merge.error.message}</Alert>}
          <Typography variant="body2" color="text.secondary">
            Create a new workspace from existing source selections in the same
            repository. Its index and memories are independent.
          </Typography>
          <TextField
            autoFocus
            required
            label="New workspace name"
            value={name}
            disabled={merge.isPending}
            onChange={(event) => setName(event.target.value)}
          />
          <Autocomplete
            multiple
            options={workspaces}
            value={selected}
            disabled={merge.isPending}
            onChange={(_, values) => setSelected(values)}
            getOptionLabel={(workspace) => workspace.name}
            isOptionEqualToValue={(a, b) => a.id === b.id}
            renderInput={(params) => (
              <TextField {...params} label="Source workspaces" />
            )}
          />
          {!sameRepository && (
            <Alert severity="warning">
              Choose workspaces from the same repository.
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button disabled={merge.isPending} onClick={onClose}>
          Cancel
        </Button>
        <Button
          variant="contained"
          disabled={
            !name.trim() ||
            selected.length < 2 ||
            !sameRepository ||
            merge.isPending
          }
          onClick={() => merge.mutate()}
        >
          {merge.isPending ? "Merging…" : "Create merged workspace"}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
