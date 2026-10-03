import {
  Box3,
  BufferAttribute,
  BufferGeometry,
  Color,
  DynamicDrawUsage,
  LineBasicMaterial,
  LineSegments,
  Matrix4,
  PerspectiveCamera,
  Points,
  Scene,
  ShaderMaterial,
  Vector3,
  WebGLRenderer,
} from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import type { GraphData, GraphNode } from "../../../shared/api/models";
import { graphPalettes, type GraphPalette } from "../graphPalette";
import { pickGraphNode, writeEdgePositions } from "./graphBuffers";
import { GraphBufferStore } from "./GraphBufferStore";
import type { LayoutRequest, LayoutResponse } from "./layoutProtocol";
import { LayoutQueue } from "./layoutQueue";

export type GraphHover = { node: GraphNode; x: number; y: number } | null;
export type LayoutStatus = { busy: boolean; error?: string };
type Callbacks = {
  onSelect: (node: GraphNode | null) => void;
  onHover: (hover: GraphHover) => void;
  onStatus: (status: LayoutStatus) => void;
};

/** Two draw calls for any graph size; layout and picking stay independent. */
export class GraphScene {
  private palette = graphPalettes.dark;
  private readonly renderer: WebGLRenderer;
  private readonly scene = new Scene();
  private readonly camera = new PerspectiveCamera(50, 1, 0.1, 10000000);
  private readonly controls: OrbitControls;
  private readonly observer: ResizeObserver;
  private readonly worker: Worker;
  private readonly layoutQueue = new LayoutQueue((request) => {
    this.worker.postMessage(request, [
      request.positions.buffer,
      request.links.buffer,
    ]);
  });
  private workerFailed = false;
  private readonly pointMaterial = new ShaderMaterial({
    transparent: true,
    depthWrite: false,
    uniforms: {
      scale: { value: 1 },
      minimumSize: { value: 10 },
      maximumSize: { value: 28 },
    },
    vertexShader: `
      attribute vec3 color;
      attribute float size;
      attribute float opacity;
      uniform float scale;
      uniform float minimumSize;
      uniform float maximumSize;
      varying vec3 pointColor;
      varying float pointOpacity;
      void main() {
        vec4 viewPosition = modelViewMatrix * vec4(position, 1.0);
        gl_Position = projectionMatrix * viewPosition;
        gl_PointSize = clamp(size * scale / max(1.0, -viewPosition.z), minimumSize, maximumSize);
        pointColor = color;
        pointOpacity = opacity;
      }
    `,
    fragmentShader: `
      varying vec3 pointColor;
      varying float pointOpacity;
      void main() {
        float radius = length(gl_PointCoord - vec2(0.5)) * 2.0;
        if (radius > 1.0) discard;
        float shade = 1.0 - radius * 0.2;
        gl_FragColor = vec4(pointColor * shade, pointOpacity * (1.0 - smoothstep(0.85, 1.0, radius)));
        #include <colorspace_fragment>
      }
    `,
  });
  private readonly edgeMaterial = new LineBasicMaterial({
    vertexColors: true,
    transparent: true,
    opacity: 0.5,
    depthWrite: false,
  });
  private readonly points = new Points(
    new BufferGeometry(),
    this.pointMaterial,
  );
  private readonly lines = new LineSegments(
    new BufferGeometry(),
    this.edgeMaterial,
  );
  private readonly projection = new Matrix4();
  private readonly colorCache = new Map<string, Color>();
  private readonly bufferStore = new GraphBufferStore();
  private get buffers() {
    return this.bufferStore.buffers;
  }
  private selectedId?: string;
  private edgeCapacity = 0;
  private search = "";
  private revision = 0;
  private pending?: Extract<LayoutResponse, { positions: Float32Array }>;
  private frame = 0;
  private dirty = true;
  private disposed = false;
  private autoFit = true;
  private width = 1;
  private height = 1;
  private pointerDown?: { x: number; y: number };
  private hoverPointer?: { x: number; y: number };
  private lastHover = 0;

