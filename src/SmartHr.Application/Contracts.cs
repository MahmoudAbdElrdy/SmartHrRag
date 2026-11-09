using SmartHr.Domain;

namespace SmartHr.Application;

public interface IPolicyRepository
{
    Task AddAsync(PolicyDocument document, CancellationToken cancellationToken);
    Task<List<PolicyChunk>> GetChunksAsync(CancellationToken cancellationToken);
    Task<PolicyDocument?> GetDocumentAsync(Guid id, CancellationToken cancellationToken);
    Task<PolicyDocument?> FindByHashAsync(string contentHash, CancellationToken cancellationToken);
    Task<List<DocumentSummary>> ListDocumentsAsync(CancellationToken cancellationToken);
    Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);

    /// <summary>Name of the embedding model, stored per chunk to detect model changes.</summary>
    string ModelName => "";
}

public interface IChatService
{
    Task<string> AnswerAsync(string prompt, CancellationToken cancellationToken);
}

public interface ITextExtractor
{
    string Extract(string fileName, Stream stream);
}

public sealed record TextChunk(string Section, string Text);

public sealed record DocumentSummary(
    Guid Id,
    string Name,
    string SourceFile,
    string Status,
    string? Error,
    int ChunkCount,
    DateTime CreatedUtc
);

public sealed record IngestResult(Guid DocumentId, bool Duplicate, int Chunks);

public sealed record SearchHit(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    string Section,
    string Text,
    double Score
);

public sealed record AskResponse(string Answer, IReadOnlyList<SearchHit> Sources)
{
    public Guid? QuestionId { get; init; }
}

public sealed record EvaluationCase(
    string Question,
    string? ExpectedDocument,
    string? ExpectedSection,
    Guid? ExpectedChunkId
);

public sealed record EvaluationCaseResult(
    string Question,
    string? ExpectedDocument,
    string? ExpectedSection,
    int Rank,
    IReadOnlyList<string> Retrieved
);

public sealed record EvaluationResult(
    int Questions,
    double RecallAtK,
    double PrecisionAtK,
    double Mrr
)
{
    public IReadOnlyList<EvaluationCaseResult> Cases { get; init; } = [];
}
