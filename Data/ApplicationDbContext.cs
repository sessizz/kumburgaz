using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Kumburgaz.Web.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IHttpContextAccessor? httpContextAccessor = null) : IdentityDbContext<ApplicationUser>(options)
{
    private bool savingAuditLogs;

    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<UnitAccount> UnitAccounts => Set<UnitAccount>();
    public DbSet<AccountUnitAccess> AccountUnitAccesses => Set<AccountUnitAccess>();
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
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<MahsupIslem> MahsupIslemleri => Set<MahsupIslem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<DocumentRecord> DocumentRecords => Set<DocumentRecord>();
    public DbSet<ReportLine> ReportLines => Set<ReportLine>();
    public DbSet<ReportLineCategory> ReportLineCategories => Set<ReportLineCategory>();
    public DbSet<ReportManualEntry> ReportManualEntries => Set<ReportManualEntry>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchRow> ImportBatchRows => Set<ImportBatchRow>();
    public DbSet<ConsistencyCheckResult> ConsistencyCheckResults => Set<ConsistencyCheckResult>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureSoftDelete<Block>(builder);
        ConfigureSoftDelete<Unit>(builder);
        ConfigureSoftDelete<Account>(builder);
        ConfigureSoftDelete<UnitAccount>(builder);
        ConfigureSoftDelete<CombinedUnitMember>(builder);
        ConfigureSoftDelete<DuesType>(builder);
        ConfigureSoftDelete<BillingGroup>(builder);
        ConfigureSoftDelete<BillingGroupUnit>(builder);
        ConfigureSoftDelete<DuesInstallment>(builder);
        ConfigureSoftDelete<Collection>(builder);
        ConfigureSoftDelete<CollectionAllocation>(builder);
        ConfigureSoftDelete<IncomeExpenseCategory>(builder);
        ConfigureSoftDelete<LedgerTransaction>(builder);
        ConfigureSoftDelete<BankAccount>(builder);
        ConfigureSoftDelete<CashBox>(builder);
        ConfigureSoftDelete<Attachment>(builder);
        ConfigureSoftDelete<MahsupIslem>(builder);

        builder.Entity<AuditLog>()
            .HasIndex(x => new { x.EntityName, x.EntityId, x.CreatedAt });

        builder.Entity<AuditLog>()
            .HasIndex(x => x.CreatedAt);

        builder.Entity<ImportBatch>()
            .HasIndex(x => x.ImportNo)
            .IsUnique();

        builder.Entity<ImportBatch>()
            .HasIndex(x => new { x.Type, x.Status, x.CreatedAt });

        builder.Entity<ImportBatchRow>()
            .HasOne(x => x.ImportBatch)
            .WithMany(x => x.Rows)
            .HasForeignKey(x => x.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ImportBatchRow>()
            .HasIndex(x => new { x.ImportBatchId, x.LineNo })
            .IsUnique();

        builder.Entity<ImportBatchRow>()
            .HasIndex(x => x.NormalizedKey);

        builder.Entity<ConsistencyCheckResult>()
            .HasIndex(x => new { x.Resolved, x.Severity, x.CreatedAt });

        builder.Entity<ConsistencyCheckResult>()
            .Property(x => x.Difference)
            .HasPrecision(18, 2);

        builder.Entity<ReportLineCategory>()
            .HasOne(x => x.ReportLine)
            .WithMany(x => x.Categories)
            .HasForeignKey(x => x.ReportLineId)
            .OnDelete(DeleteBehavior.Cascade);

        // Bir kategori yalnızca tek rapor satırına atanabilir (çift sayım engeli).
        builder.Entity<ReportLineCategory>()
            .HasIndex(x => x.IncomeExpenseCategoryId)
            .IsUnique();

        builder.Entity<ReportLineCategory>()
            .HasIndex(x => x.IsDuesCollections)
            .IsUnique()
            .HasFilter("\"IsDuesCollections\"");

        builder.Entity<ReportManualEntry>()
            .HasOne(x => x.ReportLine)
            .WithMany(x => x.ManualEntries)
            .HasForeignKey(x => x.ReportLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ReportManualEntry>()
            .HasIndex(x => new { x.EntryDate, x.Section, x.Visible });

        builder.Entity<ReportManualEntry>()
            .Property(x => x.CashAmount)
            .HasPrecision(18, 2);

        builder.Entity<ReportManualEntry>()
            .Property(x => x.BankAmount)
            .HasPrecision(18, 2);

        builder.Entity<Block>()
            .HasIndex(x => new { x.SiteId, x.Name })
            .IsUnique();

        builder.Entity<Unit>()
            .HasIndex(x => new { x.BlockId, x.UnitNo })
            .IsUnique();

        builder.Entity<Account>()
            .HasIndex(x => new { x.AccountType, x.Name });

        builder.Entity<ApplicationUser>()
            .HasOne(x => x.Account)
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<UnitAccount>()
            .HasIndex(x => new { x.UnitId, x.Role, x.Active });

        builder.Entity<AccountUnitAccess>()
            .HasIndex(x => new { x.AccountId, x.UnitId })
            .IsUnique();

        builder.Entity<AccountUnitAccess>()
            .HasOne(x => x.Account)
            .WithMany(x => x.UnitAccessGrants)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AccountUnitAccess>()
            .HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Cascade);

        // Silinmiş hesap/daire üzerinden erişim tanımı görünmesin (soft-delete ile uyum).
        builder.Entity<AccountUnitAccess>()
            .HasQueryFilter(x => !x.Account!.IsDeleted && !x.Unit!.IsDeleted);

        builder.Entity<UnitAccount>()
            .HasOne(x => x.Unit)
            .WithMany(x => x.UnitAccounts)
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UnitAccount>()
            .HasOne(x => x.Account)
            .WithMany(x => x.UnitAccounts)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

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
            .HasOne(x => x.ResponsibleAccount)
            .WithMany(x => x.DuesInstallments)
            .HasForeignKey(x => x.ResponsibleAccountId)
            .OnDelete(DeleteBehavior.SetNull);

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

        builder.Entity<Attachment>()
            .HasIndex(x => new { x.EntityType, x.EntityId });

        builder.Entity<MahsupIslem>()
            .HasOne(x => x.Collection)
            .WithMany()
            .HasForeignKey(x => x.CollectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MahsupIslem>()
            .HasOne(x => x.LedgerTransaction)
            .WithMany()
            .HasForeignKey(x => x.LedgerTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MahsupIslem>()
            .HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MahsupIslem>()
            .HasIndex(x => x.CollectionId)
            .IsUnique();

        builder.Entity<MahsupIslem>()
            .HasIndex(x => x.LedgerTransactionId)
            .IsUnique();

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

        // Notification: AspNetUsers'a bilincli olarak soft referans (FK yok), boylece
        // sakin/personel kullanicisi silinirse bildirim gecmisi hata vermeden kalir.
        builder.Entity<Notification>()
            .HasIndex(x => new { x.RecipientUserId, x.ReadAt });

        // PushSubscription: Notification ile ayni sebeple soft referans (FK yok).
        builder.Entity<PushSubscription>()
            .HasIndex(x => x.Endpoint)
            .IsUnique();
        builder.Entity<PushSubscription>()
            .HasIndex(x => x.UserId);

        builder.Entity<RolePermission>()
            .HasIndex(x => new { x.RoleName, x.Module })
            .IsUnique();

        Seed(builder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(true, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        if (savingAuditLogs)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var pendingAuditLogs = PrepareAudits();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        if (pendingAuditLogs.Count > 0)
        {
            savingAuditLogs = true;
            try
            {
                foreach (var pending in pendingAuditLogs)
                {
                    pending.Log.EntityId = BuildEntityKey(pending.Entry);
                }

                AuditLogs.AddRange(pendingAuditLogs.Select(x => x.Log));
                await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }
            finally
            {
                savingAuditLogs = false;
            }
        }

        return result;
    }

    private List<PendingAuditLog> PrepareAudits()
    {
        ChangeTracker.DetectChanges();

        var user = httpContextAccessor?.HttpContext?.User;
        var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = user?.FindFirst(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType)?.Value
            ?? user?.Identity?.Name;
        var ip = httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var correlationId = httpContextAccessor?.HttpContext?.TraceIdentifier;
        var now = DateTime.UtcNow;
        var logs = new List<PendingAuditLog>();

        foreach (var entry in ChangeTracker.Entries()
                     .Where(x => x.Entity is not AuditLog)
                     .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.State == EntityState.Modified && !entry.Properties.Any(x => x.IsModified))
            {
                continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Create,
                EntityState.Deleted => AuditAction.Delete,
                _ => AuditAction.Update
            };

            var oldValues = entry.State == EntityState.Added ? null : SerializeValues(entry, original: true);

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable softDeletable)
            {
                softDeletable.IsDeleted = true;
                softDeletable.DeletedAt = now;
                softDeletable.DeletedByUserId = userId;
                softDeletable.DeletedByUserName = userName;
                entry.State = EntityState.Modified;
                entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified = true;
                entry.Property(nameof(ISoftDeletable.DeletedAt)).IsModified = true;
                entry.Property(nameof(ISoftDeletable.DeletedByUserId)).IsModified = true;
                entry.Property(nameof(ISoftDeletable.DeletedByUserName)).IsModified = true;
            }

            var newValues = action == AuditAction.Delete && entry.Entity is not ISoftDeletable
                ? null
                : SerializeValues(entry, original: false);

            logs.Add(new PendingAuditLog(entry, new AuditLog
            {
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = BuildEntityKey(entry),
                Action = action,
                OldValuesJson = oldValues,
                NewValuesJson = newValues,
                UserId = userId,
                UserName = userName,
                IpAddress = ip,
                CreatedAt = now,
                CorrelationId = correlationId
            }));
        }

        return logs;
    }

    private static string? SerializeValues(EntityEntry entry, bool original)
    {
        var sensitiveProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(ApplicationUser.PasswordHash),
            nameof(ApplicationUser.SecurityStamp),
            nameof(ApplicationUser.ConcurrencyStamp)
        };

        var values = entry.Properties
            .Where(x => !x.Metadata.IsShadowProperty())
            .Where(x => entry.Entity is not ApplicationUser || !sensitiveProperties.Contains(x.Metadata.Name))
            // Mobil giriş PIN'i denetim kaydına düz metin yazılmaz.
            .Where(x => entry.Entity is not Account || !string.Equals(x.Metadata.Name, nameof(Account.MobilePassword), StringComparison.Ordinal))
            .ToDictionary(
                x => x.Metadata.Name,
                x => original ? x.OriginalValue : x.CurrentValue);

        return JsonSerializer.Serialize(values);
    }

    private static string BuildEntityKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return string.Empty;
        }

        var values = key.Properties
            .Select(x => entry.Property(x.Name).CurrentValue ?? entry.Property(x.Name).OriginalValue)
            .Select(x => x?.ToString() ?? string.Empty);

        return string.Join(",", values);
    }

    private static void ConfigureSoftDelete<TEntity>(ModelBuilder builder)
        where TEntity : class, ISoftDeletable
    {
        builder.Entity<TEntity>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<TEntity>().HasIndex(x => x.IsDeleted);
        builder.Entity<TEntity>().Property(x => x.DeletedByUserId).HasMaxLength(450);
        builder.Entity<TEntity>().Property(x => x.DeletedByUserName).HasMaxLength(256);
    }

    private sealed record PendingAuditLog(EntityEntry Entry, AuditLog Log);

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
                UnitId = null,
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
