using System.Diagnostics;

namespace PortfolioService.Common;

public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var suppliedCorrelationId = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsValid(suppliedCorrelationId)
            ? suppliedCorrelationId!
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
            if (logger.IsEnabled(LogLevel.Information))
            {
                var elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                LogRequestCompleted(
                    logger,
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    elapsedMilliseconds);
            }
        }
    }

    private static bool IsValid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 64
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "HTTP {Method} {Path} completed with {StatusCode} in {ElapsedMilliseconds} ms")]
    private static partial void LogRequestCompleted(
        ILogger logger,
        string method,
        string? path,
        int statusCode,
        double elapsedMilliseconds);
}
