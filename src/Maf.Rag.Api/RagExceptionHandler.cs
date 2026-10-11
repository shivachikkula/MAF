using Maf.Rag.Core;
using Maf.Rag.Core.Parsing;
using Microsoft.AspNetCore.Diagnostics;

namespace Maf.Rag.Api;

/// <summary>Maps domain exceptions to RFC 7807 problem responses.</summary>
internal sealed class RagExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            UnsupportedDocumentException => StatusCodes.Status415UnsupportedMediaType,
            EmptyDocumentException => StatusCodes.Status422UnprocessableEntity,
            BadHttpRequestException bad => bad.StatusCode,
            _ => 0,
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = exception.Message },
        });
    }
}
