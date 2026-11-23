using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartHr.Domain;

namespace SmartHr.Application;

/// <summary>Coordinates ingestion, hybrid retrieval, grounded answers, and evaluation.</summary>
public sealed class RagService(
    IPolicyRepository repository,
    IEmbeddingService embeddings,
    IChatService chat,
    StructureChunker chunker,
    VectorCache cache,
    RetrievalOptions? retrieval = null,
    ICrossEncoder? crossEncoder = null
)
{
    public const int MaximumTopK = 20;
    public const int MaximumRerankPool = 32;

    private readonly RetrievalOptions options = retrieval ?? new RetrievalOptions();
    private readonly ICrossEncoder reranker = crossEncoder ?? DisabledCrossEncoder.Instance;

    public static IReadOnlyList<RetrievalMode> AllModes { get; } = Enum.GetValues<RetrievalMode>();

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
        CancellationToken cancellationToken,
        RetrievalMode? mode = null
    )
    {
        var outcome = await SearchCoreAsync(question, topK, cancellationToken, mode);
        return outcome.Hits;
    }

    public async Task<AskResponse> AskAsync(
        string question,
        int topK,
        CancellationToken cancellationToken,
        RetrievalMode? mode = null
    )
    {
        var sources = await SearchAsync(question, topK, cancellationToken, mode);
        if (sources.Count == 0)
        {
            var indexed = await cache.GetAsync(repository.GetChunksAsync, cancellationToken);
            var message = indexed.Count == 0 ? PromptBuilder.NoPoliciesMessage : PromptBuilder.NoMatchMessage;
            return new AskResponse(message, sources);
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

    public Task<EvaluationResult> EvaluateAsync(
        IReadOnlyList<EvaluationCase> cases,
        int topK,
        CancellationToken cancellationToken,
        RetrievalMode? mode = null
    ) => EvaluateModeAsync(cases, topK, ResolveMode(mode), cancellationToken);

    public async Task<IReadOnlyList<EvaluationResult>> EvaluateAllModesAsync(
        IReadOnlyList<EvaluationCase> cases,
        int topK,
        CancellationToken cancellationToken
    )
    {
        var results = new List<EvaluationResult>(AllModes.Count);
        foreach (var mode in AllModes)
        {
            results.Add(await EvaluateModeAsync(cases, topK, mode, cancellationToken));
        }

        return results;
    }

    public static string BuildEmbeddingText(string documentName, string section, string text) =>
        $"Policy: {documentName}\nSection: {section}\n{text}";

    private async Task<EvaluationResult> EvaluateModeAsync(
        IReadOnlyList<EvaluationCase> cases,
        int topK,
        RetrievalMode mode,
        CancellationToken cancellationToken
    )
    {
        if (cases.Count == 0)
        {
            throw new ArgumentException("At least one evaluation case is required.", nameof(cases));
        }

        var k = Math.Clamp(topK, 1, MaximumTopK);
        var answerable = cases.Count(testCase => !testCase.Unanswerable);
        double found = 0, precision = 0, reciprocal = 0;
        var details = new List<EvaluationCaseResult>(cases.Count);

        foreach (var testCase in cases)
        {
            var started = Stopwatch.GetTimestamp();
            var outcome = await SearchCoreAsync(testCase.Question, k, cancellationToken, mode);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var hits = outcome.Hits;
            var rankIndex = testCase.Unanswerable ? -1 : hits.ToList().FindIndex(hit => Matches(hit, testCase));
            var relevant = testCase.Unanswerable ? 0 : hits.Count(hit => Matches(hit, testCase));
            details.Add(
                new EvaluationCaseResult(
                    testCase.Question,
                    testCase.ExpectedDocument,
                    testCase.ExpectedSection,
                    rankIndex < 0 ? 0 : rankIndex + 1,
                    hits.Select(hit => $"{hit.DocumentName} > {hit.Section}").ToList(),
                    elapsedMs,
                    testCase.Unanswerable,
                    outcome.RerankerApplied
                )
            );

            if (testCase.Unanswerable || rankIndex < 0)
            {
                continue;
            }

            found++;
            precision += (double)relevant / k;
            reciprocal += 1d / (rankIndex + 1);
        }

        var denominator = answerable == 0 ? 0 : answerable;
        return new EvaluationResult(
            cases.Count,
            denominator == 0 ? 0 : found / denominator,
            denominator == 0 ? 0 : precision / denominator,
            denominator == 0 ? 0 : reciprocal / denominator
        )
        {
            Cases = details,
            Mode = mode.ToString(),
            AnswerableQuestions = answerable,
            UnanswerableQuestions = cases.Count - answerable,
            MeanLatencyMs = details.Count == 0 ? 0 : details.Average(item => item.LatencyMs),
            RerankerConfigured = mode == RetrievalMode.HybridRerank && reranker.IsEnabled,
        };
    }

    private async Task<(IReadOnlyList<SearchHit> Hits, bool RerankerApplied)> SearchCoreAsync(
        string question,
        int topK,
        CancellationToken cancellationToken,
        RetrievalMode? mode
    )
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("A question is required.", nameof(question));
        }

        var resolved = ResolveMode(mode);
        var k = Math.Clamp(topK, 1, MaximumTopK);
        var trimmed = question.Trim();
        var chunks = await cache.GetAsync(repository.GetChunksAsync, cancellationToken);

        var vectorHits = new List<SearchHit>();
        var keywordHits = new List<SearchHit>();
        if (resolved != RetrievalMode.Keyword)
        {
            var questionVector = await embeddings.EmbedAsync(trimmed, cancellationToken);
            vectorHits = RankByVector(chunks, questionVector);
        }

        if (resolved != RetrievalMode.Vector)
        {
            keywordHits = RankByKeyword(chunks, trimmed);
        }

        IReadOnlyList<SearchHit> ranked = resolved switch
        {
            RetrievalMode.Vector => vectorHits,
            RetrievalMode.Keyword => keywordHits,
            _ => Fuse(vectorHits, keywordHits),
        };

        var applied = false;
        if (resolved == RetrievalMode.HybridRerank)
        {
            var pool = Math.Clamp(options.CandidatePool <= 0 ? 20 : options.CandidatePool, k, MaximumRerankPool);
            var candidates = ranked.Take(pool).ToList();
            var reranked = await Reranker.ApplyAsync(reranker, trimmed, candidates, cancellationToken);
            ranked = reranked.Hits;
            applied = reranked.Applied;
        }

        return (ranked.Take(k).ToList(), applied);
    }

    private RetrievalMode ResolveMode(RetrievalMode? mode) => mode ?? options.Mode;

    private static List<SearchHit> RankByVector(IReadOnlyList<CachedChunk> chunks, float[] questionVector) =>
        chunks
            .Where(chunk => chunk.Vector.Length == questionVector.Length)
            .Select(chunk => ToHit(chunk, VectorMath.CosineSimilarity(questionVector, chunk.Vector)))
            .OrderByDescending(hit => hit.Score)
            .ToList();

    private List<SearchHit> RankByKeyword(IReadOnlyList<CachedChunk> chunks, string question) =>
        Bm25.Rank(chunks, question, options.Bm25K1, options.Bm25B)
            .Select(hit => ToHit(hit.Chunk, hit.Score))
            .ToList();

    private List<SearchHit> Fuse(IReadOnlyList<SearchHit> vectorHits, IReadOnlyList<SearchHit> keywordHits)
    {
        var k = options.RrfK <= 0 ? ReciprocalRankFusion.DefaultK : options.RrfK;
        return ReciprocalRankFusion
            .Fuse<SearchHit, Guid>([vectorHits, keywordHits], hit => hit.ChunkId, k)
            .Select(hit => hit.Item with { Score = hit.Score })
            .ToList();
    }

    private static SearchHit ToHit(CachedChunk chunk, double score) =>
        new(chunk.ChunkId, chunk.DocumentId, chunk.DocumentName, chunk.Section, chunk.Text, score);
}
