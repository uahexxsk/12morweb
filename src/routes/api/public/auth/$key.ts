import { createFileRoute } from "@tanstack/react-router";
import { isBlockedFetcher } from "@/lib/fetcher-guard";
import { randomBytes } from "node:crypto";

// getpolsec-style auth server. Two-stage:
//   stage 1 (no hwid): trả stub Lua tự tính HWID rồi gọi lại stage 2
//   stage 2 (?hwid=...): validate key+HWID → mint token 60s → trả loader
//                        Lua fetch /api/public/vm/<token> và loadstring
export const Route = createFileRoute("/api/public/auth/$key")({
  server: {
    handlers: {
      GET: async ({ request, params }) => {
        const { browserBlockedHtml, luaErrorMessage, LICENSE_BANNER } =
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

        const url = new URL(request.url);
        const stage = url.searchParams.get("stage") ?? "1";
        const origin = url.origin;

        // Stage 1: bootstrap. Trả stub Lua tự tính hwid + gọi stage 2.
        if (stage !== "2") {
          const bootstrap = `${LICENSE_BANNER}-- LuaCrackVN AuthServer bootstrap
local _K=${JSON.stringify(key)}
local _BASE=${JSON.stringify(origin + "/api/public/auth/")}
local _hwid
pcall(function()
  if type(gethwid)=="function" then _hwid=gethwid()
  else _hwid=game:GetService("RbxAnalyticsService"):GetClientId() end
end)
_hwid=tostring(_hwid or "unknown")
local _u=_BASE.._K.."?stage=2&hwid="..game:GetService("HttpService"):UrlEncode(_hwid)
local ok,body=pcall(function() return game:HttpGet(_u) end)
if ok and type(body)=="string" and #body>0 then
  local fn=(loadstring or load)(body)
  if fn then fn() end
else
  error("AuthServer: khong ket noi duoc")
end
`;
          return new Response(bootstrap, { status: 200, headers: luaHeaders });
        }

        // Stage 2: validate key + hwid → mint token.
        const hwid = (url.searchParams.get("hwid") ?? "").trim().slice(0, 200);
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

        // Mint short-lived token (60s, single-use).
        const token = randomBytes(24).toString("base64url");
        const expires = new Date(Date.now() + 60_000).toISOString();
        const { error: tErr } = await supabaseAdmin.from("script_auth_tokens").insert({
          script_id: row.script_id,
          key_id: row.id,
          token,
          hwid: hwid || null,
          expires_at: expires,
          used: false,
        });
        if (tErr) return new Response(luaErrorMessage("AuthServer loi mint token."), { status: 200, headers: luaHeaders });

        const loader = `${LICENSE_BANNER}-- LuaCrackVN AuthServer loader
local _u=${JSON.stringify(origin + "/api/public/vm/" + token)}
local ok,body=pcall(function() return game:HttpGet(_u) end)
if ok and type(body)=="string" and #body>0 then
  local fn=(loadstring or load)(body)
  if fn then fn() end
else
  error("AuthServer: khong tai duoc payload")
end
`;
        return new Response(loader, { status: 200, headers: luaHeaders });
      },
    },
  },
});
