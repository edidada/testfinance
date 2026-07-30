using System.Collections.Concurrent;
using System.Text;

namespace Wealth.Api.Clearing;

// ===== 资金清算（Fund Clearing） =====

public sealed record FundClearingRecord(Guid Id, string ProductCode, DateOnly BusinessDate, decimal SubscriptionAmount, decimal RedemptionAmount, decimal NetFlow, string Status, DateTimeOffset ClearedAt)
{
    // 净流 = 申购 - 赎回
};

public sealed class FundClearingService
{
    private readonly ConcurrentDictionary<Guid, FundClearingRecord> _records = new();

    public FundClearingRecord Clear(string productCode, DateOnly businessDate, decimal subscriptionAmount, decimal redemptionAmount)
    {
        var net = decimal.Round(subscriptionAmount - redemptionAmount, 2, MidpointRounding.ToEven);
        var record = new FundClearingRecord(Guid.NewGuid(), productCode, businessDate, subscriptionAmount, redemptionAmount, net, "CLEARED", DateTimeOffset.UtcNow);
        _records[record.Id] = record;
        return record;
    }

    public IReadOnlyList<FundClearingRecord> ByDate(DateOnly businessDate) =>
        _records.Values.Where(x => x.BusinessDate == businessDate).OrderBy(x => x.ProductCode).ToArray();
}

// ===== 份额清算（Share Clearing） =====

public sealed record ShareClearingRecord(Guid Id, Guid AccountId, string ProductCode, DateOnly BusinessDate, decimal SubscribedShares, decimal RedeemedShares, decimal NetShares, DateTimeOffset ClearedAt);

public sealed class ShareClearingService
{
    private readonly ConcurrentDictionary<Guid, ShareClearingRecord> _records = new();

    public ShareClearingRecord Clear(Guid accountId, string productCode, DateOnly businessDate, decimal subscribed, decimal redeemed)
    {
        var net = decimal.Round(subscribed - redeemed, 6, MidpointRounding.ToEven);
        var record = new ShareClearingRecord(Guid.NewGuid(), accountId, productCode, businessDate, subscribed, redeemed, net, DateTimeOffset.UtcNow);
        _records[record.Id] = record;
        return record;
    }
}

// ===== 对账文件生成（TA 与托管银行对账） =====

public sealed class ReconciliationFileGenerator
{
    // 生成 TA 对账文件（CSV 格式）
    public string GenerateTaReconciliationFile(DateOnly businessDate, IEnumerable<FundClearingRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProductCode,BusinessDate,SubscriptionAmount,RedemptionAmount,NetFlow,Status");
        foreach (var r in records.Where(x => x.BusinessDate == businessDate))
            sb.AppendLine($"{r.ProductCode},{r.BusinessDate:yyyy-MM-dd},{r.SubscriptionAmount},{r.RedemptionAmount},{r.NetFlow},{r.Status}");
        return sb.ToString();
    }
}

// ===== 托管银行接口（接口骨架） =====

public interface ICustodianGateway
{
    Task<decimal> QueryHoldingAsync(string productCode, DateOnly asOf, CancellationToken ct = default);
    Task<bool> TransferCashAsync(string productCode, decimal amount, string direction, CancellationToken ct = default);
    Task<string> DownloadReconciliationFileAsync(DateOnly businessDate, CancellationToken ct = default);
}

public sealed class FakeCustodianGateway : ICustodianGateway
{
    public Task<decimal> QueryHoldingAsync(string productCode, DateOnly asOf, CancellationToken ct = default)
        => Task.FromResult(10_000_000m);
    public Task<bool> TransferCashAsync(string productCode, decimal amount, string direction, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<string> DownloadReconciliationFileAsync(DateOnly businessDate, CancellationToken ct = default)
        => Task.FromResult($"ProductCode,BusinessDate,Amount\nTEST,{businessDate:yyyy-MM-dd},1000000");
}

// ===== 差异处理（TA 与托管对账差异） =====

public enum ClearingDiffType { Balance, Transaction, Fee }
public sealed record ClearingDiff(Guid Id, string ProductCode, ClearingDiffType Type, decimal TaAmount, decimal CustodianAmount, decimal Difference, string Status, DateTimeOffset DetectedAt);

public sealed class ClearingDiffService
{
    private readonly ConcurrentDictionary<Guid, ClearingDiff> _diffs = new();

    public ClearingDiff Detect(string productCode, ClearingDiffType type, decimal taAmount, decimal custodianAmount)
    {
        var diff = decimal.Round(taAmount - custodianAmount, 2, MidpointRounding.ToEven);
        if (diff == 0m) throw new InvalidOperationException("无差异，无需登记。");
        var d = new ClearingDiff(Guid.NewGuid(), productCode, type, taAmount, custodianAmount, diff, "PENDING", DateTimeOffset.UtcNow);
        _diffs[d.Id] = d;
        return d;
    }

    public ClearingDiff Resolve(Guid diffId, string resolution)
    {
        if (!_diffs.TryGetValue(diffId, out var d)) throw new KeyNotFoundException("差异单不存在。");
        return _diffs[diffId] = d with { Status = $"RESOLVED:{resolution}" };
    }

    public IReadOnlyList<ClearingDiff> Pending() => _diffs.Values.Where(x => x.Status == "PENDING").ToArray();
}
