using Microsoft.EntityFrameworkCore;
using PortfolioService.Domain;

namespace PortfolioService.Infrastructure;

public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : DbContext(options)
{
    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<MarketPrice> MarketPrices => Set<MarketPrice>();

    public DbSet<TradeEvent> TradeEvents => Set<TradeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("portfolio");

        modelBuilder.Entity<Instrument>(entity =>
        {
            entity.ToTable("Instruments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Isin).HasMaxLength(12).IsUnicode(false).IsRequired();
            entity.Property(x => x.Symbol).HasMaxLength(16).IsUnicode(false).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
            entity.HasIndex(x => x.Isin).IsUnique().HasDatabaseName("UX_Instruments_Isin");
            entity.HasIndex(x => x.Symbol).IsUnique().HasDatabaseName("UX_Instruments_Symbol");

            entity.HasData(new
            {
                Id = 1,
                Isin = "US0378331005",
                Symbol = "AAPL",
                Name = "Apple Inc.",
                Currency = "USD",
            });
        });

        modelBuilder.Entity<MarketPrice>(entity =>
        {
            entity.ToTable("MarketPrices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PriceDate).HasColumnType("date");
            entity.Property(x => x.ClosePrice).HasPrecision(19, 6);
            entity.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
            entity.HasOne(x => x.Instrument).WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.InstrumentId, x.PriceDate })
                .IsUnique()
                .IsDescending(false, true)
                .IncludeProperties(x => new { x.ClosePrice, x.Currency })
                .HasDatabaseName("UX_MarketPrices_Instrument_PriceDate");

            entity.HasData(
                new
                {
                    Id = 1L,
                    InstrumentId = 1,
                    PriceDate = new DateOnly(2025, 3, 1),
                    ClosePrice = 186.000000m,
                    Currency = "USD",
                },
                new
                {
                    Id = 2L,
                    InstrumentId = 1,
                    PriceDate = new DateOnly(2025, 3, 2),
                    ClosePrice = 190.000000m,
                    Currency = "USD",
                });
        });

        modelBuilder.Entity<TradeEvent>(entity =>
        {
            entity.ToTable("TradeEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalReference).HasMaxLength(64).IsRequired();
            entity.Property(x => x.AccountId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Side).HasConversion<int>();
            entity.Property(x => x.Quantity).HasPrecision(19, 6);
            entity.Property(x => x.UnitPrice).HasPrecision(19, 6);
            entity.Property(x => x.TradeDate).HasColumnType("date");
            entity.Property(x => x.AsOfUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.EventKind).HasConversion<int>();
            entity.HasOne(x => x.Instrument).WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.ExternalReference, x.AsOfUtc })
                .IsUnique()
                .IsDescending(false, true)
                .HasDatabaseName("UX_TradeEvents_ExternalReference_AsOfUtc");
            entity.HasIndex(x => new { x.ExternalReference, x.VersionNumber })
                .IsUnique()
                .HasDatabaseName("UX_TradeEvents_ExternalReference_VersionNumber");
            entity.HasIndex(x => new { x.AccountId, x.ExternalReference, x.AsOfUtc })
                .IsDescending(false, false, true)
                .IncludeProperties(x => new
                {
                    x.TradeDate,
                    x.InstrumentId,
                    x.Side,
                    x.Quantity,
                    x.UnitPrice,
                })
                .HasDatabaseName("IX_TradeEvents_Account_ExternalReference_AsOfUtc");
        });
    }
}
