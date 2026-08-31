using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PortfolioService.Common;

public sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var apiException = exception as ApiException;
        var statusCode = apiException?.StatusCode ?? StatusCodes.Status500InternalServerError;
        var code = apiException?.Code ?? "unexpected_error";
        var detail = apiException?.Message ?? "An unexpected error occurred.";

        if (apiException is null)
        {
            LogUnhandledException(logger, exception);
        }
        else
        {
            LogApiException(logger, code, statusCode, exception);
        }

        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = apiException is null ? "Unexpected error" : "Request could not be processed",
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["trace_id"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Error,
        Message = "Unhandled exception while processing the HTTP request")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Request rejected with API error {ErrorCode} and status {StatusCode}")]
    private static partial void LogApiException(
        ILogger logger,
        string errorCode,
        int statusCode,
        Exception exception);
}
