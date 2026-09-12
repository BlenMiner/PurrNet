using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PurrNet.Utils
{
    internal static class PurrJson
    {
        public static void ReadStringMap(string json, Dictionary<string, string> into)
        {
            int pos = 0;
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != '{')
                return;
            pos++;

            while (true)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] == '}')
                    return;
                if (json[pos] == ',')
                {
                    pos++;
                    continue;
                }
                if (json[pos] != '"')
                    return;

                string key = ReadString(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] != ':')
                    return;
                pos++;
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length)
                    return;

                if (json[pos] == '"')
                {
                    into[key] = ReadString(json, ref pos);
                    continue;
                }

                int start = pos;
                while (pos < json.Length && json[pos] != ',' && json[pos] != '}')
                    pos++;
                string literal = json.Substring(start, pos - start).Trim();
                if (literal.Length > 0 && literal != "null")
                    into[key] = literal;
            }
        }

        public static string WriteStringMap(IReadOnlyDictionary<string, string> map)
        {
            if (map.Count == 0)
                return "{}";

            var sb = new StringBuilder();
            sb.Append("{\n");
            bool first = true;
            foreach (var pair in map)
            {
                if (!first)
                    sb.Append(",\n");
                first = false;
                sb.Append("  ");
                WriteString(sb, pair.Key);
                sb.Append(": ");
                WriteString(sb, pair.Value);
            }

            sb.Append("\n}");
            return sb.ToString();
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int or long or short or byte or uint or ulong or ushort or sbyte or float or double or decimal:
                    sb.Append(((System.IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    break;
                case IDictionary dictionary:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;
                        WriteString(sb, entry.Key.ToString());
                        sb.Append(':');
                        Write(sb, entry.Value);
                    }

                    sb.Append('}');
                    break;
                }
                case IEnumerable enumerable:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in enumerable)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }

                    sb.Append(']');
                    break;
                }
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                pos++;
        }

        private static string ReadString(string json, ref int pos)
        {
            pos++;
            var sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos++];
                if (c == '"')
                    break;
                if (c != '\\' || pos >= json.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char escaped = json[pos++];
                switch (escaped)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (pos + 4 <= json.Length &&
                            int.TryParse(json.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            sb.Append((char)code);
                            pos += 4;
                        }
                        break;
                    default: sb.Append(escaped); break;
                }
            }

            return sb.ToString();
        }
    }
}
