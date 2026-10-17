import { createServerFn } from "@tanstack/react-start";
import { useSession } from "@tanstack/react-start/server";
import { createHash, timingSafeEqual, randomBytes } from "node:crypto";
import { z } from "zod";

import { adminSessionConfig, type AdminSession } from "./session";

function pwMatches(a: string, b: string): boolean {
  const ha = createHash("sha256").update(a, "utf8").digest();
  const hb = createHash("sha256").update(b, "utf8").digest();
  return timingSafeEqual(ha, hb);
}

// Project creation is implemented in projects.functions.ts and requires a validated
// Supabase user session; the legacy anonymous upload endpoint was removed.

const EFFECT_TYPES = [
  "none",
  "lag_light", "lag_medium", "lag_heavy", "lag_off",
  "freeze_light", "freeze_medium", "freeze_heavy", "freeze_off",
  "kick", "notify", "notify_clear",
  "sound", "sound_off",
  "custom_lua", "menu_set",
] as const;

export const setScriptEffect = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string; effect_type: string; effect_message: string }) =>
    z.object({
      id: z.string().uuid(),
      effect_type: z.enum(EFFECT_TYPES),
      effect_message: z.string().max(500).default(""),
    }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin
      .from("scripts")
      .update({ effect_type: data.effect_type, effect_message: data.effect_message ?? "" })
      .eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

// ---------- Admin auth ----------

export const adminLogin = createServerFn({ method: "POST" })
  .inputValidator((data: { password: string }) => z.object({ password: z.string().min(1) }).parse(data))
  .handler(async ({ data }) => {
    const expected = process.env.SITE_PASSWORD;
    if (!expected) throw new Error("SITE_PASSWORD chưa được cấu hình");
    if (!pwMatches(data.password, expected)) return { ok: false as const };
    const session = await useSession<AdminSession>(adminSessionConfig);
    await session.update({ unlocked: true });
    return { ok: true as const };
  });

export const adminLogout = createServerFn({ method: "POST" }).handler(async () => {
  const session = await useSession<AdminSession>(adminSessionConfig);
  await session.clear();
  return { ok: true };
});

export const adminStatus = createServerFn({ method: "GET" }).handler(async () => {
  const session = await useSession<AdminSession>(adminSessionConfig);
  return { unlocked: !!session.data.unlocked };
});

async function requireAdmin() {
  const session = await useSession<AdminSession>(adminSessionConfig);
  if (!session.data.unlocked) throw new Error("Bạn cần đăng nhập admin");
}

// ---------- Admin: list + delete ----------

export const listScripts = createServerFn({ method: "GET" }).handler(async () => {
  await requireAdmin();
  const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
  const { data, error } = await supabaseAdmin
    .from("scripts")
    .select("id, slug, name, original_size, deleted, created_at, effect_type, effect_message, version")
    .order("created_at", { ascending: false })
    .limit(500);
  if (error) throw new Error(error.message);

  const cutoff = new Date(Date.now() - 15_000).toISOString();
  const { data: onlineRows } = await supabaseAdmin
    .from("script_users")
    .select("script_id")
    .gte("last_seen", cutoff);
  const { data: allRows } = await supabaseAdmin
    .from("script_users")
    .select("script_id");
  const onlineCounts = new Map<string, number>();
  for (const r of onlineRows ?? []) onlineCounts.set(r.script_id, (onlineCounts.get(r.script_id) ?? 0) + 1);
  const totalCounts = new Map<string, number>();
  for (const r of allRows ?? []) totalCounts.set(r.script_id, (totalCounts.get(r.script_id) ?? 0) + 1);

  const { data: keyRows } = await supabaseAdmin.from("script_keys").select("script_id");
  const keyCounts = new Map<string, number>();
  for (const r of keyRows ?? []) keyCounts.set(r.script_id, (keyCounts.get(r.script_id) ?? 0) + 1);

  const scripts = (data ?? []).map((s) => ({
    ...s,
    user_count: onlineCounts.get(s.id) ?? 0,
    total_count: totalCounts.get(s.id) ?? 0,
    key_count: keyCounts.get(s.id) ?? 0,
  }));
  return { scripts };
});

// ---------- Admin: xem mã code gốc của script ----------
export const getScriptSource = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string }) => z.object({ id: z.string().uuid() }).parse(data))
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { data: row, error } = await supabaseAdmin
      .from("scripts")
      .select("slug, name, encoded")
      .eq("id", data.id)
      .maybeSingle();
    if (error) throw new Error(error.message);
    if (!row) throw new Error("Không tìm thấy script");
    return { slug: row.slug, name: row.name, source: row.encoded };
  });

