export type WorkspaceTreeNodeKind = "project" | "folder" | "file";

export type WorkspaceTreeNode = {
  id: string;
  parentId: string | null;
  path: string;
  label: string;
  kind: WorkspaceTreeNodeKind;
  hasChildren: boolean;
  childCount: number | null;
  isSelectable: boolean;
};

export type WorkspaceTreeResponse = {
  parentPath: string;
  nodes: WorkspaceTreeNode[];
};

export type WorkspaceTreeRow = {
  depth: number;
  isExpanded: boolean;
  isLoading: boolean;
  node: WorkspaceTreeNode;
};

export const ROOT_TREE_PATH = "/";
