using SmartHr.Application;

namespace SmartHr.Api;

public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/policies").WithTags("Policies");

        group.MapPost(
            "/text",
            async (
                TextUploadRequest request,
                RagService rag,
                CancellationToken cancellationToken
            ) =>
            {
                if (
                    string.IsNullOrWhiteSpace(request.Name)
                    || string.IsNullOrWhiteSpace(request.Text)
                )
                {
                    return Results.BadRequest(new { message = "Name and text are required." });
                }

                var documentId = await rag.IngestAsync(
                    request.Name,
                    "inline",
                    request.Text,
                    cancellationToken
                );

                return Results.Ok(new { documentId });
            }
        );

        group
            .MapPost(
                "/upload",
                async (
                    IFormFile? file,
                    RagService rag,
                    ITextExtractor extractor,
                    CancellationToken cancellationToken
                ) =>
                {
                    if (file is null || file.Length is 0 or > 10_000_000)
                    {
                        return Results.BadRequest(
                            new { message = "File size must be 1 byte to 10 MB." }
                        );
                    }

                    string text;
                    await using (var stream = file.OpenReadStream())
                    {
                        try
                        {
                            text = extractor.Extract(file.FileName, stream);
                        }
                        catch (NotSupportedException exception)
                        {
                            return Results.BadRequest(new { message = exception.Message });
                        }
                    }

                    var documentId = await rag.IngestAsync(
                        Path.GetFileNameWithoutExtension(file.FileName),
                        file.FileName,
                        text,
                        cancellationToken
                    );

                    return Results.Ok(new { documentId });
                }
            )
            .DisableAntiforgery();

        group.MapGet(
            "/{id:guid}",
            async (Guid id, IPolicyRepository repository, CancellationToken cancellationToken) =>
            {
                var document = await repository.GetDocumentAsync(id, cancellationToken);
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
                        Chunks = document
                            .Chunks.OrderBy(chunk => chunk.Sequence)
                            .Select(chunk => new
                            {
                                chunk.Id,
                                chunk.Sequence,
                                chunk.Section,
                                chunk.Content,
                            }),
                    }
                );
            }
        );

        return endpoints;
    }
}
