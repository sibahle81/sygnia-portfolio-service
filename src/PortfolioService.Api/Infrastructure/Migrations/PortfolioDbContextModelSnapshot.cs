using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using PortfolioService.Domain;

#pragma warning disable CA1861 // Snapshot metadata intentionally mirrors EF-generated array arguments.

namespace PortfolioService.Infrastructure.Migrations;

[DbContext(typeof(PortfolioDbContext))]
public sealed class PortfolioDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema("portfolio")
            .HasAnnotation("ProductVersion", "8.0.22")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);

        SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

        modelBuilder.Entity<Instrument>(entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int")
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);
            entity.Property<string>("Currency").IsRequired().IsUnicode(false).IsFixedLength().HasMaxLength(3).HasColumnType("char(3)");
            entity.Property<string>("Isin").IsRequired().IsUnicode(false).HasMaxLength(12).HasColumnType("varchar(12)");
            entity.Property<string>("Name").IsRequired().HasMaxLength(128).HasColumnType("nvarchar(128)");
            entity.Property<string>("Symbol").IsRequired().IsUnicode(false).HasMaxLength(16).HasColumnType("varchar(16)");
            entity.HasKey("Id");
            entity.HasIndex("Isin").IsUnique().HasDatabaseName("UX_Instruments_Isin");
            entity.HasIndex("Symbol").IsUnique().HasDatabaseName("UX_Instruments_Symbol");
            entity.ToTable("Instruments", "portfolio");
            entity.HasData(new
            {
                Id = 1,
                Currency = "USD",
                Isin = "US0378331005",
                Name = "Apple Inc.",
                Symbol = "AAPL",
            });
        });

        modelBuilder.Entity<MarketPrice>(entity =>
        {
            entity.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);
            entity.Property<decimal>("ClosePrice").HasPrecision(19, 6).HasColumnType("decimal(19,6)");
            entity.Property<string>("Currency").IsRequired().IsUnicode(false).IsFixedLength().HasMaxLength(3).HasColumnType("char(3)");
            entity.Property<int>("InstrumentId").HasColumnType("int");
            entity.Property<DateOnly>("PriceDate").HasColumnType("date");
            entity.HasKey("Id");
            entity.HasIndex("InstrumentId", "PriceDate")
                .IsUnique()
                .IsDescending(false, true)
                .HasDatabaseName("UX_MarketPrices_Instrument_PriceDate")
                .HasAnnotation("SqlServer:Include", new[] { "ClosePrice", "Currency" });
            entity.ToTable("MarketPrices", "portfolio");
            entity.HasData(
                new
                {
                    Id = 1L,
                    ClosePrice = 186.000000m,
                    Currency = "USD",
                    InstrumentId = 1,
                    PriceDate = new DateOnly(2025, 3, 1),
                },
                new
                {
                    Id = 2L,
                    ClosePrice = 190.000000m,
                    Currency = "USD",
                    InstrumentId = 1,
                    PriceDate = new DateOnly(2025, 3, 2),
                });
        });

        modelBuilder.Entity<TradeEvent>(entity =>
        {
            entity.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);
            entity.Property<string>("AccountId").IsRequired().HasMaxLength(64).HasColumnType("nvarchar(64)");
            entity.Property<DateTime>("AsOfUtc").HasColumnType("datetime2(7)");
            entity.Property<TradeEventKind>("EventKind").HasConversion<int>().HasColumnType("int");
            entity.Property<string>("ExternalReference").IsRequired().HasMaxLength(64).HasColumnType("nvarchar(64)");
            entity.Property<int>("InstrumentId").HasColumnType("int");
            entity.Property<decimal>("Quantity").HasPrecision(19, 6).HasColumnType("decimal(19,6)");
            entity.Property<DateTime>("ReceivedAtUtc").HasColumnType("datetime2(7)");
            entity.Property<TradeSide>("Side").HasConversion<int>().HasColumnType("int");
            entity.Property<DateOnly>("TradeDate").HasColumnType("date");
            entity.Property<decimal>("UnitPrice").HasPrecision(19, 6).HasColumnType("decimal(19,6)");
            entity.Property<int>("VersionNumber").HasColumnType("int");
            entity.HasKey("Id");
            entity.HasIndex("InstrumentId");
            entity.HasIndex("AccountId", "ExternalReference", "AsOfUtc")
                .IsDescending(false, false, true)
                .HasDatabaseName("IX_TradeEvents_Account_ExternalReference_AsOfUtc")
                .HasAnnotation(
                    "SqlServer:Include",
                    new[] { "TradeDate", "InstrumentId", "Side", "Quantity", "UnitPrice" });
            entity.HasIndex("ExternalReference", "AsOfUtc")
                .IsUnique()
                .IsDescending(false, true)
                .HasDatabaseName("UX_TradeEvents_ExternalReference_AsOfUtc");
            entity.HasIndex("ExternalReference", "VersionNumber")
                .IsUnique()
                .HasDatabaseName("UX_TradeEvents_ExternalReference_VersionNumber");
            entity.ToTable("TradeEvents", "portfolio");
        });

        modelBuilder.Entity<MarketPrice>(entity =>
        {
            entity.HasOne("PortfolioService.Domain.Instrument", "Instrument")
                .WithMany()
                .HasForeignKey("InstrumentId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.Navigation("Instrument");
        });

        modelBuilder.Entity<TradeEvent>(entity =>
        {
            entity.HasOne("PortfolioService.Domain.Instrument", "Instrument")
                .WithMany()
                .HasForeignKey("InstrumentId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.Navigation("Instrument");
        });
    }
}

#pragma warning restore CA1861
