// JSONObject.cs
// Lightweight JSON parser for XWear Importer.

using System.Collections.Generic;
using System.Text;

namespace XWearImporter
{
    public class JSONObject
    {
        public Dictionary<string, JSONObject> dict = new();
        public List<JSONObject> list = new();
        public string str;
        public double n;
        public bool b;

        public bool isDict => dict.Count > 0;
        public bool isList => list.Count > 0;
        public double f => n;
        public float ff => (float)n;
        public int i => (int)n;

        public bool HasField(string k) => dict.ContainsKey(k);
        public JSONObject GetField(string k) => dict.TryGetValue(k, out var v) ? v : new JSONObject();

        public static JSONObject Parse(string json)
        {
            var p = new JsonParser(json);
            p.SkipWs();
            var v = p.ParseValue();
            p.SkipWs();
            return v;
        }
    }

    class JsonParser
    {
        string s; int i;
        public JsonParser(string s) { this.s = s; this.i = 0; }

        public void SkipWs() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        public JSONObject ParseValue()
        {
            SkipWs();
            if (i >= s.Length) return new JSONObject();
            char c = s[i];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return ParseString();
            if (c == 't' || c == 'f') return ParseBool();
            if (c == 'n')
            {
                if (i + 4 <= s.Length && s.Substring(i, 4) == "null")
                {
                    i += 4;
                    return new JSONObject();
                }
            }
            return ParseNumber();
        }

        JSONObject ParseObject()
        {
            var o = new JSONObject();
            i++;
            SkipWs();
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs();
                if (i >= s.Length || s[i] == '}') { if (i < s.Length) i++; break; }
                var key = ParseString().str;
                SkipWs();
                if (i < s.Length && s[i] == ':') i++;
                var val = ParseValue();
                if (key != null) o.dict[key] = val;
                SkipWs();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return o;
        }

        JSONObject ParseArray()
        {
            var o = new JSONObject();
            i++;
            SkipWs();
            if (i < s.Length && s[i] == ']') { i++; return o; }
            while (true)
            {
                o.list.Add(ParseValue());
                SkipWs();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return o;
        }

        JSONObject ParseString()
        {
            var o = new JSONObject();
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    sb.Append(n switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '"' => '"',
                        '\\' => '\\',
                        _ => n,
                    });
                    i += 2;
                }
                else
                {
                    sb.Append(s[i]);
                    i++;
                }
            }
            i++;
            o.str = sb.ToString();
            return o;
        }

        JSONObject ParseNumber()
        {
            var o = new JSONObject();
            int start = i;
            if (i < s.Length && s[i] == '-') i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                i++;
            if (i == start) i++;
            double.TryParse(s.Substring(start, i - start), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out o.n);
            return o;
        }

        JSONObject ParseBool()
        {
            var o = new JSONObject();
            if (s.Substring(i, 4) == "true") { o.b = true; i += 4; }
            else if (s.Substring(i, 5) == "false") { o.b = false; i += 5; }
            return o;
        }
    }
}
