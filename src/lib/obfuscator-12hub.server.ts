import { spawn } from "node:child_process";
import { mkdtemp, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

export const HUB12_PROFILES = ["random", "lite", "balanced", "strong", "max", "ultra"] as const;
export type Hub12Profile = (typeof HUB12_PROFILES)[number];

const MAX_SOURCE_BYTES = 5 * 1024 * 1024;
const MAX_OUTPUT_BYTES = 10 * 1024 * 1024;
const requestedTimeout = Number(process.env.HUB12_BUILD_TIMEOUT_MS || 240_000);
const BUILD_TIMEOUT_MS = Number.isFinite(requestedTimeout)
  ? Math.max(15_000, Math.min(600_000, requestedTimeout))
  : 240_000;
const requestedConcurrency = Number(process.env.HUB12_MAX_CONCURRENT_JOBS || 2);
const MAX_CONCURRENT_BUILDS = Number.isFinite(requestedConcurrency)
  ? Math.max(1, Math.min(4, Math.floor(requestedConcurrency)))
  : 2;
let activeBuilds = 0;

function tail(text: string, max = 1_600): string {
  const value = (text || "").trim();
  return value.length > max ? value.slice(-max) : value;
}

function runProcess(command: string, args: string[], cwd: string, timeoutMs: number): Promise<void> {
  return new Promise((resolvePromise, reject) => {
    const child = spawn(command, args, {
      cwd,
      env: {
        ...process.env,
        HUB12_LUAC: process.env.HUB12_LUAC || "luac5.1",
        HUB12_LUAJIT: process.env.HUB12_LUAJIT || "luajit",
      },
      stdio: ["ignore", "pipe", "pipe"],
      shell: false,
    });

    let stdout = "";
    let stderr = "";
    let settled = false;
    const collect = (target: "stdout" | "stderr", chunk: Buffer | string) => {
      const value = chunk.toString();
      if (target === "stdout") stdout = (stdout + value).slice(-12_000);
      else stderr = (stderr + value).slice(-12_000);
    };
    child.stdout.on("data", (chunk: Buffer) => collect("stdout", chunk));
    child.stderr.on("data", (chunk: Buffer) => collect("stderr", chunk));

    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      child.kill("SIGKILL");
      reject(new Error(`Engine quá thời gian ${Math.round(timeoutMs / 1000)} giây. Thử file nhỏ hơn hoặc profile nhẹ hơn.`));
    }, timeoutMs);

    child.once("error", (error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      reject(new Error(`Không khởi chạy được 12hub engine: ${error.message}`));
    });
    child.once("close", (code) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      if (code !== 0) {
        reject(new Error(tail(stderr || stdout || `Engine kết thúc với mã ${String(code)}.`)));
        return;
      }
      resolvePromise();
    });
  });
}

/** Runs the original 12hub Lua 5.1 VM pipeline in a bounded temporary directory. */
export async function obfuscateWith12Hub(source: string, requestedProfile: Hub12Profile = "random"): Promise<string> {
  if (typeof source !== "string" || !source.trim()) throw new Error("Code Lua không được để trống.");
  if (Buffer.byteLength(source, "utf8") > MAX_SOURCE_BYTES) throw new Error("File quá lớn (giới hạn 5 MiB).");
  const profile: Hub12Profile = HUB12_PROFILES.includes(requestedProfile) ? requestedProfile : "random";
  if (activeBuilds >= MAX_CONCURRENT_BUILDS) {
    throw new Error("Máy chủ đang xử lý đủ số job obfuscate. Vui lòng thử lại sau.");
  }
  activeBuilds += 1;
  let temp = "";
  try {
    const cliDll = process.env.HUB12_CLI_DLL || resolve(process.cwd(), "engine-publish", "12hub.dll");
    try {
      await stat(cliDll);
    } catch {
      throw new Error("Không tìm thấy 12hub.dll. Trên Railway, deploy bằng Dockerfile đi kèm để build engine .NET.");
    }

    temp = await mkdtemp(join(tmpdir(), "luacrackvn-12hub-"));
    const input = join(temp, "input.lua");
    const output = join(temp, "output.lua");
    await writeFile(input, source, { encoding: "utf8", flag: "wx" });
    await runProcess(
      process.env.DOTNET_CMD || "dotnet",
      [cliDll, input, "--profile", profile, "--output", output],
      process.cwd(),
      BUILD_TIMEOUT_MS,
    );

    const result = await readFile(output, "utf8");
    if (!result.trim()) throw new Error("Engine trả về file rỗng.");
    if (Buffer.byteLength(result, "utf8") > MAX_OUTPUT_BYTES) throw new Error("Output vượt giới hạn 10 MiB.");
    if (!result.startsWith("-- this is file projects by [.gg/12hub]")) {
      throw new Error("Không xác thực được output của 12hub engine.");
    }
    return result;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`Obfuscate thất bại. Engine yêu cầu Lua 5.1 chuẩn; cú pháp Luau riêng có thể không tương thích. ${tail(message)}`);
  } finally {
    if (temp) await rm(temp, { recursive: true, force: true }).catch(() => undefined);
    activeBuilds -= 1;
  }
}
