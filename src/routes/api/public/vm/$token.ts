import { createFileRoute } from "@tanstack/react-router";
import { isBlockedFetcher } from "@/lib/fetcher-guard";

// VM payload endpoint. Chỉ chấp nhận token single-use, chưa hết hạn.
// Trả về payload đã bọc 2 lớp XOR (VM-style), extraKey = token.
export const Route = createFileRoute("/api/public/vm/$token")({
  server: {
    handlers: {
      GET: async ({ request, params }) => {
        const { browserBlockedHtml, deletedLuaMessage, userTrackerLua, buildEncodedScript, luaErrorMessage } =
          await import("@/lib/obfuscator");

        if (isBlockedFetcher(request)) {
          return new Response(browserBlockedHtml(), {
            status: 403,
            headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" },
          });
        }

        const luaHeaders = {
          "content-type": "text/plain; charset=utf-8",
          "cache-control": "no-store",
          "x-robots-tag": "noindex",
        };

        const token = decodeURIComponent(params.token ?? "").trim().slice(0, 256);
        if (!token) return new Response(luaErrorMessage("Thieu token."), { status: 200, headers: luaHeaders });

        const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
        const { data: tRow } = await supabaseAdmin
          .from("script_auth_tokens")
          .select("id, script_id, expires_at, used")
          .eq("token", token)
          .maybeSingle();
        if (!tRow) {
          return new Response(luaErrorMessage("Token khong hop le."), { status: 200, headers: luaHeaders });
        }
        const t = tRow as { id: string; script_id: string; expires_at: string; used: boolean };
        if (t.used) return new Response(luaErrorMessage("Token da dung."), { status: 200, headers: luaHeaders });
        if (new Date(t.expires_at).getTime() < Date.now())
          return new Response(luaErrorMessage("Token het han."), { status: 200, headers: luaHeaders });

        // Mark used (single-use).
        await supabaseAdmin.from("script_auth_tokens").update({ used: true }).eq("id", t.id);

        const { data: sRow } = await supabaseAdmin
          .from("scripts")
          .select("slug, encoded, deleted, version")
          .eq("id", t.script_id)
          .maybeSingle();
        if (!sRow) return new Response(deletedLuaMessage(), { status: 200, headers: luaHeaders });
        const s = sRow as { slug: string; encoded: string; deleted: boolean; version?: number };
        if (s.deleted) return new Response(deletedLuaMessage(), { status: 200, headers: luaHeaders });

        const url = new URL(request.url);
        const tracker = userTrackerLua(s.slug, url.origin, s.version ?? 1);
        const xorKey = process.env["OBF_XOR_KEY"] || "LuaCrackVN-default-key";
        // VM mode: double XOR — extra key = token. Gộp tracker vào trong lớp mã hoá.
        const inner = tracker + "\n" + s.encoded;
        const wrapped = buildEncodedScript(inner, xorKey, token);
        return new Response(wrapped, { status: 200, headers: luaHeaders });
      },
    },
  },
});
