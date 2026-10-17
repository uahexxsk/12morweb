import { createFileRoute, Link } from "@tanstack/react-router";
import { useServerFn } from "@tanstack/react-start";
import { useEffect, useRef, useState } from "react";
import type { Session } from "@supabase/supabase-js";
import {
  Check, CloudUpload, Copy, ExternalLink, FileCode2, FolderPlus,
  LoaderCircle, LogOut, ShieldCheck, Trash2, Upload, UserRound,
} from "lucide-react";

import { supabase } from "@/integrations/supabase/client";
import {
  createProjectWithScript,
  deleteMyProjectScript,
  listMyProjects,
} from "@/lib/projects.functions";

export const Route = createFileRoute("/")({
  head: () => ({
    meta: [
      { title: "LuaCrackVN — Projects & Loaders" },
      { name: "description", content: "Đăng nhập Google hoặc Discord, tạo project Lua, obfuscate script và phát hành loadstring riêng." },
      { property: "og:title", content: "LuaCrackVN — Projects & Loaders" },
      { property: "og:description", content: "Tạo project, quản lý script và loader riêng của bạn." },
    ],
  }),
  component: Home,
});

type ObfuscationProfile = "random" | "lite" | "balanced" | "strong" | "max" | "ultra";
type Project = {
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

function makeLoadstring(origin: string, version: "v1" | "v4", slug: string) {
  const url = `${origin}/files/${version}/loaders/${slug}.lua`;
  return `loadstring(game:HttpGet(${JSON.stringify(url)}))()`;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function Home() {
  const createProject = useServerFn(createProjectWithScript);
  const getProjects = useServerFn(listMyProjects);
  const deleteScript = useServerFn(deleteMyProjectScript);
  const [session, setSession] = useState<Session | null>(null);
  const [authReady, setAuthReady] = useState(false);
  const [authBusy, setAuthBusy] = useState(false);
  const [projectName, setProjectName] = useState("");
  const [code, setCode] = useState("");
  const [profile, setProfile] = useState<ObfuscationProfile>("random");
  const [projects, setProjects] = useState<Project[]>([]);
  const [busy, setBusy] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [createdLoadstrings, setCreatedLoadstrings] = useState<{ v1: string; v4: string } | null>(null);
  const [selectedFile, setSelectedFile] = useState("");
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let active = true;
    void supabase.auth.getSession().then(({ data, error: sessionError }) => {
      if (!active) return;
      if (sessionError) setError(sessionError.message);
      setSession(data.session);
      setAuthReady(true);
    }).catch((reason: unknown) => {
      if (active) {
        setError(reason instanceof Error ? reason.message : "Không đọc được phiên đăng nhập.");
        setAuthReady(true);
      }
    });
    const { data: { subscription } } = supabase.auth.onAuthStateChange((_event, nextSession) => {
      if (!active) return;
      setSession(nextSession);
      setAuthReady(true);
      if (!nextSession) setProjects([]);
    });
    return () => {
      active = false;
      subscription.unsubscribe();
    };
  }, []);

  useEffect(() => {
    if (!session?.user.id) {
      setProjects([]);
      return;
    }
    let active = true;
    void getProjects().then((result) => {
      if (active) setProjects(result.projects as Project[]);
    }).catch((reason: unknown) => {
      if (active) setError(reason instanceof Error ? reason.message : "Không tải được projects.");
    });
    return () => { active = false; };
  }, [session?.user.id]);

  async function signIn(provider: "google" | "discord") {
    setAuthBusy(true);
    setError(null);
    try {
      const { error: signInError } = await supabase.auth.signInWithOAuth({
        provider,
        options: { redirectTo: window.location.origin },
      });
      if (signInError) throw signInError;
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Đăng nhập OAuth thất bại.");
      setAuthBusy(false);
    }
  }

  async function signOut() {
    setAuthBusy(true);
    try {
      const { error: signOutError } = await supabase.auth.signOut();
      if (signOutError) throw signOutError;
      setSession(null);
      setProjects([]);
      setCreatedLoadstrings(null);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Không đăng xuất được.");
    } finally {
      setAuthBusy(false);
    }
  }

  async function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) {
      setError("File vượt giới hạn 5 MiB.");
      event.target.value = "";
      return;
    }
    try {
      const source = await file.text();
      setCode(source);
      setSelectedFile(file.name);
      if (!projectName.trim()) setProjectName(file.name.replace(/\.(lua|txt)$/i, ""));
      setError(null);
    } catch {
      setError("Không đọc được file. Hãy thử file .lua hoặc .txt khác.");
    }
  }

  async function submitProject() {
    if (!projectName.trim()) {
      setError("Nhập tên project trước.");
      return;
    }
    if (!code.trim()) {
      setError("Upload file hoặc dán source Lua trước.");
      return;
    }
    setBusy(true);
    setError(null);
    setNotice(null);
    setCreatedLoadstrings(null);
    try {
      const result = await createProject({ data: { projectName: projectName.trim(), code, profile } });
      const slug = result.script.slug;
      setCreatedLoadstrings({
        v1: makeLoadstring(window.location.origin, "v1", slug),
        v4: makeLoadstring(window.location.origin, "v4", slug),
      });
      setNotice("Đã tạo project, lưu source lên Pastefy và tạo loader riêng.");
      setCode("");
      setSelectedFile("");
      if (fileRef.current) fileRef.current.value = "";
      const refreshed = await getProjects();
      setProjects(refreshed.projects as Project[]);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Không tạo được project.");
    } finally {
      setBusy(false);
    }
  }

  async function copyText(value: string) {
    try {
      await navigator.clipboard.writeText(value);
      setNotice("Đã copy vào clipboard.");
    } catch {
      setError("Không copy tự động được. Hãy chọn và copy nội dung bằng tay.");
    }
  }

  async function removeScript(scriptId: string) {
    if (!window.confirm("Xoá script này? Loader sẽ bị vô hiệu hoá và paste tương ứng trên Pastefy cũng sẽ bị xoá.")) return;
    setDeletingId(scriptId);
    setError(null);
    setNotice(null);
    try {
      await deleteScript({ data: { scriptId } });
      setProjects((current) => current
        .map((project) => ({ ...project, scripts: project.scripts.filter((script) => script.id !== scriptId) }))
        .filter((project) => project.scripts.length > 0));
      setCreatedLoadstrings(null);
      setNotice("Đã xoá script. Loader không còn phát nội dung và Pastefy đã được yêu cầu xoá.");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Không xoá được script.");
      // Refresh state: the server intentionally disables the loader before calling Pastefy.
      try {
        const refreshed = await getProjects();
        setProjects(refreshed.projects as Project[]);
      } catch { /* keep the current list if the refresh also fails */ }
    } finally {
      setDeletingId(null);
    }
  }

  const signedIn = Boolean(session?.user);

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="sticky top-0 z-20 border-b border-border/60 bg-background/80 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-5 py-4">
          <Link to="/" className="flex items-center gap-3 font-bold tracking-tight">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-accent-neon text-lg font-black text-black">L</span>
            <span className="text-lg">LuaCrack<span className="text-accent-neon">VN</span></span>
          </Link>
          {signedIn ? (
            <div className="flex min-w-0 items-center gap-3">
              <div className="hidden min-w-0 text-right sm:block">
                <p className="max-w-56 truncate text-sm font-semibold">{session?.user.email ?? "Đã đăng nhập"}</p>
                <p className="text-xs text-muted-foreground">Session tự lưu</p>
              </div>
              <button onClick={signOut} disabled={authBusy} className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm hover:border-accent-neon disabled:opacity-50">
                <LogOut className="h-4 w-4" /> Thoát
              </button>
            </div>
          ) : (
            <span className="text-xs text-muted-foreground">Google / Discord OAuth</span>
          )}
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-5 py-10 md:py-14">
        <section className="mb-10 grid gap-6 md:grid-cols-[1.2fr_0.8fr] md:items-end">
          <div>
            <p className="mb-3 text-xs font-semibold uppercase tracking-[0.3em] text-accent-neon">Your own Lua loaders</p>
            <h1 className="text-4xl font-black tracking-tight md:text-6xl">Script của bạn.<br /><span className="text-accent-neon">Loader riêng của bạn.</span></h1>
            <p className="mt-5 max-w-2xl leading-relaxed text-muted-foreground">Đăng nhập, tạo project, tải Lua lên Pastefy, chạy 12hub VM obfuscator và nhận loadstring mang domain của web này. Xoá script sẽ vô hiệu hoá loader và gửi yêu cầu xoá paste trên Pastefy.</p>
          </div>
          <div className="grid grid-cols-3 gap-3 text-sm">
            <Stat icon={<ShieldCheck className="h-4 w-4" />} label="Auth" value="OAuth" />
            <Stat icon={<FileCode2 className="h-4 w-4" />} label="Engine" value="12hub VM" />
            <Stat icon={<CloudUpload className="h-4 w-4" />} label="Loader" value="v1 + v4" />
          </div>
        </section>

        {error && <div role="alert" className="mb-5 rounded-xl border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive">{error}</div>}
        {notice && <div className="mb-5 rounded-xl border border-accent-neon/30 bg-accent-neon/5 px-4 py-3 text-sm text-foreground">{notice}</div>}

        {!authReady ? (
          <div className="rounded-2xl border border-border bg-card p-10 text-center">
            <LoaderCircle className="mx-auto h-7 w-7 animate-spin text-accent-neon" />
            <p className="mt-3 text-sm text-muted-foreground">Đang khôi phục phiên đăng nhập…</p>
          </div>
        ) : !signedIn ? (
          <section className="mx-auto max-w-xl rounded-2xl border border-border bg-card p-7 shadow-2xl shadow-black/20 md:p-9">
            <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-accent-neon/10 text-accent-neon"><UserRound className="h-7 w-7" /></div>
            <h2 className="text-center text-2xl font-bold">Đăng nhập để bắt đầu</h2>
            <p className="mx-auto mb-7 mt-2 max-w-sm text-center text-sm leading-relaxed text-muted-foreground">Phiên đăng nhập được lưu và tự làm mới. Bạn không cần đăng nhập lại mỗi lần mở web, trừ khi đăng xuất hoặc phiên bị thu hồi.</p>
            <div className="grid gap-3 sm:grid-cols-2">
              <button onClick={() => void signIn("google")} disabled={authBusy} className="inline-flex items-center justify-center gap-3 rounded-xl border border-border bg-background px-4 py-3 font-semibold transition hover:border-accent-neon disabled:opacity-50">
                <GoogleMark /> Tiếp tục với Google
              </button>
              <button onClick={() => void signIn("discord")} disabled={authBusy} className="inline-flex items-center justify-center gap-3 rounded-xl border border-border bg-background px-4 py-3 font-semibold transition hover:border-accent-neon disabled:opacity-50">
                <DiscordMark /> Tiếp tục với Discord
              </button>
            </div>
            <p className="mt-5 text-center text-xs text-muted-foreground">Cần bật Google và Discord trong Supabase Authentication → Sign In / Providers.</p>
          </section>
        ) : (
          <div className="grid gap-8 lg:grid-cols-[0.92fr_1.08fr]">
            <section className="rounded-2xl border border-border bg-card p-5 shadow-xl shadow-black/10 md:p-6">
              <div className="mb-5 flex items-center gap-3">
                <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-accent-neon/10 text-accent-neon"><FolderPlus className="h-5 w-5" /></div>
                <div><h2 className="text-xl font-bold">Tạo project mới</h2><p className="text-sm text-muted-foreground">Upload → Pastefy → obfuscate → loader</p></div>
              </div>
              <label className="mb-2 block text-sm font-medium" htmlFor="project-name">Tên project</label>
              <input id="project-name" value={projectName} onChange={(event) => setProjectName(event.target.value)} maxLength={120} placeholder="Ví dụ: My Lua Script" className="mb-4 w-full rounded-lg border border-border bg-background px-3 py-2.5 outline-none transition focus:border-accent-neon" />

              <div className="mb-2 flex items-center justify-between gap-3">
                <label className="text-sm font-medium">Source Lua</label>
                <button type="button" onClick={() => fileRef.current?.click()} className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-xs font-semibold hover:border-accent-neon"><Upload className="h-3.5 w-3.5" /> Chọn file</button>
                <input ref={fileRef} type="file" accept=".lua,.txt,text/plain" hidden onChange={(event) => void handleFileChange(event)} />
              </div>
              {selectedFile && <div className="mb-2 flex items-center gap-2 rounded-md bg-accent-neon/5 px-3 py-2 text-xs text-accent-neon"><Check className="h-3.5 w-3.5" /> {selectedFile}</div>}
              <textarea value={code} onChange={(event) => setCode(event.target.value)} spellCheck={false} placeholder={'-- Dán code Lua vào đây\nprint("Hello LuaCrackVN")'} className="h-64 w-full resize-y rounded-lg border border-border bg-black/70 p-4 font-mono text-sm text-emerald-300 outline-none transition focus:border-accent-neon" />
              <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
                <label className="flex items-center gap-2 text-sm text-muted-foreground">Profile
                  <select value={profile} onChange={(event) => setProfile(event.target.value as ObfuscationProfile)} disabled={busy} className="rounded-lg border border-border bg-background px-3 py-2 text-foreground outline-none focus:border-accent-neon">
                    <option value="random">Random</option><option value="lite">Lite</option><option value="balanced">Balanced</option><option value="strong">Strong</option><option value="max">Max</option><option value="ultra">Ultra</option>
                  </select>
                </label>
                <span className="text-xs text-muted-foreground">{formatBytes(new Blob([code]).size)} / 5 MiB</span>
              </div>
              <button onClick={() => void submitProject()} disabled={busy} className="mt-5 inline-flex w-full items-center justify-center gap-2 rounded-xl bg-accent-neon px-5 py-3 font-bold text-black transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-60">
                {busy ? <><LoaderCircle className="h-4 w-4 animate-spin" /> Đang tải lên và obfuscate…</> : <><CloudUpload className="h-4 w-4" /> Tạo project + loader</>}
              </button>
              <p className="mt-3 text-xs leading-relaxed text-muted-foreground">Tạo project cần cấu hình token Pastefy trên server. Nếu bước tạo loader thất bại, web sẽ cố gắng dọn paste/project vừa tạo.</p>
            </section>

            <section className="space-y-5">
              {createdLoadstrings && (
                <div className="rounded-2xl border border-accent-neon/40 bg-accent-neon/5 p-5">
                  <h3 className="mb-3 text-lg font-bold">Loadstring vừa tạo</h3>
                  <LoaderOutput version="v4" value={createdLoadstrings.v4} onCopy={() => void copyText(createdLoadstrings.v4)} />
                  <LoaderOutput version="v1" value={createdLoadstrings.v1} onCopy={() => void copyText(createdLoadstrings.v1)} />
                </div>
              )}
              <div className="rounded-2xl border border-border bg-card p-5 md:p-6">
                <div className="mb-5 flex items-center justify-between gap-3">
                  <div><h2 className="text-xl font-bold">Projects của bạn</h2><p className="mt-1 text-sm text-muted-foreground">Loader của từng script được quản lý theo project.</p></div>
                  <span className="rounded-full border border-border px-3 py-1 text-xs text-muted-foreground">{projects.length} project</span>
                </div>
                {projects.length === 0 ? (
                  <div className="rounded-xl border border-dashed border-border px-4 py-10 text-center">
                    <FileCode2 className="mx-auto h-8 w-8 text-muted-foreground" />
                    <p className="mt-3 font-semibold">Chưa có project</p>
                    <p className="mt-1 text-sm text-muted-foreground">Tạo project đầu tiên bằng form bên trái.</p>
                  </div>
                ) : (
                  <div className="space-y-4">
                    {projects.map((project) => (
                      <article key={project.id} className="rounded-xl border border-border bg-background/70 p-4">
                        <div className="mb-3 flex items-start justify-between gap-3">
                          <div className="min-w-0"><h3 className="truncate font-bold">{project.name}</h3><p className="mt-1 text-xs text-muted-foreground">Tạo lúc {new Date(project.created_at).toLocaleString()}</p></div>
                          <span className="shrink-0 rounded-md bg-muted px-2 py-1 text-xs text-muted-foreground">{project.scripts.length} script</span>
                        </div>
                        {project.scripts.length === 0 ? <p className="text-sm text-muted-foreground">Project chưa có script.</p> : project.scripts.map((script) => {
                          const v1 = makeLoadstring(window.location.origin, "v1", script.slug);
                          const v4 = makeLoadstring(window.location.origin, "v4", script.slug);
                          const disabled = script.deleted;
                          return (
                            <div key={script.id} className="border-t border-border pt-3 first:border-t-0 first:pt-0">
                              <div className="mb-3 flex items-start justify-between gap-3">
                                <div className="min-w-0"><p className="break-words text-sm font-semibold">{script.name || project.name}</p><p className="mt-1 text-xs text-muted-foreground">{formatBytes(script.original_size)} · {new Date(script.created_at).toLocaleDateString()}</p></div>
                                <button onClick={() => void removeScript(script.id)} disabled={deletingId === script.id} aria-label="Xoá script và Pastefy" title="Xoá script và Pastefy" className="inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-destructive/30 px-2.5 py-2 text-xs text-destructive transition hover:bg-destructive/10 disabled:opacity-50">
                                  {deletingId === script.id ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />} Xoá
                                </button>
                              </div>
                              <LoaderOutput version="v4" value={v4} disabled={disabled} onCopy={() => void copyText(v4)} />
                              <LoaderOutput version="v1" value={v1} disabled={disabled} onCopy={() => void copyText(v1)} />
                              {script.pastefy_url && <a className="mt-2 inline-flex items-center gap-1.5 text-xs text-accent-neon hover:underline" href={script.pastefy_url} target="_blank" rel="noreferrer"><ExternalLink className="h-3.5 w-3.5" /> Mở Pastefy source</a>}
                            </div>
                          );
                        })}
                      </article>
                    ))}
                  </div>
                )}
              </div>
            </section>
          </div>
        )}
      </main>
      <footer className="border-t border-border/60 py-6 text-center text-xs text-muted-foreground">© {new Date().getFullYear()} LuaCrackVN · Authenticated projects · Versioned loaders</footer>
    </div>
  );
}

