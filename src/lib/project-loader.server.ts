import { isBlockedFetcher } from "@/lib/fetcher-guard";

/** Shared implementation for the public versioned loader URLs. */
export async function serveProjectLoader(request: Request, slug: string): Promise<Response> {
  const luaHeaders = {
    "content-type": "text/plain; charset=utf-8",
    "cache-control": "no-store, max-age=0",
    "x-robots-tag": "noindex, nofollow",
    "x-content-type-options": "nosniff",
  };

  if (isBlockedFetcher(request)) {
    return new Response("-- LuaCrackVN loader endpoint.\nerror(\"Use this URL through a compatible Lua HttpGet loader.\")\n", {
      status: 403,
      headers: { ...luaHeaders, "content-type": "text/plain; charset=utf-8" },
    });
  }

  const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
  const { data, error } = await supabaseAdmin.from("scripts")
    .select("id, encoded, deleted")
    .eq("slug", slug)
    .maybeSingle();
  if (error) {
    console.error("[project-loader] database query failed", error.message);
    return new Response("-- Loader temporarily unavailable.\nerror(\"Loader unavailable.\")\n", {
      status: 503,
      headers: luaHeaders,
    });
  }
  if (!data || data.deleted) {
    return new Response("-- This script was deleted or is no longer available.\nerror(\"This loadstring has been deleted.\")\n", {
      status: 404,
      headers: luaHeaders,
    });
  }

  const url = new URL(request.url);
  const key = (url.searchParams.get("key") ?? "").trim().slice(0, 128);
  const hwid = (url.searchParams.get("hwid") ?? "").trim().slice(0, 200);
  if (key) {
    const { data: keyRow } = await supabaseAdmin.from("script_keys")
      .select("id, hwid, auto_reset_hwid")
      .eq("script_id", data.id)
      .eq("key", key)
      .maybeSingle();
    if (!keyRow) {
      return new Response("-- Invalid key.\nerror(\"KEY khong hop le hoac da bi xoa.\")\n", { status: 200, headers: luaHeaders });
    }
    const now = new Date().toISOString();
    if (keyRow.auto_reset_hwid) {
      await supabaseAdmin.from("script_keys").update({ hwid: hwid || null, last_used: now }).eq("id", keyRow.id);
    } else if (!keyRow.hwid) {
      await supabaseAdmin.from("script_keys").update({ hwid: hwid || "unknown", last_used: now }).eq("id", keyRow.id);
    } else if (hwid && keyRow.hwid !== hwid) {
      return new Response("-- HWID mismatch.\nerror(\"KEY nay da khoa vao may khac. Nho admin reset HWID.\")\n", { status: 200, headers: luaHeaders });
    } else {
      await supabaseAdmin.from("script_keys").update({ last_used: now }).eq("id", keyRow.id);
    }
  }

  const xorKey = process.env.OBF_XOR_KEY;
  if (!xorKey || xorKey.length < 32) {
    return new Response("-- Server configuration error.\nerror(\"OBF_XOR_KEY must contain at least 32 characters.\")\n", {
      status: 503,
      headers: luaHeaders,
    });
  }

  const { buildEncodedScript } = await import("@/lib/obfuscator");
  const wrapped = buildEncodedScript(data.encoded, xorKey);
  return new Response(wrapped, { status: 200, headers: luaHeaders });
}