  constructor(
    private readonly container: HTMLElement,
    private readonly callbacks: Callbacks,
  ) {
    const cleanup: (() => void)[] = [];
    try {
      this.renderer = new WebGLRenderer({ antialias: true });
      cleanup.push(() => {
        this.renderer.dispose();
        this.renderer.forceContextLoss();
      });
      this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
      this.renderer.setClearColor(this.palette.background);
      this.renderer.domElement.setAttribute(
        "aria-label",
        "Interactive dependency graph",
      );
      container.appendChild(this.renderer.domElement);
      cleanup.push(() => this.renderer.domElement.remove());
      this.camera.position.set(0, 0, 1200);
      this.controls = new OrbitControls(this.camera, this.renderer.domElement);
      cleanup.push(() => this.controls.dispose());
      this.controls.enableDamping = true;
      this.controls.addEventListener("change", this.markDirty);
      this.controls.addEventListener("start", this.stopAutoFit);
      this.points.frustumCulled = false;
      this.lines.frustumCulled = false;
      this.scene.add(this.lines, this.points);
      this.worker = new Worker(
        new URL("./graphLayout.worker.ts", import.meta.url),
        { type: "module" },
      );
      cleanup.push(() => this.worker.terminate());
      this.worker.onmessage = ({ data }: MessageEvent<LayoutResponse>) => {
        if (this.disposed) return;
        // A first snapshot, empty result, or failure acknowledges worker capacity.
        this.layoutQueue.acknowledge(data.revision);
        if (data.revision !== this.revision) return;
        if ("error" in data) {
          this.layoutFailed();
          return;
        }
        if (data.positions.length === this.buffers.positions.length)
          this.pending = data;
      };
      this.worker.onerror = () => {
        this.workerFailed = true;
        this.layoutQueue.dispose();
        this.layoutFailed();
      };
      this.observer = new ResizeObserver(this.resize);
      this.observer.observe(container);
      cleanup.push(() => this.observer.disconnect());
      this.renderer.domElement.addEventListener(
        "pointerdown",
        this.onPointerDown,
      );
      this.renderer.domElement.addEventListener("pointerup", this.onPointerUp);
      this.renderer.domElement.addEventListener(
        "pointermove",
        this.onPointerMove,
      );
      this.renderer.domElement.addEventListener(
        "pointerleave",
        this.onPointerLeave,
      );
      this.frame = requestAnimationFrame(this.animate);
    } catch (error) {
      cleanup.reverse().forEach((dispose) => dispose());
      this.points.geometry.dispose();
      this.lines.geometry.dispose();
      this.pointMaterial.dispose();
      this.edgeMaterial.dispose();
      throw error;
    }
  }

  setData(data: GraphData, stream?: object) {
    const wasEmpty = this.buffers.nodes.length === 0;
    const update = this.bufferStore.update(data, stream);
    const { nodesChanged, positionsChanged, nodeStart, linkStart } = update;
    this.pending = undefined;
    this.revision++;
    this.callbacks.onHover(null);
    if (positionsChanged || !this.points.geometry.hasAttribute("position")) {
      this.points.geometry.dispose();
      this.points.geometry = new BufferGeometry();
      const capacity = this.bufferStore.positions.length / 3;
      this.points.geometry.setAttribute(
        "position",
        new BufferAttribute(this.bufferStore.positions, 3).setUsage(
          DynamicDrawUsage,
        ),
      );
      this.points.geometry.setAttribute(
        "color",
        new BufferAttribute(new Float32Array(capacity * 3), 3),
      );
      this.points.geometry.setAttribute(
        "opacity",
        new BufferAttribute(new Float32Array(capacity), 1),
      );
      this.points.geometry.setAttribute(
        "size",
        new BufferAttribute(new Float32Array(capacity), 1),
      );
    }
    this.points.geometry.setDrawRange(0, data.nodes.length);
    if (nodesChanged) {
      const position = this.points.geometry.getAttribute(
        "position",
      ) as BufferAttribute;
      position.addUpdateRange(
        nodeStart * 3,
        (data.nodes.length - nodeStart) * 3,
      );
      position.needsUpdate = true;
      this.pointMaterial.uniforms.minimumSize.value =
        (data.nodes.length < 200 ? 10 : 3.5) * this.renderer.getPixelRatio();
    }
    this.ensureEdgeCapacity(this.buffers.links.length);
    const edgePosition = this.lines.geometry.getAttribute(
      "position",
    ) as BufferAttribute;
    const start = linkStart;
    writeEdgePositions(
      edgePosition.array as Float32Array,
      this.buffers.positions,
      this.buffers.links,
      start,
    );
    edgePosition.addUpdateRange(
      start * 3,
      (this.buffers.links.length - start) * 3,
    );
    edgePosition.needsUpdate = true;
    this.lines.geometry.setDrawRange(0, this.buffers.links.length);
    this.updateColors(
      nodesChanged || this.selectedId !== undefined,
      this.selectedId === undefined ? start : 0,
      positionsChanged || this.selectedId !== undefined ? 0 : nodeStart,
    );
    if (wasEmpty) this.autoFit = true;
    if (nodesChanged && this.autoFit) this.fit();
    this.callbacks.onStatus({ busy: data.nodes.length > 0 });
    if (this.workerFailed) this.layoutFailed();
    else
      this.layoutQueue.enqueue(this.revision, (revision): LayoutRequest => ({
        revision,
        ids: this.buffers.nodes.map((node) => node.id),
        positions: this.buffers.positions.slice(),
        links: this.buffers.links.slice(),
      }));
    this.dirty = true;
  }