function LoaderOutput({ version, value, onCopy, disabled = false }: { version: "v1" | "v4"; value: string; onCopy: () => void; disabled?: boolean }) {
  return (
    <div className="mt-3 rounded-lg border border-border bg-background p-3">
      <div className="mb-2 flex items-center justify-between gap-2"><span className="text-[11px] font-bold uppercase tracking-widest text-accent-neon">/files/{version}/loaders</span><button onClick={onCopy} disabled={disabled} className="inline-flex items-center gap-1 text-xs hover:text-accent-neon disabled:opacity-40"><Copy className="h-3.5 w-3.5" /> Copy</button></div>
      <code className="block break-all font-mono text-xs leading-relaxed text-emerald-300">{disabled ? "-- Loader đã bị vô hiệu hoá" : value}</code>
    </div>
  );
}

function Stat({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return <div className="rounded-xl border border-border bg-card/70 p-3"><div className="mb-3 text-accent-neon">{icon}</div><p className="text-xs text-muted-foreground">{label}</p><p className="mt-1 text-sm font-bold">{value}</p></div>;
}

function GoogleMark() {
  return <span aria-hidden="true" className="flex h-5 w-5 items-center justify-center rounded-full bg-white text-sm font-black text-[#4285F4]">G</span>;
}

function DiscordMark() {
  return <span aria-hidden="true" className="flex h-5 w-5 items-center justify-center rounded bg-[#5865F2] text-[10px] font-black text-white">D</span>;
}
