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
    rolldownOptions: {
      output: {
        // React and the UI toolkit change far less often than Argus itself, so they get chunks of
        // their own that browsers keep cached across upgrades. Pages still load when first opened.
        codeSplitting: {
          groups: [
            { name: "react", test: /node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/ },
            { name: "mantine", test: /node_modules[\\/]@mantine[\\/]/ },
          ],
        },
      },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
  },
});
