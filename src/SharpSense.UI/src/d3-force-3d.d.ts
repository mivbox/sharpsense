declare module "d3-force-3d" {
  export interface Simulation<NodeDatum extends object> {
    stop(): Simulation<NodeDatum>;
    tick(iterations?: number): Simulation<NodeDatum>;
    alpha(): number;
    alphaDecay(value: number): Simulation<NodeDatum>;
    velocityDecay(value: number): Simulation<NodeDatum>;
    force(name: string, force: Force<NodeDatum>): Simulation<NodeDatum>;
  }

  export interface ForceLink<
    NodeDatum extends object,
  > extends Force<NodeDatum> {
    distance(value: number): ForceLink<NodeDatum>;
    strength(value: number): ForceLink<NodeDatum>;
  }

  export function forceSimulation<NodeDatum extends object>(
    nodes: NodeDatum[],
    dimensions?: number,
  ): Simulation<NodeDatum>;
  export function forceCenter<NodeDatum extends object>(
    x?: number,
    y?: number,
    z?: number,
  ): Force<NodeDatum>;
  export function forceLink<NodeDatum extends object>(
    links: { source: NodeDatum; target: NodeDatum }[],
  ): ForceLink<NodeDatum>;
  export interface Force<NodeDatum extends object> {
    (alpha: number): void;
    initialize?: (nodes: NodeDatum[], ...args: unknown[]) => void;
  }

  export interface ForceCollide<
    NodeDatum extends object,
  > extends Force<NodeDatum> {
    strength(strength: number): ForceCollide<NodeDatum>;
    iterations(iterations: number): ForceCollide<NodeDatum>;
  }

  export interface ForceManyBody<
    NodeDatum extends object,
  > extends Force<NodeDatum> {
    strength(
      strength: number | ((node: NodeDatum) => number),
    ): ForceManyBody<NodeDatum>;
    theta(theta: number): ForceManyBody<NodeDatum>;
    distanceMin(distance: number): ForceManyBody<NodeDatum>;
    distanceMax(distance: number): ForceManyBody<NodeDatum>;
  }

  export function forceCollide<NodeDatum extends object>(
    radius?: number | ((node: NodeDatum) => number),
  ): ForceCollide<NodeDatum>;

  export function forceManyBody<
    NodeDatum extends object,
  >(): ForceManyBody<NodeDatum>;
}
