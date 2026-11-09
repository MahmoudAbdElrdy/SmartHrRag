using SmartHr.Application;
using SmartHr.Domain;

namespace SmartHr.Tests;

public class IngestionTests
{
    private sealed class FakeRepository : IPolicyRepository
    {
        public List<PolicyDocument> Docs { get; } = new();

        public Task AddAsync(PolicyDocument document, CancellationToken ct)
        {
            foreach (var c in document.Chunks)
            {
                c.Document = document;
                c.DocumentId = document.Id;
            }

            Docs.Add(document);
            return Task.CompletedTask;
        }

        public Task<List<PolicyChunk>> GetChunksAsync(CancellationToken ct) =>
            Task.FromResult(Docs.SelectMany(d => d.Chunks).ToList());

        public Task<PolicyDocument?> GetDocumentAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Docs.FirstOrDefault(d => d.Id == id));

        public Task<PolicyDocument?> FindByHashAsync(string hash, CancellationToken ct) =>
            Task.FromResult(Docs.FirstOrDefault(d => d.ContentHash == hash));

        public Task<List<DocumentSummary>> ListDocumentsAsync(CancellationToken ct) =>
            Task.FromResult(
                Docs.Select(d => new DocumentSummary(d.Id, d.Name, d.SourceFile, d.Status, d.Error, d.Chunks.Count, d.CreatedUtc)).ToList()
            );

        public Task<bool> DeleteDocumentAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Docs.RemoveAll(d => d.Id == id) > 0);

        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Bag-of-letters embedding: deterministic and good enough to rank keyword overlap.</summary>
    private sealed class FakeEmbeddings : IEmbeddingService
    {
        public int Calls { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            Calls++;
            var v = new float[26];
            foreach (var ch in text.ToLowerInvariant().Where(char.IsAsciiLetterLower))
            {
                v[ch - 'a']++;
            }

            return Task.FromResult(v);
        }
    }

    private sealed class FakeChat : IChatService
    {
        public string? LastPrompt { get; private set; }

        public Task<string> AnswerAsync(string prompt, CancellationToken ct)
        {
            LastPrompt = prompt;
            return Task.FromResult("answer [Source 1]");
        }
    }

    private static (RagService Rag, FakeRepository Repo, FakeEmbeddings Emb, FakeChat Chat) Create()
    {
        var repo = new FakeRepository();
        var emb = new FakeEmbeddings();
        var chat = new FakeChat();
        return (new RagService(repo, emb, chat, new StructureChunker(), new VectorCache()), repo, emb, chat);
    }

    [Fact]
    public async Task Same_content_is_ingested_once()
    {
        var (rag, repo, emb, _) = Create();
        var first = await rag.IngestAsync("Leave", "a.md", "## A\n\nTwenty one days.", default);
        var calls = emb.Calls;
        var second = await rag.IngestAsync("Leave copy", "b.md", "## A\r\n\r\nTwenty one days.  ", default);

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(first.DocumentId, second.DocumentId);
        Assert.Single(repo.Docs);
        Assert.Equal(calls, emb.Calls);
        Assert.Equal(IngestionStatus.Ready, repo.Docs[0].Status);
    }

    [Fact]
    public async Task Empty_document_throws_argument_exception()
    {
        var (rag, _, _, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => rag.IngestAsync("x", "x", "   ", default));
    }

    [Fact]
    public async Task Cache_is_invalidated_after_ingest_and_delete()
    {
        var (rag, _, _, _) = Create();
        Assert.Empty(await rag.SearchAsync("zebra", 3, default));

        var doc = await rag.IngestAsync("Zoo", "z.md", "## Animals\n\nzebra zebra zebra", default);
        Assert.Single(await rag.SearchAsync("zebra", 3, default));

        Assert.True(await rag.DeleteAsync(doc.DocumentId, default));
        Assert.Empty(await rag.SearchAsync("zebra", 3, default));
    }

    [Fact]
    public async Task Ask_returns_sources_and_uses_prompt()
    {
        var (rag, _, _, chat) = Create();
        await rag.IngestAsync("Leave", "l.md", "## Annual\n\nannual leave days\n\n## Payroll\n\nsalary paid monthly", default);

        var response = await rag.AskAsync("annual leave", 1, default);

        Assert.Single(response.Sources);
        Assert.Equal("Annual", response.Sources[0].Section);
        Assert.Contains("[Source 1: Leave > Annual]", chat.LastPrompt);
    }

    [Fact]
    public async Task Evaluation_matches_by_document_and_section()
    {
        var (rag, _, _, _) = Create();
        await rag.IngestAsync("Leave", "l.md", "## Annual\n\nannual leave days\n\n## Payroll\n\nsalary paid monthly xyz", default);

        var result = await rag.EvaluateAsync(
            [new EvaluationCase("salary paid monthly", "Leave", "Payroll", null)],
            1,
            default
        );

        Assert.Equal(1d, result.RecallAtK);
        Assert.Equal(1d, result.Mrr);
    }
}
