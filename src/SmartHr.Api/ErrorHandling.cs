using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartHr.Infrastructure;

namespace SmartHr.Api;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails responses.</summary>
public sealed class ProblemExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ProblemExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var (status, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            NotSupportedException => (StatusCodes.Status400BadRequest, "Unsupported input"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid request"),
            OllamaUnavailableException => (StatusCodes.Status503ServiceUnavailable, "AI model service unavailable"),
            HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "Upstream service unavailable"),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested =>
                (499, "Client closed request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Request {Path} failed", context.Request.Path);
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = context,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = status,
                    Title = title,
                    Detail = status == 500 ? "An unexpected error occurred." : exception.Message,
                },
            }
        );
    }
}
