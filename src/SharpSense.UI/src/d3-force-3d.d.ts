declare module "d3-force-3d" {
  export interface Force<NodeDatum extends object> {
    (alpha: number): void;
    initialize?: (nodes: NodeDatum[], ...args: unknown[]) => void;
  }

  export interface ForceCollide<NodeDatum extends object> extends Force<NodeDatum> {
    strength(strength: number): ForceCollide<NodeDatum>;
    iterations(iterations: number): ForceCollide<NodeDatum>;
  }

  export interface ForceManyBody<NodeDatum extends object> extends Force<NodeDatum> {
    strength(
      strength:
        | number
        | ((node: NodeDatum) => number)
    ): ForceManyBody<NodeDatum>;
    theta(theta: number): ForceManyBody<NodeDatum>;
    distanceMin(distance: number): ForceManyBody<NodeDatum>;
    distanceMax(distance: number): ForceManyBody<NodeDatum>;
  }

  export function forceCollide<NodeDatum extends object>(
    radius?: number | ((node: NodeDatum) => number)
  ): ForceCollide<NodeDatum>;

  export function forceManyBody<NodeDatum extends object>(): ForceManyBody<NodeDatum>;
}
