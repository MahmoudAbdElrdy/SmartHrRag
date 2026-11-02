using SmartHr.Application;

namespace SmartHr.Api;

public static class RagEndpoints
{
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("RAG");

        group.MapPost(
            "/search",
            async (SearchRequest request, RagService rag, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                {
                    return Results.BadRequest(new { message = "A question is required." });
                }

                var hits = await rag.SearchAsync(request.Question, request.TopK, cancellationToken);

                return Results.Ok(hits);
            }
        );

        group.MapPost(
            "/ask",
            async (SearchRequest request, RagService rag, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                {
                    return Results.BadRequest(new { message = "A question is required." });
                }

                var answer = await rag.AskAsync(request.Question, request.TopK, cancellationToken);

                return Results.Ok(answer);
            }
        );

        group.MapPost(
            "/evaluate",
            async (
                EvaluationRequest request,
                RagService rag,
                CancellationToken cancellationToken
            ) =>
            {
                if (request.Cases is null || request.Cases.Count == 0)
                {
                    return Results.BadRequest(
                        new { message = "At least one evaluation case is required." }
                    );
                }

                var cases = request
                    .Cases.Select(item => (item.Question, item.ExpectedChunkId))
                    .ToList();

                var result = await rag.EvaluateAsync(cases, request.TopK, cancellationToken);

                return Results.Ok(result);
            }
        );

        return endpoints;
    }
}
