import type { LayoutRequest } from "./layoutProtocol";

type PendingLayout = {
  revision: number;
  create: (revision: number) => LayoutRequest;
};

/** Coalesce layout work while the renderer continues displaying every loaded page. */
export class LayoutQueue {
  private inFlight?: number;
  private pending?: PendingLayout;
  private disposed = false;

  constructor(private readonly send: (request: LayoutRequest) => void) {}

  enqueue(revision: number, create: PendingLayout["create"]) {
    if (this.disposed) return;
    const request = { revision, create };
    if (this.inFlight !== undefined) this.pending = request;
    else this.dispatch(request);
  }

  acknowledge(revision: number) {
    if (this.disposed || this.inFlight !== revision) return;
    this.inFlight = undefined;
    const next = this.pending;
    this.pending = undefined;
    if (next) this.dispatch(next);
  }

  private dispatch(request: PendingLayout) {
    this.inFlight = request.revision;
    try {
      this.send(request.create(request.revision));
    } catch (error) {
      this.inFlight = undefined;
      throw error;
    }
  }

  dispose() {
    this.disposed = true;
    this.pending = undefined;
    this.inFlight = undefined;
  }
}
