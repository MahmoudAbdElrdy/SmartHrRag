using SmartHr.Application;

namespace SmartHr.Tests;

public class ChunkerTests
{
    private readonly StructureChunker chunker = new();

    [Fact]
    public void Splits_on_markdown_headings_and_tracks_section()
    {
        var chunks = chunker.Split("# Policy\n\n## Annual Leave\n\nTwenty one days.\n\n## Sick Leave\n\nThirty days.");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Annual Leave", chunks[0].Section);
        Assert.Equal("Sick Leave", chunks[1].Section);
        Assert.DoesNotContain("…", chunks[1].Text); // no overlap across sections
    }

    [Fact]
    public void Large_section_is_split_with_overlap_and_size_limit()
    {
        var paragraph = string.Join(' ', Enumerable.Repeat("word", 60)); // ~300 chars
        var text = "## Long\n\n" + string.Join("\n\n", Enumerable.Repeat(paragraph, 8));

        var chunks = chunker.Split(text, maxCharacters: 700, overlapCharacters: 100);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal("Long", c.Section));
        Assert.All(chunks.Skip(1), c => Assert.StartsWith("…", c.Text));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 700 + 110));
    }

    [Fact]
    public void Table_rows_keep_header()
    {
        var rows = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"| row {i} with some text | {i} |"));
        var chunks = chunker.Split("## T\n\n| A | B |\n|---|---|\n" + rows, maxCharacters: 300, overlapCharacters: 0);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.StartsWith("| A | B |", c.Text));
    }

    [Fact]
    public void Arabic_text_is_preserved()
    {
        var chunks = chunker.Split("## الاستحقاق\n\nيستحق الموظف 21 يوم إجازة.");
        Assert.Equal("الاستحقاق", chunks[0].Section);
        Assert.Contains("21 يوم", chunks[0].Text);
    }

    [Fact]
    public void Rejects_tiny_chunk_size() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => chunker.Split("x", maxCharacters: 50));
}
