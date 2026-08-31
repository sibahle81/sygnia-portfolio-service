# Solution design and trade-offs

## Scope

The implementation deliberately focuses on the two required tasks: correct trade versioning, USD snapshots, operability, and a production-shaped SQL artifact. FX, cash, authentication, and a UI are excluded so the core behavior is complete, explainable, and tested within the intended timebox.

The code uses feature-oriented folders in one deployable API project. A larger platform could split domain, application, and infrastructure assemblies, but that separation would add ceremony without creating a useful boundary for this service yet.

## Trade-event model

`TradeEvents` is an append-only table of accepted business versions. An initial event is version 1. A correction with the same `external_ref` and a later `as_of` creates a new row and increments `VersionNumber`; no prior row is updated.

The processing rules are:

- no prior event: accept an initial version and return `201`;
- same `external_ref`, same `as_of`, identical payload: return `200 duplicate` without writing;
- earlier `as_of`: return `200 stale` without writing;
- same `as_of`, different payload: return `409 event_version_conflict` because the event identity is ambiguous;
- later `as_of`, unchanged quantity and price: return `200 no_change` without writing;
- later `as_of`, changed quantity or price: append a correction and return `201`;
- a correction cannot silently move account, instrument, side, or trade date. Those identity changes return `409` and would require an explicit cancel/rebook design.

This retains the audit history while ensuring downstream totals use one effective version per external reference.

### Concurrency and idempotency

The service takes an update/range lock for one `external_ref` with SQL Server `UPDLOCK, HOLDLOCK` inside a transaction. This serializes concurrent versions for the same business key while unrelated references proceed independently. Two unique indexes provide final integrity guards for `(ExternalReference, AsOfUtc)` and `(ExternalReference, VersionNumber)` if another writer is introduced later.

The concurrent integration test sends eight identical requests simultaneously and asserts that one accepted row exists. SQL Server transient retry is enabled for short infrastructure failures.

## Snapshot semantics

A snapshot for account `A` and date `D` represents information known by the end of `D` in UTC:

- `TradeDate <= D`;
- `AsOfUtc < D + 1 day`;
- latest accepted version per `ExternalReference` inside that cutoff;
- signed quantity is BUY quantity minus SELL quantity;
- zero positions are omitted;
- the latest USD closing price on or before `D` is used.

A correction received on 2025-03-02 therefore does not rewrite the 2025-03-01 knowledge-as-of snapshot. This is intentional and makes historical operational reports reproducible. A separate economic restatement mode could apply the latest correction regardless of receipt date, but that is a different query contract and is not mixed into this endpoint.

### Cost and rounding assumptions

Internal quantity, price, and value columns use `decimal(19,6)`. API monetary responses are rounded to two decimals with `MidpointRounding.AwayFromZero`.

Unit cost is the weighted average acquisition price from effective BUY events:

`sum(BUY quantity * BUY price) / sum(BUY quantity)`

SELL events reduce the open quantity without changing this average. Unrealized P/L is `net quantity * (market price - unit cost)`. This simplified method does not model realized P/L, tax lots, corporate actions, fees, or a reliable short-position cost basis. Those would need a dedicated position/cost engine and explicit accounting rules.

Only USD instruments and USD prices are supported. Missing prices or non-USD data fail visibly with `422` instead of silently understating account value. Cash and FX are not included, so `overall_value_usd` equals `total_market_value_usd`.

## SQL deep dive

`portfolio.GetPortfolioSnapshot` returns two result sets:

1. instrument quantity, weighted unit cost, market price, market value, and unrealized P/L;
2. account total market value and overall value.

It is parameterized and set-based. `ROW_NUMBER()` selects the effective correction version, aggregation calculates positions and buy cost in one pass, and `OUTER APPLY TOP (1)` selects the latest eligible price. A temporary table materializes the valued positions once so both result sets are consistent without repeating the expensive portion of the query. The procedure throws if a required price or USD constraint is violated.

### Indexing, SARGability, and higher volumes

The main snapshot index starts with `AccountId`, then `ExternalReference`, then descending `AsOfUtc`, and includes the trade and valuation columns used by the procedure. The ingestion key index supports the range lock and version lookup. The price index starts with `InstrumentId` and descending `PriceDate`, including price and currency for the `TOP (1)` seek.

