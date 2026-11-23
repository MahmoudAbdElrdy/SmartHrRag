using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartHr.Application;

namespace SmartHr.Infrastructure;

/// <summary>
/// Calls a Hugging Face Text Embeddings Inference compatible POST /rerank endpoint.
/// Ollama has no rerank API. Failures propagate so <see cref="Reranker"/> can keep the fused order.
/// </summary>
public sealed class TeiRerankClient(HttpClient http, RetrievalOptions options, ILogger<TeiRerankClient> logger)
    : ICrossEncoder
{
    private readonly RerankerOptions settings = options.Reranker ?? new RerankerOptions();

    public bool IsEnabled =>
        settings.Enabled && !string.IsNullOrWhiteSpace(settings.BaseUrl);

    public async Task<IReadOnlyList<RerankScore>> ScoreAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled)
        {
            return [];
        }

        if (passages.Count == 0)
        {
            return [];
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(timeout);

        try
        {
            using var response = await http.PostAsJsonAsync(
                NormalizePath(settings.Path),
                new
                {
                    query,
                    texts = passages,
                    truncate = true,
                    raw_scores = false,
                },
                attempt.Token
            );
            var body = await response.Content.ReadAsStringAsync(attempt.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Reranker returned {(int)response.StatusCode}: {Trim(body)}"
                );
            }

            return RerankResponseParser.Parse(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Reranker at {BaseUrl} timed out after {Seconds}s. Retrieval will keep the fused order.",
                settings.BaseUrl,
                timeout.TotalSeconds
            );
            throw new TimeoutException($"Reranker timed out after {timeout.TotalSeconds}s.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(
                ex,
                "Reranker at {BaseUrl} failed. Retrieval will keep the fused order.",
                settings.BaseUrl
            );
            throw;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/rerank";
        }

        return path.StartsWith('/') ? path : "/" + path;
    }

    private static string Trim(string body) =>
        body.Length <= 300 ? body : body[..300];
}
