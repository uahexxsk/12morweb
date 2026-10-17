import { createFileRoute } from "@tanstack/react-router";
import { isBlockedFetcher } from "@/lib/fetcher-guard";


export const Route = createFileRoute("/api/public/raw/$slug")({
  server: {
    handlers: {
      GET: async ({ request, params }) => {
        const { browserBlockedHtml, deletedLuaMessage, buildEncodedScript, luaErrorMessage } =
          await import("@/lib/obfuscator");

        if (isBlockedFetcher(request)) {
          return new Response(browserBlockedHtml(), {
            status: 403,
            headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" },
          });
        }

        const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
        const { data, error } = await supabaseAdmin
          .from("scripts")
          .select("id, encoded, deleted, version")
          .eq("slug", params.slug)
          .maybeSingle();

        const luaHeaders = {
          "content-type": "text/plain; charset=utf-8",
          "cache-control": "no-store",
          "x-robots-tag": "noindex",
        };

        if (error || !data) return new Response(deletedLuaMessage(), { status: 200, headers: luaHeaders });
        if ((data as { deleted: boolean }).deleted) return new Response(deletedLuaMessage(), { status: 200, headers: luaHeaders });

        // ---- Key + HWID (tuỳ chọn): không có key vẫn bật được bình thường.
        // Chỉ kiểm tra khi loadstring có kèm ?key=...
        const scriptId = (data as { id: string }).id;
        const url = new URL(request.url);
        const key = (url.searchParams.get("key") ?? "").trim().slice(0, 128);
        const hwid = (url.searchParams.get("hwid") ?? "").trim().slice(0, 200);

        if (key) {
          const { data: keyRow } = await supabaseAdmin
            .from("script_keys")
            .select("id, hwid, auto_reset_hwid")
            .eq("script_id", scriptId)
            .eq("key", key)
            .maybeSingle();
          if (!keyRow) {
            return new Response(luaErrorMessage("KEY khong hop le hoac da bi xoa."), { status: 200, headers: luaHeaders });
          }
          const row = keyRow as { id: string; hwid: string | null; auto_reset_hwid: boolean | null };
          const now = new Date().toISOString();
          if (row.auto_reset_hwid) {
            // Tự reset HWID mỗi lần bật -> ai cầm key cũng bật được.
            await supabaseAdmin
              .from("script_keys")
              .update({ hwid: hwid || null, last_used: now })
              .eq("id", row.id);
          } else if (!row.hwid) {
            await supabaseAdmin
              .from("script_keys")
              .update({ hwid: hwid || "unknown", last_used: now })
              .eq("id", row.id);
          } else if (hwid && row.hwid !== hwid) {
            return new Response(luaErrorMessage("KEY nay da khoa vao may khac. Nho admin reset HWID."), {
              status: 200, headers: luaHeaders,
            });
          } else {
            await supabaseAdmin.from("script_keys").update({ last_used: now }).eq("id", row.id);
          }
        }


        const xorKey = process.env["OBF_XOR_KEY"];
        if (!xorKey || xorKey.length < 32) {
          return new Response("-- LuaCrackVN server misconfigured: OBF_XOR_KEY must be 32+ chars.\nerror(\"Server configuration error.\")\n", {
            status: 503,
            headers: luaHeaders,
          });
        }
        // Serve only the user's transformed payload; no client tracking or remote effects are injected.
        const wrapped = buildEncodedScript((data as { encoded: string }).encoded, xorKey);
        return new Response(wrapped, { status: 200, headers: luaHeaders });
      },
    },
  },
});