  private ensureEdgeCapacity(size: number) {
    if (
      this.edgeCapacity >= size &&
      this.lines.geometry.hasAttribute("position")
    )
      return;
    this.edgeCapacity = Math.max(2048, size, this.edgeCapacity * 2);
    const positions = new Float32Array(this.edgeCapacity * 3);
    const colors = new Float32Array(this.edgeCapacity * 3);
    const previousPositions = this.lines.geometry.getAttribute("position");
    const previousColors = this.lines.geometry.getAttribute("color");
    if (previousPositions) positions.set(previousPositions.array);
    if (previousColors) colors.set(previousColors.array);
    this.lines.geometry.dispose();
    this.lines.geometry = new BufferGeometry();
    this.lines.geometry.setAttribute(
      "position",
      new BufferAttribute(positions, 3).setUsage(DynamicDrawUsage),
    );
    this.lines.geometry.setAttribute(
      "color",
      new BufferAttribute(colors, 3).setUsage(DynamicDrawUsage),
    );
  }

  setPalette(palette: GraphPalette) {
    this.palette = palette;
    this.renderer.setClearColor(palette.background);
    this.colorCache.clear();
    this.updateColors();
    this.dirty = true;
  }

  setSelection(selectedId: string | undefined, search: string) {
    if (this.selectedId === selectedId && this.search === search) return;
    this.selectedId = selectedId;
    this.search = search.toLowerCase().trim();
    this.updateColors();
  }

  fit = () => {
    if (!this.buffers.positions.length) return;
    const box = new Box3().setFromArray(this.buffers.positions);
    const center = box.getCenter(new Vector3());
    const size = box.getSize(new Vector3()).length();
    const distance =
      Math.max(
        100,
        size /
          (2 * Math.tan((this.camera.fov * Math.PI) / 360)) /
          Math.min(1, this.camera.aspect),
      ) * 1.2;
    const direction = this.camera.position
      .clone()
      .sub(this.controls.target)
      .normalize();
    this.camera.position.copy(center).addScaledVector(direction, distance);
    this.controls.target.copy(center);
    this.controls.update();
    this.dirty = true;
  };