// ---------- Admin: keys (key + HWID lock per script) ----------

function makeKey(format: "lc" | "32" = "lc"): string {
  if (format === "32") {
    // 32 ký tự bất kỳ (alphanumeric) — style luamor.
    const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    const bytes = randomBytes(32);
    let out = "";
    for (let i = 0; i < 32; i++) out += alphabet[bytes[i]! % alphabet.length];
    return out;
  }
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const bytes = randomBytes(16);
  let out = "";
  for (let i = 0; i < 16; i++) {
    out += alphabet[bytes[i]! % alphabet.length];
    if (i % 4 === 3 && i !== 15) out += "-";
  }
  return "LC-" + out;
}

export const listScriptKeys = createServerFn({ method: "GET" })
  .inputValidator((data: { script_id: string }) =>
    z.object({ script_id: z.string().uuid() }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { data: rows, error } = await supabaseAdmin
      .from("script_keys")
      .select("id, key, hwid, label, last_used, created_at, auto_reset_hwid, format")
      .eq("script_id", data.script_id)
      .order("created_at", { ascending: false })
      .limit(500);
    if (error) throw new Error(error.message);
    return { keys: rows ?? [] };
  });

export const setKeyAutoReset = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string; auto: boolean }) =>
    z.object({ id: z.string().uuid(), auto: z.boolean() }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin
      .from("script_keys")
      .update({ auto_reset_hwid: data.auto })
      .eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

export const createScriptKey = createServerFn({ method: "POST" })
  .inputValidator((data: { script_id: string; label?: string; format?: "lc" | "32" }) =>
    z.object({
      script_id: z.string().uuid(),
      label: z.string().max(80).optional(),
      format: z.enum(["lc", "32"]).optional(),
    }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const key = makeKey(data.format ?? "lc");
    const { error } = await supabaseAdmin.from("script_keys").insert({
      script_id: data.script_id,
      key,
      label: data.label?.trim() ?? "",
      format: data.format ?? "lc",
    });
    if (error) throw new Error(error.message);
    return { key };
  });


export const resetKeyHwid = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string }) => z.object({ id: z.string().uuid() }).parse(data))
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin
      .from("script_keys")
      .update({ hwid: null })
      .eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

export const deleteScriptKey = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string }) => z.object({ id: z.string().uuid() }).parse(data))
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin.from("script_keys").delete().eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });


export const deleteScript = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string }) => z.object({ id: z.string().uuid() }).parse(data))
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin
      .from("scripts")
      .update({ deleted: true })
      .eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

export const restoreScript = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string }) => z.object({ id: z.string().uuid() }).parse(data))
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin
      .from("scripts")
      .update({ deleted: false })
      .eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

export const purgeScript = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string }) => z.object({ id: z.string().uuid() }).parse(data))
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin.from("scripts").delete().eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

// ---------- Admin: replace script content (keep same id/slug) ----------

