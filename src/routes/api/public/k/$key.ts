import { createFileRoute } from "@tanstack/react-router";
import { isBlockedFetcher } from "@/lib/fetcher-guard";

export const Route = createFileRoute("/api/public/k/$key")({
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

        const key = decodeURIComponent(params.key ?? "").trim().slice(0, 128);
        if (!key) return new Response(luaErrorMessage("Thieu KEY."), { status: 200, headers: luaHeaders });

        const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
        const { data: keyRow } = await supabaseAdmin
          .from("script_keys")
          .select("id, script_id, hwid, auto_reset_hwid")
          .eq("key", key)
          .maybeSingle();
        if (!keyRow) {
          return new Response(luaErrorMessage("KEY khong hop le hoac da bi xoa."), { status: 200, headers: luaHeaders });
        }
        const row = keyRow as { id: string; script_id: string; hwid: string | null; auto_reset_hwid: boolean | null };

        const url = new URL(request.url);
        const hwid = (url.searchParams.get("hwid") ?? "").trim().slice(0, 200);
        const now = new Date().toISOString();
        if (row.auto_reset_hwid) {
          await supabaseAdmin.from("script_keys").update({ hwid: hwid || null, last_used: now }).eq("id", row.id);
        } else if (!row.hwid) {
          await supabaseAdmin.from("script_keys").update({ hwid: hwid || "unknown", last_used: now }).eq("id", row.id);
        } else if (hwid && row.hwid !== hwid) {
          return new Response(luaErrorMessage("KEY nay da khoa vao may khac. Nho admin reset HWID."), {
            status: 200,
            headers: luaHeaders,
          });
        } else {
          await supabaseAdmin.from("script_keys").update({ last_used: now }).eq("id", row.id);
        }

        const { data, error } = await supabaseAdmin
          .from("scripts")
          .select("slug, encoded, deleted, version")
          .eq("id", row.script_id)
          .maybeSingle();

        if (error || !data) return new Response(deletedLuaMessage(), { status: 200, headers: luaHeaders });
        const s = data as { slug: string; encoded: string; deleted: boolean; version?: number };
        if (s.deleted) return new Response(deletedLuaMessage(), { status: 200, headers: luaHeaders });

        const tracker = userTrackerLua(s.slug, url.origin, s.version ?? 1);
        const xorKey = process.env["OBF_XOR_KEY"] || "LuaCrackVN-default-key";
        const inner = tracker + "\n" + s.encoded;
        const wrapped = buildEncodedScript(inner, xorKey);
        return new Response(wrapped, { status: 200, headers: luaHeaders });
      },
    },
  },
});
