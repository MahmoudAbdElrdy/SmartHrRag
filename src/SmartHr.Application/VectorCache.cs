using System.Text.Json;
using SmartHr.Domain;

namespace SmartHr.Application;

public sealed record CachedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    string Section,
    string Text,
    float[] Vector
);

/// <summary>Process-wide snapshot of chunk vectors. Invalidated on ingest/delete.</summary>
public sealed class VectorCache
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile IReadOnlyList<CachedChunk>? snapshot;

    public void Invalidate() => snapshot = null;

    public async Task<IReadOnlyList<CachedChunk>> GetAsync(
        Func<CancellationToken, Task<List<PolicyChunk>>> load,
        CancellationToken cancellationToken
    )
    {
        var current = snapshot;
        if (current is not null)
        {
            return current;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (snapshot is not null)
            {
                return snapshot;
            }

            var chunks = await load(cancellationToken);
            var loaded = chunks
                .Select(c => (Chunk: c, Vector: ReadVector(c.VectorJson)))
                .Where(x => x.Vector is { Length: > 0 })
                .Select(x => new CachedChunk(
                    x.Chunk.Id,
                    x.Chunk.DocumentId,
                    x.Chunk.Document?.Name ?? "",
                    x.Chunk.Section,
                    x.Chunk.Content,
                    x.Vector!
                ))
                .ToList();
            snapshot = loaded;
            return loaded;
        }
        finally
        {
            gate.Release();
        }
    }

    private static float[]? ReadVector(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<float[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
