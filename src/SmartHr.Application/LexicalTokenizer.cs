using System.Text;

namespace SmartHr.Application;

/// <summary>
/// Tokenizes text for BM25. Arabic is normalized first, Latin is lowercased,
/// and a leading ال is removed so الإجازة and اجازه land on the same token.
/// </summary>
public static class LexicalTokenizer
{
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = ArabicNormalizer.Normalize(text).ToLowerInvariant();
        var tokens = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var token = current.ToString();
            current.Clear();
            if (token.StartsWith("ال", StringComparison.Ordinal) && token.Length > 3)
            {
                token = token[2..];
            }

            if (token.Length > 0)
            {
                tokens.Add(token);
            }
        }

        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return tokens;
    }
}
