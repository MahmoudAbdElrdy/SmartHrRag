namespace SmartHr.Domain;

public static class IngestionStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Employee = "Employee";
}

public sealed class PolicyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string Status { get; set; } = IngestionStatus.Pending;
    public string? Error { get; set; }
    public int CharacterCount { get; set; }
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
    public string? EmbeddingModel { get; set; }
}

/// <summary>A retrieval test case matched by document name and section (stable across re-ingestion).</summary>
public sealed class RetrievalCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = "";
    public string ExpectedDocument { get; set; } = "";
    public string? ExpectedSection { get; set; }
    public Guid? ExpectedChunkId { get; set; }
}

public sealed class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EmployeeNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Department { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public DateTime HireDate { get; set; }
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Employee;
    public Guid? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class QuestionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public string SourcesJson { get; set; } = "[]";
    public int TopK { get; set; }
    public long LatencyMs { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<AnswerFeedback> Feedback { get; set; } = new();
}

public sealed class AnswerFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionLogId { get; set; }
    public QuestionLog QuestionLog { get; set; } = null!;
    public Guid? UserId { get; set; }
    public bool Helpful { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EvaluationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public int TopK { get; set; }
    public int Questions { get; set; }
    public double RecallAtK { get; set; }
    public double PrecisionAtK { get; set; }
    public double Mrr { get; set; }
    public string DetailsJson { get; set; } = "[]";
}
