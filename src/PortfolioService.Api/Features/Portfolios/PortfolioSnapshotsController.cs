using Microsoft.AspNetCore.Mvc;
using PortfolioService.Common;

namespace PortfolioService.Features.Portfolios;

[ApiController]
[Route("api/v1/portfolios/{accountId}/snapshots")]
public sealed class PortfolioSnapshotsController(IPortfolioSnapshotService snapshotService) : ControllerBase
{
    [HttpGet("{valuationDate}")]
    [ProducesResponseType<PortfolioSnapshotResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PortfolioSnapshotResponse>> Get(
        string accountId,
        DateOnly valuationDate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 64)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "invalid_account_id",
                "account_id must contain between 1 and 64 characters.");
        }

        var snapshot = await snapshotService.GetSnapshotAsync(accountId, valuationDate, cancellationToken);
        return Ok(snapshot);
    }
}
