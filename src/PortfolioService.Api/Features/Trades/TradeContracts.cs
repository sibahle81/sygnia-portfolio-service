using System.ComponentModel.DataAnnotations;
using PortfolioService.Domain;

namespace PortfolioService.Features.Trades;

public sealed class TradeSubmissionRequest : IValidatableObject
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression(@"^[A-Za-z0-9._:/-]+$")]
    public required string ExternalRef { get; init; }

    [Required]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression(@"^[A-Za-z0-9._:/-]+$")]
    public required string AccountId { get; init; }

    [Required]
    public required InstrumentReferenceRequest Instrument { get; init; }

    [EnumDataType(typeof(TradeSide))]
    public TradeSide Side { get; init; }

    public decimal Quantity { get; init; }

    public decimal Price { get; init; }

    public DateOnly TradeDate { get; init; }

    public DateTimeOffset AsOf { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TradeDate == default)
        {
            yield return new ValidationResult("trade_date is required.", [nameof(TradeDate)]);
        }

        if (AsOf == default)
        {
            yield return new ValidationResult("as_of is required.", [nameof(AsOf)]);
        }

        if (Quantity is <= 0m or > 9_999_999_999_999m)
        {
            yield return new ValidationResult(
                "quantity must be greater than zero and no more than 9999999999999.",
                [nameof(Quantity)]);
        }

        if (Price is <= 0m or > 9_999_999_999_999m)
        {
            yield return new ValidationResult(
                "price must be greater than zero and no more than 9999999999999.",
                [nameof(Price)]);
        }
    }
}

public sealed class InstrumentReferenceRequest
{
    [Required]
    [RegularExpression("^[A-Za-z0-9]{12}$")]
    public required string Isin { get; init; }
}

public enum TradeIngestionOutcome
{
    Accepted,
    Corrected,
    Duplicate,
    Stale,
    NoChange,
}

public sealed record TradeSubmissionResponse(
    TradeIngestionOutcome Outcome,
    string ExternalRef,
    long EventId,
    int CurrentVersion,
    DateTime CurrentAsOf,
    string Message);

public sealed record TradeEventResponse(
    long EventId,
    string ExternalRef,
    string AccountId,
    string Isin,
    string Symbol,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    DateOnly TradeDate,
    DateTime AsOf,
    DateTime ReceivedAt,
    TradeEventKind EventKind,
    int Version);
