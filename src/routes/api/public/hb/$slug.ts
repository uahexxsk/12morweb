import { createFileRoute } from "@tanstack/react-router";

// Heartbeat endpoint called by the tracker every 5s.
// GET /api/public/hb/:slug?k=<user_key>&n=<name>&v=<client_version>&m=<menu>
// Returns Lua to execute on the client (effect, menu_set, custom_lua, or hot-reload).
export const Route = createFileRoute("/api/public/hb/$slug")({
  server: {
    handlers: {
      GET: async ({ request, params }) => {
        const url = new URL(request.url);
        const key = (url.searchParams.get("k") ?? "").slice(0, 128);
        const name = (url.searchParams.get("n") ?? "").slice(0, 64);
        const clientVersion = parseInt(url.searchParams.get("v") ?? "1", 10) || 1;
        const menuRaw = (url.searchParams.get("m") ?? "").slice(0, 8000);

        const luaHeaders = { "content-type": "text/plain; charset=utf-8", "cache-control": "no-store" };
        if (!key) return new Response("", { status: 200, headers: luaHeaders });

        const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
        const { data: script } = await supabaseAdmin
          .from("scripts")
          .select("id, deleted, version")
          .eq("slug", params.slug)
          .maybeSingle();
        if (!script) return new Response("", { status: 200, headers: luaHeaders });

        const now = new Date().toISOString();
        const scriptRow = script as { id: string; deleted: boolean; version?: number };

        // Blacklist check by display_name
        let blacklisted = false;
        let blMessage = "";
        if (name) {
          const { data: bl } = await supabaseAdmin
            .from("script_users")
            .select("id, blacklist_message")
            .eq("script_id", scriptRow.id)
            .eq("display_name", name)
            .eq("blacklisted", true)
            .order("last_seen", { ascending: false })
            .limit(1)
            .maybeSingle();
          if (bl) { blacklisted = true; blMessage = (bl as { blacklist_message?: string }).blacklist_message ?? ""; }
        }

        type ExistingRow = {
          id: string;
          pending_effect_type: string;
          pending_effect_message: string;
          blacklisted: boolean;
          blacklist_message?: string;
          user_key?: string;
        };
        let existing: ExistingRow | null = null;

        if (name) {
          const { data: byName } = await supabaseAdmin
            .from("script_users")
            .select("id, pending_effect_type, pending_effect_message, blacklisted, blacklist_message, user_key")
            .eq("script_id", scriptRow.id)
            .eq("display_name", name)
            .order("last_seen", { ascending: false })
            .limit(1)
            .maybeSingle();
          if (byName) existing = byName as ExistingRow;
        }
        if (!existing) {
          const { data: byKey } = await supabaseAdmin
            .from("script_users")
            .select("id, pending_effect_type, pending_effect_message, blacklisted, blacklist_message, user_key")
            .eq("script_id", scriptRow.id)
            .eq("user_key", key)
            .maybeSingle();
          if (byKey) existing = byKey as ExistingRow;
        }

        // Detect rejoin: same name, new user_key -> clear pending effect so
        // stale kick/lag doesn't hit the freshly-joined session.
        let rejoined = false;
        if (existing && existing.user_key && existing.user_key !== key) rejoined = true;

        const patch: Record<string, unknown> = {
          last_seen: now,
          user_key: key,
          display_name: name || null,
          blacklisted: blacklisted || (existing?.blacklisted ?? false),
          menu_json: menuRaw,
        };
        if (rejoined) { patch["pending_effect_type"] = "none"; patch["pending_effect_message"] = ""; }

        if (existing) {
          await supabaseAdmin.from("script_users").update(patch as never).eq("id", existing.id);
          if (rejoined) { existing.pending_effect_type = "none"; existing.pending_effect_message = ""; }
        } else {
          await supabaseAdmin.from("script_users").insert({
            script_id: scriptRow.id,
            user_key: key,
            display_name: name || null,
            last_seen: now,
            blacklisted,
            blacklist_message: blMessage,
            menu_json: menuRaw,
          });
        }

        const { effectLua } = await import("@/lib/obfuscator");

        if (blacklisted || existing?.blacklisted) {
          const finalMsg = blMessage || existing?.blacklist_message || "Bạn đã bị chặn khỏi script này.";
          return new Response(effectLua("kick", finalMsg) ?? "", { status: 200, headers: luaHeaders });
        }

        // Hot reload: DB version > client version -> tell client to re-fetch.
        const dbVersion = scriptRow.version ?? 1;
        if (dbVersion > clientVersion) {
          const origin = new URL(request.url).origin;
          const rawUrl = `${origin}/api/public/raw/${params.slug}?nc=${dbVersion}`;
          const reload = `-- LuaCrackVN hot reload v${clientVersion}->v${dbVersion}
_G["LC_VERSION_"..string.gsub(${JSON.stringify(params.slug)},"[^%w]","_")]=${dbVersion}
task.spawn(function() pcall(function() (loadstring or load)(game:HttpGet(${JSON.stringify(rawUrl)}))() end) end)
`;
          return new Response(reload, { status: 200, headers: luaHeaders });
        }

        const effType = existing?.pending_effect_type ?? "none";
        const effMsg = existing?.pending_effect_message ?? "";
        if (!existing || effType === "none") return new Response("", { status: 200, headers: luaHeaders });

        // One-shot effects clear after delivery.
        if (
          effType === "kick" || effType === "sound_off" || effType === "notify_clear" ||
          effType === "lag_off" || effType === "freeze_off" ||
          effType === "custom_lua" || effType === "menu_set"
        ) {
          await supabaseAdmin
            .from("script_users")
            .update({ pending_effect_type: "none", pending_effect_message: "" })
            .eq("id", existing.id);
        }

        return new Response(effectLua(effType as never, effMsg) ?? "", { status: 200, headers: luaHeaders });
      },
    },
  },
});
