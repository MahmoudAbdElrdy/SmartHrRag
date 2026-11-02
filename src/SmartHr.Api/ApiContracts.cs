namespace SmartHr.Api;

public sealed record TextUploadRequest(string Name, string Text);

public sealed record SearchRequest(string Question, int TopK = 3);

public sealed record EvaluationCaseRequest(string Question, Guid ExpectedChunkId);

public sealed record EvaluationRequest(List<EvaluationCaseRequest> Cases, int TopK = 3);
