export type GraphPalette = {
  background: string;
  edge: string;
  dimEdge: string;
  outgoing: string;
  incoming: string;
  fallback: string;
  nodes: Readonly<Record<string, string>>;
};

export const graphPalettes: Record<"dark" | "light", GraphPalette> = {
  dark: {
    background: "#181818",
    edge: "#7a9684",
    dimEdge: "#303638",
    outgoing: "#86aebe",
    incoming: "#c5ab73",
    fallback: "#a9b8ad",
    nodes: {
      project: "#7faf89",
      class: "#83b3a2",
      interface: "#c3b580",
      method: "#86aebe",
      property: "#c7a775",
      field: "#c98e88",
      document: "#a9b8ad",
      namespace: "#9cae85",
      component: "#97bd8d",
      enum: "#c7b985",
      struct: "#88b8bb",
      record: "#83b3a2",
      http: "#c79b82",
      package: "#b1a28e",
    },
  },
  light: {
    background: "#f0f0f0",
    edge: "#587963",
    dimEdge: "#d6dcdf",
    outgoing: "#3d6f83",
    incoming: "#8c681f",
    fallback: "#647369",
    nodes: {
      project: "#3f7650",
      class: "#397967",
      interface: "#867637",
      method: "#3d6f83",
      property: "#906a32",
      field: "#a05750",
      document: "#647369",
      namespace: "#687c45",
      component: "#577e42",
      enum: "#847529",
      struct: "#36777b",
      record: "#397967",
      http: "#93603f",
      package: "#796b56",
    },
  },
};
