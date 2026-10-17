using System;
using System.Globalization;
using System.Text;
using System.Collections.Generic;

namespace Hub12.Obfuscator.VM_Generation
{
    /// <summary>
    /// PSU-inspired numeric expression diversification for generated VM source.
    /// It only rewrites plain decimal integer tokens outside strings/comments, and wraps
    /// the equivalent expression in parentheses so surrounding operator precedence is kept.
    /// </summary>
    public static class LuaNumberRandomizer
    {
        public static string Apply(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            StringBuilder output = new StringBuilder(source.Length + source.Length / 3);
            // Do not reuse a disguise expression for repeated numeric literals in one build.
            HashSet<string> usedExpressions = new HashSet<string>(StringComparer.Ordinal);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                if (c == '\'' || c == '"')
                {
                    int end = SkipQuoted(source, i);
                    output.Append(source, i, end - i); i = end; continue;
                }
                if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
                {
                    int longEnd = TrySkipLongBracket(source, i + 2);
                    int end = longEnd >= 0 ? longEnd : SkipLine(source, i);
                    output.Append(source, i, end - i); i = end; continue;
                }
                if (c == '[')
                {
                    int longEnd = TrySkipLongBracket(source, i);
                    if (longEnd >= 0)
                    { output.Append(source, i, longEnd - i); i = longEnd; continue; }
                }
                if (c >= '0' && c <= '9')
                {
                    int end = ScanNumber(source, i);
                    string token = source.Substring(i, end - i);
                    bool identifierPrefix = i > 0 && IsIdentifierPart(source[i - 1]);
                    bool identifierSuffix = end < source.Length && IsIdentifierPart(source[end]);
                    if (!identifierPrefix && !identifierSuffix && IsPlainInteger(token) &&
                        long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out long value) &&
                        value >= 0 && value <= 1000000000L)
                    {
                        output.Append(RandomizeInteger(token, usedExpressions));
                    }
                    else output.Append(token);
                    i = end; continue;
                }
                output.Append(c); i++;
            }
            return output.ToString();
        }

        private static string RandomizeInteger(string token, HashSet<string> usedExpressions)
        {
            var random = Hub12.TwelveHubEngine.Random;
            while (true)
            {
                int a = random.Next(3, 1000000);
                int b = random.Next(3, 1000000);
                string expression;
                switch (random.Next(0, 8))
                {
                    case 0: expression = "((" + token + "+" + a + ")-" + a + ")"; break;
                    case 1: expression = "((" + token + "-" + a + ")+" + a + ")"; break;
                    case 2: expression = "(((" + token + "+" + a + ")+" + b + ")-" + b + "-" + a + ")"; break;
                    case 3: expression = "(((" + token + "-" + a + ")-" + b + ")+" + b + "+" + a + ")"; break;
                    case 4: expression = "(((" + token + "+" + a + ")-" + b + ")+" + b + "-" + a + ")"; break;
                    case 5: expression = "(((" + token + "-" + b + ")+" + a + ")-" + a + "+" + b + ")"; break;
                    case 6: expression = "(((" + token + "+" + a + ")+" + b + ")-" + a + "-" + b + ")"; break;
                    default: expression = "(((" + token + "-" + a + ")+" + b + ")+" + a + "-" + b + ")"; break;
                }
                if (usedExpressions.Add(expression)) return expression;
            }
        }

        private static bool IsPlainInteger(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            for (int i = 0; i < token.Length; i++)
                if (token[i] < '0' || token[i] > '9') return false;
            return token.Length == 1 || token[0] != '0';
        }

        private static int ScanNumber(string s, int start)
        {
            int i = start;
            if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
            {
                i += 2;
                while (i < s.Length && IsHex(s[i])) i++;
                if (i < s.Length && s[i] == '.' && (i + 1 >= s.Length || s[i + 1] != '.'))
                { i++; while (i < s.Length && IsHex(s[i])) i++; }
                if (i < s.Length && (s[i] == 'p' || s[i] == 'P'))
                {
                    i++; if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                }
                return i;
            }
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i < s.Length && s[i] == '.' && (i + 1 >= s.Length || s[i + 1] != '.'))
            { i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++; if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            return i;
        }

        private static bool IsHex(char c) => (c >= '0' && c <= '9') ||
            (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static bool IsIdentifierPart(char c) => (c >= 'a' && c <= 'z') ||
            (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';

        private static int SkipQuoted(string s, int start)
        {
            char q = s[start]; int i = start + 1;
            while (i < s.Length)
            {
                if (s[i] == '\\') { i = Math.Min(i + 2, s.Length); continue; }
                if (s[i++] == q) break;
            }
            return i;
        }

        private static int TrySkipLongBracket(string s, int start)
        {
            if (start >= s.Length || s[start] != '[') return -1;
            int p = start + 1;
            while (p < s.Length && s[p] == '=') p++;
            if (p >= s.Length || s[p] != '[') return -1;
            int equals = p - start - 1;
            string close = "]" + new string('=', equals) + "]";
            int at = s.IndexOf(close, p + 1, StringComparison.Ordinal);
            return at < 0 ? s.Length : at + close.Length;
        }

        private static int SkipLine(string s, int start)
        { int i = start; while (i < s.Length && s[i] != '\n' && s[i] != '\r') i++; return i; }
    }
}
