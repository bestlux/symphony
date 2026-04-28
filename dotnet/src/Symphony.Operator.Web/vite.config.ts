import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "/operator/",
  build: {
    outDir: "../Symphony.Service/wwwroot/operator",
    emptyOutDir: true
  },
  server: {
    proxy: {
      "/api": "http://127.0.0.1:4027"
    }
  }
});
