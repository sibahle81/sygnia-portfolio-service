using Microsoft.AspNetCore.Mvc;
using PortfolioService.Common;

namespace PortfolioService.Features.Trades;

[ApiController]
[Route("api/v1/trades")]
public sealed class TradesController(ITradeIngestionService tradeIngestionService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<TradeSubmissionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<TradeSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TradeSubmissionResponse>> Submit(
        [FromBody] TradeSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await tradeIngestionService.IngestAsync(request, cancellationToken);
        var response = new TradeSubmissionResponse(
            result.Outcome,
            result.CurrentEvent.ExternalReference,
            result.CurrentEvent.Id,
            result.CurrentEvent.VersionNumber,
            DateTime.SpecifyKind(result.CurrentEvent.AsOfUtc, DateTimeKind.Utc),
            result.Message);

        return result.Outcome is TradeIngestionOutcome.Accepted or TradeIngestionOutcome.Corrected
            ? CreatedAtAction(
                nameof(GetEvents),
                new { externalReference = result.CurrentEvent.ExternalReference },
                response)
            : Ok(response);
    }

    [HttpGet("{externalReference}/events")]
    [ProducesResponseType<IReadOnlyList<TradeEventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TradeEventResponse>>> GetEvents(
        string externalReference,
        CancellationToken cancellationToken)
    {
        var events = await tradeIngestionService.GetEventsAsync(externalReference, cancellationToken);
        if (events.Count == 0)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "trade_not_found",
                $"No accepted events exist for external reference '{externalReference}'.");
        }

        return Ok(events);
    }
}
