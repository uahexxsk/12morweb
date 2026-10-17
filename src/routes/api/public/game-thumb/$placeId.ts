import { createFileRoute } from "@tanstack/react-router";

// Proxies Roblox's game-icon thumbnail service. Returns JSON { url } that
// the admin UI can drop into an <img src>. Cached for a short window; the
// admin refreshes by cache-busting with a timestamp query.
export const Route = createFileRoute("/api/public/game-thumb/$placeId")({
  server: {
    handlers: {
      GET: async ({ params }) => {
        const pid = String(params.placeId).replace(/[^0-9]/g, "");
        const headers = {
          "content-type": "application/json; charset=utf-8",
          "cache-control": "public, max-age=60",
        };
        if (!pid) return new Response(JSON.stringify({ url: null }), { headers });
        try {
          // 1) placeId -> universeId
          const u = await fetch(`https://apis.roblox.com/universes/v1/places/${pid}/universe`);
          const uj = (await u.json()) as { universeId?: number };
          const uid = uj?.universeId;
          if (!uid) return new Response(JSON.stringify({ url: null, name: null }), { headers });

          // 2) game icon
          const t = await fetch(
            `https://thumbnails.roblox.com/v1/games/icons?universeIds=${uid}&size=512x512&format=Png&isCircular=false`,
          );
          const tj = (await t.json()) as { data?: Array<{ imageUrl?: string }> };
          const url = tj?.data?.[0]?.imageUrl ?? null;

          // 3) name (optional; the tracker already sends it, but this lets us verify)
          let name: string | null = null;
          try {
            const g = await fetch(`https://games.roblox.com/v1/games?universeIds=${uid}`);
            const gj = (await g.json()) as { data?: Array<{ name?: string; playing?: number }> };
            name = gj?.data?.[0]?.name ?? null;
          } catch {}

          return new Response(JSON.stringify({ url, name, universeId: uid }), { headers });
        } catch {
          return new Response(JSON.stringify({ url: null, name: null }), { headers });
        }
      },
    },
  },
});
