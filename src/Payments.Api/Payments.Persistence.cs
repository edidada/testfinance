using Finance.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Payments.Api;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : FinanceDbContext(options)
{
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<LedgerLine> LedgerLines => Set<LedgerLine>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        ConfigureFinancialControls(b);
        b.Entity<PaymentEntity>(e =>
        {
            e.ToTable("payments"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.TenantId, x.MerchantId, x.CreatedAt });
            e.Property(x => x.Amount).HasPrecision(19, 4); e.Property(x => x.RefundedAmount).HasPrecision(19, 4);
            e.Property(x => x.Version).IsConcurrencyToken(); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Reference).HasMaxLength(128);
        });
        b.Entity<LedgerLine>(e => { e.ToTable("ledger_entries"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.PaymentId, x.OccurredAt }); e.Property(x => x.Amount).HasPrecision(19, 4); });
    }
}

public sealed class PaymentEntity : AggregateRoot
{
    public required string MerchantId { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string Reference { get; init; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Authorized;
    public decimal RefundedAmount { get; set; }
}

public sealed class LedgerLine
{
    public long Id { get; set; }
    public long JournalId { get; set; }
    public Guid PaymentId { get; set; }
    public required string AccountCode { get; set; }
    public required string Direction { get; set; }
    public decimal Amount { get; set; }
    public required string Memo { get; set; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
