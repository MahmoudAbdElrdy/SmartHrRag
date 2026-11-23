namespace SmartHr.Application;

public sealed record Bm25Hit(CachedChunk Chunk, double Score);

/// <summary>
/// Okapi BM25 over the chunks already cached for cosine search.
/// SQL Server on Linux (the image in docker-compose) has no Full-Text Search,
/// so the lexical index stays in the application and can apply Arabic normalization.
/// </summary>
public static class Bm25
{
    public static IReadOnlyList<Bm25Hit> Rank(
        IReadOnlyList<CachedChunk> chunks,
        string query,
        double k1 = 1.2,
        double b = 0.75
    )
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var queryTerms = LexicalTokenizer.Tokenize(query).Distinct(StringComparer.Ordinal).ToArray();
        if (queryTerms.Length == 0 || chunks.Count == 0)
        {
            return [];
        }

        if (k1 <= 0)
        {
            k1 = 1.2;
        }

        b = Math.Clamp(b, 0, 1);

        var termFrequency = new Dictionary<string, int>[chunks.Count];
        var lengths = new int[chunks.Count];
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        double lengthSum = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var token in LexicalTokenizer.Tokenize(chunks[i].Text))
            {
                counts.TryGetValue(token, out var count);
                counts[token] = count + 1;
                lengths[i]++;
            }

            termFrequency[i] = counts;
            lengthSum += lengths[i];
            foreach (var term in counts.Keys)
            {
                documentFrequency.TryGetValue(term, out var docs);
                documentFrequency[term] = docs + 1;
            }
        }

        var averageLength = chunks.Count == 0 ? 0 : lengthSum / chunks.Count;
        var corpusSize = chunks.Count;
        var ranked = new List<Bm25Hit>();

        for (var i = 0; i < corpusSize; i++)
        {
            double score = 0;
            var counts = termFrequency[i];
            var lengthRatio = averageLength == 0 ? 0 : lengths[i] / averageLength;
            foreach (var term in queryTerms)
            {
                if (!counts.TryGetValue(term, out var freq) || freq == 0)
                {
                    continue;
                }

                documentFrequency.TryGetValue(term, out var docsWithTerm);
                var idf = Math.Log(1 + (corpusSize - docsWithTerm + 0.5) / (docsWithTerm + 0.5));
                var denominator = freq + k1 * (1 - b + b * lengthRatio);
                score += idf * (freq * (k1 + 1)) / denominator;
            }

            if (score > 0)
            {
                ranked.Add(new Bm25Hit(chunks[i], score));
            }
        }

        ranked.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        return ranked;
    }
}
