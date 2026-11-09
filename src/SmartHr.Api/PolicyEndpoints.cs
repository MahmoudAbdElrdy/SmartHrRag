using SmartHr.Application;
using SmartHr.Domain;

namespace SmartHr.Api;

public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/policies").WithTags("Policies");

        group.MapGet(
            "/",
            async (IPolicyRepository repository, CancellationToken ct) =>
                Results.Ok(await repository.ListDocumentsAsync(ct))
        );

        group
            .MapPost(
                "/text",
                async (TextUploadRequest request, RagService rag, CancellationToken ct) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Text))
                    {
                        throw new ArgumentException("Name and text are required.");
                    }

                    return Results.Ok(await rag.IngestAsync(request.Name, "inline", request.Text, ct));
                }
            );

        group
            .MapPost(
                "/upload",
                async (IFormFile? file, RagService rag, ITextExtractor extractor, CancellationToken ct) =>
                {
                    if (file is null || file.Length is 0 or > 10_000_000)
                    {
                        throw new ArgumentException("File size must be 1 byte to 10 MB.");
                    }

                    string text;
                    await using (var stream = file.OpenReadStream())
                    {
                        text = extractor.Extract(file.FileName, stream);
                    }

                    var result = await rag.IngestAsync(
                        Path.GetFileNameWithoutExtension(file.FileName),
                        file.FileName,
                        text,
                        ct
                    );
                    return Results.Ok(result);
                }
            )
            .DisableAntiforgery();

        group.MapGet(
            "/{id:guid}",
            async (Guid id, IPolicyRepository repository, CancellationToken ct) =>
            {
                var document = await repository.GetDocumentAsync(id, ct);
                if (document is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(
                    new
                    {
                        document.Id,
                        document.Name,
                        document.SourceFile,
                        document.Status,
                        document.CreatedUtc,
                        Chunks = document
                            .Chunks.OrderBy(chunk => chunk.Sequence)
                            .Select(chunk => new { chunk.Id, chunk.Sequence, chunk.Section, chunk.Content }),
                    }
                );
            }
        );

        group
            .MapDelete(
                "/{id:guid}",
                async (Guid id, RagService rag, CancellationToken ct) =>
                    await rag.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound()
            );

        return endpoints;
    }
}
