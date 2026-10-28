using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 작은 JSON 읽기 도구(에디터 전용). JsonUtility는 사전·중첩 배열을 못 읽어서 직접 파싱한다.
// 결과: Dictionary<string, object> / List<object> / double / string / bool / null
public static class MiniJson
{
    public static object Parse(string text)
    {
        int i = 0;
        var v = Value(text, ref i);
        Skip(text, ref i);
        if (i != text.Length) throw new System.FormatException($"JSON 끝에 남은 글자: 위치 {i}");
        return v;
    }

    static void Skip(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static object Value(string s, ref int i)
    {
        Skip(s, ref i);
        if (i >= s.Length) throw new System.FormatException("JSON이 중간에 끝남");
        char c = s[i];
        if (c == '{') return Obj(s, ref i);
        if (c == '[') return Arr(s, ref i);
        if (c == '"') return Str(s, ref i);
        if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
        if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
        if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
        return Num(s, ref i);
    }

    static Dictionary<string, object> Obj(string s, ref int i)
    {
        var d = new Dictionary<string, object>();
        i++;
        Skip(s, ref i);
        if (s[i] == '}') { i++; return d; }
        while (true)
        {
            Skip(s, ref i);
            string key = Str(s, ref i);
            Skip(s, ref i);
            if (s[i] != ':') throw new System.FormatException($"':' 없음: 위치 {i}");
            i++;
            d[key] = Value(s, ref i);
            Skip(s, ref i);
            if (s[i] == ',') { i++; continue; }
            if (s[i] == '}') { i++; return d; }
            throw new System.FormatException($"',' 또는 '}}' 없음: 위치 {i}");
        }
    }

    static List<object> Arr(string s, ref int i)
    {
        var a = new List<object>();
        i++;
        Skip(s, ref i);
        if (s[i] == ']') { i++; return a; }
        while (true)
        {
            a.Add(Value(s, ref i));
            Skip(s, ref i);
            if (s[i] == ',') { i++; continue; }
            if (s[i] == ']') { i++; return a; }
            throw new System.FormatException($"',' 또는 ']' 없음: 위치 {i}");
        }
    }

    static string Str(string s, ref int i)
    {
        if (s[i] != '"') throw new System.FormatException($"문자열 아님: 위치 {i}");
        i++;
        var sb = new StringBuilder();
        while (s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                char e = s[i];
                if (e == 'u') { sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; }
                else sb.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'b' => '\b', 'f' => '\f', _ => e });
            }
            else sb.Append(s[i]);
            i++;
        }
        i++;
        return sb.ToString();
    }

    static double Num(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
    }
}
