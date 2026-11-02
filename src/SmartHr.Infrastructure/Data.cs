using Microsoft.EntityFrameworkCore;
using SmartHr.Application;
using SmartHr.Domain;

namespace SmartHr.Infrastructure;

public sealed class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    public DbSet<PolicyDocument> Documents => Set<PolicyDocument>();
    public DbSet<PolicyChunk> Chunks => Set<PolicyChunk>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<PolicyDocument>().HasKey(x => x.Id);
        m.Entity<PolicyDocument>().Property(x => x.Name).HasMaxLength(300);
        m.Entity<PolicyChunk>().HasKey(x => x.Id);
        m.Entity<PolicyChunk>()
            .HasOne(x => x.Document)
            .WithMany(x => x.Chunks)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        m.Entity<PolicyChunk>().HasIndex(x => new { x.DocumentId, x.Sequence });
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
        db.Chunks.Include(x => x.Document).AsNoTracking().ToListAsync(ct);

    public Task<PolicyDocument?> GetDocumentAsync(Guid id, CancellationToken ct) =>
        db.Documents.Include(x => x.Chunks).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
