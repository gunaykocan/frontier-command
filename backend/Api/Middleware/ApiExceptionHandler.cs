using Game.Application.Common;
using Game.Domain.Rules;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Game.Api.Middleware;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Match not found"),
            MatchConcurrencyException => (StatusCodes.Status409Conflict, "Match state changed"),
            DomainRuleException => (StatusCodes.Status422UnprocessableEntity, "Move rejected"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception");
        }
        else
        {
            logger.LogWarning(exception, "API request rejected with status {StatusCode}", status);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status >= StatusCodes.Status500InternalServerError
                    ? "The server could not complete the request."
                    : exception.Message
            },
            cancellationToken);

        return true;
    }
}
