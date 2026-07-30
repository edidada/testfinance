using System.Collections.Concurrent;

namespace Lending.Api.Bills;

// ===== 票据承兑（银行承兑汇票开票） =====

public enum BillStatus { Issued, Accepted, Discounted, Matured, Honored, Dishonored }
public sealed record AcceptanceBill(
    Guid Id, string BillNo, string Drawer, string Payee, string AcceptorBank,
    decimal FaceAmount, DateOnly IssueDate, DateOnly DueDate, decimal AcceptanceFeeRate,
    BillStatus Status, DateTimeOffset CreatedAt);

public sealed class AcceptanceService
{
    private readonly ConcurrentDictionary<Guid, AcceptanceBill> _bills = new();

    public AcceptanceBill Issue(string billNo, string drawer, string payee, string acceptorBank, decimal faceAmount, int termDays)
    {
        if (faceAmount <= 0) throw new ArgumentException("票面金额必须大于 0。");
        if (termDays is < 1 or > 365) throw new ArgumentException("票据期限 1-365 天。");
        var issue = DateOnly.FromDateTime(DateTime.UtcNow);
        var bill = new AcceptanceBill(Guid.NewGuid(), billNo, drawer, payee, acceptorBank, faceAmount, issue, issue.AddDays(termDays), 0.0005m, BillStatus.Issued, DateTimeOffset.UtcNow);
        _bills[bill.Id] = bill;
        return bill;
    }

    public AcceptanceBill Accept(Guid billId)
    {
        var bill = Get(billId);
        if (bill.Status != BillStatus.Issued) throw new InvalidOperationException("票据已承兑。");
        return Update(bill with { Status = BillStatus.Accepted });
    }

    public AcceptanceBill Honor(Guid billId)
    {
        var bill = Get(billId);
        if (bill.Status != BillStatus.Accepted) throw new InvalidOperationException("仅已承兑票据可兑付。");
        return Update(bill with { Status = BillStatus.Honored });
    }

    public AcceptanceBill Get(Guid billId) => _bills.TryGetValue(billId, out var b) ? b : throw new KeyNotFoundException("票据不存在。");
    private AcceptanceBill Update(AcceptanceBill b) { _bills[b.Id] = b; return b; }
}

// ===== 票据贴现（贴现息计算） =====

public sealed record DiscountRecord(Guid Id, Guid BillId, string DiscountHolder, decimal FaceAmount, decimal DiscountRate, int DaysToMaturity, decimal DiscountInterest, decimal NetProceeds, DateTimeOffset DiscountedAt);

public sealed class DiscountService
{
    // 贴现息 = 票面金额 × 贴现率 × 到期天数 / 360
    // 实得金额 = 票面金额 - 贴现息
    public DiscountRecord Discount(Guid billId, string holder, decimal faceAmount, decimal annualDiscountRate, int daysToMaturity)
    {
        if (faceAmount <= 0) throw new ArgumentException("票面金额必须大于 0。");
        if (daysToMaturity <= 0) throw new ArgumentException("到期天数必须大于 0。");
        var interest = decimal.Round(faceAmount * annualDiscountRate * daysToMaturity / 360m, 2, MidpointRounding.ToEven);
        var net = faceAmount - interest;
        return new DiscountRecord(Guid.NewGuid(), billId, holder, faceAmount, annualDiscountRate, daysToMaturity, interest, net, DateTimeOffset.UtcNow);
    }
}

// ===== 票据转贴现（银行间买卖） =====

public enum RediscountDirection { Buy, Sell }
public sealed record RediscountRecord(Guid Id, Guid BillId, RediscountDirection Direction, string CounterpartyBank, decimal FaceAmount, decimal TradePrice, decimal Profit, DateTimeOffset TradedAt);

public sealed class RediscountService
{
    // 转贴现价格 = 票面 × (1 - 转贴现率 × 剩余天数/360)
    public RediscountRecord Trade(Guid billId, RediscountDirection direction, string counterpartyBank, decimal faceAmount, decimal annualRate, int remainingDays)
    {
        if (faceAmount <= 0 || remainingDays <= 0) throw new ArgumentException("参数无效。");
        var price = decimal.Round(faceAmount * (1 - annualRate * remainingDays / 360m), 2, MidpointRounding.ToEven);
        var profit = direction == RediscountDirection.Sell ? price - faceAmount * 0.98m : faceAmount - price;
        return new RediscountRecord(Guid.NewGuid(), billId, direction, counterpartyBank, faceAmount, price, decimal.Round(profit, 2, MidpointRounding.ToEven), DateTimeOffset.UtcNow);
    }
}

// ===== 票据池（企业票据集中托管与融资） =====

public sealed record BillPoolEntry(Guid PoolId, Guid BillId, decimal FaceAmount, DateOnly DueDate, bool Pledged);
public sealed record BillPool(Guid Id, string Owner, decimal TotalFaceAmount, decimal FinancedAmount, IReadOnlyList<BillPoolEntry> Entries);

public sealed class BillPoolService
{
    private readonly ConcurrentDictionary<Guid, BillPool> _pools = new();

    public BillPool Open(string owner)
    {
        var pool = new BillPool(Guid.NewGuid(), owner, 0m, 0m, []);
        _pools[pool.Id] = pool;
        return pool;
    }

    public BillPool Deposit(Guid poolId, Guid billId, decimal faceAmount, DateOnly dueDate)
    {
        var pool = Get(poolId);
        var entry = new BillPoolEntry(poolId, billId, faceAmount, dueDate, false);
        var entries = pool.Entries.Append(entry).ToArray();
        return Update(pool with { Entries = entries, TotalFaceAmount = pool.TotalFaceAmount + faceAmount });
    }

    public BillPool Finance(Guid poolId, decimal amount)
    {
        var pool = Get(poolId);
        var available = pool.TotalFaceAmount * 0.9m; // 票据池融资上限 90%
        if (amount > available - pool.FinancedAmount) throw new InvalidOperationException("超出票据池可用融资额度。");
        return Update(pool with { FinancedAmount = pool.FinancedAmount + amount });
    }

    public BillPool Get(Guid poolId) => _pools.TryGetValue(poolId, out var p) ? p : throw new KeyNotFoundException("票据池不存在。");
    private BillPool Update(BillPool p) { _pools[p.Id] = p; return p; }
}
