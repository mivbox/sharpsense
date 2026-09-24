import { useEffect, useRef, useState } from "react";
import { Alert, Box, Paper, Typography } from "@mui/material";
import type { GraphData, GraphNode } from "../../shared/api/models";
import {
  GraphScene,
  type GraphHover,
  type LayoutStatus,
} from "./rendering/GraphScene";

export default function GraphCanvas({
  data,
  stream,
  selected,
  search,
  fitToken,
  onSelect,
}: {
  data: GraphData;
  stream?: object;
  selected: GraphNode | null;
  search: string;
  fitToken: number;
  onSelect: (node: GraphNode | null) => void;
}) {
  const container = useRef<HTMLDivElement | null>(null);
  const scene = useRef<GraphScene | null>(null);
  const onSelectRef = useRef(onSelect);
  const [status, setStatus] = useState<LayoutStatus>({ busy: true });
  const [hover, setHover] = useState<GraphHover>(null);
  const { nodes, edges } = data;
  const selectedId = selected?.id;

  useEffect(() => {
    onSelectRef.current = onSelect;
  }, [onSelect]);
  useEffect(() => {
    if (!container.current) return;
    let mounted = true;
    let instance: GraphScene;
    try {
      instance = new GraphScene(container.current, {
        onSelect: (node) => onSelectRef.current(node),
        onHover: setHover,
        onStatus: setStatus,
      });
    } catch {
      queueMicrotask(() => {
        if (mounted)
          setStatus({
            busy: false,
            error:
              "The 3D view is unavailable in this browser. Use List view to explore every loaded node.",
          });
      });
      return () => {
        mounted = false;
      };
    }
    scene.current = instance;
    return () => {
      mounted = false;
      instance.dispose();
      scene.current = null;
    };
  }, []);
  useEffect(() => {
    scene.current?.setData({ nodes, edges }, stream);
  }, [nodes, edges, stream]);
  useEffect(() => {
    scene.current?.setSelection(selectedId, search);
  }, [selectedId, search]);
  useEffect(() => {
    scene.current?.fit();
  }, [fitToken]);

  return (
    <Box
      data-testid="graph-canvas"
      aria-busy={status.busy}
      sx={{
        height: "100%",
        minHeight: 340,
        position: "relative",
        overflow: "hidden",
      }}
    >
      <Box ref={container} sx={{ position: "absolute", inset: 0 }} />
      {status.error && (
        <Alert
          severity="warning"
          sx={{ position: "absolute", top: 12, left: 12, right: 12 }}
        >
          {status.error}
        </Alert>
      )}
      {hover && (
        <Paper
          elevation={4}
          sx={{
            position: "absolute",
            left: hover.x,
            top: hover.y,
            width: 280,
            maxWidth: "calc(100% - 16px)",
            px: 1.5,
            py: 1,
            pointerEvents: "none",
          }}
        >
          <Typography variant="caption" sx={{ overflowWrap: "anywhere" }}>
            {hover.node.label}
          </Typography>
          <Typography variant="caption" color="text.secondary" display="block">
            {hover.node.type}
            {hover.node.codeNodeId ? ` · #${hover.node.codeNodeId}` : ""}
          </Typography>
        </Paper>
      )}
      <Typography
        variant="caption"
        color="text.secondary"
        sx={{
          position: "absolute",
          bottom: 16,
          left: 16,
          pointerEvents: "none",
        }}
      >
        Drag to orbit · Scroll to zoom · Click to inspect
        {selected && " · Outgoing blue · Incoming purple"}
      </Typography>
    </Box>
  );
}
