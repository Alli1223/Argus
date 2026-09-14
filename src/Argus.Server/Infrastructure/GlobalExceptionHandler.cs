using Microsoft.AspNetCore.Diagnostics;

namespace Argus.Server.Infrastructure;

/// <summary>
/// Turns client errors raised while binding requests (malformed JSON, oversized bodies, …) into
/// ProblemDetails responses. Anything else falls through to the default 500 handling.
/// </summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case BadHttpRequestException badRequest:
                context.Response.StatusCode = badRequest.StatusCode;
                return await problemDetails.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    Exception = exception,
                    ProblemDetails = { Status = badRequest.StatusCode, Title = "Bad request", Detail = badRequest.Message },
                });

            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                // The client disconnected; there is nobody left to answer.
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                return true;

            default:
                return false;
        }
    }
}
