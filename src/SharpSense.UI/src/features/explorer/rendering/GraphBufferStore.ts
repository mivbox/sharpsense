import type { GraphData } from "../../../shared/api/models";
import {
  prepareGraphBuffers,
  writeGraphLinks,
  writeInitialNodePosition,
  type GraphBuffers,
} from "./graphBuffers";

/** Mutable renderer-owned storage. Public graph snapshots and query pages remain read-only. */
export class GraphBufferStore {
  private stream?: object;
  private edgeCount = 0;
  private positionStorage = new Float32Array(0);
  private linkStorage = new Uint32Array(0);
  buffers: GraphBuffers = prepareGraphBuffers({ nodes: [], edges: [] });

  get positions() {
    return this.positionStorage;
  }

  update(data: GraphData, stream?: object) {
    const previous = this.buffers;
    const append =
      stream !== undefined &&
      stream === this.stream &&
      data.nodes.length >= previous.nodes.length &&
      data.edges.length >= this.edgeCount;
    const nodeStart = append ? previous.nodes.length : 0;
    const linkStart = append ? previous.links.length : 0;
    const nodesChanged = data.nodes !== previous.nodes;
    let positionsChanged: boolean;

    if (!append) {
      const next = prepareGraphBuffers(data, previous);
      positionsChanged = this.ensurePositions(data.nodes.length);
      this.positionStorage.set(next.positions);
      this.ensureLinks(next.links.length);
      this.linkStorage.set(next.links);
      this.buffers = {
        nodes: data.nodes,
        indices: next.indices,
        positions: this.positionStorage.subarray(0, data.nodes.length * 3),
        links: this.linkStorage.subarray(0, next.links.length),
      };
    } else {
      positionsChanged = this.ensurePositions(data.nodes.length);
      // Initialize only appended IDs; existing positions retain worker updates.
      for (let index = nodeStart; index < data.nodes.length; index++) {
        previous.indices.set(data.nodes[index].id, index);
        writeInitialNodePosition(
          data.nodes[index],
          this.positionStorage,
          index * 3,
        );
      }
      this.ensureLinks(linkStart + (data.edges.length - this.edgeCount) * 2);
      const linkCount = writeGraphLinks(
        this.linkStorage,
        data.edges,
        previous.indices,
        this.edgeCount,
        linkStart,
      );
      this.buffers = {
        nodes: data.nodes,
        indices: previous.indices,
        positions: this.positionStorage.subarray(0, data.nodes.length * 3),
        links: this.linkStorage.subarray(0, linkCount),
      };
    }

    this.stream = stream;
    this.edgeCount = data.edges.length;
    return { append, nodeStart, linkStart, nodesChanged, positionsChanged };
  }

  applyLayout(positions: Float32Array) {
    if (positions.length !== this.buffers.positions.length) return false;
    this.buffers.positions.set(positions);
    return true;
  }

  private ensurePositions(count: number) {
    const required = count * 3;
    if (required <= this.positionStorage.length) return false;
    const storage = new Float32Array(
      Math.max(2048 * 3, required, this.positionStorage.length * 2),
    );
    storage.set(this.buffers.positions);
    this.positionStorage = storage;
    return true;
  }

  private ensureLinks(count: number) {
    if (count <= this.linkStorage.length) return;
    const storage = new Uint32Array(
      Math.max(2048, count, this.linkStorage.length * 2),
    );
    storage.set(this.buffers.links);
    this.linkStorage = storage;
  }
}
