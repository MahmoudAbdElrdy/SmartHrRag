using System.Text.RegularExpressions;

namespace SmartHr.Application;

/// <summary>Splits policy text into section-aware chunks for embedding and retrieval.</summary>
public sealed partial class StructureChunker
{
    private const int DefaultMaxCharacters = 1_100;

    public IReadOnlyList<TextChunk> Split(string text, int maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (maxCharacters < 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters),
                "Chunk size must be at least 200 characters."
            );
        }

        var normalizedText = text.Replace("\r\n", "\n").Trim();
        var chunks = new List<TextChunk>();
        var section = "General";
        var paragraphs = new List<string>();
        var paragraphLength = 0;

        void Flush()
        {
            if (paragraphs.Count == 0)
            {
                return;
            }

            chunks.Add(new TextChunk(section, string.Join("\n\n", paragraphs)));
            paragraphs.Clear();
            paragraphLength = 0;
        }

        foreach (var rawBlock in ParagraphSeparator().Split(normalizedText))
        {
            var block = rawBlock.Trim();
            if (block.Length == 0)
            {
                continue;
            }

            var heading = MarkdownHeading().Match(block);
            if (heading.Success)
            {
                Flush();
                section = heading.Groups[1].Value.Trim();
                continue;
            }

            if (block.Length > maxCharacters)
            {
                Flush();
                chunks.AddRange(
                    SplitLargeBlock(block, maxCharacters)
                        .Select(piece => new TextChunk(section, piece))
                );
                continue;
            }

            var addedLength = block.Length + (paragraphs.Count == 0 ? 0 : 2);
            if (paragraphLength + addedLength > maxCharacters)
            {
                Flush();
            }

            paragraphs.Add(block);
            paragraphLength += addedLength;
        }

        Flush();
        return chunks;
    }

    private static IEnumerable<string> SplitLargeBlock(string block, int maxCharacters)
    {
        var lines = block.Split('\n');
        var isMarkdownTable =
            lines.Length > 1
            && lines[0].Contains('|')
            && lines[1].Contains('|')
            && lines[1].Contains('-');

        return isMarkdownTable
            ? SplitTable(lines, maxCharacters)
            : SplitAtWordBoundaries(block, maxCharacters);
    }

    private static IEnumerable<string> SplitTable(IReadOnlyList<string> lines, int maxCharacters)
    {
        var header = lines[0] + "\n" + lines[1];
        var rowBudget = Math.Max(80, maxCharacters - header.Length - 1);
        var rows = new List<string>();
        var currentLength = 0;

        foreach (var row in lines.Skip(2))
        {
            if (row.Length > rowBudget)
            {
                if (rows.Count > 0)
                {
                    yield return header + "\n" + string.Join("\n", rows);
                    rows.Clear();
                    currentLength = 0;
                }

                foreach (var piece in SplitAtWordBoundaries(row, rowBudget))
                {
                    yield return header + "\n" + piece;
                }

                continue;
            }

            if (currentLength + row.Length + 1 > rowBudget && rows.Count > 0)
            {
                yield return header + "\n" + string.Join("\n", rows);
                rows.Clear();
                currentLength = 0;
            }

            rows.Add(row);
            currentLength += row.Length + 1;
        }

        if (rows.Count > 0)
        {
            yield return header + "\n" + string.Join("\n", rows);
        }
    }

    private static IEnumerable<string> SplitAtWordBoundaries(string text, int maxCharacters)
    {
        var remaining = text.Trim();

        while (remaining.Length > maxCharacters)
        {
            var splitAt = remaining.LastIndexOf(' ', maxCharacters);
            if (splitAt < maxCharacters / 2)
            {
                splitAt = maxCharacters;
            }

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].Trim();
        }

        if (remaining.Length > 0)
        {
            yield return remaining;
        }
    }

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex ParagraphSeparator();

    [GeneratedRegex(@"^#{1,6}\s+(.+)$")]
    private static partial Regex MarkdownHeading();
}
