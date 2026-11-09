using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartHr.Application;

namespace SmartHr.Infrastructure;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string EmbedModel { get; set; } = "bge-m3";
    public string ChatModel { get; set; } = "llama3.2:3b";
    public int EmbedTimeoutSeconds { get; set; } = 60;
    public int ChatTimeoutSeconds { get; set; } = 180;
    public int MaxRetries { get; set; } = 3;
}

/// <summary>Thrown when Ollama is unreachable or keeps failing after retries.</summary>
public sealed class OllamaUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class OllamaClient(HttpClient http, OllamaOptions config, ILogger<OllamaClient> logger)
    : IEmbeddingService,
        IChatService
{
    public string ModelName => config.EmbedModel;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        using var json = await PostWithRetryAsync(
            "/api/embed",
            new { model = config.EmbedModel, input = text },
            TimeSpan.FromSeconds(config.EmbedTimeoutSeconds),
            ct
        );
        return json
            .RootElement.GetProperty("embeddings")[0]
            .EnumerateArray()
            .Select(x => x.GetSingle())
            .ToArray();
    }

    public async Task<string> AnswerAsync(string prompt, CancellationToken ct)
    {
        using var json = await PostWithRetryAsync(
            "/api/generate",
            new
            {
                model = config.ChatModel,
                prompt,
                stream = false,
                options = new { temperature = 0 },
            },
            TimeSpan.FromSeconds(config.ChatTimeoutSeconds),
            ct
        );
        return json.RootElement.GetProperty("response").GetString() ?? "";
    }

    private async Task<JsonDocument> PostWithRetryAsync(
        string path,
        object body,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        var attempts = Math.Max(1, config.MaxRetries);
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(timeout);
            try
            {
                using var response = await http.PostAsJsonAsync(path, body, attemptCts.Token);
                if (IsTransient(response.StatusCode))
                {
                    last = new HttpRequestException($"Ollama returned {(int)response.StatusCode}");
                }
                else
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var detail = await response.Content.ReadAsStringAsync(ct);
                        throw new OllamaUnavailableException(
                            $"Ollama {path} failed with {(int)response.StatusCode}: {detail}"
                        );
                    }

                    return await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync(ct),
                        cancellationToken: ct
                    );
                }
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                last = new TimeoutException($"Ollama {path} timed out after {timeout.TotalSeconds}s", ex);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }

            logger.LogWarning(last, "Ollama {Path} attempt {Attempt}/{Max} failed", path, attempt, attempts);
            if (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), ct);
            }
        }

        throw new OllamaUnavailableException(
            $"Ollama is unavailable at {http.BaseAddress} ({path}). Is the container running and the model pulled?",
            last
        );
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)code >= 500;
}
