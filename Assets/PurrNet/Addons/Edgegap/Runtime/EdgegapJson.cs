using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PurrNet.Edgegap.Runtime
{
    internal static class EdgegapJson
    {
        public static object Parse(string json)
        {
            int pos = 0;
            return ReadValue(json, ref pos);
        }

        private static object ReadValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length)
                return null;

            switch (json[pos])
            {
                case '{': return ReadObject(json, ref pos);
                case '[': return ReadArray(json, ref pos);
                case '"': return ReadString(json, ref pos);
                case 't': pos += 4; return true;
                case 'f': pos += 5; return false;
                case 'n': pos += 4; return null;
                default: return ReadNumber(json, ref pos);
            }
        }

        private static Dictionary<string, object> ReadObject(string json, ref int pos)
        {
            var result = new Dictionary<string, object>();
            pos++;
            while (true)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length)
                    return result;
                char c = json[pos];
                if (c == '}')
                {
                    pos++;
                    return result;
                }
                if (c == ',')
                {
                    pos++;
                    continue;
                }
                if (c != '"')
                    return result;

                string key = ReadString(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] != ':')
                    return result;
                pos++;
                result[key] = ReadValue(json, ref pos);
            }
        }

        private static List<object> ReadArray(string json, ref int pos)
        {
            var result = new List<object>();
            pos++;
            while (true)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length)
                    return result;
                char c = json[pos];
                if (c == ']')
                {
                    pos++;
                    return result;
                }
                if (c == ',')
                {
                    pos++;
                    continue;
                }
                result.Add(ReadValue(json, ref pos));
            }
        }

        private static object ReadNumber(string json, ref int pos)
        {
            int start = pos;
            while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] is '-' or '+' or '.' or 'e' or 'E'))
                pos++;
            if (pos == start)
            {
                pos++;
                return null;
            }

            return double.TryParse(json.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
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

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                pos++;
        }

        public static string GetString(Dictionary<string, object> json, string key)
        {
            return json.TryGetValue(key, out var value) ? value as string : null;
        }

        public static int GetInt(Dictionary<string, object> json, string key)
        {
            return json.TryGetValue(key, out var value) && value is double number ? (int)number : 0;
        }
    }
}
