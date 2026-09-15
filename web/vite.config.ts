/// <reference types="vitest/config" />
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// In development the ASP.NET Core server listens on :5080 (see launchSettings.json) and the
// Vite dev server proxies API and SignalR traffic to it, so the browser sees a single origin.
const apiTarget = process.env.ARGUS_API_URL ?? "http://localhost:5080";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": apiTarget,
      "/hubs": { target: apiTarget, ws: true },
      "/health": apiTarget,
      "/downloads": apiTarget,
    },
  },
  build: {
    outDir: "dist",
    sourcemap: true,
  },
  test: {
    environment: "jsdom",
  },
});
