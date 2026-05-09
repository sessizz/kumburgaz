using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<CombinedUnitMember> CombinedUnitMembers => Set<CombinedUnitMember>();
    public DbSet<DuesType> DuesTypes => Set<DuesType>();
    public DbSet<BillingGroup> BillingGroups => Set<BillingGroup>();
    public DbSet<BillingGroupUnit> BillingGroupUnits => Set<BillingGroupUnit>();
    public DbSet<DuesInstallment> DuesInstallments => Set<DuesInstallment>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionAllocation> CollectionAllocations => Set<CollectionAllocation>();
    public DbSet<IncomeExpenseCategory> IncomeExpenseCategories => Set<IncomeExpenseCategory>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<CashBox> CashBoxes => Set<CashBox>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<DocumentRecord> DocumentRecords => Set<DocumentRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Block>()
            .HasIndex(x => new { x.SiteId, x.Name })
            .IsUnique();

        builder.Entity<Unit>()
            .HasIndex(x => new { x.BlockId, x.UnitNo })
            .IsUnique();

        builder.Entity<CombinedUnitMember>()
            .HasIndex(x => new { x.CombinedUnitId, x.ComponentUnitId })
            .IsUnique();

        builder.Entity<CombinedUnitMember>()
            .HasOne(x => x.CombinedUnit)
            .WithMany(x => x.CombinedUnitMembers)
            .HasForeignKey(x => x.CombinedUnitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CombinedUnitMember>()
            .HasOne(x => x.ComponentUnit)
            .WithMany(x => x.MemberOfCombinedUnits)
            .HasForeignKey(x => x.ComponentUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DuesType>()
            .HasIndex(x => x.Name)
            .IsUnique();

        builder.Entity<BillingGroupUnit>()
            .HasIndex(x => new { x.BillingGroupId, x.UnitId, x.StartPeriod })
            .IsUnique();

        builder.Entity<DuesInstallment>()
            .HasIndex(x => new { x.BillingGroupId, x.Period, x.UnitId })
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

        builder.Entity<Collection>()
            .HasOne(x => x.CashBox)
            .WithMany()
            .HasForeignKey(x => x.CashBoxId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Collection>()
            .HasOne(x => x.BankAccount)
            .WithMany()
            .HasForeignKey(x => x.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CollectionAllocation>()
            .Property(x => x.AppliedAmount)
            .HasPrecision(18, 2);

        builder.Entity<LedgerTransaction>()
            .Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Entity<LedgerTransaction>()
            .HasOne(x => x.CashBox)
            .WithMany()
            .HasForeignKey(x => x.CashBoxId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<LedgerTransaction>()
            .HasOne(x => x.BankAccount)
            .WithMany()
            .HasForeignKey(x => x.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CashBox>()
            .Property(x => x.OpeningBalance)
            .HasPrecision(18, 2);

        builder.Entity<BankAccount>()
            .Property(x => x.OpeningBalance)
            .HasPrecision(18, 2);

        builder.Entity<Announcement>()
            .HasIndex(x => x.PublishDate);

        builder.Entity<ServiceRequest>()
            .HasIndex(x => new { x.Status, x.Priority, x.CreatedAt });

        builder.Entity<ServiceRequest>()
            .HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<DocumentRecord>()
            .HasIndex(x => new { x.Category, x.DocumentDate });

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
            new Unit { Id = 1, BlockId = 1, UnitNo = "1", OwnerName = "Ornek Malik", Active = true, IsCombined = false },
            new Unit { Id = 2, BlockId = 1, UnitNo = "2", OwnerName = "Ornek Malik", Active = true, IsCombined = false },
            new Unit { Id = 3, BlockId = 1, UnitNo = "3", OwnerName = "Daire Sahibi 3", Active = true, IsCombined = false }
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
                Active = true,
                IsMerged = true
            },
            new BillingGroup
            {
                Id = 2,
                Name = "A3 Tek",
                DuesTypeId = 1,
                EffectiveStartPeriod = "2025-2026",
                Active = true,
                IsMerged = false
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

        builder.Entity<CashBox>().HasData(new CashBox
        {
            Id = 1,
            Name = "Kasa",
            OpeningBalance = 0m,
            OpeningBalanceDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Active = true
        });

        builder.Entity<Announcement>().HasData(
            new Announcement
            {
                Id = 1,
                Title = "Havuz Bakım Çalışması",
                Body = "26 Mayıs Pazar günü havuzumuz bakım nedeniyle kapalı olacaktır.",
                Priority = "Önemli",
                PublishDate = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
                IsPublished = true
            },
            new Announcement
            {
                Id = 2,
                Title = "Ortak Alan Aydınlatmaları",
                Body = "Ortak alan aydınlatmalarında yenileme çalışmaları başlamıştır.",
                Priority = "Normal",
                PublishDate = new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc),
                IsPublished = true
            }
        );

        builder.Entity<ServiceRequest>().HasData(
            new ServiceRequest
            {
                Id = 1,
                Title = "Asansör arızası - A Blok",
                Description = "A Blok asansörü katta kalıyor.",
                UnitId = 1,
                Status = ServiceRequestStatus.Open,
                Priority = ServiceRequestPriority.Urgent,
                AssignedTo = "Teknik Servis",
                CreatedAt = new DateTime(2026, 5, 7, 8, 30, 0, DateTimeKind.Utc),
                DueDate = new DateTime(2026, 5, 10, 18, 0, 0, DateTimeKind.Utc)
            },
            new ServiceRequest
            {
                Id = 2,
                Title = "Bahçe aydınlatması",
                Description = "Ortak bahçe aydınlatması kontrol edilecek.",
                UnitId = null,
                Status = ServiceRequestStatus.InProgress,
                Priority = ServiceRequestPriority.Normal,
                AssignedTo = "Site Görevlisi",
                CreatedAt = new DateTime(2026, 5, 6, 11, 0, 0, DateTimeKind.Utc)
            }
        );

        builder.Entity<DocumentRecord>().HasData(
            new DocumentRecord
            {
                Id = 1,
                Title = "2026 Genel Kurul Tutanağı",
                Category = "Toplantı",
                Url = "",
                Note = "Genel kurul karar özeti.",
                DocumentDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
            },
            new DocumentRecord
            {
                Id = 2,
                Title = "Güvenlik Hizmeti Sözleşmesi",
                Category = "Sözleşme",
                Url = "",
                Note = "Yıllık güvenlik hizmet sözleşmesi.",
                DocumentDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
