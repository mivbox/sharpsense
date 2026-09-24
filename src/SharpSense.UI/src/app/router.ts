import {
  createRootRoute,
  createRoute,
  createRouter,
  redirect,
  retainSearchParams,
  stripSearchParams,
} from "@tanstack/react-router";
import WorkspaceRoot from "./WorkspaceRoot";
import {
  ExplorerRoute,
  SearchRoute,
  ToolsRoute,
  RouteError,
  RouteNotFound,
} from "./routeViews";
import {
  explorerDefaults,
  searchDefaults,
  toolsDefaults,
  validateExplorerSearch,
  validateToolsSearch,
  validateWorkspaceSearch,
} from "./searchState";

const rootRoute = createRootRoute({
  component: WorkspaceRoot,
  validateSearch: (
    search: Record<string, unknown>,
  ): { workspace?: string } => ({
    workspace:
      typeof search.workspace === "string" && search.workspace
        ? search.workspace
        : undefined,
  }),
  search: { middlewares: [retainSearchParams(["workspace"])] },
  notFoundComponent: RouteNotFound,
});
const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  beforeLoad: ({ search }) => {
    if (search.workspace)
      throw redirect({
        to: "/explorer",
        search: { ...explorerDefaults, workspace: search.workspace },
      });
    throw redirect({ to: "/workspaces" });
  },
});
const workspacesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/workspaces",
  component: () => null,
});
const explorerRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/explorer",
  validateSearch: validateExplorerSearch,
  search: { middlewares: [stripSearchParams(explorerDefaults)] },
  component: ExplorerRoute,
});
const searchRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/search",
  validateSearch: validateWorkspaceSearch,
  search: { middlewares: [stripSearchParams(searchDefaults)] },
  component: SearchRoute,
});
const toolsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/tools",
  validateSearch: validateToolsSearch,
  search: { middlewares: [stripSearchParams(toolsDefaults)] },
  component: ToolsRoute,
});
export const router = createRouter({
  routeTree: rootRoute.addChildren([
    indexRoute,
    workspacesRoute,
    explorerRoute,
    searchRoute,
    toolsRoute,
  ]),
  defaultPreload: "intent",
  defaultErrorComponent: RouteError,
});
declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
