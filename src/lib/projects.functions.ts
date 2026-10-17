import { createServerFn } from "@tanstack/react-start";
import { randomBytes } from "node:crypto";
import { z } from "zod";
import { requireSupabaseAuth } from "@/integrations/supabase/auth-middleware";

type ProjectSummary = {
  id: string;
  name: string;
  created_at: string;
  scripts: Array<{
    id: string;
    slug: string;
    name: string | null;
    original_size: number;
    created_at: string;
    pastefy_url: string | null;
    deleted: boolean;
  }>;
};

type PastefyResponse = {
  paste?: {
    id?: string;
    raw_url?: string;
    url?: string;
    visibility?: string;
  };
  message?: string;
  error?: string;
};

function makeSlug(): string {
  return randomBytes(12).toString("base64url");
}

function safeApiMessage(body: PastefyResponse | null): string {
  const message = body?.message ?? body?.error;
  return typeof message === "string" && message.length < 180 ? ` (${message})` : "";
}

async function createPastefyPaste(title: string, code: string): Promise<{
  id: string;
  url: string;
}> {
  const token = process.env.PASTEFY_API_TOKEN;
  if (!token) {
    throw new Error("Thiếu PASTEFY_API_TOKEN trên Railway. Thêm token từ Pastefy API Keys trước khi tạo project.");
  }

  const response = await fetch("https://pastefy.app/api/v2/paste", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
      Accept: "application/json",
    },
    body: JSON.stringify({
      title: title.slice(0, 120),
      content: code,
      encrypted: false,
      visibility: "UNLISTED",
      tags: ["LuaCrackVN"],
    }),
    signal: AbortSignal.timeout(20_000),
  });

  const payload = await response.json().catch(() => null) as PastefyResponse | null;
  if (!response.ok || !payload?.paste?.id) {
    throw new Error(`Pastefy không tạo được paste (HTTP ${response.status})${safeApiMessage(payload)}`);
  }
  const id = payload.paste.id;
  const url = payload.paste.url || `https://pastefy.app/${encodeURIComponent(id)}`;
  return { id, url };
}

async function fetchPastefySource(pasteId: string): Promise<string> {
  const token = process.env.PASTEFY_API_TOKEN;
  if (!token) throw new Error("Thiếu PASTEFY_API_TOKEN khi đọc source Pastefy.");
  const url = `https://pastefy.app/api/v2/paste/${encodeURIComponent(pasteId)}/raw`;
  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${token}`, Accept: "text/plain" },
    redirect: "error",
    signal: AbortSignal.timeout(20_000),
  });
  if (!response.ok) throw new Error(`Không đọc được raw paste từ Pastefy (HTTP ${response.status}).`);
  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("text/html")) throw new Error("Pastefy trả về trang HTML thay vì source Lua.");
  const source = await response.text();
  if (!source.trim()) throw new Error("Pastefy paste đang rỗng.");
  if (Buffer.byteLength(source, "utf8") > 5_242_880) throw new Error("Source Pastefy vượt giới hạn 5 MiB.");
  return source;
}

async function deletePastefyPaste(id: string | null): Promise<void> {
  if (!id) return;
  const token = process.env.PASTEFY_API_TOKEN;
  if (!token) {
    throw new Error("Không thể xoá paste trên Pastefy vì thiếu PASTEFY_API_TOKEN. Hãy cấu hình token rồi thử lại.");
  }
  const response = await fetch(`https://pastefy.app/api/v2/paste/${encodeURIComponent(id)}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
    signal: AbortSignal.timeout(15_000),
  });
  // 404 means it was already deleted, which satisfies the deletion goal.
  if (!response.ok && response.status !== 404) {
    throw new Error(`Pastefy từ chối xoá paste (HTTP ${response.status}); loader vẫn bị vô hiệu hoá để tránh lộ script.`);
  }
}

