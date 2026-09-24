import { useEffect, useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Autocomplete,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import AddRoundedIcon from "@mui/icons-material/AddRounded";
import DeleteOutlineRoundedIcon from "@mui/icons-material/DeleteOutlineRounded";
import { discoverSources, saveWorkspace } from "./api";
import { absoluteDiscoveredSource } from "./sourceDiscovery";
import {
  sourceKinds,
  sourceLabels,
  type WorkspaceSource,
  type WorkspaceSummary,
  type WorkspaceInput,
} from "./models";

export function WorkspaceEditorDialog({
  workspace,
  onClose,
  onSaved,
}: {
  workspace?: WorkspaceSummary;
  onClose: () => void;
  onSaved: (workspace: WorkspaceSummary) => void;
}) {
  const client = useQueryClient();
  const [name, setName] = useState(workspace?.name ?? "");
  const [repositoryRoot, setRepositoryRoot] = useState(
    workspace?.repositoryRoot ?? "",
  );
  const [sources, setSources] = useState<WorkspaceSource[]>(
    workspace?.sources.length
      ? workspace.sources
      : [{ kind: "CSharp", path: "" }],
  );
  const [discoveredSelection, setDiscoveredSelection] = useState<
    WorkspaceSource[]
  >([]);
  const activeDiscovery = useRef<AbortController | null>(null);
  useEffect(() => () => activeDiscovery.current?.abort(), []);
  const discover = useMutation({
    mutationFn: (sourceBase: string) => {
      activeDiscovery.current?.abort();
      const controller = new AbortController();
      activeDiscovery.current = controller;
      return discoverSources(sourceBase, controller.signal);
    },
  });
  const discovery =
    discover.variables === repositoryRoot.trim() ? discover.data : undefined;
  const save = useMutation({
    mutationFn: (input: WorkspaceInput) => saveWorkspace(input, workspace?.id),
    onSuccess: async (saved) => {
      await client.invalidateQueries({ queryKey: ["workspace-catalog"] });
      onSaved(saved);
    },
  });
  const valid =
    name.trim() &&
    repositoryRoot.trim() &&
    sources.length > 0 &&
    sources.every((source) => source.path.trim());
  const removesSources = workspace?.sources.some(
    (original) =>
      !sources.some(
        (source) =>
          source.kind === original.kind && source.path.trim() === original.path,
      ),
  );
  const changeSource = (index: number, patch: Partial<WorkspaceSource>) => {
    setSources((previous) =>
      previous.map((source, i) =>
        i === index ? { ...source, ...patch } : source,
      ),
    );
  };

  return (
    <Dialog
      open
      onClose={() => {
        if (!save.isPending) onClose();
      }}
      fullWidth
      maxWidth="md"
    >
      <DialogTitle>
        {workspace ? "Edit workspace" : "Create workspace"}
      </DialogTitle>
      <DialogContent dividers>
        <Stack spacing={3}>
          {save.error && <Alert severity="error">{save.error.message}</Alert>}
          {removesSources && (
            <Alert severity="warning">
              The next analysis removes symbols from deselected sources and
              deletes memories attached to those symbols.
            </Alert>
          )}
          <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
            <TextField
              autoFocus
              required
              fullWidth
              label="Workspace name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              disabled={save.isPending}
              placeholder="customer-experience"
            />
            <TextField
              required
              fullWidth
              label="Repository directory"
              value={repositoryRoot}
              onChange={(event) => {
                setRepositoryRoot(event.target.value);
                activeDiscovery.current?.abort();
                discover.reset();
                setDiscoveredSelection([]);
              }}
              disabled={Boolean(workspace) || save.isPending}
              placeholder="/Users/you/sources/product"
              helperText="Absolute path on the machine running SharpSense."
            />
          </Stack>
          <Stack spacing={1.5}>
            <Button
              variant="outlined"
              disabled={
                !repositoryRoot.trim() || save.isPending || discover.isPending
              }
              onClick={() => {
                setDiscoveredSelection([]);
                discover.mutate(repositoryRoot.trim());
              }}
            >
              {discover.isPending ? "Discovering sources…" : "Discover sources"}
            </Button>
            {discover.error && (
              <Alert severity="error">{discover.error.message}</Alert>
            )}
            {discovery && (
              <>
                <Autocomplete
                  multiple
                  options={discovery.sources}
                  value={discoveredSelection}
                  onChange={(_, selected) => setDiscoveredSelection(selected)}
                  isOptionEqualToValue={(a, b) =>
                    a.kind === b.kind && a.path === b.path
                  }
                  getOptionLabel={(source) =>
                    `${source.path} (${source.kind === "CSharp" ? "C#" : source.kind})`
                  }
                  renderInput={(params) => (
                    <TextField
                      {...params}
                      label="Discovered sources"
                      helperText="Choose only the projects and documentation you want in this workspace."
                    />
                  )}
                />
                <Button
                  disabled={!discoveredSelection.length || save.isPending}
                  onClick={() => {
                    setSources((previous) => [
                      ...previous.filter((source) => source.path.trim()),
                      ...discoveredSelection
                        .map((source) =>
                          absoluteDiscoveredSource(
                            discovery.repositoryRoot,
                            source,
                          ),
                        )
                        .filter(
                          (source) =>
                            !previous.some(
                              (existing) =>
                                existing.kind === source.kind &&
                                existing.path === source.path,
                            ),
                        ),
                    ]);
                    setDiscoveredSelection([]);
                  }}
                >
                  Add selected sources
                </Button>
              </>
            )}
          </Stack>
          <Stack spacing={1}>
            <Typography variant="subtitle1">Code and documentation</Typography>
            <Typography variant="body2" color="text.secondary">
              Combine C# projects, a frontend, and selected docs. Paths are
              relative to the repository directory, or absolute paths on the
              machine running SharpSense.
            </Typography>
          </Stack>
          {sources.map((source, index) => (
            <Stack
              key={index}
              direction={{ xs: "column", sm: "row" }}
              spacing={1.5}
              alignItems="flex-start"
            >
              <TextField
                select
                label="Source type"
                value={source.kind}
                disabled={save.isPending}
                sx={{
                  minWidth: { sm: 230 },
                  width: { xs: "100%", sm: "auto" },
                }}
                onChange={(event) =>
                  changeSource(index, {
                    kind: event.target.value as WorkspaceSource["kind"],
                  })
                }
              >
                {sourceKinds.map((kind) => (
                  <MenuItem key={kind} value={kind}>
                    {sourceLabels[kind]}
                  </MenuItem>
                ))}
              </TextField>
              <TextField
                required
                fullWidth
                label="Source path"
                value={source.path}
                disabled={save.isPending}
                onChange={(event) =>
                  changeSource(index, { path: event.target.value })
                }
                placeholder={
                  source.kind === "CSharp"
                    ? "services/Orders/Orders.csproj"
                    : source.kind === "TypeScript"
                      ? "apps/frontend/tsconfig.app.json"
                      : "docs/**/*.md"
                }
              />
              <IconButton
                aria-label={`Remove source ${index + 1}`}
                disabled={sources.length === 1 || save.isPending}
                onClick={() =>
                  setSources((previous) =>
                    previous.filter((_, i) => i !== index),
                  )
                }
              >
                <DeleteOutlineRoundedIcon />
              </IconButton>
            </Stack>
          ))}
          <Button
            startIcon={<AddRoundedIcon />}
            variant="outlined"
            disabled={save.isPending}
            onClick={() =>
              setSources((previous) => [
                ...previous,
                { kind: "Markdown", path: "" },
              ])
            }
          >
            Add source
          </Button>
          <Typography variant="body2" color="text.secondary">
            Configuration and indexes are stored in your SharpSense home
            directory. No configuration is added to your repository.
          </Typography>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={save.isPending}>
          Cancel
        </Button>
        <Button
          variant="contained"
          disabled={!valid || save.isPending || discover.isPending}
          onClick={() =>
            save.mutate({
              name: name.trim(),
              repositoryRoot: repositoryRoot.trim(),
              sources: sources.map((source) => ({
                ...source,
                path: source.path.trim(),
              })),
            })
          }
        >
          {save.isPending
            ? "Saving…"
            : workspace
              ? "Save workspace"
              : "Create workspace"}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
