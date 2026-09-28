import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Typography,
} from "@mui/material";
import ExpandMoreRoundedIcon from "@mui/icons-material/ExpandMoreRounded";
import RefreshRoundedIcon from "@mui/icons-material/RefreshRounded";
import { ErrorState, LoadingRows } from "../../shared/ui/States";
import { GraphStatsView } from "./GraphStatsView";
import { useGraphStats } from "./queries";
import { useWorkspaceOverview } from "../../shared/api/queries";

export function IndexStatusDialog({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  const stats = useGraphStats(open);
  const overview = useWorkspaceOverview();
  const sources = overview.data?.sources ?? [];

  return (
    <Dialog
      open={open}
      onClose={onClose}
      fullWidth
      maxWidth="sm"
      aria-labelledby="index-status-title"
    >
      <DialogTitle id="index-status-title">
        {overview.data?.name ?? "Workspace"} index
      </DialogTitle>
      <DialogContent dividers>
        {stats.error && (
          <ErrorState error={stats.error} retry={() => void stats.refetch()} />
        )}
        {stats.isPending ? (
          <LoadingRows count={6} />
        ) : stats.data ? (
          <GraphStatsView stats={stats.data} />
        ) : null}
        {sources.length > 0 && (
          <Accordion variant="outlined" disableGutters sx={{ mt: 2 }}>
            <AccordionSummary expandIcon={<ExpandMoreRoundedIcon />}>
              <Typography>Selected sources ({sources.length})</Typography>
            </AccordionSummary>
            <AccordionDetails>
              <List dense disablePadding>
                {sources.map((source) => (
                  <ListItem
                    key={`${source.kind}:${source.path}`}
                    disableGutters
                  >
                    <ListItemText
                      primary={source.path}
                      secondary={source.kind === "CSharp" ? "C#" : source.kind}
                      slotProps={{
                        primary: { sx: { overflowWrap: "anywhere" } },
                      }}
                    />
                  </ListItem>
                ))}
              </List>
            </AccordionDetails>
          </Accordion>
        )}
      </DialogContent>
      <DialogActions>
        <Button
          onClick={() => void stats.refetch()}
          startIcon={<RefreshRoundedIcon />}
          disabled={stats.isFetching}
        >
          {stats.isFetching ? "Refreshing…" : "Refresh"}
        </Button>
        <Button onClick={onClose}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}
