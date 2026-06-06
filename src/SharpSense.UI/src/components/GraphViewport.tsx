import CenterFocusStrongIcon from "@mui/icons-material/CenterFocusStrong";
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Divider,
  FormControlLabel,
  FormGroup,
  IconButton,
  Paper,
  Stack,
  Tooltip,
  Typography
} from "@mui/material";
import { forceCollide, forceManyBody } from "d3-force-3d";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import ForceGraph3D from "react-force-graph-3d";
import type { ForceGraphMethods } from "react-force-graph-3d";
import type { ReactNode } from "react";
import type { GraphApiEdge, GraphApiNode, GraphApiResponse } from "../types/graph";

type GraphViewportProps = {
  errorMessage: string | null;
  graphData: GraphApiResponse;
  hasSelection: boolean;
  isLoading: boolean;
  onSelectNode?: (nodeId: number | null) => void;
  onToggleShowEdges: (showEdges: boolean) => void;
  searchText: string;
  showEdges: boolean;
};

type GraphNodeDatum = GraphApiNode & {
  degree: number;
  inDegree: number;
  outDegree: number;
  neighborIds: Set<string>;
  volume: number;
  isTopHub: boolean;
  x?: number;
  y?: number;
  z?: number;
  vx?: number;
  vy?: number;
  vz?: number;
  fx?: number;
  fy?: number;
  fz?: number;
};

type GraphLinkDatum = GraphApiEdge & {
  source: string | GraphNodeDatum;
  target: string | GraphNodeDatum;
  sourceId: string;
  targetId: string;
  baseColor: string;
  baseWidth: number;
};

type PreparedGraph = {
  graphData: {
    nodes: GraphNodeDatum[];
    links: GraphLinkDatum[];
  };
  nodeById: Map<string, GraphNodeDatum>;
  nodeTypeCounts: Map<string, number>;
  nodeTypes: string[];
  topHubIds: Set<string>;
};

type ViewportSize = {
  width: number;
  height: number;
};

const DEFAULT_NODE_COLOR = "#60a5fa";
const DEFAULT_EDGE_COLOR = "#cbd5e1";
const SELECTED_NODE_ALPHA = 0.95;
const EXTERNAL_NODE_ALPHA = 0.28;
const NODE_GHOST_ALPHA = 0.05;
const EXTERNAL_NODE_GHOST_ALPHA = 0.12;
const INTERNAL_EDGE_ALPHA = 0.82;
const BOUNDARY_EDGE_ALPHA = 0.38;
const EDGE_GHOST_ALPHA = 0.05;
const BOUNDARY_EDGE_GHOST_ALPHA = 0.08;
const NODE_REL_SIZE = 2.6;
const MACRO_ARCHITECTURE_NODE_TYPES = new Set(["project", "class", "interface"]);
const NODE_TYPE_ORDER = ["project", "class", "interface", "method", "property", "field"];

