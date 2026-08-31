using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioService.Common;
using PortfolioService.Configuration;
using PortfolioService.Domain;
using PortfolioService.Infrastructure;

namespace PortfolioService.Features.Trades;

public interface ITradeIngestionService
{
    Task<TradeIngestionResult> IngestAsync(TradeSubmissionRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<TradeEventResponse>> GetEventsAsync(string externalReference, CancellationToken cancellationToken);
}

public sealed record TradeIngestionResult(TradeIngestionOutcome Outcome, TradeEvent CurrentEvent, string Message);

public sealed partial class TradeIngestionService(
    PortfolioDbContext dbContext,
    IOptionsMonitor<TradeProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<TradeIngestionService> logger) : ITradeIngestionService
{
    public async Task<TradeIngestionResult> IngestAsync(
        TradeSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var externalReference = request.ExternalRef.Trim();
        var accountId = request.AccountId.Trim();
        var isin = request.Instrument.Isin.Trim().ToUpperInvariant();
        var asOfUtc = DateTime.SpecifyKind(request.AsOf.UtcDateTime, DateTimeKind.Utc);
        var tradeDateStartUtc = DateTime.SpecifyKind(request.TradeDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        if (asOfUtc < tradeDateStartUtc)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "invalid_temporal_order",
                "as_of cannot be earlier than the start of trade_date in UTC.");
        }

        var instrument = await dbContext.Instruments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Isin == isin, cancellationToken)
            ?? throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "unknown_instrument",
                $"No reference instrument exists for ISIN '{isin}'.");

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(
            () => IngestWithinTransactionAsync(
                request,
                externalReference,
                accountId,
                instrument.Id,
                asOfUtc,
                cancellationToken));
    }

    public async Task<IReadOnlyList<TradeEventResponse>> GetEventsAsync(
        string externalReference,
        CancellationToken cancellationToken)
    {
        var normalizedReference = externalReference.Trim();
        var events = await dbContext.TradeEvents
            .AsNoTracking()
            .Include(x => x.Instrument)
            .Where(x => x.ExternalReference == normalizedReference)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync(cancellationToken);

        return events
            .Select(x => new TradeEventResponse(
                x.Id,
                x.ExternalReference,
                x.AccountId,
                x.Instrument.Isin,
                x.Instrument.Symbol,
                x.Side,
                x.Quantity,
                x.UnitPrice,
                x.TradeDate,
                EnsureUtc(x.AsOfUtc),
                EnsureUtc(x.ReceivedAtUtc),
                x.EventKind,
                x.VersionNumber))
            .ToList();
    }

    private async Task<TradeIngestionResult> IngestWithinTransactionAsync(
        TradeSubmissionRequest request,
        string externalReference,
        string accountId,
        int instrumentId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // UPDLOCK + HOLDLOCK serializes all versions of one external reference. The
        // unique indexes remain the final integrity guard if another write path is added.
        var latest = await dbContext.TradeEvents
            .FromSqlInterpolated($"""
                SELECT *
                FROM [portfolio].[TradeEvents] WITH (UPDLOCK, HOLDLOCK)
                WHERE [ExternalReference] = {externalReference}
                """)
            .AsNoTracking()
            .OrderByDescending(x => x.AsOfUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            var initialEvent = CreateEvent(
                request,
                externalReference,
                accountId,
                instrumentId,
                asOfUtc,
                TradeEventKind.Initial,
                versionNumber: 1);
            dbContext.TradeEvents.Add(initialEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            LogInitialAccepted(
                logger,
                initialEvent.Id,
                externalReference,
                initialEvent.VersionNumber);
            return new TradeIngestionResult(
                TradeIngestionOutcome.Accepted,
                initialEvent,
                "Initial trade event accepted.");
        }

        if (asOfUtc < latest.AsOfUtc)
        {
            await transaction.CommitAsync(cancellationToken);
            LogStaleIgnored(
                logger,
                externalReference,
                asOfUtc,
                latest.AsOfUtc);
            return new TradeIngestionResult(
                TradeIngestionOutcome.Stale,
                latest,
                "Event ignored because a later version is already current.");
        }

        if (asOfUtc == latest.AsOfUtc)
        {
            if (!HasSamePayload(latest, request, accountId, instrumentId))
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    "event_version_conflict",
                    "The same external_ref and as_of already exist with a different payload.");
            }

            await transaction.CommitAsync(cancellationToken);
            LogDuplicateIgnored(
                logger,
                externalReference,
                asOfUtc);
            return new TradeIngestionResult(
                TradeIngestionOutcome.Duplicate,
                latest,
                "Exact duplicate ignored; no portfolio impact was applied.");
        }

        if (!HasSameIdentity(latest, request, accountId, instrumentId))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "immutable_trade_identity_changed",
                "A correction may change quantity or price, but not account, instrument, side, or trade_date.");
        }

        if (latest.Quantity == request.Quantity && latest.UnitPrice == request.Price)
        {
            await transaction.CommitAsync(cancellationToken);
            LogNoChangeIgnored(
                logger,
                externalReference,
                asOfUtc);
            return new TradeIngestionResult(
                TradeIngestionOutcome.NoChange,
                latest,
                "Later event ignored because quantity and price are unchanged.");
        }

        if (!options.CurrentValue.CorrectionProcessingEnabled)
        {
            LogCorrectionDisabled(logger, externalReference);
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "correction_processing_disabled",
                "Correction processing is currently disabled; retry when the feature is enabled.");
        }

        var correction = CreateEvent(
            request,
            externalReference,
            accountId,
            instrumentId,
            asOfUtc,
            TradeEventKind.Correction,
            latest.VersionNumber + 1);
        dbContext.TradeEvents.Add(correction);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogCorrectionAccepted(
            logger,
            correction.Id,
            externalReference,
            latest.VersionNumber,
            correction.VersionNumber);
        return new TradeIngestionResult(
            TradeIngestionOutcome.Corrected,
            correction,
            "Later correction accepted; the previous version remains in the audit trail.");
    }

    private TradeEvent CreateEvent(
        TradeSubmissionRequest request,
        string externalReference,
        string accountId,
        int instrumentId,
        DateTime asOfUtc,
        TradeEventKind eventKind,
        int versionNumber)
    {
        return TradeEvent.Create(
            externalReference,
            accountId,
            instrumentId,
            request.Side,
            request.Quantity,
            request.Price,
            request.TradeDate,
            asOfUtc,
            timeProvider.GetUtcNow().UtcDateTime,
            eventKind,
            versionNumber);
    }

    private static bool HasSamePayload(
        TradeEvent current,
        TradeSubmissionRequest request,
        string accountId,
        int instrumentId)
    {
        return HasSameIdentity(current, request, accountId, instrumentId)
            && current.Quantity == request.Quantity
            && current.UnitPrice == request.Price;
    }

    private static bool HasSameIdentity(
        TradeEvent current,
        TradeSubmissionRequest request,
        string accountId,
        int instrumentId)
    {
        return string.Equals(current.AccountId, accountId, StringComparison.OrdinalIgnoreCase)
            && current.InstrumentId == instrumentId
            && current.Side == request.Side
            && current.TradeDate == request.TradeDate;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Accepted initial trade event {EventId} for {ExternalReference} at version {VersionNumber}")]
    private static partial void LogInitialAccepted(
        ILogger logger,
        long eventId,
        string externalReference,
        int versionNumber);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Ignored stale trade event for {ExternalReference}; received {ReceivedAsOfUtc}, current {CurrentAsOfUtc}")]
    private static partial void LogStaleIgnored(
        ILogger logger,
        string externalReference,
        DateTime receivedAsOfUtc,
        DateTime currentAsOfUtc);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Ignored duplicate trade event for {ExternalReference} at {AsOfUtc}")]
    private static partial void LogDuplicateIgnored(
        ILogger logger,
        string externalReference,
        DateTime asOfUtc);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Ignored no-change trade event for {ExternalReference} at {AsOfUtc}")]
    private static partial void LogNoChangeIgnored(
        ILogger logger,
        string externalReference,
        DateTime asOfUtc);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Rejected correction for {ExternalReference} because correction processing is disabled")]
    private static partial void LogCorrectionDisabled(ILogger logger, string externalReference);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Information,
        Message = "Accepted correction event {EventId} for {ExternalReference}; version advanced from {PreviousVersion} to {VersionNumber}")]
    private static partial void LogCorrectionAccepted(
        ILogger logger,
        long eventId,
        string externalReference,
        int previousVersion,
        int versionNumber);
}
