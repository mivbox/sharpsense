import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  AppBar,
  Box,
  Button,
  Card,
  CardActions,
  CardContent,
  Chip,
  Container,
  Divider,
  Grid,
  Paper,
  Stack,
  Toolbar,
  Typography,
} from "@mui/material";
import AddRoundedIcon from "@mui/icons-material/AddRounded";
import BoltRoundedIcon from "@mui/icons-material/BoltRounded";
import FolderOutlinedIcon from "@mui/icons-material/FolderOutlined";
import MergeRoundedIcon from "@mui/icons-material/MergeRounded";
import { ErrorState, LoadingRows } from "../../shared/ui/States";
import { workspaceCatalogOptions } from "./queries";
import { WorkspaceEditorDialog } from "./WorkspaceEditorDialog";
import { MergeWorkspacesDialog } from "./MergeWorkspacesDialog";
import type { WorkspaceSummary } from "../../shared/workspace/models";

export function WorkspacesPage({
  onOpen,
}: {
  onOpen: (workspace: WorkspaceSummary) => void;
}) {
  const catalog = useQuery(workspaceCatalogOptions);
  const [editor, setEditor] = useState<WorkspaceSummary | "new" | null>(null);
  const [merging, setMerging] = useState(false);
  const workspaces = catalog.data?.workspaces ?? [];

  return (
    <Box sx={{ minHeight: "100dvh", bgcolor: "background.default" }}>
      <AppBar position="static" color="transparent" elevation={0}>
        <Toolbar>
          <BoltRoundedIcon color="primary" sx={{ mr: 1 }} />
          <Typography variant="h6">SharpSense</Typography>
          <Chip label="1.0" size="small" sx={{ ml: 1.5 }} />
        </Toolbar>
        <Divider />
      </AppBar>
      <Container maxWidth="lg" sx={{ py: { xs: 3, md: 6 } }}>
        <Stack spacing={4}>
          <Stack
            direction={{ xs: "column", sm: "row" }}
            spacing={2}
            sx={{ justifyContent: "space-between" }}
          >
            <Box>
              <Typography variant="h4" component="h1">
                Your workspaces
              </Typography>
              <Typography color="text.secondary" sx={{ mt: 1 }}>
                Choose the code and documentation you want to explore together.
              </Typography>
            </Box>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
              <Button
                startIcon={<MergeRoundedIcon />}
                disabled={workspaces.length < 2}
                onClick={() => setMerging(true)}
              >
                Merge
              </Button>
              <Button
                variant="contained"
                startIcon={<AddRoundedIcon />}
                onClick={() => setEditor("new")}
              >
                New workspace
              </Button>
            </Stack>
          </Stack>
          {catalog.error && (
            <ErrorState
              error={catalog.error}
              retry={() => void catalog.refetch()}
            />
          )}
          {catalog.isPending ? (
            <LoadingRows count={4} />
          ) : workspaces.length === 0 ? (
            <Paper
              variant="outlined"
              sx={{ p: { xs: 3, md: 6 }, textAlign: "center" }}
            >
              <FolderOutlinedIcon color="primary" sx={{ fontSize: 48 }} />
              <Typography variant="h6" sx={{ mt: 2 }}>
                Create your first workspace
              </Typography>
              <Typography color="text.secondary" sx={{ mt: 1, mb: 3 }}>
                Bring together a frontend, selected C# projects, and
                documentation from a repository.
              </Typography>
              <Button variant="contained" onClick={() => setEditor("new")}>
                Create workspace
              </Button>
            </Paper>
          ) : (
            <Grid container spacing={2}>
              {workspaces.map((workspace) => (
                <Grid key={workspace.id} size={{ xs: 12, sm: 6, lg: 4 }}>
                  <Card
                    variant="outlined"
                    sx={{
                      height: "100%",
                      display: "flex",
                      flexDirection: "column",
                    }}
                  >
                    <CardContent sx={{ flexGrow: 1 }}>
                      <Stack
                        direction="row"
                        spacing={1.5}
                        sx={{ alignItems: "center" }}
                      >
                        <FolderOutlinedIcon color="primary" />
                        <Typography
                          variant="h6"
                          sx={{ overflowWrap: "anywhere" }}
                        >
                          {workspace.name}
                        </Typography>
                      </Stack>
                      <Typography
                        variant="body2"
                        color="text.secondary"
                        sx={{ mt: 1, overflowWrap: "anywhere" }}
                      >
                        {workspace.repositoryRoot}
                      </Typography>
                      <Stack
                        direction="row"
                        sx={{ gap: 1, flexWrap: "wrap", mt: 2 }}
                      >
                        <Chip
                          size="small"
                          label={`${workspace.sources.length} ${workspace.sources.length === 1 ? "source" : "sources"}`}
                        />
                        {[
                          ...new Set(
                            workspace.sources.map((source) => source.kind),
                          ),
                        ].map((kind) => (
                          <Chip
                            key={kind}
                            size="small"
                            variant="outlined"
                            label={kind === "CSharp" ? "C#" : kind}
                          />
                        ))}
                      </Stack>
                    </CardContent>
                    <CardActions sx={{ px: 2, pb: 2 }}>
                      <Button onClick={() => onOpen(workspace)}>
                        Open workspace
                      </Button>
                      <Button
                        color="inherit"
                        onClick={() => setEditor(workspace)}
                      >
                        Edit sources
                      </Button>
                    </CardActions>
                  </Card>
                </Grid>
              ))}
            </Grid>
          )}
        </Stack>
      </Container>
      {editor && (
        <WorkspaceEditorDialog
          workspace={editor === "new" ? undefined : editor}
          onClose={() => setEditor(null)}
          onSaved={(workspace) => {
            setEditor(null);
            onOpen(workspace);
          }}
        />
      )}
      {merging && (
        <MergeWorkspacesDialog
          workspaces={workspaces}
          onClose={() => setMerging(false)}
          onSaved={(workspace) => {
            setMerging(false);
            onOpen(workspace);
          }}
        />
      )}
    </Box>
  );
}