  private updateColors(updateNodes = true, start = 0, nodeStart = 0) {
    const colors = this.points.geometry.getAttribute("color");
    const opacity = this.points.geometry.getAttribute("opacity");
    const sizes = this.points.geometry.getAttribute("size");
    const edgeColors = this.lines.geometry.getAttribute(
      "color",
    ) as BufferAttribute;
    if (
      !(colors instanceof BufferAttribute) ||
      !(opacity instanceof BufferAttribute) ||
      !(sizes instanceof BufferAttribute) ||
      !edgeColors
    )
      return;
    const selected = this.selectedId
      ? this.buffers.indices.get(this.selectedId)
      : undefined;
    const related = new Set<number>();
    if (selected !== undefined) related.add(selected);
    const normalEdge = new Color(this.palette.edge),
      dimEdge = new Color(this.palette.dimEdge);
    const outgoing = new Color(this.palette.outgoing),
      incoming = new Color(this.palette.incoming);
    for (let index = start; index < this.buffers.links.length; index += 2) {
      const source = this.buffers.links[index],
        target = this.buffers.links[index + 1];
      let color = selected === undefined ? normalEdge : dimEdge;
      if (source === selected || target === selected) {
        related.add(source);
        related.add(target);
        color = source === selected ? outgoing : incoming;
      }
      edgeColors.setXYZ(index, color.r, color.g, color.b);
      edgeColors.setXYZ(index + 1, color.r, color.g, color.b);
    }
    if (updateNodes)
      for (let index = nodeStart; index < this.buffers.nodes.length; index++) {
        const node = this.buffers.nodes[index];
        let color = this.colorCache.get(node.type);
        if (!color) {
          color = new Color(
            this.palette.nodes[node.type] ?? this.palette.fallback,
          );
          this.colorCache.set(node.type, color);
        }
        colors.setXYZ(index, color.r, color.g, color.b);
        const matches =
          !this.search ||
          [
            node.label,
            node.type,
            node.relativePath,
            String(node.codeNodeId),
          ].some((value) => value?.toLowerCase().includes(this.search));
        opacity.setX(
          index,
          !matches || (selected !== undefined && !related.has(index))
            ? 0.2
            : node.scope === "external"
              ? 0.65
              : 1,
        );
        const size =
          node.type === "project"
            ? 16
            : node.type === "class" || node.type === "interface"
              ? 11
              : 7;
        sizes.setX(index, index === selected ? size * 1.6 : size);
      }
    if (updateNodes) {
      colors.addUpdateRange(
        nodeStart * 3,
        (this.buffers.nodes.length - nodeStart) * 3,
      );
      opacity.addUpdateRange(nodeStart, this.buffers.nodes.length - nodeStart);
      sizes.addUpdateRange(nodeStart, this.buffers.nodes.length - nodeStart);
      colors.needsUpdate = true;
      opacity.needsUpdate = true;
      sizes.needsUpdate = true;
    }
    edgeColors.addUpdateRange(
      start * 3,
      (this.buffers.links.length - start) * 3,
    );
    edgeColors.needsUpdate = true;
    this.dirty = true;
  }

  private resize = () => {
    this.width = Math.max(1, this.container.clientWidth);
    this.height = Math.max(1, this.container.clientHeight);
    this.renderer.setSize(this.width, this.height);
    this.camera.aspect = this.width / this.height;
    this.camera.updateProjectionMatrix();
    this.pointMaterial.uniforms.scale.value =
      (this.height * this.renderer.getPixelRatio()) /
      (2 * Math.tan((this.camera.fov * Math.PI) / 360));
    this.pointMaterial.uniforms.maximumSize.value =
      28 * this.renderer.getPixelRatio();
    this.dirty = true;
  };
  private markDirty = () => {
    this.dirty = true;
  };
  private stopAutoFit = () => {
    this.autoFit = false;
  };
  private layoutFailed = () => {
    if (!this.disposed)
      this.callbacks.onStatus({
        busy: false,
        error:
          "Automatic layout is unavailable. You can still explore every loaded node.",
      });
  };

  private animate = () => {
    if (this.disposed) return;
    if (this.pending) {
      const { positions, settled } = this.pending;
      this.pending = undefined;
      this.bufferStore.applyLayout(positions);
      const position = this.points.geometry.getAttribute(
        "position",
      ) as BufferAttribute;
      position.clearUpdateRanges();
      position.addUpdateRange(0, positions.length);
      position.needsUpdate = true;
      const edgePosition = this.lines.geometry.getAttribute(
        "position",
      ) as BufferAttribute;
      writeEdgePositions(
        edgePosition.array as Float32Array,
        positions,
        this.buffers.links,
      );
      edgePosition.clearUpdateRanges();
      edgePosition.needsUpdate = true;
      if (settled) {
        if (this.autoFit) this.fit();
        this.callbacks.onStatus({ busy: false });
      }
      this.dirty = true;
    }
    this.controls.update();
    if (this.dirty) {
      this.renderer.render(this.scene, this.camera);
      this.dirty = false;
    }
    if (
      this.hoverPointer &&
      !this.pointerDown &&
      performance.now() - this.lastHover > 80
    ) {
      const pointer = this.hoverPointer;
      this.hoverPointer = undefined;
      this.lastHover = performance.now();
      const index = this.pick(pointer.x, pointer.y);
      const rect = this.renderer.domElement.getBoundingClientRect();
      this.renderer.domElement.style.cursor =
        index === null ? "grab" : "pointer";
      this.callbacks.onHover(
        index === null
          ? null
          : {
              node: this.buffers.nodes[index],
              x: Math.min(
                pointer.x - rect.left + 14,
                Math.max(8, this.width - 300),
              ),
              y: Math.max(
                8,
                Math.min(pointer.y - rect.top - 58, this.height - 90),
              ),
            },
      );
    }
    this.frame = requestAnimationFrame(this.animate);
  };

