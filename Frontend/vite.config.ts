import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
const apiTarget = process.env.VITE_API_PROXY_TARGET ?? "http://127.0.0.1:5080";
export default defineConfig({
  plugins: [react()],
  build: { assetsInlineLimit: 0 },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": apiTarget,
      "/backuphub": { target: apiTarget, ws: true },
    },
  },
  test: {
    include: ["src/**/*.test.{ts,tsx}"],
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    clearMocks: true,
  },
});
