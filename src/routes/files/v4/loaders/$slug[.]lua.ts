import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/files/v4/loaders/$slug[.]lua")({
  server: {
    handlers: {
      GET: async ({ request, params }) => {
        const { serveProjectLoader } = await import("@/lib/project-loader.server");
        return serveProjectLoader(request, params.slug);
      },
    },
  },
});
