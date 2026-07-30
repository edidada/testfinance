using System.Collections.Concurrent;

namespace Payments.Api.RiskControl;

// ===== 交易限额校验 =====

public sealed record LimitRule(string MerchantId, decimal DailyLimit, decimal SingleLimit, int DailyCountLimit);
public sealed record LimitCheckResult(bool Passed, string Reason, decimal UsedDailyAmount, int UsedDailyCount);

public sealed class LimitChecker
{
    private readonly ConcurrentDictionary<string, LimitRule> _rules = new();
    private readonly ConcurrentDictionary<string, (decimal Amount, int Count)> _dailyUsage = new();

    public void Configure(LimitRule rule) => _rules[rule.MerchantId] = rule;

    public LimitCheckResult Check(string merchantId, decimal amount)
    {
        if (!_rules.TryGetValue(merchantId, out var rule))
            return new LimitCheckResult(true, "无限额配置", 0m, 0);

        var dateKey = $"{merchantId}:{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}";
        var (usedAmount, usedCount) = _dailyUsage.GetOrAdd(dateKey, _ => (0m, 0));

        if (amount > rule.SingleLimit)
            return new LimitCheckResult(false, $"单笔超限：{amount} > {rule.SingleLimit}", usedAmount, usedCount);
        if (usedAmount + amount > rule.DailyLimit)
            return new LimitCheckResult(false, $"日累计超限：{usedAmount + amount} > {rule.DailyLimit}", usedAmount, usedCount);
        if (usedCount + 1 > rule.DailyCountLimit)
            return new LimitCheckResult(false, $"日笔数超限：{usedCount + 1} > {rule.DailyCountLimit}", usedAmount, usedCount);

        return new LimitCheckResult(true, "通过", usedAmount, usedCount);
    }

    public void Record(string merchantId, decimal amount)
    {
        var dateKey = $"{merchantId}:{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}";
        _dailyUsage.AddOrUpdate(dateKey, (amount, 1), (_, v) => (v.Amount + amount, v.Count + 1));
    }
}

// ===== 黑白名单 =====

public enum ListType { Blacklist, Whitelist, Greylist }
public sealed record ListEntry(string Value, ListType Type, string Reason, DateTimeOffset AddedAt);

public sealed class BlacklistService
{
    private readonly ConcurrentDictionary<string, ListEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string value, ListType type, string reason) =>
        _entries[value] = new ListEntry(value, type, reason, DateTimeOffset.UtcNow);

    public void Remove(string value) => _entries.TryRemove(value, out _);

    public bool IsBlacklisted(string value) => _entries.TryGetValue(value, out var e) && e.Type == ListType.Blacklist;
    public bool IsWhitelisted(string value) => _entries.TryGetValue(value, out var e) && e.Type == ListType.Whitelist;
    public ListEntry? Get(string value) => _entries.TryGetValue(value, out var e) ? e : null;
}

// ===== 异常交易识别 =====

public enum AnomalyType { HighFrequencySmallAmount, LargeAmountSpike, VelocitySpike, OffHours }
public sealed record AnomalyAlert(Guid Id, string CustomerId, AnomalyType Type, string Description, decimal Amount, DateTimeOffset DetectedAt);

public sealed class AnomalyDetector
{
    private readonly ConcurrentDictionary<string, List<(decimal Amount, DateTimeOffset Time)>> _history = new();

    // 高频小额：10 分钟内超过 20 笔且单笔 < 100
    public AnomalyAlert? DetectHighFrequencySmallAmount(string customerId, decimal amount, DateTimeOffset now)
    {
        var list = _history.GetOrAdd(customerId, _ => new List<(decimal, DateTimeOffset)>());
        lock (list)
        {
            list.Add((amount, now));
            var recent = list.Where(x => x.Time > now.AddMinutes(-10)).ToList();
            if (recent.Count >= 20 && amount < 100m)
                return new AnomalyAlert(Guid.NewGuid(), customerId, AnomalyType.HighFrequencySmallAmount,
                    $"10 分钟内 {recent.Count} 笔交易，单笔 {amount}", amount, now);
        }
        return null;
    }

    // 大额跳变：单笔超过客户历史均值 10 倍
    public AnomalyAlert? DetectLargeAmountSpike(string customerId, decimal amount, DateTimeOffset now)
    {
        var list = _history.GetOrAdd(customerId, _ => new List<(decimal, DateTimeOffset)>());
        lock (list)
        {
            var historical = list.Where(x => x.Time < now.AddHours(-1)).ToList();
            if (historical.Count >= 5 && amount > historical.Average(x => x.Amount) * 10)
                return new AnomalyAlert(Guid.NewGuid(), customerId, AnomalyType.LargeAmountSpike,
                    $"大额跳变：{amount} > 历史均值 {historical.Average(x => x.Amount):F2} × 10", amount, now);
            list.Add((amount, now));
        }
        return null;
    }
}

// ===== 支付风控规则引擎 =====

public sealed record RiskRule(string Id, string Name, Func<RiskContext, bool> Predicate, string Action, string? Description);
public sealed record RiskContext(string CustomerId, string MerchantId, decimal Amount, string Currency, string IpAddress, int HourOfDay);
public sealed record RiskEvaluationResult(bool Approved, IReadOnlyList<string> HitRules, IReadOnlyList<string> Reasons);

public sealed class PaymentRiskEngine
{
    private readonly ConcurrentDictionary<string, RiskRule> _rules = new();

    public void Register(RiskRule rule) => _rules[rule.Id] = rule;

    public RiskEvaluationResult Evaluate(RiskContext ctx)
    {
        var hit = new List<string>();
        var reasons = new List<string>();
        foreach (var r in _rules.Values)
        {
            if (r.Predicate(ctx))
            {
                hit.Add(r.Id);
                if (r.Action == "BLOCK" && r.Description is not null) reasons.Add(r.Description);
            }
        }
        return new RiskEvaluationResult(reasons.Count == 0, hit, reasons);
    }

    // 注册默认风控规则
    public void RegisterDefaults()
    {
        Register(new RiskRule("R_NIGHT_LARGE", "夜间大额", c => c.HourOfDay is >= 23 or < 5 && c.Amount >= 50_000m, "BLOCK", "夜间 23-5 点大额交易"));
        Register(new RiskRule("R_HIGH_RISK_MERCHANT", "高风险商户", c => c.MerchantId.StartsWith("RISK_"), "BLOCK", "高风险商户交易"));
        Register(new RiskRule("R_FOREIGN_IP", "境外 IP", c => !string.IsNullOrEmpty(c.IpAddress) && c.IpAddress.StartsWith("10.") == false && c.IpAddress.Split('.')[0] is not "192" and not "172" and not "10", "REVIEW", "境外 IP 访问"));
    }
}
