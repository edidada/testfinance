using System.Collections.Concurrent;

namespace Wealth.Api.Compliance;

// ===== 适当性匹配（客户风险等级 vs 产品风险等级） =====

public sealed record SuitabilityResult(int CustomerRiskLevel, int ProductRiskLevel, bool Matched, string Reason)
{
    // 客户风险等级 ≥ 产品风险等级 才匹配
};

public static class SuitabilityMatcher
{
    public static SuitabilityResult Match(int customerRiskLevel, int productRiskLevel)
    {
        if (customerRiskLevel is < 1 or > 5) throw new ArgumentException("客户风险等级必须 1-5。");
        if (productRiskLevel is < 1 or > 5) throw new ArgumentException("产品风险等级必须 1-5。");
        var matched = customerRiskLevel >= productRiskLevel;
        var reason = matched
            ? $"客户风险等级 {customerRiskLevel} ≥ 产品风险等级 {productRiskLevel}，适当性匹配"
            : $"客户风险等级 {customerRiskLevel} < 产品风险等级 {productRiskLevel}，适当性不匹配";
        return new SuitabilityResult(customerRiskLevel, productRiskLevel, matched, reason);
    }
}

// ===== AML 反洗钱规则（大额/可疑交易识别） =====

public enum AmlAlertType { LargeCash, Structuring, RapidMovement, HighRiskJurisdiction }
public sealed record AmlAlert(Guid Id, string CustomerId, AmlAlertType Type, decimal Amount, string Description, DateTimeOffset DetectedAt);

public sealed class AmlRuleEngine
{
    private readonly ConcurrentBag<AmlAlert> _alerts = new();
    private readonly ConcurrentDictionary<string, List<(decimal Amount, DateTimeOffset Time)>> _history = new();

    // 大额现金交易：单笔 ≥ 5 万
    public AmlAlert? CheckLargeCash(string customerId, decimal amount, string transactionType)
    {
        if (transactionType == "CASH" && amount >= 50_000m)
        {
            var alert = new AmlAlert(Guid.NewGuid(), customerId, AmlAlertType.LargeCash, amount,
                $"大额现金交易：{amount:C} ≥ 5 万", DateTimeOffset.UtcNow);
            _alerts.Add(alert);
            return alert;
        }
        return null;
    }

    // 结构性拆分（Structuring）：短期内多笔接近 5 万的交易
    public AmlAlert? CheckStructuring(string customerId, decimal amount, DateTimeOffset now)
    {
        var list = _history.GetOrAdd(customerId, _ => new List<(decimal, DateTimeOffset)>());
        lock (list)
        {
            list.Add((amount, now));
            var recent = list.Where(x => x.Time > now.AddHours(-24)).ToList();
            // 24 小时内 ≥ 3 笔，单笔 4-5 万
            if (recent.Count >= 3 && recent.All(x => x.Amount is >= 40_000m and < 50_000m))
            {
                var total = recent.Sum(x => x.Amount);
                var alert = new AmlAlert(Guid.NewGuid(), customerId, AmlAlertType.Structuring, total,
                    $"结构性拆分：24 小时内 {recent.Count} 笔接近 5 万，合计 {total:C}", now);
                _alerts.Add(alert);
                return alert;
            }
        }
        return null;
    }

    // 快速资金转移：资金到账后 1 小时内转出 ≥ 10 万
    public AmlAlert? CheckRapidMovement(string customerId, decimal inflowAmount, decimal outflowAmount, TimeSpan elapsed)
    {
        if (inflowAmount >= 100_000m && outflowAmount >= 100_000m && elapsed < TimeSpan.FromHours(1))
        {
            var alert = new AmlAlert(Guid.NewGuid(), customerId, AmlAlertType.RapidMovement, outflowAmount,
                $"快速转移：到账 {inflowAmount:C}，{elapsed.TotalMinutes:F0} 分钟内转出 {outflowAmount:C}", DateTimeOffset.UtcNow);
            _alerts.Add(alert);
            return alert;
        }
        return null;
    }

    public IReadOnlyList<AmlAlert> Alerts() => _alerts.ToArray();
}

// ===== 双录管理（接口骨架） =====

public interface IDualRecordingService
{
    Task<bool> StartRecordingAsync(string customerId, string productId, CancellationToken ct = default);
    Task<bool> StopRecordingAsync(string sessionId, CancellationToken ct = default);
    Task<string> GetRecordingUrlAsync(string sessionId, CancellationToken ct = default);
}

public sealed class FakeDualRecordingService : IDualRecordingService
{
    public Task<bool> StartRecordingAsync(string customerId, string productId, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<bool> StopRecordingAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<string> GetRecordingUrlAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult($"https://recording.example.com/{sessionId}.mp4");
}
