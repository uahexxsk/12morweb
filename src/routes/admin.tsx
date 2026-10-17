import { createFileRoute, redirect } from "@tanstack/react-router";

// Admin UI intentionally removed from the public website. Privileged server
// functions still enforce requireAdmin() and do not become public by hiding this route.
export const Route = createFileRoute("/admin")({
  beforeLoad: () => {
    throw redirect({ to: "/" });
  },
  component: () => null,
});
