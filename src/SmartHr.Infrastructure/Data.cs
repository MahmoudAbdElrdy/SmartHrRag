using Microsoft.EntityFrameworkCore;
using SmartHr.Application;
using SmartHr.Domain;

namespace SmartHr.Infrastructure;

public sealed class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    public DbSet<PolicyDocument> Documents => Set<PolicyDocument>();
    public DbSet<PolicyChunk> Chunks => Set<PolicyChunk>();
    public DbSet<RetrievalCase> RetrievalCases => Set<RetrievalCase>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<QuestionLog> QuestionLogs => Set<QuestionLog>();
    public DbSet<AnswerFeedback> AnswerFeedback => Set<AnswerFeedback>();
    public DbSet<EvaluationRun> EvaluationRuns => Set<EvaluationRun>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<PolicyDocument>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.SourceFile).HasMaxLength(500);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.ContentHash);
        });

        m.Entity<PolicyChunk>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Section).HasMaxLength(500);
            e.Property(x => x.EmbeddingModel).HasMaxLength(200);
            e.HasOne(x => x.Document)
                .WithMany(x => x.Chunks)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DocumentId, x.Sequence });
        });

        m.Entity<RetrievalCase>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Question).HasMaxLength(1000);
            e.Property(x => x.ExpectedDocument).HasMaxLength(300);
            e.Property(x => x.ExpectedSection).HasMaxLength(500);
        });

        m.Entity<Employee>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeNumber).HasMaxLength(20);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Department).HasMaxLength(100);
            e.Property(x => x.JobTitle).HasMaxLength(100);
            e.HasIndex(x => x.EmployeeNumber).IsUnique();
        });

        m.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserName).HasMaxLength(100);
            e.Property(x => x.Role).HasMaxLength(20);
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasOne(x => x.Employee)
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        m.Entity<QuestionLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Question).HasMaxLength(2000);
            e.HasIndex(x => new { x.UserId, x.CreatedUtc });
        });

        m.Entity<AnswerFeedback>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne(x => x.QuestionLog)
                .WithMany(x => x.Feedback)
                .HasForeignKey(x => x.QuestionLogId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<EvaluationRun>().HasKey(x => x.Id);
    }
}

public sealed class PolicyRepository(RagDbContext db) : IPolicyRepository
{
    public async Task AddAsync(PolicyDocument doc, CancellationToken ct)
    {
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<PolicyChunk>> GetChunksAsync(CancellationToken ct) =>
        db.Chunks.Include(x => x.Document)
            .Where(x => x.Document.Status == IngestionStatus.Ready)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<PolicyDocument?> GetDocumentAsync(Guid id, CancellationToken ct) =>
        db.Documents.Include(x => x.Chunks).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<PolicyDocument?> FindByHashAsync(string contentHash, CancellationToken ct) =>
        db.Documents.Include(x => x.Chunks)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ContentHash == contentHash, ct);

    public Task<List<DocumentSummary>> ListDocumentsAsync(CancellationToken ct) =>
        db.Documents.AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new DocumentSummary(
                x.Id,
                x.Name,
                x.SourceFile,
                x.Status,
                x.Error,
                x.Chunks.Count,
                x.CreatedUtc
            ))
            .ToListAsync(ct);

    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null)
        {
            return false;
        }

        db.Documents.Remove(doc);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
