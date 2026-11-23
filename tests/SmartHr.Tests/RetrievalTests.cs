using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmartHr.Application;
using SmartHr.Infrastructure;

namespace SmartHr.Tests;

public class RetrievalTests
{
    [Fact]
    public void Normalizer_maps_alef_variants_alef_maksura_and_teh_marbuta()
    {
        Assert.Equal("اااا", ArabicNormalizer.Normalize("أإآٱ"));
        Assert.Equal("ي", ArabicNormalizer.Normalize("ى"));
        Assert.Equal("ه", ArabicNormalizer.Normalize("ة"));
        Assert.Equal("اجازه", ArabicNormalizer.Normalize("إجازة"));
        Assert.Equal("اجازه", ArabicNormalizer.Normalize("اجازه"));
    }

    [Fact]
    public void Normalizer_strips_tashkeel_and_tatweel_and_keeps_latin()
    {
        Assert.Equal("اجازه", ArabicNormalizer.Normalize("إِجَازَة"));
        Assert.Equal("كتاب", ArabicNormalizer.Normalize("كِتَابْ"));
        Assert.Equal("اجازه", ArabicNormalizer.Normalize("اجـازة"));
        Assert.Equal("رحمن", ArabicNormalizer.Normalize("رحم\u0670ن"));
        Assert.Equal("Policy 25%", ArabicNormalizer.Normalize("Policy 25%"));
        Assert.Equal("", ArabicNormalizer.Normalize(null));
        Assert.Equal("", ArabicNormalizer.Normalize(""));

        var original = "إجازة";
        _ = ArabicNormalizer.Normalize(original);
        Assert.Equal("إجازة", original);
    }

    [Fact]
    public void Tokenizer_folds_misspellings_and_the_definite_article()
    {
        Assert.Equal(["اجازه"], LexicalTokenizer.Tokenize("إجازة"));
        Assert.Equal(["اجازه"], LexicalTokenizer.Tokenize("اجازه"));
        Assert.Equal(["اجازه"], LexicalTokenizer.Tokenize("الإجازة"));
        Assert.Equal(["الاجازه"], ArabicNormalizer.Normalize("الإجازة").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(LexicalTokenizer.Tokenize("شهادة طبية"), LexicalTokenizer.Tokenize("شهاده طبيه"));
        Assert.Equal(LexicalTokenizer.Tokenize("يُصرف"), LexicalTokenizer.Tokenize("يصرف"));
        Assert.Equal(["policy", "25"], LexicalTokenizer.Tokenize("Policy 25%"));
        Assert.Empty(LexicalTokenizer.Tokenize("   "));
    }

    [Fact]
    public void Bm25_ranks_normalized_arabic_and_exact_percentages()
    {
        var payroll = Chunk("موعد", "يُصرف الراتب يوم 27 من كل شهر.");
        var leave = Chunk("الاستحقاق", "الموظف يستحق 21 يوم إجازة سنوية.");
        var overtime = Chunk("Rates", "Overtime on weekends is paid at 150% of the hourly wage.");
        var sick = Chunk("Paid", "The next 10 days at 75% pay.");

        var arabic = Bm25.Rank([payroll, leave], "اجازه سنويه");
        Assert.Equal(leave.ChunkId, arabic[0].Chunk.ChunkId);
        Assert.DoesNotContain(arabic, hit => hit.Chunk.ChunkId == payroll.ChunkId);

        var percent = Bm25.Rank([sick, overtime], "150%");
        Assert.Equal(overtime.ChunkId, percent[0].Chunk.ChunkId);
        Assert.DoesNotContain(percent, hit => hit.Chunk.ChunkId == sick.ChunkId);
    }

    [Fact]
    public void Rrf_sums_reciprocal_ranks_with_k_60_and_ignores_duplicates()
    {
        var fused = ReciprocalRankFusion.Fuse<string, string>(
            [["both", "only-vector"], ["both", "only-keyword"]],
            item => item
        );

        Assert.Equal("both", fused[0].Item);
        Assert.Equal(1d / 61 + 1d / 61, fused[0].Score, 12);
        Assert.Equal(1d / 62, fused.Single(item => item.Item == "only-keyword").Score, 12);
        Assert.Equal(1d / 62, fused.Single(item => item.Item == "only-vector").Score, 12);

        var duplicates = ReciprocalRankFusion.Fuse<string, string>([["a", "a", "b"]], item => item, 60);
        Assert.Equal(["a", "b"], duplicates.Select(item => item.Item).ToArray());
        Assert.Equal(1d / 61, duplicates[0].Score, 12);
        Assert.Equal(1d / 62, duplicates[1].Score, 12);
    }

    [Fact]
    public void Rrf_k_changes_the_score_and_rejects_non_positive_k()
    {
        var fused = ReciprocalRankFusion.Fuse<string, string>([["a"]], item => item, 1);
        Assert.Equal(0.5, fused[0].Score, 12);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReciprocalRankFusion.Fuse<string, string>([["a"]], item => item, 0)
        );
        Assert.Empty(ReciprocalRankFusion.Fuse<string, string>([], item => item));
    }

    [Fact]
    public void Rrf_item_in_both_lists_beats_an_item_in_only_one()
    {
        var fused = ReciprocalRankFusion.Fuse<string, string>(
            [["shared", "vector-only"], ["shared"]],
            item => item,
            60
        );

        Assert.Equal("shared", fused[0].Item);
        Assert.True(fused[0].Score > fused.Single(item => item.Item == "vector-only").Score);
    }

