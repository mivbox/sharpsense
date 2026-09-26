import { defineConfig, type ProxyOptions } from "vite";
import react from "@vitejs/plugin-react";

const apiTarget = process.env.SHARPSENSE_API_URL ?? "http://localhost:50069";
const proxy: ProxyOptions = {
  target: apiTarget,
  changeOrigin: true,
  configure(proxyServer) {
    proxyServer.on("proxyReq", (request, incoming) => {
      // The dev server proxies same-origin browser traffic to the local API.
      if (incoming.headers.origin)
        request.setHeader("Origin", new URL(apiTarget).origin);
    });
  },
};

export default defineConfig({
  plugins: [react()],
  server: { proxy: { "/api": proxy, "/openapi": proxy } },
  build: { outDir: "dist", emptyOutDir: true },
});