export function GraphViewport({
  errorMessage,
  graphData,
  hasSelection,
  isLoading,
  onToggleShowEdges,
  searchText,
  showEdges
}: GraphViewportProps) {
  const graphRef = useRef<ForceGraphMethods<GraphNodeDatum, GraphLinkDatum> | undefined>(
    undefined
  );
  const autoFitRef = useRef(false);
  const viewportSize = useViewportSize();
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const preparedGraph = useMemo(() => buildPreparedGraph(graphData), [graphData]);
  const knownNodeTypesRef = useRef<Set<string>>(new Set(preparedGraph.nodeTypes));
  const normalizedSearchText = normalizeText(searchText);
  const [visibleTypes, setVisibleTypes] = useState<Set<string>>(() =>
    getDefaultVisibleTypes(preparedGraph.nodeTypes)
  );

  useEffect(() => {
    setVisibleTypes((current) => {
      const previousKnownTypes = knownNodeTypesRef.current;
      const previouslyShowedAllKnownTypes = [...previousKnownTypes].every((type) =>
        current.has(type)
      );
      const next = new Set(
        [...current].filter((type) => preparedGraph.nodeTypeCounts.has(type))
      );

      preparedGraph.nodeTypes.forEach((type) => {
        const isNewType = !previousKnownTypes.has(type);
        if (isNewType && (previouslyShowedAllKnownTypes || shouldTypeStartVisible(type))) {
          next.add(type);
        }
      });

      return areSetsEqual(current, next) ? current : next;
    });

    knownNodeTypesRef.current = new Set(preparedGraph.nodeTypes);
  }, [preparedGraph.nodeTypeCounts, preparedGraph.nodeTypes]);

  const matchingNodeIds = useMemo(() => {
    if (!normalizedSearchText) {
      return new Set<string>();
    }

    return new Set(
      preparedGraph.graphData.nodes
        .filter((node) => matchesNode(node, normalizedSearchText))
        .map((node) => node.id)
    );
  }, [normalizedSearchText, preparedGraph.graphData.nodes]);
  const visibleNodeIds = useMemo(
    () =>
      new Set(
        preparedGraph.graphData.nodes
          .filter(
            (node) =>
              visibleTypes.has(node.type) &&
              (!normalizedSearchText || matchingNodeIds.has(node.id))
          )
          .map((node) => node.id)
      ),
    [matchingNodeIds, normalizedSearchText, preparedGraph.graphData.nodes, visibleTypes]
  );
  const renderGraphData = useMemo(
    () => ({
      nodes: preparedGraph.graphData.nodes,
      links: showEdges ? preparedGraph.graphData.links : []
    }),
    [preparedGraph.graphData.links, preparedGraph.graphData.nodes, showEdges]
  );
  const selectedNode = selectedNodeId
    ? preparedGraph.nodeById.get(selectedNodeId) ?? null
    : null;
  const interactiveSelectedNode = selectedNode?.isClickable ? selectedNode : null;
  const selectedRelatedNodeIds = useMemo(() => {
    if (!interactiveSelectedNode) {
      return null;
    }

    return new Set<string>([
      interactiveSelectedNode.id,
      ...interactiveSelectedNode.neighborIds
    ]);
  }, [interactiveSelectedNode]);
  const selectedNeighborNodes = useMemo(() => {
    if (!interactiveSelectedNode) {
      return [];
    }

    return [...interactiveSelectedNode.neighborIds]
      .map((nodeId) => preparedGraph.nodeById.get(nodeId))
      .filter((node): node is GraphNodeDatum => Boolean(node))
      .sort((left, right) => {
        if (right.degree !== left.degree) {
          return right.degree - left.degree;
        }

        return left.label.localeCompare(right.label);
      });
  }, [interactiveSelectedNode, preparedGraph.nodeById]);

  useEffect(() => {
    if (selectedNodeId && !visibleNodeIds.has(selectedNodeId)) {
      setSelectedNodeId(null);
    }
  }, [selectedNodeId, visibleNodeIds]);

  useEffect(() => {
    autoFitRef.current = false;
  }, [graphData.edges.length, graphData.nodes.length]);

  useEffect(() => {
    const graph = graphRef.current;
    if (!graph) {
      return;
    }

    graph.d3Force("charge", forceManyBody<GraphNodeDatum>().strength(-150));
    graph.d3Force(
      "collision",
      forceCollide<GraphNodeDatum>(
        (node) => Math.cbrt(Math.max(node.volume, 1)) * NODE_REL_SIZE + 2.5
      )
    );
  }, [preparedGraph.graphData.links, preparedGraph.graphData.nodes]);

  const isLinkVisible = useCallback(
    (link: GraphLinkDatum): boolean =>
      showEdges && visibleNodeIds.has(link.sourceId) && visibleNodeIds.has(link.targetId),
    [showEdges, visibleNodeIds]
  );
  const isSelectedRelatedLink = useCallback(
    (link: GraphLinkDatum): boolean => {
      if (!interactiveSelectedNode || !selectedRelatedNodeIds) {
        return false;
      }

      if (
        link.sourceId === interactiveSelectedNode.id ||
        link.targetId === interactiveSelectedNode.id
      ) {
        return true;
      }

      return (
        selectedRelatedNodeIds.has(link.sourceId) &&
        selectedRelatedNodeIds.has(link.targetId)
      );
    },
    [interactiveSelectedNode, selectedRelatedNodeIds]
  );
  const recenterCamera = useCallback(() => {
    if (!graphRef.current || visibleNodeIds.size === 0) {
      return;
    }

    graphRef.current.zoomToFit(
      1200,
      80,
      (node) => visibleNodeIds.has(String(node.id ?? ""))
    );
  }, [visibleNodeIds]);

  useEffect(() => {
    if (autoFitRef.current || visibleNodeIds.size === 0) {
      return;
    }

    const timeoutId = window.setTimeout(() => {
      if (autoFitRef.current) {
        return;
      }

      recenterCamera();
      autoFitRef.current = true;
    }, 450);

    return () => {
      window.clearTimeout(timeoutId);
    };
  }, [recenterCamera, visibleNodeIds.size]);

  const handleNodeClick = useCallback((node: GraphNodeDatum) => {
    if (!node.isClickable) {
      return;
    }

    setSelectedNodeId(node.id);
    onSelectNode?.(node.id);

    if (!graphRef.current) {
      return;
    }

    const x = node.x ?? 0;
    const y = node.y ?? 0;
    const z = node.z ?? 0;
    const distance = Math.max(70, Math.cbrt(node.volume) * 18);
    const magnitude = Math.hypot(x, y, z) || 1;
    const distanceRatio = 1 + distance / magnitude;

    graphRef.current.cameraPosition(
      {
        x: x * distanceRatio,
        y: y * distanceRatio,
        z: z * distanceRatio
      },
      { x, y, z },
      1500
    );
  }, []);
  const getRenderedNodeColor = useCallback(
    (node: GraphNodeDatum): string => {
      const color = getNodeTypeColor(node.type);
      let alpha = node.scope === "external" ? EXTERNAL_NODE_ALPHA : SELECTED_NODE_ALPHA;

      if (selectedRelatedNodeIds) {
        alpha = selectedRelatedNodeIds.has(node.id)
          ? node.scope === "external"
            ? 0.65
            : 1
          : node.scope === "external"
            ? EXTERNAL_NODE_GHOST_ALPHA
            : NODE_GHOST_ALPHA;
      }

      return toRgba(color, alpha);
    },
    [selectedRelatedNodeIds]
  );
  const getLinkColor = useCallback(
    (link: GraphLinkDatum): string => {
      let alpha = link.scope === "boundary" ? BOUNDARY_EDGE_ALPHA : INTERNAL_EDGE_ALPHA;

      if (selectedRelatedNodeIds) {
        alpha = isSelectedRelatedLink(link)
          ? 1
          : link.scope === "boundary"
            ? BOUNDARY_EDGE_GHOST_ALPHA
            : EDGE_GHOST_ALPHA;
      }

      return toRgba(link.baseColor, alpha);
    },
    [isSelectedRelatedLink, selectedRelatedNodeIds]
  );
  const getLinkWidth = useCallback(
    (link: GraphLinkDatum): number => {
      let width = link.baseWidth;

      if (link.scope === "boundary") {
        width = Math.max(1, width - 0.2);
      }

      if (isSelectedRelatedLink(link)) {
        width = Math.max(width * 1.3, width + 0.8);
      }

      return width;
    },
    [isSelectedRelatedLink]
  );
  const getLinkDirectionalParticles = useCallback(
    (link: GraphLinkDatum): number => {
      if (!showEdges || !isLinkVisible(link) || link.scope === "boundary") {
        return 0;
      }

      const connectedToHub =
        preparedGraph.topHubIds.has(link.sourceId) || preparedGraph.topHubIds.has(link.targetId);
      const connectedToSelection =
        interactiveSelectedNode !== null &&
        (link.sourceId === interactiveSelectedNode.id || link.targetId === interactiveSelectedNode.id);

      return connectedToHub || connectedToSelection ? 2 : 0;
    },
    [interactiveSelectedNode, isLinkVisible, preparedGraph.topHubIds, showEdges]
  );
  const getNodeLabel = useCallback(
    (node: GraphNodeDatum): string =>
      `${node.label} | ${node.type} | degree ${node.degree}${
        node.scope === "external" ? " | ghost" : ""
      }${node.isTopHub ? " | top hub" : ""}`,
    []
  );
  const getLinkLabel = useCallback(
    (link: GraphLinkDatum): string => {
      const sourceLabel = preparedGraph.nodeById.get(link.sourceId)?.label ?? link.sourceId;
      const targetLabel = preparedGraph.nodeById.get(link.targetId)?.label ?? link.targetId;
      const boundarySuffix = link.scope === "boundary" ? " | boundary" : "";

      return `${sourceLabel} -> ${targetLabel} (${link.type})${boundarySuffix}`;
    },
    [preparedGraph.nodeById]
  );
  const hasVisibleTypes =
    preparedGraph.nodeTypes.length > 0 && visibleTypes.size === preparedGraph.nodeTypes.length;

  return (
    <Box
      sx={{
        height: "100vh",
        width: "100%",
        "& canvas": {
          background: "transparent !important"
        }
      }}
    >
      {graphData.nodes.length > 0 && (
        <>
          <ForceGraph3D
            ref={graphRef}
            width={viewportSize.width}
            height={viewportSize.height}
            backgroundColor="rgba(0,0,0,0)"
            graphData={renderGraphData}
            nodeId="id"
            nodeVal="volume"
            nodeRelSize={NODE_REL_SIZE}
            nodeLabel={getNodeLabel}
            nodeVisibility={(node) => visibleNodeIds.has(node.id)}
            nodeColor={getRenderedNodeColor}
            linkLabel={getLinkLabel}
            linkVisibility={isLinkVisible}
            linkColor={getLinkColor}
            linkWidth={getLinkWidth}
            linkDirectionalParticles={getLinkDirectionalParticles}
            linkDirectionalParticleWidth={3.5}
            linkDirectionalParticleSpeed={0.0035}
            linkDirectionalParticleColor={getLinkColor}
            numDimensions={3}
            showNavInfo={false}
            enablePointerInteraction
            onNodeClick={handleNodeClick}
            onBackgroundClick={() => {
              setSelectedNodeId(null);
            }}
            showPointerCursor
          />

          <Paper
            elevation={8}
            sx={{
              position: "absolute",
              top: 24,
              right: 24,
              zIndex: 10,
              width: { xs: "calc(100% - 48px)", md: 320 },
              p: 2,
              border: "1px solid rgba(148, 163, 184, 0.16)",
              backgroundColor: "rgba(2, 6, 23, 0.7)",
              backdropFilter: "blur(18px)"
            }}
          >
            <Stack spacing={1.5}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography variant="subtitle1">Filter Legend</Typography>
                <Button
                  size="small"
                  disabled={hasVisibleTypes}
                  onClick={() => {
                    setVisibleTypes(new Set(preparedGraph.nodeTypes));
                  }}
                >
                  Show all
                </Button>
              </Stack>

              <Typography variant="caption" color="text.secondary">
                Boundary dependencies stay visible as low-opacity ghost nodes.
              </Typography>

              <FormControlLabel
                control={
                  <Checkbox
                    data-testid="toggle-edges-checkbox"
                    checked={showEdges}
                    onChange={(event) => {
                      onToggleShowEdges(event.target.checked);
                    }}
                  />
                }
                label={
                  <Stack
                    direction="row"
                    spacing={1}
                    alignItems="center"
                    justifyContent="space-between"
                    sx={{ width: "100%" }}
                  >
                    <Typography variant="body2">Show edges</Typography>
                    <Chip
                      size="small"
                      variant={showEdges ? "filled" : "outlined"}
                      label={graphData.edges.length}
                    />
                  </Stack>
                }
              />

              {!showEdges && (
                <Typography variant="caption" color="text.secondary">
                  Edges load on demand so the node view stays responsive in large workspaces.
                </Typography>
              )}

              <Divider />

              <FormGroup>
                {preparedGraph.nodeTypes.map((type) => {
                  const color = getNodeTypeColor(type);
                  const count = preparedGraph.nodeTypeCounts.get(type) ?? 0;
                  const checked = visibleTypes.has(type);

                  return (
                    <FormControlLabel
                      key={type}
                      control={
                        <Checkbox
                          checked={checked}
                          onChange={() => {
                            setVisibleTypes((current) => {
                              const next = new Set(current ?? preparedGraph.nodeTypes);

                              if (next.has(type)) {
                                next.delete(type);
                              } else {
                                next.add(type);
                              }

                              return next;
                            });
                          }}
                          sx={{
                            color,
                            "&.Mui-checked": {
                              color
                            }
                          }}
                        />
                      }
                      label={
                        <Stack
                          direction="row"
                          spacing={1}
                          justifyContent="space-between"
                          alignItems="center"
                          sx={{ width: "100%" }}
                        >
                          <Typography variant="body2" sx={{ textTransform: "capitalize" }}>
                            {type}
                          </Typography>
                          <Chip size="small" variant="outlined" label={count} />
                        </Stack>
                      }
                    />
                  );
                })}
              </FormGroup>
            </Stack>
          </Paper>

          <Tooltip title="Recenter camera">
            <IconButton
              aria-label="Recenter camera"
              onClick={recenterCamera}
              sx={{
                position: "absolute",
                top: 24,
                right: { xs: 24, md: 368 },
                zIndex: 10,
                border: "1px solid rgba(148, 163, 184, 0.16)",
                backgroundColor: "rgba(2, 6, 23, 0.72)",
                backdropFilter: "blur(18px)",
                color: "#ffffff",
                "&:hover": {
                  backgroundColor: "rgba(15, 23, 42, 0.92)"
                }
              }}
            >
              <CenterFocusStrongIcon />
            </IconButton>
          </Tooltip>

          {interactiveSelectedNode && (
            <Paper
              elevation={8}
              sx={{
                position: "absolute",
                left: { xs: 24, md: 456 },
                bottom: 24,
                zIndex: 10,
                width: { xs: "calc(100% - 48px)", md: 380 },
                p: 2,
                border: "1px solid rgba(148, 163, 184, 0.16)",
                backgroundColor: "rgba(2, 6, 23, 0.72)",
                backdropFilter: "blur(18px)",
                boxShadow: "0 24px 80px rgba(2, 6, 23, 0.45)"
              }}
            >
              <Stack spacing={1.5}>
                <Typography variant="subtitle1">Selected Node</Typography>
                <Typography variant="h6" sx={{ wordBreak: "break-word" }}>
                  {interactiveSelectedNode.label}
                </Typography>
                <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
                  <Chip
                    size="small"
                    sx={{
                      borderColor: getNodeTypeColor(interactiveSelectedNode.type),
                      color: getNodeTypeColor(interactiveSelectedNode.type),
                      textTransform: "capitalize"
                    }}
                    variant="outlined"
                    label={interactiveSelectedNode.type}
                  />
                  <Chip size="small" label={`Degree ${interactiveSelectedNode.degree}`} />
                  <Chip size="small" label={`In ${interactiveSelectedNode.inDegree}`} />
                  <Chip size="small" label={`Out ${interactiveSelectedNode.outDegree}`} />
                  {interactiveSelectedNode.isTopHub && (
                    <Chip size="small" color="primary" label="Top hub" />
                  )}
                </Stack>

                <Typography variant="caption" color="text.secondary">
                  {interactiveSelectedNode.id}
                </Typography>

                <Divider />

                <Typography variant="body2" color="text.secondary">
                  Connected nodes
                </Typography>
                <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
                  {selectedNeighborNodes.slice(0, 8).map((node) => (
                    <Chip
                      key={node.id}
                      size="small"
                      variant="outlined"
                      disabled={!node.isClickable}
                      label={node.label}
                      onClick={() => {
                        handleNodeClick(node);
                      }}
                    />
                  ))}
                  {selectedNeighborNodes.length === 0 && (
                    <Typography variant="caption" color="text.secondary">
                      No neighboring nodes.
                    </Typography>
                  )}
                  {selectedNeighborNodes.length > 8 && (
                    <Chip
                      size="small"
                      variant="outlined"
                      label={`+${selectedNeighborNodes.length - 8} more`}
                    />
                  )}
                </Stack>
              </Stack>
            </Paper>
          )}
        </>
      )}

      <CenteredState>
        {!hasSelection ? (
          <StateCard
            dataTestId="graph-empty-state"
            title="Nothing selected"
            description="Tick a folder or file in the Solution Explorer to load a scoped dependency graph."
          />
        ) : errorMessage ? (
          <Alert severity="error">{errorMessage}</Alert>
        ) : isLoading ? (
          <StateCard
            dataTestId="graph-loading-state"
            title="Loading selected scope"
            description="Fetching analyzed nodes and edges..."
          />
        ) : graphData.nodes.length === 0 ? (
          <StateCard
            dataTestId="graph-no-results-state"
            title="No nodes in scope"
            description="The selected paths do not currently resolve to analyzed graph data."
          />
        ) : null}
      </CenteredState>
    </Box>
  );
}

