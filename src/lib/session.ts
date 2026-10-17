// Shared session config for admin gate. Server-only.
export const adminSessionConfig = {
  password: process.env.SESSION_SECRET ?? "dev-insecure-session-secret-please-set",
  name: "luacrack-admin",
  maxAge: 60 * 60 * 24 * 7,
  cookie: {
    httpOnly: true,
    secure: true,
    sameSite: "lax" as const,
    path: "/",
  },
};

export type AdminSession = { unlocked?: boolean };
