import type {
  GraphData,
  GraphEdge,
  GraphNode,
} from "../../../shared/api/models";

export type GraphBuffers = {
  nodes: readonly GraphNode[];
  indices: Map<string, number>;
  positions: Float32Array;
  links: Uint32Array;
};

function hash(value: string) {
  let result = 2166136261;
  for (let index = 0; index < value.length; index++) {
    result = Math.imul(result ^ value.charCodeAt(index), 16777619);
  }
  return result >>> 0;
}

function coordinate(seed: number, axis: number) {
  return (
    ((Math.imul(seed ^ (axis * 374761393), 668265263) >>> 0) / 4294967296 -
      0.5) *
    2
  );
}

export function writeInitialNodePosition(
  node: GraphNode,
  positions: Float32Array,
  offset: number,
) {
  const cluster = hash(node.projectId ?? "workspace");
  const seed = hash(node.id);
  for (let axis = 0; axis < 3; axis++) {
    positions[offset + axis] =
      coordinate(cluster, axis + 1) * 450 + coordinate(seed, axis + 4) * 100;
  }
}

export function prepareGraphBuffers(
  data: GraphData,
  previous?: GraphBuffers,
): GraphBuffers {
  const indices = new Map(data.nodes.map((node, index) => [node.id, index]));
  const positions = new Float32Array(data.nodes.length * 3);
  data.nodes.forEach((node, index) => {
    const previousIndex = previous?.indices.get(node.id);
    if (previous && previousIndex !== undefined) {
      positions.set(
        previous.positions.subarray(previousIndex * 3, previousIndex * 3 + 3),
        index * 3,
      );
      return;
    }
    writeInitialNodePosition(node, positions, index * 3);
  });
  return {
    nodes: data.nodes,
    indices,
    positions,
    links: prepareGraphLinks(data.edges, indices),
  };
}

export function prepareGraphLinks(
  edges: readonly GraphEdge[],
  indices: Map<string, number>,
) {
  const links = new Uint32Array(edges.length * 2);
  const count = writeGraphLinks(links, edges, indices);
  return links.subarray(0, count);
}

export function writeGraphLinks(
  links: Uint32Array,
  edges: readonly GraphEdge[],
  indices: ReadonlyMap<string, number>,
  edgeStart = 0,
  offset = 0,
) {
  for (let index = edgeStart; index < edges.length; index++) {
    const edge = edges[index];
    const source = indices.get(edge.source);
    const target = indices.get(edge.target);
    if (source === undefined || target === undefined) continue;
    links[offset++] = source;
    links[offset++] = target;
  }
  return offset;
}

export function writeEdgePositions(
  target: Float32Array,
  positions: Float32Array,
  links: Uint32Array,
  start = 0,
) {
  for (let index = start; index < links.length; index++) {
    const source = links[index] * 3;
    const destination = index * 3;
    target[destination] = positions[source];
    target[destination + 1] = positions[source + 1];
    target[destination + 2] = positions[source + 2];
  }
}

type PointSizing = {
  sizes: ArrayLike<number>;
  scale: number;
  minimumSize: number;
  maximumSize: number;
  pixelRatio: number;
};

/** Pick visible sprites in CSS pixels, retaining a minimum target for small nodes. */
export function pickGraphNode(
  positions: Float32Array,
  matrix: readonly number[],
  width: number,
  height: number,
  pointerX: number,
  pointerY: number,
  radius: number,
  sizing?: PointSizing,
): number | null {
  let selected: number | null = null;
  let closest = Infinity;
  let nearestDepth = Infinity;
  for (let index = 0; index < positions.length; index += 3) {
    const x = positions[index],
      y = positions[index + 1],
      z = positions[index + 2];
    const w = matrix[3] * x + matrix[7] * y + matrix[11] * z + matrix[15];
    if (w <= 0) continue;
    const depth =
      (matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14]) / w;
    if (depth < -1 || depth > 1) continue;
    // Perspective clip W equals -viewPosition.z. Match the point shader's
    // clamped device-pixel diameter before converting to a CSS-pixel radius.
    const hitRadius = sizing
      ? Math.max(
          radius,
          Math.min(
            sizing.maximumSize,
            Math.max(
              sizing.minimumSize,
              (sizing.sizes[index / 3] * sizing.scale) / Math.max(1, w),
            ),
          ) /
            (2 * sizing.pixelRatio),
        )
      : radius;
    const projectedX =
      (matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12]) / w;
    const dx = ((projectedX + 1) * width) / 2 - pointerX;
    if (Math.abs(dx) > hitRadius) continue;
    const projectedY =
      (matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13]) / w;
    const dy = ((1 - projectedY) * height) / 2 - pointerY;
    const distance = dx * dx + dy * dy;
    if (distance > hitRadius * hitRadius) continue;
    if (distance < closest || (distance === closest && depth < nearestDepth)) {
      closest = distance;
      nearestDepth = depth;
      selected = index / 3;
    }
  }
  return selected;
}