function CenteredState({ children }: { children: ReactNode }) {
  if (!children) {
    return null;
  }

  return (
    <Box
      sx={{
        position: "absolute",
        inset: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        pointerEvents: "none"
      }}
    >
      {children}
    </Box>
  );
}

function StateCard({
  dataTestId,
  description,
  title
}: {
  dataTestId?: string;
  description: string;
  title: string;
}) {
  return (
    <Paper
      data-testid={dataTestId}
      elevation={8}
      sx={{
        maxWidth: 420,
        px: 3,
        py: 2.5,
        textAlign: "center",
        border: "1px solid rgba(148, 163, 184, 0.16)",
        backgroundColor: "rgba(2, 6, 23, 0.76)",
        backdropFilter: "blur(18px)"
      }}
    >
      <Stack spacing={1}>
        <Typography variant="h6">{title}</Typography>
        <Typography variant="body2" color="text.secondary">
          {description}
        </Typography>
      </Stack>
    </Paper>
  );
}

function useViewportSize(): ViewportSize {
  const [viewportSize, setViewportSize] = useState<ViewportSize>(() => getViewportSize());

  useEffect(() => {
    const handleResize = () => {
      setViewportSize(getViewportSize());
    };

    window.addEventListener("resize", handleResize);

    return () => {
      window.removeEventListener("resize", handleResize);
    };
  }, []);

  return viewportSize;
}

