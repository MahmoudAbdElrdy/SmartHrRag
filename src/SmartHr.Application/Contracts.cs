using SmartHr.Domain;

namespace SmartHr.Application;

public interface IPolicyRepository
{
    Task AddAsync(PolicyDocument document, CancellationToken cancellationToken);
    Task<List<PolicyChunk>> GetChunksAsync(CancellationToken cancellationToken);
    Task<PolicyDocument?> GetDocumentAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
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

public sealed record SearchHit(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    string Section,
    string Text,
    double Score
);

public sealed record AskResponse(string Answer, IReadOnlyList<SearchHit> Sources);

public sealed record EvaluationResult(
    int Questions,
    double RecallAtK,
    double PrecisionAtK,
    double Mrr
);
