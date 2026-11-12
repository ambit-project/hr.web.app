import tailwindcss from "@tailwindcss/vite";
import { tanstackStart } from "@tanstack/react-start/plugin/vite";
import viteReact from "@vitejs/plugin-react";
import { nitro } from "nitro/vite";
import { defineConfig } from "vite";

export default defineConfig({
	resolve: { tsconfigPaths: true },
	// React's plugin must come after Start's; nitro builds the production server (.output/server/index.mjs).
	plugins: [tailwindcss(), tanstackStart(), viteReact(), nitro()],
});