function buildPreparedGraph(graphData: GraphApiResponse): PreparedGraph {
  const nodeById = new Map<string, GraphNodeDatum>();
  const nodeTypeCounts = new Map<string, number>();

  graphData.nodes.forEach((node) => {
    const normalizedType = normalizeText(node.type);

    nodeById.set(node.id, {
      ...node,
      type: normalizedType,
      degree: 0,
      inDegree: 0,
      outDegree: 0,
      neighborIds: new Set<string>(),
      volume: getNodeVolume(normalizedType, 0, node.scope),
      isTopHub: false
    });
    nodeTypeCounts.set(normalizedType, (nodeTypeCounts.get(normalizedType) ?? 0) + 1);
  });

  const links: GraphLinkDatum[] = [];

  graphData.edges.forEach((edge) => {
    const sourceNode = nodeById.get(edge.source);
    const targetNode = nodeById.get(edge.target);

    if (!sourceNode || !targetNode) {
      return;
    }

    sourceNode.outDegree += 1;
    targetNode.inDegree += 1;
    sourceNode.neighborIds.add(targetNode.id);
    targetNode.neighborIds.add(sourceNode.id);

    links.push({
      ...edge,
      source: edge.source,
      target: edge.target,
      sourceId: edge.source,
      targetId: edge.target,
      baseColor: getEdgeColor(edge.type),
      baseWidth: getEdgeWidth(edge.type)
    });
  });

  const nodes = [...nodeById.values()];

  nodes.forEach((node) => {
    node.degree = node.inDegree + node.outDegree;
    node.volume = getNodeVolume(node.type, node.degree, node.scope);
  });

  const topHubIds = new Set(
    [...nodes]
      .filter((node) => node.scope === "selected")
      .sort((left, right) => {
        if (right.degree !== left.degree) {
          return right.degree - left.degree;
        }

        return left.label.localeCompare(right.label);
      })
      .slice(0, 10)
      .map((node) => node.id)
  );

  nodes.forEach((node) => {
    node.isTopHub = topHubIds.has(node.id);
  });

  return {
    graphData: {
      nodes,
      links
    },
    nodeById,
    nodeTypeCounts,
    nodeTypes: sortNodeTypes([...nodeTypeCounts.keys()]),
    topHubIds
  };
}