  private pick(x: number, y: number) {
    const sizes = this.points.geometry.getAttribute("size");
    if (!sizes) return null;
    this.camera.updateMatrixWorld();
    this.projection.multiplyMatrices(
      this.camera.projectionMatrix,
      this.camera.matrixWorldInverse,
    );
    const rect = this.renderer.domElement.getBoundingClientRect();
    return pickGraphNode(
      this.buffers.positions,
      this.projection.elements,
      this.width,
      this.height,
      x - rect.left,
      y - rect.top,
      10,
      {
        sizes: sizes.array,
        scale: this.pointMaterial.uniforms.scale.value,
        minimumSize: this.pointMaterial.uniforms.minimumSize.value,
        maximumSize: this.pointMaterial.uniforms.maximumSize.value,
        pixelRatio: this.renderer.getPixelRatio(),
      },
    );
  }
  private onPointerDown = (event: PointerEvent) => {
    if (event.button === 0)
      this.pointerDown = { x: event.clientX, y: event.clientY };
    this.callbacks.onHover(null);
  };
  private onPointerUp = (event: PointerEvent) => {
    const down = this.pointerDown;
    this.pointerDown = undefined;
    if (!down || Math.hypot(event.clientX - down.x, event.clientY - down.y) > 5)
      return;
    const index = this.pick(event.clientX, event.clientY);
    this.callbacks.onSelect(index === null ? null : this.buffers.nodes[index]);
    if (index === null) return;
    this.autoFit = false;
    const center = new Vector3().fromArray(this.buffers.positions, index * 3);
    const position = new Vector3();
    let radius = 70;
    for (let offset = 0; offset < this.buffers.links.length; offset += 2) {
      const source = this.buffers.links[offset],
        target = this.buffers.links[offset + 1];
      if (source !== index && target !== index) continue;
      position.fromArray(
        this.buffers.positions,
        (source === index ? target : source) * 3,
      );
      radius = Math.max(radius, position.distanceTo(center));
    }
    const direction = this.camera.position
      .clone()
      .sub(this.controls.target)
      .normalize();
    this.camera.position.copy(center).addScaledVector(direction, radius * 2.5);
    this.controls.target.copy(center);
    this.controls.update();
    this.dirty = true;
  };
  private onPointerMove = (event: PointerEvent) => {
    this.hoverPointer = { x: event.clientX, y: event.clientY };
  };
  private onPointerLeave = () => {
    this.hoverPointer = undefined;
    this.pointerDown = undefined;
    this.callbacks.onHover(null);
  };

  dispose() {
    this.disposed = true;
    cancelAnimationFrame(this.frame);
    this.worker.terminate();
    this.layoutQueue.dispose();
    this.observer.disconnect();
    this.controls.dispose();
    this.points.geometry.dispose();
    this.lines.geometry.dispose();
    this.pointMaterial.dispose();
    this.edgeMaterial.dispose();
    this.renderer.domElement.removeEventListener(
      "pointerdown",
      this.onPointerDown,
    );
    this.renderer.domElement.removeEventListener("pointerup", this.onPointerUp);
    this.renderer.domElement.removeEventListener(
      "pointermove",
      this.onPointerMove,
    );
    this.renderer.domElement.removeEventListener(
      "pointerleave",
      this.onPointerLeave,
    );
    this.renderer.dispose();
    this.renderer.forceContextLoss();
    this.renderer.domElement.remove();
  }
}
