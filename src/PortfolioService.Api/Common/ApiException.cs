namespace PortfolioService.Common;

public sealed class ApiException(int statusCode, string code, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;
}
