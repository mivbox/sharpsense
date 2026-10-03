import {
  Box,
  Button,
  Chip,
  Divider,
  IconButton,
  Stack,
  Typography,
} from "@mui/material";
import ArrowForwardRoundedIcon from "@mui/icons-material/ArrowForwardRounded";
import CloseRoundedIcon from "@mui/icons-material/CloseRounded";
import type { SearchHit, ToolSelection } from "../../shared/api/models";

export function SearchResultDetails({
  selected,
  onOpenTool,
  onClose,
}: {
  selected: SearchHit;
  onOpenTool: (selection: ToolSelection) => void;
  onClose?: () => void;
}) {
  return (
    <Stack spacing={2}>
      <Stack
        direction="row"
        spacing={1}
        useFlexGap
        sx={{ alignItems: "center" }}
      >
        <Chip
          label={selected.kind}
          size="small"
          color="primary"
          variant="outlined"
        />
        <Chip label={"#" + selected.nodeId} size="small" variant="outlined" />
        {onClose && (
          <IconButton
            aria-label="Close search result"
            onClick={onClose}
            sx={{ ml: "auto" }}
          >
            <CloseRoundedIcon />
          </IconButton>
        )}
      </Stack>
      <Typography variant="subtitle1" sx={{ overflowWrap: "anywhere" }}>
        {selected.label}
      </Typography>
      <Typography
        variant="caption"
        color="text.secondary"
        sx={{ overflowWrap: "anywhere" }}
      >
        {selected.path}
        {selected.startLine ? ":" + selected.startLine : ""}
      </Typography>
      <Divider />
      <Typography variant="overline" color="text.secondary">
        Explore this result
      </Typography>
      <Button
        variant="contained"
        endIcon={<ArrowForwardRoundedIcon />}
        onClick={() =>
          onOpenTool({
            tool: "context",
            nodeId: selected.nodeId,
            label: selected.label,
          })
        }
        data-testid="search-inspect-context"
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
              nodeId: selected.nodeId,
              label: selected.label,
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
              nodeId: selected.nodeId,
              label: selected.label,
            })
          }
        >
          Assess impact
        </Button>
      </Stack>
      {selected.summary && (
        <>
          <Divider />
          <Typography variant="overline" color="text.secondary">
            Indexed excerpt
          </Typography>
          <Box
            component="pre"
            sx={{
              m: 0,
              whiteSpace: "pre-wrap",
              overflowWrap: "anywhere",
              fontFamily: "monospace",
              fontSize: 12,
              lineHeight: 1.7,
              maxHeight: 350,
              overflow: "auto",
            }}
          >
            {selected.summary}
          </Box>
        </>
      )}
    </Stack>
  );
}
