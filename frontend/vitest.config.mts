import { dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

const root = dirname(fileURLToPath(import.meta.url));

// The "@" alias mirrors the one in tsconfig.json, so tests import code the same way the app does.
export default defineConfig({
  esbuild: { jsx: "automatic" },
  resolve: { alias: { "@": root } },
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
  },
});
