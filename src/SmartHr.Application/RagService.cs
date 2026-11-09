using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartHr.Domain;

namespace SmartHr.Application;

/// <summary>Coordinates ingestion, semantic retrieval, grounded answers, and evaluation.</summary>
public sealed class RagService(
    IPolicyRepository repository,
    IEmbeddingService embeddings,
    IChatService chat,
    StructureChunker chunker,
    VectorCache cache
)
{
    private const int MaximumTopK = 20;

    public static string ComputeHash(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public async Task<IngestResult> IngestAsync(
        string name,
        string source,
        string text,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A document name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("The document is empty.", nameof(text));
        }

        var hash = ComputeHash(text);
        var existing = await repository.FindByHashAsync(hash, cancellationToken);
        if (existing is not null && existing.Status == IngestionStatus.Ready)
        {
            return new IngestResult(existing.Id, true, existing.Chunks.Count);
        }

        if (existing is not null)
        {
            // A previous failed attempt: remove it and retry.
            await repository.DeleteDocumentAsync(existing.Id, cancellationToken);
        }

        var pieces = chunker.Split(text);
        if (pieces.Count == 0)
        {
            throw new ArgumentException("The document contains no readable text.", nameof(text));
        }

        var document = new PolicyDocument
        {
            Name = name.Trim(),
            SourceFile = source,
            ContentHash = hash,
            CharacterCount = text.Length,
            Status = IngestionStatus.Processing,
        };

        for (var sequence = 0; sequence < pieces.Count; sequence++)
        {
            var piece = pieces[sequence];
            var embeddingText = BuildEmbeddingText(document.Name, piece.Section, piece.Text);
            var vector = await embeddings.EmbedAsync(embeddingText, cancellationToken);

            document.Chunks.Add(
                new PolicyChunk
                {
                    Sequence = sequence,
                    Section = piece.Section,
                    Content = embeddingText,
                    VectorJson = JsonSerializer.Serialize(vector),
                    EmbeddingModel = embeddings.ModelName,
                }
            );
        }

        document.Status = IngestionStatus.Ready;
        await repository.AddAsync(document, cancellationToken);
        cache.Invalidate();
        return new IngestResult(document.Id, false, document.Chunks.Count);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteDocumentAsync(id, cancellationToken);
        if (deleted)
        {
            cache.Invalidate();
        }

        return deleted;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string question,
        int topK,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("A question is required.", nameof(question));
        }

        var questionVector = await embeddings.EmbedAsync(question.Trim(), cancellationToken);
        var chunks = await cache.GetAsync(repository.GetChunksAsync, cancellationToken);

        return chunks
            .Where(c => c.Vector.Length == questionVector.Length)
            .Select(c => (Chunk: c, Score: VectorMath.CosineSimilarity(questionVector, c.Vector)))
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(topK, 1, MaximumTopK))
            .Select(x => new SearchHit(
                x.Chunk.ChunkId,
                x.Chunk.DocumentId,
                x.Chunk.DocumentName,
                x.Chunk.Section,
                x.Chunk.Text,
                x.Score
            ))
            .ToList();
    }

    public async Task<AskResponse> AskAsync(
        string question,
        int topK,
        CancellationToken cancellationToken
    )
    {
        var sources = await SearchAsync(question, topK, cancellationToken);
        if (sources.Count == 0)
        {
            return new AskResponse(PromptBuilder.NoPoliciesMessage, sources);
        }

        var answer = await chat.AnswerAsync(PromptBuilder.Build(question, sources), cancellationToken);
        return new AskResponse(answer, sources);
    }

    public static bool Matches(SearchHit hit, EvaluationCase testCase)
    {
        if (testCase.ExpectedChunkId is { } id && id != Guid.Empty)
        {
            return hit.ChunkId == id;
        }

        if (
            string.IsNullOrWhiteSpace(testCase.ExpectedDocument)
            || !string.Equals(hit.DocumentName, testCase.ExpectedDocument, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(testCase.ExpectedSection)
            || hit.Section.Contains(testCase.ExpectedSection, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<EvaluationResult> EvaluateAsync(
        IReadOnlyList<EvaluationCase> cases,
        int topK,
        CancellationToken cancellationToken
    )
    {
        if (cases.Count == 0)
        {
            throw new ArgumentException("At least one evaluation case is required.", nameof(cases));
        }

        var k = Math.Clamp(topK, 1, MaximumTopK);
        double found = 0, precision = 0, reciprocal = 0;
        var details = new List<EvaluationCaseResult>();

        foreach (var testCase in cases)
        {
            var hits = await SearchAsync(testCase.Question, k, cancellationToken);
            var relevant = hits.Count(h => Matches(h, testCase));
            var rank = hits.ToList().FindIndex(h => Matches(h, testCase));
            details.Add(
                new EvaluationCaseResult(
                    testCase.Question,
                    testCase.ExpectedDocument,
                    testCase.ExpectedSection,
                    rank < 0 ? 0 : rank + 1,
                    hits.Select(h => $"{h.DocumentName} > {h.Section}").ToList()
                )
            );

            if (rank < 0)
            {
                continue;
            }

            found++;
            precision += (double)relevant / k;
            reciprocal += 1d / (rank + 1);
        }

        return new EvaluationResult(
            cases.Count,
            found / cases.Count,
            precision / cases.Count,
            reciprocal / cases.Count
        )
        {
            Cases = details,
        };
    }

    public static string BuildEmbeddingText(string documentName, string section, string text) =>
        $"Policy: {documentName}\nSection: {section}\n{text}";
}
