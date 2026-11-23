using System.Text;

namespace SmartHr.Application;

/// <summary>
/// Character normalization for Arabic lexical search.
/// Alef variants become ا, ى becomes ي, ة becomes ه, and tashkeel and tatweel are removed.
/// The stored chunk text and the text sent to the embedding model stay unchanged.
/// </summary>
public static class ArabicNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var buffer = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (IsTashkeel(ch) || ch == '\u0640')
            {
                continue;
            }

            buffer.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => ch,
            });
        }

        return buffer.ToString();
    }

    /// <summary>Arabic diacritics, superscript alef, and Quranic annotation marks.</summary>
    public static bool IsTashkeel(char ch) =>
        ch is >= '\u0610' and <= '\u061A'
            or >= '\u064B' and <= '\u065F'
            or '\u0670'
            or >= '\u06D6' and <= '\u06ED';
}