export const createProjectWithScript = createServerFn({ method: "POST" })
  .middleware([requireSupabaseAuth])
  .inputValidator((input: { projectName: string; code: string; profile?: string }) =>
    z.object({
      projectName: z.string().trim().min(1, "Nhập tên project").max(120),
      code: z.string().min(1, "Code không được rỗng").max(5_242_880, "Code quá lớn (giới hạn 5 MiB)"),
      profile: z.enum(["random", "lite", "balanced", "strong", "max", "ultra"]).default("random"),
    }).parse(input),
  )
  .handler(async ({ data, context }) => {
    const userId = context.userId;
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { obfuscateWith12Hub } = await import("@/lib/obfuscator-12hub.server");

    const projectResult = await supabaseAdmin.from("projects")
      .insert({ owner_id: userId, name: data.projectName.trim() })
      .select("id, name, created_at")
      .single();
    if (projectResult.error || !projectResult.data) {
      throw new Error(projectResult.error?.message ?? "Không tạo được project.");
    }

    let pastefyId: string | null = null;
    try {
      const paste = await createPastefyPaste(data.projectName, data.code);
      pastefyId = paste.id;
      const sourceFromPaste = await fetchPastefySource(paste.id);
      const encoded = await obfuscateWith12Hub(
        sourceFromPaste,
        data.profile as "random" | "lite" | "balanced" | "strong" | "max" | "ultra",
      );
      const slug = makeSlug();
      const scriptResult = await supabaseAdmin.from("scripts").insert({
        slug,
        name: data.projectName.trim(),
        encoded,
        original_size: Buffer.byteLength(sourceFromPaste, "utf8"),
        owner_id: userId,
        project_id: projectResult.data.id,
        pastefy_id: paste.id,
        pastefy_url: paste.url,
      }).select("id, slug, name, original_size, created_at, pastefy_url, deleted").single();
      if (scriptResult.error || !scriptResult.data) {
        throw new Error(scriptResult.error?.message ?? "Không lưu được script.");
      }
      return {
        project: { ...projectResult.data },
        script: scriptResult.data,
      };
    } catch (error) {
      // Roll back external and local records so a failed build leaves no orphan paste/project.
      if (pastefyId) {
        try { await deletePastefyPaste(pastefyId); } catch (cleanupError) {
          console.error("[Pastefy cleanup after failed create]", cleanupError);
        }
      }
      await supabaseAdmin.from("projects").delete().eq("id", projectResult.data.id).eq("owner_id", userId);
      throw error;
    }
  });

export const listMyProjects = createServerFn({ method: "GET" })
  .middleware([requireSupabaseAuth])
  .handler(async ({ context }): Promise<{ projects: ProjectSummary[] }> => {
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { data: projectRows, error: projectError } = await supabaseAdmin.from("projects")
      .select("id, name, created_at")
      .eq("owner_id", context.userId)
      .order("created_at", { ascending: false })
      .limit(200);
    if (projectError) throw new Error(projectError.message);
    if (!projectRows?.length) return { projects: [] };

    const projectIds = projectRows.map((project) => project.id);
    const { data: scriptRows, error: scriptsError } = await supabaseAdmin.from("scripts")
      .select("id, slug, name, original_size, created_at, pastefy_url, deleted, project_id")
      .eq("owner_id", context.userId)
      .in("project_id", projectIds)
      .order("created_at", { ascending: false });
    if (scriptsError) throw new Error(scriptsError.message);

    const scriptMap = new Map<string, ProjectSummary["scripts"]>();
    for (const row of scriptRows ?? []) {
      if (!row.project_id) continue;
      const list = scriptMap.get(row.project_id) ?? [];
      list.push({
        id: row.id,
        slug: row.slug,
        name: row.name,
        original_size: row.original_size,
        created_at: row.created_at,
        pastefy_url: row.pastefy_url,
        deleted: row.deleted,
      });
      scriptMap.set(row.project_id, list);
    }
    return {
      projects: projectRows.map((project) => ({ ...project, scripts: scriptMap.get(project.id) ?? [] })),
    };
  });

export const deleteMyProjectScript = createServerFn({ method: "POST" })
  .middleware([requireSupabaseAuth])
  .inputValidator((input: { scriptId: string }) => z.object({ scriptId: z.string().uuid() }).parse(input))
  .handler(async ({ data, context }) => {
    const { supabaseAdmin } = await import("@/integrations/supabase/client.server");
    const { data: script, error } = await supabaseAdmin.from("scripts")
      .select("id, project_id, pastefy_id, deleted")
      .eq("id", data.scriptId)
      .eq("owner_id", context.userId)
      .maybeSingle();
    if (error) throw new Error(error.message);
    if (!script) throw new Error("Không tìm thấy script hoặc bạn không có quyền xoá.");

    // Disable the loader first. If Pastefy is temporarily unavailable, keep the loader disabled
    // so the Pastefy source is not exposed, and let the user retry the delete operation.
    const disabled = await supabaseAdmin.from("scripts").update({ deleted: true })
      .eq("id", script.id).eq("owner_id", context.userId);
    if (disabled.error) throw new Error(disabled.error.message);
    await deletePastefyPaste(script.pastefy_id);

    const deleted = await supabaseAdmin.from("scripts").delete()
      .eq("id", script.id).eq("owner_id", context.userId);
    if (deleted.error) throw new Error(`Pastefy đã xoá và loader đã vô hiệu hoá, nhưng chưa xoá được bản ghi: ${deleted.error.message}`);

    if (script.project_id) {
      const { data: remaining, error: remainingError } = await supabaseAdmin.from("scripts")
        .select("id").eq("project_id", script.project_id).limit(1);
      if (!remainingError && (!remaining || remaining.length === 0)) {
        await supabaseAdmin.from("projects").delete()
          .eq("id", script.project_id).eq("owner_id", context.userId);
      }
    }
    return { ok: true };
  });
