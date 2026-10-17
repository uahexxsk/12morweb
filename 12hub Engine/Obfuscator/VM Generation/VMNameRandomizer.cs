using System;
using System.Collections.Generic;
using System.Text;

namespace Hub12.Obfuscator.VM_Generation
{
    /// <summary>
    /// Renames selected implementation-local VM identifiers per build. It is a lexical
    /// pass: quoted/long strings and Lua comments are copied byte-for-byte.
    /// </summary>
    public static class VMNameRandomizer
    {
        private static readonly string[] TargetNames =
        {
            "Byte", "Char", "Sub", "Concat", "LDExp", "GetFEnv", "Setmetatable", "Select", "Unpack", "ToNumber",
            "BitXOR", "gBit", "Pos", "gBits32", "gBits8", "gFloat", "gSizet", "gString", "gInt", "_R",
            "Deserialize", "Instrs", "Functions", "Lines", "ByteString", "Chunk", "ConstCount", "Consts", "Cons",
            "Type", "Wrap", "Instr", "Const", "Proto", "Params", "InstrPoint", "Top", "Vararg", "Args",
            "PCount", "Lupvals", "Stk", "Varargsz", "Inst", "Enum", "Idx", "Data1", "Data2", "Opco",
            "PCall", "Loop", "Registers", "Env", "K", "KI",
            "CheckPayloadIntegrity", "Checksum", "Index", "NeedBytes", "TypeOf", "Raise"
        };

        public static string Apply(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;

            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            VisitIdentifiers(source, token => used.Add(token));

            Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string token in TargetNames)
            {
                if (!used.Contains(token)) continue;
                string alias;
                do
                {
                    alias = "_" + RandomIdentifierTail(8 + Hub12.TwelveHubEngine.Random.Next(0, 9));
                } while (used.Contains(alias));
                used.Add(alias);
                aliases[token] = alias;
            }

            return Rewrite(source, aliases);
        }

        private static string RandomIdentifierTail(int length)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
            StringBuilder sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(alphabet[Hub12.TwelveHubEngine.Random.Next(0, alphabet.Length)]);
            return sb.ToString();
        }

        private static void VisitIdentifiers(string source, Action<string> visitor)
        {
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                if (c == '\'' || c == '"')
                {
                    i = SkipQuoted(source, i);
                    continue;
                }
                if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
                {
                    int longEnd = TrySkipLongBracket(source, i + 2);
                    i = longEnd >= 0 ? longEnd : SkipLine(source, i);
                    continue;
                }
                if (c == '[')
                {
                    int longEnd = TrySkipLongBracket(source, i);
                    if (longEnd >= 0) { i = longEnd; continue; }
                }
                if (IsIdentifierStart(c))
                {
                    int start = i++;
                    while (i < source.Length && IsIdentifierPart(source[i])) i++;
                    visitor(source.Substring(start, i - start));
                    continue;
                }
                i++;
            }
        }

        private static string Rewrite(string source, Dictionary<string, string> aliases)
        {
            StringBuilder result = new StringBuilder(source.Length + 128);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                if (c == '\'' || c == '"')
                {
                    int end = SkipQuoted(source, i);
                    result.Append(source, i, end - i);
                    i = end;
                    continue;
                }
                if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
                {
                    int longEnd = TrySkipLongBracket(source, i + 2);
                    int end = longEnd >= 0 ? longEnd : SkipLine(source, i);
                    result.Append(source, i, end - i);
                    i = end;
                    continue;
                }
                if (c == '[')
                {
                    int longEnd = TrySkipLongBracket(source, i);
                    if (longEnd >= 0)
                    {
                        result.Append(source, i, longEnd - i);
                        i = longEnd;
                        continue;
                    }
                }
                if (IsIdentifierStart(c))
                {
                    int start = i++;
                    while (i < source.Length && IsIdentifierPart(source[i])) i++;
                    string token = source.Substring(start, i - start);
                    if (aliases.TryGetValue(token, out string alias)) result.Append(alias);
                    else result.Append(token);
                    continue;
                }
                result.Append(c);
                i++;
            }
            return result.ToString();
        }

        private static int SkipQuoted(string source, int start)
        {
            char quote = source[start];
            int i = start + 1;
            while (i < source.Length)
            {
                if (source[i] == '\\') { i = Math.Min(i + 2, source.Length); continue; }
                if (source[i++] == quote) break;
            }
            return i;
        }

        // Returns the first index after a Lua long-bracket string/comment, or -1.
        private static int TrySkipLongBracket(string source, int start)
        {
            if (start >= source.Length || source[start] != '[') return -1;
            int p = start + 1;
            while (p < source.Length && source[p] == '=') p++;
            if (p >= source.Length || source[p] != '[') return -1;
            int equals = p - start - 1;
            string close = "]" + new string('=', equals) + "]";
            int closingAt = source.IndexOf(close, p + 1, StringComparison.Ordinal);
            return closingAt < 0 ? source.Length : closingAt + close.Length;
        }

        private static int SkipLine(string source, int start)
        {
            int i = start;
            while (i < source.Length && source[i] != '\n') i++;
            return i;
        }

        private static bool IsIdentifierStart(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

        private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || (c >= '0' && c <= '9');
    }
}
