import { createFileRoute } from "@tanstack/react-router";

// Liveness of the web server for the container HEALTHCHECK; it does not call the API.
export const Route = createFileRoute("/health")({
	server: {
		handlers: {
			GET: () => new Response("ok", { headers: { "content-type": "text/plain" } }),
		},
	},
});
