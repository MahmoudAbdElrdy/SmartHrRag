using System.Net.Http.Json;
using System.Text.Json;
using SmartHr.Application;

namespace SmartHr.Infrastructure;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string EmbedModel { get; set; } = "nomic-embed-text";
    public string ChatModel { get; set; } = "llama3.2:3b";
}

public sealed class OllamaClient(HttpClient http, OllamaOptions config)
    : IEmbeddingService,
        IChatService
{
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            "/api/embed",
            new { model = config.EmbedModel, input = text },
            ct
        );
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        return json
            .RootElement.GetProperty("embeddings")[0]
            .EnumerateArray()
            .Select(x => x.GetSingle())
            .ToArray();
    }

    public async Task<string> AnswerAsync(string prompt, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            "/api/generate",
            new
            {
                model = config.ChatModel,
                prompt,
                stream = false,
                options = new { temperature = 0 },
            },
            ct
        );
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        return json.RootElement.GetProperty("response").GetString() ?? "";
    }
}
