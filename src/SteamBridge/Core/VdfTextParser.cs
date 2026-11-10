using System.Text;

namespace SteamBridge.Core;

public static class VdfTextParser
{
    public static Dictionary<string, object> Parse(string text)
    {
        var tokens = Tokenize(text);
        int pos = 0;
        return ParseMap(tokens, ref pos);
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < len && text[i + 1] == '/')
            {
                int nl = text.IndexOf('\n', i);
                i = nl == -1 ? len : nl + 1;
                continue;
            }

            if (c == '{' || c == '}')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < len && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < len)
                    {
                        sb.Append(text[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        sb.Append(text[i]);
                        i++;
                    }
                }
                if (i < len && text[i] == '"') i++;
                tokens.Add(sb.ToString());
                continue;
            }

            int start = i;
            while (i < len && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '"')
            {
                i++;
            }
            tokens.Add(text[start..i]);
        }

        return tokens;
    }

    private static Dictionary<string, object> ParseMap(List<string> tokens, ref int pos)
    {
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        while (pos < tokens.Count)
        {
            string token = tokens[pos];
            if (token == "}")
            {
                pos++;
                return map;
            }

            string key = token;
            pos++;
            if (pos >= tokens.Count) break;

            string valToken = tokens[pos];
            if (valToken == "{")
            {
                pos++;
                map[key] = ParseMap(tokens, ref pos);
            }
            else if (valToken == "}")
            {
                map[key] = "";
                pos++;
                return map;
            }
            else
            {
                map[key] = valToken;
                pos++;
            }
        }

        return map;
    }
}
