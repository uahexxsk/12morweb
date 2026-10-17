using System;
using System.IO;
using System.Security.Cryptography;
using Hub12;
using Hub12.Obfuscator;

namespace Hub12.CLI
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || Array.Exists(args, a => a == "--help" || a == "-h"))
            {
                PrintUsage();
                return args.Length == 0 ? 2 : 0;
            }

            string input = null;
            string output = null;
            string requestedProfile = "random";
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o":
                    case "--output":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Missing path after " + args[i]);
                            return 2;
                        }
                        output = args[++i];
                        break;
                    case "--profile":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Missing value after --profile");
                            return 2;
                        }
                        requestedProfile = args[++i].Trim().ToLowerInvariant();
                        break;
                    default:
                        if (args[i].StartsWith("-", StringComparison.Ordinal))
                        {
                            Console.Error.WriteLine("Unknown option: " + args[i]);
                            PrintUsage();
                            return 2;
                        }
                        if (input == null) input = args[i];
                        else
                        {
                            Console.Error.WriteLine("Unexpected argument: " + args[i]);
                            PrintUsage();
                            return 2;
                        }
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
            {
                Console.Error.WriteLine("Input file does not exist: " + (input ?? "(none)"));
                return 2;
            }

            if (!Array.Exists(new[] { "random", "lite", "balanced", "strong", "max", "ultra" }, p => p == requestedProfile))
            {
                Console.Error.WriteLine("Invalid profile. Use random, lite, balanced, strong, max, or ultra.");
                return 2;
            }

            input = Path.GetFullPath(input);
            string outputDirectory = string.IsNullOrWhiteSpace(output)
                ? Environment.CurrentDirectory
                : Path.GetDirectoryName(Path.GetFullPath(output));
            if (string.IsNullOrEmpty(outputDirectory)) outputDirectory = Environment.CurrentDirectory;
            Directory.CreateDirectory(outputDirectory);

            // Choose a fresh profile for every random-profile job. The engine itself also
            // generates fresh opcode mappings, mutation choices, XOR keys, and VM aliases.
            string selectedProfile = requestedProfile == "random"
                ? new[] { "lite", "balanced", "strong", "max", "ultra" }[RandomNumberGenerator.GetInt32(0, 5)]
                : requestedProfile;
            ObfuscationSettings settings = BuildSettings(selectedProfile);
            Environment.SetEnvironmentVariable("HUB12_ACTIVE_PROFILE", selectedProfile);
            Console.WriteLine("Profile selected: " + selectedProfile);

            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "12hub_" + TwelveHubEngine.CreateBuildId());
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                if (!TwelveHubEngine.Obfuscate(temporaryDirectory, input, settings, out string error))
                {
                    Console.Error.WriteLine("Obfuscation failed: " + error);
                    return 1;
                }

                string finalPath = string.IsNullOrWhiteSpace(output)
                    ? Path.Combine(outputDirectory, "12hub_" + TwelveHubEngine.LastBuildId + ".lua")
                    : Path.GetFullPath(output);
                string generated = Path.Combine(temporaryDirectory, "out.lua");
                if (!File.Exists(generated))
                {
                    Console.Error.WriteLine("The engine reported success but no output file exists. Nothing was saved.");
                    return 1;
                }

                if (new FileInfo(generated).Length == 0)
                {
                    Console.Error.WriteLine("The engine produced an empty output. Nothing was saved.");
                    return 1;
                }

                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Copy(generated, finalPath);
                Console.WriteLine("Done: " + finalPath);
                Console.WriteLine("Build ID: 12hub_" + TwelveHubEngine.LastBuildId);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("12hub error: " + ex.Message);
                return 1;
            }
            finally
            {
                try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true); }
                catch { /* Keep a completed output even if temporary cleanup fails. */ }
            }
        }

        private static ObfuscationSettings BuildSettings(string profile)
        {
            int Pick(int minInclusive, int maxInclusive) =>
                RandomNumberGenerator.GetInt32(minInclusive, maxInclusive + 1);

            ObfuscationSettings settings = new ObfuscationSettings
            {
                EncryptStrings = true,
                EncryptImportantStrings = profile == "strong" || profile == "max" || profile == "ultra",
                ControlFlow = true,
                BytecodeCompress = true,
                PreserveLineInfo = false,
                Mutate = true,
                SuperOperators = true
            };

            switch (profile)
            {
                case "lite":
                    settings.DecryptTableLen = Pick(256, 420);
                    settings.MaxMegaSuperOperators = Pick(70, 110);
                    settings.MaxMiniSuperOperators = Pick(50, 90);
                    settings.MaxMutations = Pick(100, 180);
                    break;
                case "balanced":
                    settings.DecryptTableLen = Pick(380, 560);
                    settings.MaxMegaSuperOperators = Pick(100, 170);
                    settings.MaxMiniSuperOperators = Pick(80, 130);
                    settings.MaxMutations = Pick(160, 250);
                    break;
                case "strong":
                    settings.DecryptTableLen = Pick(480, 700);
                    settings.MaxMegaSuperOperators = Pick(150, 220);
                    settings.MaxMiniSuperOperators = Pick(110, 170);
                    settings.MaxMutations = Pick(230, 340);
                    break;
                case "max":
                    settings.EncryptImportantStrings = true;
                    settings.DecryptTableLen = Pick(560, 820);
                    settings.MaxMegaSuperOperators = Pick(180, 250);
                    settings.MaxMiniSuperOperators = Pick(130, 200);
                    settings.MaxMutations = Pick(280, 400);
                    break;
                default: // ultra: highest bounded transform intensity; expect bigger/slower output
                    settings.EncryptImportantStrings = true;
                    settings.DecryptTableLen = Pick(700, 1000);
                    settings.MaxMegaSuperOperators = Pick(220, 320);
                    settings.MaxMiniSuperOperators = Pick(160, 240);
                    settings.MaxMutations = Pick(350, 500);
                    break;
            }
            return settings;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("12hub Lua Obfuscator");
            Console.WriteLine("Usage: 12hub <input.lua> [--profile random|lite|balanced|strong|max|ultra] [-o <output.lua>]");
            Console.WriteLine("All profiles use the Lua 5.1 bytecode VM pipeline.");
            Console.WriteLine("Tools: HUB12_LUAC / HUB12_LUAJIT select Lua 5.1 tools.");
        }
    }
}
