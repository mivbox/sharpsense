import {
  createGraphLayout,
  layoutPositions,
  type LayoutNode,
} from "./graphLayout";
import type { LayoutRequest, LayoutResponse } from "./layoutProtocol";

const context = self as unknown as {
  onmessage: ((event: MessageEvent<LayoutRequest>) => void) | null;
  postMessage: (message: LayoutResponse, transfer?: Transferable[]) => void;
};
let previous: LayoutNode[] = [];
let revision = 0;
let timer: ReturnType<typeof setTimeout> | undefined;

context.onmessage = ({ data }) => {
  revision = data.revision;
  clearTimeout(timer);
  try {
    const { nodes, simulation } = createGraphLayout(data, previous);
    previous = nodes;
    let ticks = 0;
    let lastSent = 0;
    const started = performance.now();
    const step = () => {
      if (revision !== data.revision) return;
      try {
        simulation.tick();
        const now = performance.now();
        // Limit background layout work, never the loaded or displayed graph.
        const settled =
          ++ticks >= 60 || simulation.alpha() < 0.015 || now - started >= 8000;
        if (settled || now - lastSent >= 150) {
          const positions = layoutPositions(nodes);
          context.postMessage({ revision, positions, settled }, [
            positions.buffer,
          ]);
          lastSent = now;
        }
        // Yield between ticks so new pages and cancellation can replace this layout.
        if (!settled) timer = setTimeout(step, 0);
      } catch (error) {
        context.postMessage({
          revision,
          error:
            error instanceof Error ? error.message : "Graph layout failed.",
        });
      }
    };
    timer = setTimeout(step, 0);
  } catch (error) {
    context.postMessage({
      revision,
      error: error instanceof Error ? error.message : "Graph layout failed.",
    });
  }
};
