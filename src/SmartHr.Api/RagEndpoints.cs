using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartHr.Application;
using SmartHr.Domain;
using SmartHr.Infrastructure;

namespace SmartHr.Api;

public static class RagEndpoints
{
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("RAG");

        group.MapPost(
            "/search",
            async (SearchRequest request, RagService rag, CancellationToken ct) =>
                Results.Ok(await rag.SearchAsync(request.Question, request.TopK, ct, request.Mode))
        );

        group.MapPost(
            "/ask",
            async (
                SearchRequest request,
                RagService rag,
                RagDbContext db,
                CancellationToken ct
            ) =>
            {
                var watch = Stopwatch.StartNew();
                var answer = await rag.AskAsync(request.Question, request.TopK, ct, request.Mode);
                var log = new QuestionLog
                {
                    UserId = null, // anonymous (no login)
                    Question = request.Question.Trim(),
                    Answer = answer.Answer,
                    TopK = request.TopK,
                    LatencyMs = watch.ElapsedMilliseconds,
                    SourcesJson = JsonSerializer.Serialize(
                        answer.Sources.Select(s => new { s.ChunkId, s.DocumentName, s.Section, s.Score })
                    ),
                };
                db.QuestionLogs.Add(log);
                await db.SaveChangesAsync(ct);
                return Results.Ok(answer with { QuestionId = log.Id });
            }
        );

        group
            .MapGet(
                "/history",
                async (RagDbContext db, int? take, CancellationToken ct) =>
                {
                    var query = db.QuestionLogs.AsNoTracking();

                    var items = await query
                        .OrderByDescending(q => q.CreatedUtc)
                        .Take(Math.Clamp(take ?? 50, 1, 200))
                        .Select(q => new
                        {
                            q.Id,
                            q.Question,
                            q.Answer,
                            q.SourcesJson,
                            q.LatencyMs,
                            q.CreatedUtc,
                            Helpful = q.Feedback.OrderByDescending(f => f.CreatedUtc)
                                .Select(f => (bool?)f.Helpful)
                                .FirstOrDefault(),
                        })
                        .ToListAsync(ct);
                    return Results.Ok(items);
                }
            );

        group.MapPost(
            "/history/{id:guid}/feedback",
            async (Guid id, FeedbackRequest request, RagDbContext db, CancellationToken ct) =>
            {
                if (!await db.QuestionLogs.AnyAsync(q => q.Id == id, ct))
                {
                    return Results.NotFound();
                }

                db.AnswerFeedback.Add(
                    new AnswerFeedback
                    {
                        QuestionLogId = id,
                        UserId = null, // anonymous (no login)
                        Helpful = request.Helpful,
                        Comment = request.Comment?.Trim(),
                    }
                );
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }
        );

        var evaluation = group.MapGroup("/evaluate");

        evaluation.MapPost(
            "/",
            async (EvaluationRequest? request, RagService rag, RagDbContext db, CancellationToken ct) =>
            {
                var topK = request?.TopK is > 0 ? request.TopK : 4;
                var cases = await LoadCasesAsync(request, db, ct);
                var result = await rag.EvaluateAsync(cases, topK, ct, request?.Mode);
                var run = ToRun(topK, result);
                db.EvaluationRuns.Add(run);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new
                {
                    runId = run.Id,
                    topK,
                    mode = result.Mode,
                    questions = result.Questions,
                    answerableQuestions = result.AnswerableQuestions,
                    unanswerableQuestions = result.UnanswerableQuestions,
                    recallAtK = result.RecallAtK,
                    precisionAtK = result.PrecisionAtK,
                    mrr = result.Mrr,
                    meanLatencyMs = result.MeanLatencyMs,
                    rerankerConfigured = result.RerankerConfigured,
                    cases = result.Cases,
                });
            }
        );

        evaluation.MapPost(
            "/compare",
            async (EvaluationRequest? request, RagService rag, RagDbContext db, CancellationToken ct) =>
            {
                var topK = request?.TopK is > 0 ? request.TopK : 4;
                var cases = await LoadCasesAsync(request, db, ct);
                var results = await rag.EvaluateAllModesAsync(cases, topK, ct);
                foreach (var result in results)
                {
                    db.EvaluationRuns.Add(ToRun(topK, result));
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(new { topK, modes = results });
            }
        );

        group
            .MapPost(
                "/admin/reembed",
                async (bool? force, IServiceProvider services, CancellationToken ct) =>
                    Results.Ok(new { reembedded = await Reembedder.ReembedAsync(services, force ?? false, ct) })
            );

        evaluation.MapGet(
            "/runs",
            async (RagDbContext db, CancellationToken ct) =>
                Results.Ok(
                    await db.EvaluationRuns.AsNoTracking()
                        .OrderByDescending(r => r.CreatedUtc)
                        .Take(20)
                        .Select(r => new { r.Id, r.CreatedUtc, r.TopK, r.Mode, r.Questions, r.RecallAtK, r.PrecisionAtK, r.Mrr })
                        .ToListAsync(ct)
                )
        );

        evaluation.MapGet(
            "/cases",
            async (RagDbContext db, CancellationToken ct) =>
                Results.Ok(await db.RetrievalCases.AsNoTracking().OrderBy(c => c.ExpectedDocument).ToListAsync(ct))
        );

        return endpoints;
    }

    private static async Task<List<EvaluationCase>> LoadCasesAsync(
        EvaluationRequest? request,
        RagDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (request?.Cases is { Count: > 0 })
        {
            return request
                .Cases.Select(c => new EvaluationCase(
                    c.Question,
                    c.ExpectedDocument,
                    c.ExpectedSection,
                    c.ExpectedChunkId,
                    c.Unanswerable
                ))
                .ToList();
        }

        return await db.RetrievalCases.AsNoTracking()
            .Select(c => new EvaluationCase(
                c.Question,
                c.ExpectedDocument,
                c.ExpectedSection,
                c.ExpectedChunkId,
                c.Unanswerable
            ))
            .ToListAsync(cancellationToken);
    }

    private static EvaluationRun ToRun(int topK, EvaluationResult result) =>
        new()
        {
            TopK = topK,
            Questions = result.Questions,
            RecallAtK = result.RecallAtK,
            PrecisionAtK = result.PrecisionAtK,
            Mrr = result.Mrr,
            Mode = result.Mode,
            DetailsJson = JsonSerializer.Serialize(result.Cases),
        };
}