Predicates compare stored columns directly to parameters; no functions are applied to `TradeDate` or `AsOfUtc`, so they remain SARGable. At scale I would validate index choices with actual execution plans, Query Store, production cardinalities, logical reads, and representative large-account workloads rather than adding overlapping indexes speculatively.

For very large history, partition `TradeEvents` by a time column aligned with the dominant retention and archive workflow, likely `TradeDate` or `ReceivedAtUtc`. Keep recent partitions online, switch closed partitions to a history table or cheaper storage, compress cold partitions, and retain a queryable audit path for regulatory requirements. Partitioning does not replace the external-reference indexes; the partition key and uniqueness design must be revisited together. Batch snapshots could be persisted by account/date after a close-of-business watermark if read volume justifies it.

## Operability and rollout

Operational behavior includes:

- interactive Swagger/OpenAPI documentation, with the service root redirecting to it;
- source-generated structured log events for accepted, duplicate, stale, corrected, disabled, and snapshot outcomes;
- caller-supplied or generated correlation IDs in a logging scope and response header;
- centralized RFC 7807 problem responses with stable machine-readable codes and trace IDs;
- liveness and database-readiness endpoints;
- SQL retry configuration;
- a correction-processing feature option read through `IOptionsMonitor`.

When `Features:CorrectionProcessingEnabled` is false, new corrections are rejected rather than interpreted under legacy semantics. Initial events and exact duplicate protection continue to work, and already accepted versions continue to produce correct snapshots. In a mature platform I would back this option with the organization’s feature-management service, include account/tenant targeting, audit flag changes, and monitor correction rejection counts during rollout.

Migrations are explicit. `--migrate-only` supports a deployment job or local setup without making every production API replica race to update the schema at startup. `ApplyMigrationsOnStartup` exists only as an opt-in convenience for controlled environments.

## Error handling and data boundaries

ASP.NET Core model validation rejects malformed payloads. Application conflicts and data gaps use stable problem codes. Reference instruments must exist before trades are accepted; ingestion does not create master data implicitly. Account IDs and external references are bounded to 64 characters and follow the SQL Server database collation for equality. A production rollout should choose and document an explicit case-sensitivity policy.

Authentication and authorization are deployment concerns not specified in the assignment. In production I would require service-to-service authentication, authorize account access, protect connection strings in a secret store, add request-size/rate limits, and avoid logging sensitive payload fields.

## Testing strategy

The integration tests host the real ASP.NET Core pipeline and use a unique SQL Server database per fixture. They apply the production migration, exercise HTTP serialization and error handling, use the real SQL Server locking behavior, call the stored procedure directly, and delete the database afterward.

The five tests cover:

- migration snapshot consistency with the runtime EF model;
- initial submission, duplicate, correction, audit history, and expected API snapshot totals;
- eight concurrent resends producing exactly one accepted event;
- a next-day correction excluded from the prior-day SQL snapshot and included in the next day;
- correction disablement retaining the initial audit row.

For a production service I would add contract tests, property-based event-order tests, load/deadlock tests, migration rollback rehearsal, and failure-injection around SQL availability.

## .NET Framework 4.8 integration

I would keep this service on .NET 8 and integrate a .NET Framework 4.8 platform through versioned HTTP or messaging rather than share EF entities or load modern assemblies into the legacy process.

If the logic had to run inside .NET Framework 4.8:

- use EF6 or carefully selected .NET Standard-compatible data libraries; EF Core 8 does not target .NET Framework;
- replace built-in ASP.NET Core dependency injection, options, and middleware with the platform’s established container and configuration patterns;
- use `packages.config` or compatible `PackageReference` conventions and pin transitive versions cautiously;
- bridge structured logs through the existing framework, such as Serilog, NLog, or log4net, preserving correlation IDs across the boundary;
- avoid `DateOnly`, `TimeProvider`, and newer runtime APIs in shared contracts, using ISO strings or compatible DTO types instead;
- keep the SQL schema and stored procedure contract stable so modern and legacy callers can coexist during migration.

The safest modernization seam is the API/database contract, not a shared implementation library.

## Deliberate omissions and next steps

The assignment’s optional business UI, FX, and cash features were not implemented. The next increments would be authentication, cancel/rebook event types, richer cost-basis rules, price ingestion, and a real feature-management provider. These are more valuable after the required event semantics and operational behavior are proven.