function matchesNode(node: GraphNodeDatum, normalizedSearchText: string): boolean {
  return (
    normalizeText(node.label).includes(normalizedSearchText) ||
    normalizeText(node.id).includes(normalizedSearchText) ||
    normalizeText(node.type).includes(normalizedSearchText)
  );
}

function getViewportSize(): ViewportSize {
  if (typeof window === "undefined") {
    return { width: 1280, height: 720 };
  }

  return {
    width: window.innerWidth,
    height: window.innerHeight
  };
}

function getNodeTypeColor(type: string): string {
  switch (normalizeText(type)) {
    case "project":
      return "#06b6d4";
    case "interface":
      return "#a855f7";
    case "class":
      return "#60a5fa";
    case "method":
      return "#22c55e";
    case "property":
      return "#fb7185";
    case "field":
      return "#f97316";
    default:
      return DEFAULT_NODE_COLOR;
  }
}

function getNodeVolume(type: string, degree: number, scope: GraphApiNode["scope"]): number {
  const normalizedType = normalizeText(type);

  let baseVolume: number;
  switch (normalizedType) {
    case "project":
      baseVolume = 210;
      break;
    case "interface":
      baseVolume = 82;
      break;
    case "class":
      baseVolume = 72;
      break;
    case "method":
      baseVolume = 26;
      break;
    case "property":
      baseVolume = 18;
      break;
    case "field":
      baseVolume = 15;
      break;
    default:
      baseVolume = 36;
      break;
  }

  const volume = baseVolume + Math.min(180, degree * 9);

  return scope === "external" ? volume * 0.65 : volume;
}