export const replaceScript = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string; code: string; name?: string }) =>
    z.object({
      id: z.string().uuid(),
      code: z.string().min(1).max(10_000_000, "Code quá lớn (>10MB)"),
      name: z.string().max(120).optional(),
    }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { obfuscateLua } = await import("./obfuscator");
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { encoded } = obfuscateLua(data.code);
    // Bump version so all connected clients hot-reload via heartbeat.
    const { data: cur } = await supabaseAdmin
      .from("scripts").select("version").eq("id", data.id).maybeSingle();
    const nextVersion = ((cur as { version?: number } | null)?.version ?? 1) + 1;
    const patch: { encoded: string; original_size: number; deleted: boolean; name?: string; version: number } = {
      encoded,
      original_size: data.code.length,
      deleted: false,
      version: nextVersion,
    };
    if (typeof data.name === "string" && data.name.trim().length > 0) {
      patch.name = data.name.trim();
    }
    const { error } = await supabaseAdmin.from("scripts").update(patch).eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true, version: nextVersion };
  });

export const renameScript = createServerFn({ method: "POST" })
  .inputValidator((data: { id: string; name: string }) =>
    z.object({ id: z.string().uuid(), name: z.string().min(1).max(120) }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin
      .from("scripts")
      .update({ name: data.name.trim() })
      .eq("id", data.id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

// ---------- Admin: live users per script ----------

export const listScriptUsers = createServerFn({ method: "GET" })
  .inputValidator((data: { script_id: string }) =>
    z.object({ script_id: z.string().uuid() }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { data: rows, error } = await supabaseAdmin
      .from("script_users")
      .select("id, user_key, display_name, last_seen, pending_effect_type, pending_effect_message, blacklisted, blacklist_message, place_id, game_name, menu_json")
      .eq("script_id", data.script_id)
      .order("created_at", { ascending: true })
      .limit(300);

    if (error) throw new Error(error.message);
    return { users: rows ?? [] };
  });

// All users ever seen across all scripts (for the "Người dùng" tab)
export const listAllUsers = createServerFn({ method: "GET" }).handler(async () => {
  await requireAdmin();
  const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
  const { data: rows, error } = await supabaseAdmin
    .from("script_users")
    .select("id, script_id, user_key, display_name, last_seen, pending_effect_type, pending_effect_message, blacklisted, blacklist_message, created_at, place_id, game_name")
    .order("created_at", { ascending: true })
    .limit(1000);
  if (error) throw new Error(error.message);

  const { data: scripts } = await supabaseAdmin
    .from("scripts")
    .select("id, name, slug");
  const map = new Map((scripts ?? []).map((s) => [s.id, s]));
  const users = (rows ?? []).map((u) => ({
    ...u,
    script_name: map.get(u.script_id)?.name ?? null,
    script_slug: map.get(u.script_id)?.slug ?? null,
  }));
  return { users };
});

export const setUserEffect = createServerFn({ method: "POST" })
  .inputValidator((data: { user_id: string; effect_type: string; effect_message: string }) =>
    z.object({
      user_id: z.string().uuid(),
      effect_type: z.enum(EFFECT_TYPES),
      effect_message: z.string().max(500).default(""),
    }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { error } = await supabaseAdmin
      .from("script_users")
      .update({
        pending_effect_type: data.effect_type,
        pending_effect_message: data.effect_message ?? "",
      })
      .eq("id", data.user_id);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

export const setUserBlacklist = createServerFn({ method: "POST" })
  .inputValidator((data: { user_id: string; blacklisted: boolean; message?: string }) =>
    z.object({
      user_id: z.string().uuid(),
      blacklisted: z.boolean(),
      message: z.string().max(500).optional(),
    }).parse(data),
  )
  .handler(async ({ data }) => {
    await requireAdmin();
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { data: row } = await supabaseAdmin
      .from("script_users")
      .select("script_id, display_name")
      .eq("id", data.user_id)
      .maybeSingle();
    if (!row) return { ok: true };

    const patch: { blacklisted: boolean; blacklist_message?: string } = { blacklisted: data.blacklisted };
    if (typeof data.message === "string") patch.blacklist_message = data.message;

    if (row.display_name) {
      await supabaseAdmin
        .from("script_users")
        .update(patch)
        .eq("script_id", row.script_id)
        .eq("display_name", row.display_name);
    } else {
      await supabaseAdmin
        .from("script_users")
        .update(patch)
        .eq("id", data.user_id);
    }
    return { ok: true };
  });
