import {
  forceCenter,
  forceLink,
  forceManyBody,
  forceSimulation,
} from "d3-force-3d";
import type { LayoutRequest } from "./layoutProtocol";

export type LayoutNode = {
  id: string;
  x: number;
  y: number;
  z: number;
  vx?: number;
  vy?: number;
  vz?: number;
};

export function createGraphLayout(
  request: LayoutRequest,
  previous: LayoutNode[] = [],
) {
  const existing = new Map(previous.map((node) => [node.id, node]));
  const nodes = request.ids.map(
    (id, index) =>
      existing.get(id) ?? {
        id,
        x: request.positions[index * 3],
        y: request.positions[index * 3 + 1],
        z: request.positions[index * 3 + 2],
      },
  );
  const links = [];
  for (let index = 0; index < request.links.length; index += 2) {
    const source = nodes[request.links[index]];
    const target = nodes[request.links[index + 1]];
    if (source && target) links.push({ source, target });
  }
  const simulation = forceSimulation(nodes, 3)
    .stop()
    .alphaDecay(0.075)
    .velocityDecay(0.45)
    .force("charge", forceManyBody<LayoutNode>().strength(-45).theta(1.1))
    .force("center", forceCenter<LayoutNode>(0, 0, 0))
    .force("links", forceLink(links).distance(45).strength(0.08));
  return { nodes, simulation };
}

export function layoutPositions(nodes: LayoutNode[]) {
  const positions = new Float32Array(nodes.length * 3);
  nodes.forEach((node, index) => {
    positions[index * 3] = node.x;
    positions[index * 3 + 1] = node.y;
    positions[index * 3 + 2] = node.z;
  });
  return positions;
}