function getEdgeColor(type: string): string {
  switch (normalizeText(type)) {
    case "projectreference":
      return "#7dd3fc";
    case "methodcall":
      return "#93c5fd";
    case "instantiates":
      return "#fbbf24";
    case "implements":
      return "#d8b4fe";
    case "fieldaccess":
      return "#6ee7b7";
    case "serviceregistration":
      return "#f9a8d4";
    default:
      return DEFAULT_EDGE_COLOR;
  }
}

function getEdgeWidth(type: string): number {
  switch (normalizeText(type)) {
    case "projectreference":
      return 2.5;
    case "serviceregistration":
      return 2.2;
    case "instantiates":
    case "implements":
      return 2.0;
    case "fieldaccess":
      return 1.8;
    default:
      return 1.5;
  }
}

function sortNodeTypes(nodeTypes: string[]): string[] {
  return [...nodeTypes].sort((left, right) => {
    const leftIndex = NODE_TYPE_ORDER.indexOf(left);
    const rightIndex = NODE_TYPE_ORDER.indexOf(right);

    if (leftIndex >= 0 && rightIndex >= 0) {
      return leftIndex - rightIndex;
    }

    if (leftIndex >= 0) {
      return -1;
    }

    if (rightIndex >= 0) {
      return 1;
    }

    return left.localeCompare(right);
  });
}

