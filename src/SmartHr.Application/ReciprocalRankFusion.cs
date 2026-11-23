namespace SmartHr.Application;

public sealed record FusedRank<T>(T Item, double Score);

/// <summary>
/// Reciprocal rank fusion. A document's score is the sum of 1 / (k + rank)
/// across every list that contains it. Rank is 1-based. k defaults to 60.
/// </summary>
public static class ReciprocalRankFusion
{
    public const int DefaultK = 60;

    public static IReadOnlyList<FusedRank<T>> Fuse<T, TKey>(
        IEnumerable<IReadOnlyList<T>> rankings,
        Func<T, TKey> key,
        int k = DefaultK
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(rankings);
        ArgumentNullException.ThrowIfNull(key);
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), "RRF k must be positive.");
        }

        var scores = new Dictionary<TKey, Accumulator<T>>();
        var order = 0;
        foreach (var ranking in rankings)
        {
            ArgumentNullException.ThrowIfNull(ranking);
            var seen = new HashSet<TKey>();
            var rank = 0;
            foreach (var item in ranking)
            {
                var id = key(item);
                if (!seen.Add(id))
                {
                    continue;
                }

                rank++;
                var contribution = 1d / (k + rank);
                if (scores.TryGetValue(id, out var existing))
                {
                    existing.Score += contribution;
                    if (rank < existing.BestRank)
                    {
                        existing.BestRank = rank;
                    }
                }
                else
                {
                    scores.Add(id, new Accumulator<T>(item, contribution, rank, order++));
                }
            }
        }

        return scores
            .Values.OrderByDescending(item => item.Score)
            .ThenBy(item => item.BestRank)
            .ThenBy(item => item.Order)
            .Select(item => new FusedRank<T>(item.Item, item.Score))
            .ToList();
    }

    private sealed class Accumulator<T>(T item, double score, int bestRank, int order)
    {
        public T Item { get; } = item;
        public double Score { get; set; } = score;
        public int BestRank { get; set; } = bestRank;
        public int Order { get; } = order;
    }
}
