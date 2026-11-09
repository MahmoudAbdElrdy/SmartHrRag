using SmartHr.Application;
using SmartHr.Domain;
using SmartHr.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace SmartHr.Api;

/// <summary>Re-embeds chunks whose stored embedding model differs from the configured one.</summary>
public static class Reembedder
{
    public static async Task<int> ReembedAsync(IServiceProvider services, bool force, CancellationToken ct)
    {
        var db = services.GetRequiredService<RagDbContext>();
        var embeddings = services.GetRequiredService<IEmbeddingService>();
        var cache = services.GetRequiredService<VectorCache>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Reembedder");
        var model = embeddings.ModelName;

        var query = db.Chunks.Include(c => c.Document).Where(c => c.Document.Status == IngestionStatus.Ready);
        if (!force)
        {
            query = query.Where(c => c.EmbeddingModel == null || c.EmbeddingModel != model);
        }

        var chunks = await query.ToListAsync(ct);
        if (chunks.Count == 0)
        {
            return 0;
        }

        logger.LogInformation("Re-embedding {Count} chunks with model {Model}...", chunks.Count, model);
        var done = 0;
        foreach (var chunk in chunks)
        {
            var text = RagService.BuildEmbeddingText(chunk.Document.Name, chunk.Section, chunk.Content);
            var vector = await embeddings.EmbedAsync(text, ct);
            chunk.VectorJson = System.Text.Json.JsonSerializer.Serialize(vector);
            chunk.EmbeddingModel = model;
            if (++done % 20 == 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        logger.LogInformation("Re-embedded {Count} chunks.", done);
        return done;
    }
}
