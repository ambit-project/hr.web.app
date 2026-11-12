import { defineConfig } from "orval";

// The client is generated from the committed snapshot (openapi/hr-v1.json), so generation needs no running API.
// Refresh the snapshot with `yarn api:fetch` while the API runs; a backend test fails when it is stale.
export default defineConfig({
	hr: {
		input: { target: "./openapi/hr-v1.json" },
		output: {
			mode: "tags-split",
			target: "./src/api/endpoints",
			schemas: "./src/api/models",
			client: "axios-functions",
			clean: true,
			override: { mutator: { path: "./src/api/mutator.ts", name: "customInstance" } },
		},
	},
});
