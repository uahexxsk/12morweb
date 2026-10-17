using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Hub12.Bytecode_Library.Bytecode;
using Hub12.Bytecode_Library.IR;
using Hub12.Obfuscator;
using Hub12.Obfuscator.Control_Flow;
using Hub12.Obfuscator.Encryption;
using Hub12.Obfuscator.VM_Generation;

namespace Hub12
{
    /// <summary>12hub Lua 5.1 virtual-machine obfuscator.</summary>
    public static class TwelveHubEngine
    {
        // A single cryptographically-seeded stream is shared by all mutation/shuffle passes.
        // Keeping the same Random instance avoids stale static references in opcode classes.
        public static readonly Random Random = new BuildRandom();
        private static readonly object BuildLock = new object();
        private static readonly Encoding LuaEncoding = CreateLuaEncoding();
        public static string LastBuildId { get; private set; } = "unbuilt";

        private static Encoding CreateLuaEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(28591);
        }


        public static string CreateBuildId()
        {
            byte[] id = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(id);
            return BitConverter.ToString(id).Replace("-", "").ToLowerInvariant();
        }

        private static string CreateFooterMarker()
        {
            // Draw every marker independently. The supplied punctuation/letter/number
            // motif is used only as an alphabet, never copied as a fixed suffix.
            const string alphabet = "*#\"'jdhdbjsisvniwiqhokcnu927/2863/981/9#7*/cho97/92980/2/€£$¢abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!?;:,.[]{}\\";
            int length = Random.Next(36, 73);
            StringBuilder marker = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                marker.Append(alphabet[Random.Next(0, alphabet.Length)]);
            return marker.ToString();
        }

        private static string ToolPath(string envName, string fallback)
        {
            string configured = Environment.GetEnvironmentVariable(envName);
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        }

        private static string ResolveLua51Compiler()
        {
            string configured = Environment.GetEnvironmentVariable("HUB12_LUAC")?.Trim();
            string[] candidates = { configured, "luac5.1", "luac-5.1", "luac51", "luac" };

            foreach (string candidate in candidates
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.Ordinal))
            {
                try
                {
                    int exitCode = RunTool(candidate, "-v", Environment.CurrentDirectory, out string version);
                    if (exitCode == 0 && version.IndexOf("Lua 5.1", StringComparison.OrdinalIgnoreCase) >= 0)
                        return candidate;
                }
                catch
                {
                    // Try the next candidate; an unavailable or incompatible compiler is not fatal yet.
                }
            }

