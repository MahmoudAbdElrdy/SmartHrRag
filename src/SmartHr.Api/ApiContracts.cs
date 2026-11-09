namespace SmartHr.Api;

public sealed record TextUploadRequest(string Name, string Text);

public sealed record SearchRequest(string Question, int TopK = 3);

public sealed record EvaluationCaseRequest(
    string Question,
    string? ExpectedDocument = null,
    string? ExpectedSection = null,
    Guid? ExpectedChunkId = null
);

public sealed record EvaluationRequest(List<EvaluationCaseRequest>? Cases = null, int TopK = 3);

public sealed record FeedbackRequest(bool Helpful, string? Comment);