function normalizeText(value: string): string {
  return value.trim().toLowerCase();
}

function shouldTypeStartVisible(type: string): boolean {
  return MACRO_ARCHITECTURE_NODE_TYPES.has(type);
}

function getDefaultVisibleTypes(nodeTypes: string[]): Set<string> {
  const defaultVisibleTypes = nodeTypes.filter((type) => shouldTypeStartVisible(type));

  return new Set(defaultVisibleTypes.length > 0 ? defaultVisibleTypes : nodeTypes);
}

function areSetsEqual(left: Set<string>, right: Set<string>): boolean {
  if (left.size !== right.size) {
    return false;
  }

  for (const value of left) {
    if (!right.has(value)) {
      return false;
    }
  }

  return true;
}

function toRgba(color: string, alpha: number): string {
  const normalizedAlpha = Math.max(0, Math.min(1, alpha));
  const normalizedColor = color.replace("#", "");

  if (normalizedColor.length !== 6) {
    return color;
  }

  const red = Number.parseInt(normalizedColor.slice(0, 2), 16);
  const green = Number.parseInt(normalizedColor.slice(2, 4), 16);
  const blue = Number.parseInt(normalizedColor.slice(4, 6), 16);

  return `rgba(${red}, ${green}, ${blue}, ${normalizedAlpha})`;
}
