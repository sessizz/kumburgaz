using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<DuesType> DuesTypes => Set<DuesType>();
    public DbSet<BillingGroup> BillingGroups => Set<BillingGroup>();
    public DbSet<BillingGroupUnit> BillingGroupUnits => Set<BillingGroupUnit>();
    public DbSet<DuesInstallment> DuesInstallments => Set<DuesInstallment>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionAllocation> CollectionAllocations => Set<CollectionAllocation>();
    public DbSet<IncomeExpenseCategory> IncomeExpenseCategories => Set<IncomeExpenseCategory>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Block>()
            .HasIndex(x => new { x.SiteId, x.Name })
            .IsUnique();

        builder.Entity<Unit>()
            .HasIndex(x => new { x.BlockId, x.UnitNo })
            .IsUnique();

        builder.Entity<DuesType>()
            .HasIndex(x => x.Name)
            .IsUnique();

        builder.Entity<BillingGroupUnit>()
            .HasIndex(x => new { x.BillingGroupId, x.UnitId, x.StartPeriod })
            .IsUnique();

        builder.Entity<DuesInstallment>()
            .HasIndex(x => new { x.BillingGroupId, x.Period })
            .IsUnique();

        builder.Entity<DuesInstallment>()
            .Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Entity<DuesInstallment>()
            .Property(x => x.RemainingAmount)
            .HasPrecision(18, 2);

        builder.Entity<Collection>()
            .Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Entity<CollectionAllocation>()
            .Property(x => x.AppliedAmount)
            .HasPrecision(18, 2);

        builder.Entity<LedgerTransaction>()
            .Property(x => x.Amount)
            .HasPrecision(18, 2);

        Seed(builder);
    }

    private static void Seed(ModelBuilder builder)
    {
        builder.Entity<Site>().HasData(new Site { Id = 1, Name = "Kumburgaz Sitesi" });
        builder.Entity<Block>().HasData(
            new Block { Id = 1, SiteId = 1, Name = "A Blok" },
            new Block { Id = 2, SiteId = 1, Name = "B Blok" },
            new Block { Id = 3, SiteId = 1, Name = "C Blok" }
        );

        builder.Entity<Unit>().HasData(
            new Unit { Id = 1, BlockId = 1, UnitNo = "1", OwnerName = "Ornek Malik", Active = true },
            new Unit { Id = 2, BlockId = 1, UnitNo = "2", OwnerName = "Ornek Malik", Active = true },
            new Unit { Id = 3, BlockId = 1, UnitNo = "3", OwnerName = "Daire Sahibi 3", Active = true }
        );

        builder.Entity<DuesType>().HasData(
            new DuesType { Id = 1, Name = "Tek Oda", Amount = 9000m, Active = true },
            new DuesType { Id = 2, Name = "Cift Oda", Amount = 12000m, Active = true }
        );

        builder.Entity<BillingGroup>().HasData(
            new BillingGroup
            {
                Id = 1,
                Name = "A1-A2 Birlesik",
                DuesTypeId = 2,
                EffectiveStartPeriod = "2025-2026",
                Active = true
            },
            new BillingGroup
            {
                Id = 2,
                Name = "A3 Tek",
                DuesTypeId = 1,
                EffectiveStartPeriod = "2025-2026",
                Active = true
            }
        );

        builder.Entity<BillingGroupUnit>().HasData(
            new BillingGroupUnit { Id = 1, BillingGroupId = 1, UnitId = 1, StartPeriod = "2025-2026" },
            new BillingGroupUnit { Id = 2, BillingGroupId = 1, UnitId = 2, StartPeriod = "2025-2026" },
            new BillingGroupUnit { Id = 3, BillingGroupId = 2, UnitId = 3, StartPeriod = "2025-2026" }
        );

        builder.Entity<IncomeExpenseCategory>().HasData(
            new IncomeExpenseCategory { Id = 1, Name = "Aidat Tahsilati", Type = "Gelir", Active = true },
            new IncomeExpenseCategory { Id = 2, Name = "Gorevli Maasi", Type = "Gider", Active = true },
            new IncomeExpenseCategory { Id = 3, Name = "Bakim Onarim", Type = "Gider", Active = true }
        );
    }
}