    [Fact]
    public async Task Disabled_or_failing_reranker_keeps_candidate_order()
    {
        var hits = new List<SearchHit> { Hit("a", 0.4), Hit("b", 0.3), Hit("c", 0.2) };

        var disabled = await Reranker.ApplyAsync(new ScriptedEncoder { IsEnabled = false }, "q", hits, default);
        Assert.False(disabled.Applied);
        Assert.Equal(["a", "b", "c"], Sections(disabled.Hits));

        var failed = await Reranker.ApplyAsync(
            new ScriptedEncoder { Error = new HttpRequestException("connection refused") },
            "q",
            hits,
            default
        );
        Assert.False(failed.Applied);
        Assert.Equal(["a", "b", "c"], Sections(failed.Hits));

        var timedOut = await Reranker.ApplyAsync(
            new ScriptedEncoder { Error = new TimeoutException("timed out") },
            "q",
            hits,
            default
        );
        Assert.False(timedOut.Applied);
        Assert.Same(hits, timedOut.Hits);
    }

    [Fact]
    public async Task Reranker_reorders_by_score_and_propagates_caller_cancellation()
    {
        var hits = new List<SearchHit> { Hit("a", 0.2), Hit("b", 0.2), Hit("c", 0.2) };
        var ranked = await Reranker.ApplyAsync(
            new ScriptedEncoder { Scores = [new RerankScore(2, 0.9), new RerankScore(0, 0.4)] },
            "q",
            hits,
            default
        );

        Assert.True(ranked.Applied);
        Assert.Equal(["c", "a", "b"], Sections(ranked.Hits));
        Assert.Equal(0.9, ranked.Hits[0].Score, 6);
        Assert.Equal(0.4, ranked.Hits[1].Score, 6);
        Assert.Equal(0.2, ranked.Hits[2].Score, 6);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Reranker.ApplyAsync(new CancellingEncoder(), "q", hits, cts.Token)
        );
    }

    [Fact]
    public void Parser_reads_tei_and_cohere_payloads()
    {
        var tei = RerankResponseParser.Parse("""[{"index":1,"score":0.2},{"index":0,"score":0.8}]""");
        Assert.Equal(1, tei[0].Index);
        Assert.Equal(0.8, tei[1].Score, 6);

        var cohere = RerankResponseParser.Parse("""{"results":[{"index":0,"relevance_score":0.4}]}""");
        Assert.Equal(0.4, cohere[0].Score, 6);

        Assert.Throws<JsonException>(() => RerankResponseParser.Parse("""{"error":"nope"}"""));
    }

    [Fact]
    public async Task Tei_client_falls_back_when_unreachable_and_reranks_a_successful_response()
    {
        var hits = new List<SearchHit> { Hit("first"), Hit("second") };
        var down = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));
        var fallback = await Reranker.ApplyAsync(Client(down, enabled: true), "leave", hits, default);
        Assert.False(fallback.Applied);
        Assert.Equal(["first", "second"], Sections(fallback.Hits));

        var disabledHandler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var disabled = await Reranker.ApplyAsync(
            Client(disabledHandler, enabled: false, baseUrl: ""),
            "leave",
            hits,
            default
        );
        Assert.Equal(0, disabledHandler.Calls);
        Assert.False(disabled.Applied);

        var up = new StubHandler(async (request, _) =>
        {
            var json = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"texts\"", json);
            Assert.Contains("\"query\":\"leave\"", json);
            Assert.Contains("\"raw_scores\":false", json);
            Assert.DoesNotContain("\"documents\"", json);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"index":1,"score":0.91},{"index":0,"score":0.2}]""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        });
        var ranked = await Reranker.ApplyAsync(Client(up, enabled: true), "leave", hits, default);
        Assert.True(ranked.Applied);
        Assert.Equal("second", ranked.Hits[0].Section);
        Assert.Equal(0.91, ranked.Hits[0].Score, 5);
        Assert.Equal(0.2, ranked.Hits[1].Score, 5);
    }

    [Fact]
    public async Task Tei_client_timeout_falls_back_and_caller_cancellation_propagates()
    {
        var hits = new List<SearchHit> { Hit("a"), Hit("b") };
        var timeout = new StubHandler((_, _) => throw new OperationCanceledException("timeout"));
        var result = await Reranker.ApplyAsync(Client(timeout, enabled: true), "q", hits, default);
        Assert.False(result.Applied);
        Assert.Equal(["a", "b"], Sections(result.Hits));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new StubHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Reranker.ApplyAsync(Client(cancelled, enabled: true), "q", hits, cts.Token)
        );
    }

    private static CachedChunk Chunk(string section, string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Policy", section, text, [1f]);

    private static SearchHit Hit(string section, double score = 0) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Doc", section, section + " text", score);

    private static string[] Sections(IReadOnlyList<SearchHit> hits) => hits.Select(hit => hit.Section).ToArray();

    private static TeiRerankClient Client(HttpMessageHandler handler, bool enabled, string? baseUrl = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://reranker.test/") };
        return new TeiRerankClient(
            http,
            new RetrievalOptions
            {
                Reranker = new RerankerOptions
                {
                    Enabled = enabled,
                    BaseUrl = baseUrl ?? "http://reranker.test",
                    TimeoutSeconds = 2,
                },
            },
            NullLogger<TeiRerankClient>.Instance
        );
    }

    private sealed class ScriptedEncoder : ICrossEncoder
    {
        public bool IsEnabled { get; init; } = true;
        public Exception? Error { get; init; }
        public IReadOnlyList<RerankScore> Scores { get; init; } = [];

        public Task<IReadOnlyList<RerankScore>> ScoreAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken
        ) =>
            Error is null
                ? Task.FromResult(Scores)
                : Task.FromException<IReadOnlyList<RerankScore>>(Error);
    }

    private sealed class CancellingEncoder : ICrossEncoder
    {
        public bool IsEnabled => true;

        public Task<IReadOnlyList<RerankScore>> ScoreAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RerankScore>>([]);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }
}
