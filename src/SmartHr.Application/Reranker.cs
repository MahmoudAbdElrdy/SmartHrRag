using System.Text.Json;

namespace SmartHr.Application;

public sealed record RerankScore(int Index, double Score);

public sealed record RerankResult(IReadOnlyList<SearchHit> Hits, bool Applied);

/// <summary>Scores query-passage pairs. Implementations throw when a configured backend cannot be reached.</summary>
public interface ICrossEncoder
{
    /// <summary>False when reranking is off or no backend URL is configured. ScoreAsync is not called.</summary>
    bool IsEnabled { get; }

    Task<IReadOnlyList<RerankScore>> ScoreAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken
    );
}

/// <summary>Used when retrieval is constructed without a cross-encoder.</summary>
public sealed class DisabledCrossEncoder : ICrossEncoder
{
    public static readonly DisabledCrossEncoder Instance = new();

    public bool IsEnabled => false;

    public Task<IReadOnlyList<RerankScore>> ScoreAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken
    ) => Task.FromResult<IReadOnlyList<RerankScore>>([]);
}

/// <summary>
/// Applies a cross-encoder to fused candidates.
/// A disabled encoder, an empty score list, or any failure other than caller cancellation
/// keeps the original order.
/// </summary>
public static class Reranker
{
    public static async Task<RerankResult> ApplyAsync(
        ICrossEncoder encoder,
        string query,
        IReadOnlyList<SearchHit> candidates,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(candidates);

        if (!encoder.IsEnabled || candidates.Count == 0)
        {
            return new RerankResult(candidates, false);
        }

        IReadOnlyList<RerankScore> scores;
        try
        {
            scores = await encoder.ScoreAsync(
                query,
                candidates.Select(candidate => candidate.Text).ToList(),
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new RerankResult(candidates, false);
        }

        if (scores.Count == 0 || !scores.Any(score => (uint)score.Index < (uint)candidates.Count))
        {
            return new RerankResult(candidates, false);
        }

        return new RerankResult(OrderByScores(candidates, scores), true);
    }

    public static IReadOnlyList<SearchHit> OrderByScores(
        IReadOnlyList<SearchHit> candidates,
        IReadOnlyList<RerankScore> scores
    )
    {
        var scoreByIndex = new Dictionary<int, double>();
        foreach (var score in scores)
        {
            if ((uint)score.Index >= (uint)candidates.Count)
            {
                continue;
            }

            scoreByIndex.TryAdd(score.Index, score.Score);
        }

        if (scoreByIndex.Count == 0)
        {
            return candidates;
        }

        return candidates
            .Select(
                (hit, index) =>
                    (
                        Hit: hit,
                        Index: index,
                        Known: scoreByIndex.TryGetValue(index, out var value),
                        Value: value
                    )
            )
            .OrderByDescending(item => item.Known)
            .ThenByDescending(item => item.Value)
            .ThenBy(item => item.Index)
            .Select(item => item.Known ? item.Hit with { Score = item.Value } : item.Hit)
            .ToList();
    }
}

/// <summary>Reads a TEI <c>/rerank</c> array or a Cohere-style <c>results</c> array.</summary>
public static class RerankResponseParser
{
    public static IReadOnlyList<RerankScore> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            array = results;
        }
        else
        {
            throw new JsonException("Reranker response was not a score list.");
        }

        var scores = new List<RerankScore>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("index", out var indexElement))
            {
                continue;
            }

            var index = indexElement.GetInt32();
            double score = 0;
            if (item.TryGetProperty("score", out var scoreElement))
            {
                score = scoreElement.GetDouble();
            }
            else if (item.TryGetProperty("relevance_score", out var relevance))
            {
                score = relevance.GetDouble();
            }

            scores.Add(new RerankScore(index, score));
        }

        return scores;
    }
}
