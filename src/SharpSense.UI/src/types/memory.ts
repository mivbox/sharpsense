export type MemoryIntent = "Convention" | "Invariant" | "Todo" | "Warning" | "Decision";

export const MEMORY_INTENTS: MemoryIntent[] = [
  "Convention",
  "Invariant",
  "Todo",
  "Warning",
  "Decision"
];

export type MemoryMetadata = {
  id: string;
  targetFullyQualifiedName: string;
  intent: MemoryIntent;
  contentHash: string;
  isStale: boolean;
  tags: string[];
  createdAt: string;
};

export type Memory = MemoryMetadata & {
  content: string;
};
