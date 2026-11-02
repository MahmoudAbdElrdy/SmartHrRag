namespace SmartHr.Domain;

public sealed class PolicyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<PolicyChunk> Chunks { get; set; } = new();
}

public sealed class PolicyChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public PolicyDocument Document { get; set; } = null!;
    public int Sequence { get; set; }
    public string Section { get; set; } = "";
    public string Content { get; set; } = "";
    public string? VectorJson { get; set; }
}

public sealed class RetrievalCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = "";
    public Guid ExpectedChunkId { get; set; }
}