            throw new InvalidOperationException(
                "No Lua 5.1 compiler found. Install Termux package 'lua51' and set HUB12_LUAC=luac5.1. " +
                "The generic 'luac' command may point to Lua 5.4 and is not compatible with this engine.");
        }

        private static void EnsureMinifierLuaPath(string minifier)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(minifier));
            string modulePattern = Path.Combine(directory, "?.lua");
            string initPattern = Path.Combine(directory, "?", "init.lua");
            string current = Environment.GetEnvironmentVariable("LUA_PATH") ?? string.Empty;
            List<string> additions = new List<string>();
            if (current.IndexOf(modulePattern, StringComparison.OrdinalIgnoreCase) < 0)
                additions.Add(modulePattern);
            if (current.IndexOf(initPattern, StringComparison.OrdinalIgnoreCase) < 0)
                additions.Add(initPattern);
            if (additions.Count == 0) return;
            if (!string.IsNullOrWhiteSpace(current)) additions.Add(current);
            Environment.SetEnvironmentVariable("LUA_PATH", string.Join(";", additions));
        }

        private static string ResolveMinifier()
        {
            string relative = Path.Combine("Lua", "Minifier", "luasrcdiet.lua");
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, relative),
                Path.Combine(Environment.CurrentDirectory, relative),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", relative))
            };

            string found = candidates.FirstOrDefault(File.Exists);
            if (found == null)
                throw new FileNotFoundException("12hub could not find Lua/Minifier/luasrcdiet.lua beside the application.");
            return Path.GetFullPath(found);
        }

        private static int RunTool(string fileName, string arguments, string workingDirectory, out string output)
        {
            StringBuilder buffer = new StringBuilder();
            object bufferLock = new object();
            using (Process proc = new Process())
            {
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                // LuaSrcDiet uses this seed for its variable-name alphabet shuffle.
                // A cryptographic per-process value prevents same-second repeat seeds.
                proc.StartInfo.Environment["HUB12_RANDOM_SEED"] =
                    RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
                proc.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null) lock (bufferLock) buffer.AppendLine(args.Data);
                };
                proc.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null) lock (bufferLock) buffer.AppendLine(args.Data);
                };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                // WaitForExit a second time ensures asynchronous output callbacks have drained.
                proc.WaitForExit();
                output = buffer.ToString().Trim();
                return proc.ExitCode;
            }
        }

        public static bool Obfuscate(string path, string input, ObfuscationSettings settings, out string error)
        {
            lock (BuildLock)
                return ObfuscateCore(path, input, settings, out error);
        }

        private static bool ObfuscateCore(string path, string input, ObfuscationSettings settings, out string error)
        {
            error = string.Empty;
            try
            {
                if (Random is BuildRandom buildRandom)
                    buildRandom.Reseed();
                LastBuildId = CreateBuildId();

                path = Path.GetFullPath(path);
                input = Path.GetFullPath(input);
                Directory.CreateDirectory(path);
                if (!File.Exists(input))
                    throw new FileNotFoundException("Input Lua file was not found.", input);

                string luac = ResolveLua51Compiler();
                Console.WriteLine("[12hub] Lua 5.1 compiler: " + luac);
                string luajit = ToolPath("HUB12_LUAJIT", "luajit");
                string minifier = ResolveMinifier();
                // Make LuaSrcDiet's require("fs")/require("init") work for direct CLI
                // invocations as well as Discord jobs that already set LUA_PATH.
                EnsureMinifierLuaPath(minifier);
                string bytecode = Path.Combine(path, "luac.out");
                string stripped = Path.Combine(path, "t0.lua");
                string encrypted = Path.Combine(path, "t1.lua");
                string vmSource = Path.Combine(path, "t2.lua");
                string minified = Path.Combine(path, "t3.lua");
                string outputPath = Path.Combine(path, "out.lua");
                string toolOutput;

                Console.WriteLine("[12hub] Checking Lua syntax...");
                if (File.Exists(bytecode)) File.Delete(bytecode);
                int exitCode = RunTool(luac, "-o " + Quote(bytecode) + " " + Quote(input), path, out toolOutput);
                if (exitCode != 0 || !File.Exists(bytecode))
                    throw new InvalidOperationException("Source validation failed. " + toolOutput);
                File.Delete(bytecode);

                Console.WriteLine("[12hub] Stripping comments and whitespace...");
                if (File.Exists(stripped)) File.Delete(stripped);
                string stripArgs = Quote(minifier) +
                    " --noopt-whitespace --noopt-emptylines --noopt-numbers --noopt-locals --noopt-strings --opt-comments " +
                    Quote(input) + " -o " + Quote(stripped);
                exitCode = RunTool(luajit, stripArgs, path, out toolOutput);
                if (exitCode != 0 || !File.Exists(stripped))
                    throw new InvalidOperationException("Lua minifier (strip pass) failed. " + toolOutput);

                Console.WriteLine("[12hub] Encrypting configured string literals...");
                File.WriteAllText(encrypted,
                    new ConstantEncryption(settings, File.ReadAllText(stripped, LuaEncoding)).EncryptStrings(), LuaEncoding);

                if (File.Exists(bytecode)) File.Delete(bytecode);
                exitCode = RunTool(luac, "-o " + Quote(bytecode) + " " + Quote(encrypted), path, out toolOutput);
                if (exitCode != 0 || !File.Exists(bytecode))
                    throw new InvalidOperationException("Could not compile the transformed Lua source. " + toolOutput);

                Console.WriteLine("[12hub] Building the randomized virtual machine...");
                Deserializer deserializer = new Deserializer(File.ReadAllBytes(bytecode));
                Chunk chunk = deserializer.DecodeFile();
                // Validate the decoded IR before and after transformations so broken branch
                // references fail the build instead of producing a corrupt VM payload.
                ChunkValidator.ValidateTree(chunk, "decoded IR");
                if (settings.ControlFlow)
                {
                    CFContext controlFlow = new CFContext(chunk);
                    controlFlow.DoChunks();
                }
                ChunkValidator.ValidateTree(chunk, "transformed IR");

                ObfuscationContext context = new ObfuscationContext(chunk);
                string vm = new Generator(context).GenerateVM(settings);
                // Randomize pass order per build; both passes preserve quoted strings/comments.
                if (Random.Next(0, 2) == 0)
                {
                    // names -> numeric diversification
                    vm = VMNameRandomizer.Apply(vm);
                    vm = LuaNumberRandomizer.Apply(vm);
                }
                else
                {
                    // numeric diversification -> names
                    vm = LuaNumberRandomizer.Apply(vm);
                    vm = VMNameRandomizer.Apply(vm);
                }
                // Keep Lua keywords separated from the next identifier after all randomizers.
                // Some opcode templates end without a separator, producing invalid tokens like elseStk.
                vm = System.Text.RegularExpressions.Regex.Replace(
                    vm, @"\b(else|then|do|return|local|and|or|not)(?=[A-Za-z_])", "$1 ");
                File.WriteAllText(vmSource, vm, LuaEncoding);

                Console.WriteLine("[12hub] Minifying generated VM...");
                if (File.Exists(minified)) File.Delete(minified);
                // Keep numeric disguises emitted by LuaNumberRandomizer intact. --opt-numbers
                // would fold expressions such as ((0+17)-17) back to a fixed literal.
                string minifyArgs = Quote(minifier) +
                    " --maximum --opt-entropy --opt-emptylines --opt-eols --noopt-numbers --opt-whitespace --opt-locals --noopt-strings " +
                    Quote(vmSource) + " -o " + Quote(minified);
                exitCode = RunTool(luajit, minifyArgs, path, out toolOutput);
                if (exitCode != 0 || !File.Exists(minified))
                {
                    // LuaSrcDiet's parser is stricter than the target Lua 5.1 compiler
                    // for some generated dispatch expressions. Preserve the VM and fall
                    // back to the unminified source; the final luac validation below is
                    // authoritative and will still reject genuinely invalid Lua.
                    string debugVm = Path.Combine(path, "12hub_vm_debug.lua");
                    try
                    {
                        File.Copy(vmSource, debugVm, true);
                        Console.WriteLine("[WARN] VM minifier rejected generated source; saved debug copy: " + debugVm);
                        Console.WriteLine("[WARN] Minifier details: " + toolOutput);
                        File.Copy(vmSource, minified, true);
                        Console.WriteLine("[12hub] Continuing with unminified VM; validating with Lua 5.1 compiler...");
                    }
                    catch (Exception debugEx)
                    {
                        throw new InvalidOperationException("Lua minifier failed and fallback copy failed: " + debugEx.Message + ". " + toolOutput);
                    }
                }

                Console.WriteLine("[12hub] Applying watermark and build metadata...");
                string vmText = File.ReadAllText(minified, LuaEncoding);
                string activeProfile = Environment.GetEnvironmentVariable("HUB12_ACTIVE_PROFILE") ?? "random";
                string banner = "-- this is file projects by [.gg/12hub]" + Environment.NewLine +
                                "-- build: 12hub_" + LastBuildId + Environment.NewLine +
                                "-- profile: " + activeProfile + Environment.NewLine;
                string marker = CreateFooterMarker();
                string output = banner + vmText.Replace("\r", string.Empty).Replace("\n", " ") +
                                Environment.NewLine + "--" + marker;
                // The marker contains Unicode punctuation. The VM payload is ASCII-safe
                // because it is compressed/base36 encoded, so write final source as UTF-8.
                File.WriteAllText(outputPath, output, new UTF8Encoding(false));

                Console.WriteLine("[12hub] Validating generated output...");
                exitCode = RunTool(luac, "-p " + Quote(outputPath), path, out toolOutput);
                if (exitCode != 0)
                {
                    File.Delete(outputPath);
                    throw new InvalidOperationException("Generated output did not pass Lua syntax validation. " + toolOutput);
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Console.Error.WriteLine("[12hub] ERROR: " + ex.Message);
                return false;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
