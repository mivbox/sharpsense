export type LayoutRequest = {
  revision: number;
  ids: string[];
  positions: Float32Array;
  links: Uint32Array;
};

export type LayoutResponse =
  | { revision: number; positions: Float32Array; settled: boolean }
  | { revision: number; error: string };
