using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Hub12.Obfuscator.Encryption
{
    /// <summary>
    /// Per-literal byte masking. Each literal receives an independent key and one of
    /// several reversible byte transforms, rather than a single repeated decoder shape.
    /// This is obfuscation, not cryptographic protection: keys are present in the output.
    /// </summary>
    public sealed class Decryptor
    {
        private enum TransformMode { Xor = 0, Add = 1, RollingAdd = 2, IndexedXor = 3 }

        public int[] Table { get; }
        public int SLen { get; set; }
        public string Name { get; }

        private readonly TransformMode _mode;
        private readonly int _rollingStep;

        public string Encrypt(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            List<byte> encrypted = new List<byte>(bytes.Length);
            int length = Table.Length;
            if (length <= 0) throw new InvalidOperationException("The string mask table cannot be empty.");

            for (int index = 0; index < bytes.Length; index++)
            {
                int key = Table[index % length];
                int value;
                switch (_mode)
                {
                    case TransformMode.Add:
                        value = (bytes[index] + key) & 255;
                        break;
                    case TransformMode.RollingAdd:
                        value = (bytes[index] + key + (int)(((long)index * _rollingStep) & 255L)) & 255;
                        break;
                    case TransformMode.IndexedXor:
                        // A different reversible shape: XOR with a key that changes by position.
                        // Keep the mask in the byte range so Lua 5.1 numeric arithmetic stays exact.
                        value = bytes[index] ^ ((key + (int)(((long)index * _rollingStep) & 255L)) & 255);
                        break;
                    default:
                        value = bytes[index] ^ key;
                        break;
                }
                encrypted.Add((byte)value);
            }

            string encodedKey = EncodeByteString(Table.Select(v => (byte)v));
            string encodedData = EncodeByteString(encrypted);
            string prefix = "((function(b) local c=\"\"; local e=string.sub; local h=string.char; " +
                "local t={} for j=0,255 do local x=h(j); t[j]=x; t[x]=j; end; " +
                "local f=\"" + encodedKey + "\"; for g=1,#b do local x=(g-1)%" +
                length.ToString(CultureInfo.InvariantCulture) + "+1; local n=";
            string formula;

            switch (_mode)
            {
                case TransformMode.Add:
                    formula = "(t[e(b,g,g)]-t[e(f,x,x)]+256)%256";
                    break;
                case TransformMode.RollingAdd:
                    formula = "(t[e(b,g,g)]-t[e(f,x,x)]-(((g-1)*" +
                        _rollingStep.ToString(CultureInfo.InvariantCulture) + ")%256)+512)%256";
                    break;
                case TransformMode.IndexedXor:
                    formula = "xor(t[e(b,g,g)],(t[e(f,x,x)]+(((g-1)*" +
                        _rollingStep.ToString(CultureInfo.InvariantCulture) + ")%256))%256)";
                    prefix = "((function(b) local function xor(a,z) local d,e=1,0; " +
                        "while a>0 and z>0 do local f,g=a%2,z%2; if f~=g then e=e+d end; " +
                        "a,z,d=(a-f)/2,(z-g)/2,d*2 end; if a<z then a=z end; " +
                        "while a>0 do local f=a%2; if f>0 then e=e+d end; " +
                        "a,d=(a-f)/2,d*2 end; return e end; " +
                        "local c=\"\"; local e=string.sub; local h=string.char; " +
                        "local t={} for j=0,255 do local x=h(j); t[j]=x; t[x]=j; end; " +
                        "local f=\"" + encodedKey + "\"; for g=1,#b do local x=(g-1)%" +
                        length.ToString(CultureInfo.InvariantCulture) + "+1; local n=";
                    break;
                default:
                    formula = "xor(t[e(b,g,g)],t[e(f,x,x)])";
                    prefix = "((function(b) local function xor(a,z) local d,e=1,0; " +
                        "while a>0 and z>0 do local f,g=a%2,z%2; if f~=g then e=e+d end; " +
                        "a,z,d=(a-f)/2,(z-g)/2,d*2 end; if a<z then a=z end; " +
                        "while a>0 do local f=a%2; if f>0 then e=e+d end; " +
                        "a,d=(a-f)/2,d*2 end; return e end; " +
                        "local c=\"\"; local e=string.sub; local h=string.char; " +
                        "local t={} for j=0,255 do local x=h(j); t[j]=x; t[x]=j; end; " +
                        "local f=\"" + encodedKey + "\"; for g=1,#b do local x=(g-1)%" +
                        length.ToString(CultureInfo.InvariantCulture) + "+1; local n=";
                    break;
            }

            return prefix + formula + "; c=c..t[n]; end; return c; end)(\"" + encodedData + "\"))";
        }

        private static string EncodeByteString(IEnumerable<byte> bytes)
        {
            StringBuilder result = new StringBuilder();
            foreach (byte value in bytes)
                result.Append('\\').Append(value.ToString(CultureInfo.InvariantCulture));
            return result.ToString();
        }

        public Decryptor(string name, int maxLen)
        {
            if (maxLen < 1) throw new ArgumentOutOfRangeException(nameof(maxLen));
            Random random = Hub12.TwelveHubEngine.Random;
            Name = name ?? string.Empty;
            Table = new int[maxLen];
            for (int i = 0; i < Table.Length; i++) Table[i] = random.Next(0, 256);
            _mode = (TransformMode)random.Next(0, 4);
            _rollingStep = random.Next(1, 256);
        }
    }

    /// <summary>
    /// Lua 5.1-aware string literal scanner. It walks source once, so expanding one
    /// literal cannot shift offsets for following matches. Comments are preserved and
    /// are never mistaken for source strings. Both quoted and long-bracket strings are
    /// decoded to the byte sequence Lua 5.1 would produce before masking.
    /// </summary>
    public sealed class ConstantEncryption
    {
        private const string Marker = "[STR_ENCRYPT]";
        private readonly string _src;
        private readonly ObfuscationSettings _settings;
        private static readonly Encoding LuaEncoding = CreateLuaEncoding();

        private static Encoding CreateLuaEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(28591);
        }

        private sealed class LongBracketOpen
        {
            public int Length;
            public int EqualsCount;
        }

        public static byte[] UnescapeLuaString(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            List<byte> bytes = new List<byte>(str.Length);
            int i = 0;
            while (i < str.Length)
            {
                char current = str[i++];
                if (current != '\\')
                {
                    bytes.Add(ToLuaByte(current));
                    continue;
                }

                if (i >= str.Length)
                {
                    // Valid input is compiler-checked before this pass. Keep this helper
                    // defensive for callers that use it directly.
                    bytes.Add((byte)'\\');
                    break;
                }

                char next = str[i++];
                switch (next)
                {
                    case 'a': bytes.Add(7); break;
                    case 'b': bytes.Add(8); break;
                    case 'f': bytes.Add(12); break;
                    case 'n': bytes.Add(10); break;
                    case 'r': bytes.Add(13); break;
                    case 't': bytes.Add(9); break;
                    case 'v': bytes.Add(11); break;
                    case '\n': bytes.Add(10); break;
                    case '\r':
                        if (i < str.Length && str[i] == '\n') i++;
                        bytes.Add(10);
                        break;
                    default:
                        if (next >= '0' && next <= '9')
                        {
                            int value = next - '0';
                            int count = 1;
                            while (count < 3 && i < str.Length && str[i] >= '0' && str[i] <= '9')
                            {
                                value = value * 10 + (str[i] - '0');
                                i++;
                                count++;
                            }
                            if (value > 255)
                                throw new FormatException("Lua decimal escape exceeds one byte.");
                            bytes.Add((byte)value);
                        }
                        else
                        {
                            // Lua 5.1 treats an escape of a non-special character as that
                            // character (for example, \\q becomes q).
                            bytes.Add(ToLuaByte(next));
                        }
                        break;
                }
            }
            return bytes.ToArray();
        }

        private static byte ToLuaByte(char value)
        {
            if (value > 255)
                throw new FormatException("A Lua 5.1 source string contains a character outside the byte range.");
            return (byte)value;
        }

        private static byte[] DecodeLongString(string body)
        {
            string normalized = body.Replace("\r\n", "\n").Replace('\r', '\n');
            // Lua long strings ignore one newline immediately after the opening [[...]].
            if (normalized.StartsWith("\n", StringComparison.Ordinal))
                normalized = normalized.Substring(1);
            return LuaEncoding.GetBytes(normalized);
        }

        private string EncryptLiteral(byte[] bytes, string name)
        {
            int maxKeyLength = Math.Max(1, _settings.DecryptTableLen);
            int keyLength = Math.Max(1, Math.Min(bytes.Length, maxKeyLength));
            return new Decryptor(name, keyLength).Encrypt(bytes);
        }

        public string EncryptStrings()
        {
            if (string.IsNullOrEmpty(_src)) return _src;

            StringBuilder output = new StringBuilder(_src.Length + 128);
            int i = 0;
            int literalIndex = 0;
            while (i < _src.Length)
            {
                char current = _src[i];

                if (current == '-' && i + 1 < _src.Length && _src[i + 1] == '-')
                {
                    int longCommentEnd = TrySkipLongBracket(_src, i + 2);
                    int end = longCommentEnd >= 0 ? longCommentEnd : SkipLine(_src, i);
                    output.Append(_src, i, end - i);
                    i = end;
                    continue;
                }

                if (current == '\'' || current == '"')
                {
                    int start = i;
                    int end = SkipQuoted(_src, start);
                    int contentStart = start + 1;
                    int contentLength = Math.Max(0, end - contentStart - (_src[end - 1] == current ? 1 : 0));
                    string raw = _src.Substring(contentStart, contentLength);
                    byte[] decoded = UnescapeLuaString(raw);
                    if (ShouldEncrypt(raw, decoded, out byte[] effectiveBytes))
                        output.Append(EncryptLiteral(effectiveBytes, "12HUB_STR_" + literalIndex++));
                    else
                        output.Append(_src, start, end - start);
                    i = end;
                    continue;
                }

                if (current == '[')
                {
                    LongBracketOpen open = ParseLongBracketOpen(_src, i);
                    if (open != null)
                    {
                        int end = FindLongBracketEnd(_src, i + open.Length, open.EqualsCount);
                        int bodyStart = i + open.Length;
                        string rawBody = _src.Substring(bodyStart, Math.Max(0, end - bodyStart - CloseLength(open.EqualsCount)));
                        byte[] decoded = DecodeLongString(rawBody);
                        if (ShouldEncrypt(rawBody, decoded, out byte[] effectiveBytes))
                            output.Append(EncryptLiteral(effectiveBytes, "12HUB_STR_" + literalIndex++));
                        else
                            output.Append(_src, i, end - i);
                        i = end;
                        continue;
                    }
                }

                output.Append(current);
                i++;
            }
            return output.ToString();
        }

        private bool ShouldEncrypt(string raw, byte[] decoded, out byte[] effectiveBytes)
        {
            effectiveBytes = decoded;
            bool marked = raw.StartsWith(Marker, StringComparison.Ordinal);
            if (marked)
            {
                // Keep the established [STR_ENCRYPT] marker contract: the marker is a
                // build-time directive and is not part of the runtime string value. Strip
                // its ASCII bytes from the already-decoded Lua value to preserve escapes.
                effectiveBytes = decoded.Length >= Marker.Length
                    ? decoded.Skip(Marker.Length).ToArray()
                    : Array.Empty<byte>();
                return true;
            }

            if (_settings.EncryptStrings) return true;
            if (!_settings.EncryptImportantStrings) return false;

            string decodedText = LuaEncoding.GetString(decoded);
            string[] importantTerms = { "http", "function", "metatable", "local" };
            return importantTerms.Any(term => decodedText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static int SkipQuoted(string source, int start)
        {
            char quote = source[start];
            int i = start + 1;
            while (i < source.Length)
            {
                if (source[i] == '\\')
                {
                    i = Math.Min(i + 2, source.Length);
                    continue;
                }
                if (source[i++] == quote) break;
            }
            return i;
        }

        private static LongBracketOpen ParseLongBracketOpen(string source, int start)
        {
            if (start >= source.Length || source[start] != '[') return null;
            int p = start + 1;
            while (p < source.Length && source[p] == '=') p++;
            if (p >= source.Length || source[p] != '[') return null;
            return new LongBracketOpen { Length = p - start + 1, EqualsCount = p - start - 1 };
        }

        private static int CloseLength(int equalsCount) => equalsCount + 2;

        private static int FindLongBracketEnd(string source, int bodyStart, int equalsCount)
        {
            string close = "]" + new string('=', equalsCount) + "]";
            int closing = source.IndexOf(close, bodyStart, StringComparison.Ordinal);
            return closing < 0 ? source.Length : closing + close.Length;
        }

        private static int TrySkipLongBracket(string source, int start)
        {
            LongBracketOpen open = ParseLongBracketOpen(source, start);
            return open == null ? -1 : FindLongBracketEnd(source, start + open.Length, open.EqualsCount);
        }

        private static int SkipLine(string source, int start)
        {
            int i = start;
            while (i < source.Length && source[i] != '\n' && source[i] != '\r') i++;
            return i;
        }

        public ConstantEncryption(ObfuscationSettings settings, string source)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _src = source ?? string.Empty;
        }
    }
}
