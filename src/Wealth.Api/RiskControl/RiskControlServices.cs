using System.Collections.Concurrent;

namespace Wealth.Api.RiskControl;

// ===== 集中度限额校验 =====

public sealed record ConcentrationLimit(string Dimension, decimal MaxRatio, string Description);
public sealed record ConcentrationCheck(string Dimension, decimal CurrentValue, decimal TotalValue, decimal CurrentRatio, bool Passed, string? Reason)
{
    // 集中度 = 单一维度价值 / 总价值
};

public static class ConcentrationChecker
{
    // 单一发行人占比不超过 10%；单一券种不超过 20%
    public static ConcentrationCheck Check(string dimension, decimal currentValue, decimal totalValue, decimal maxRatio)
    {
        if (totalValue <= 0) throw new ArgumentException("总价值必须大于 0。");
        var ratio = decimal.Round(currentValue / totalValue, 4, MidpointRounding.ToEven);
        var passed = ratio <= maxRatio;
        return new ConcentrationCheck(dimension, currentValue, totalValue, ratio, passed,
            passed ? null : $"{dimension} 集中度 {ratio:P2} 超限 {maxRatio:P2}");
    }
}

// ===== VaR 风险价值（历史模拟法简化版） =====

public sealed record VarResult(decimal Confidence95, decimal Confidence99, int HoldingDays, string Method, decimal PortfolioValue)
{
    // VaR 表示在给定置信水平下，一定持有期内最大可能损失
};

public static class VarCalculator
{
    // 历史模拟法：取收益率序列分位数
    public static VarResult Historical(IReadOnlyList<decimal> returns, decimal portfolioValue, int holdingDays = 1)
    {
        if (returns.Count < 30) throw new ArgumentException("至少需要 30 个历史收益率。");
        var sorted = returns.OrderBy(x => x).ToArray();
        // 95% 置信：取第 5% 分位；99% 置信：取第 1% 分位
        var idx95 = (int)Math.Floor(sorted.Length * 0.05);
        var idx99 = (int)Math.Floor(sorted.Length * 0.01);
        var worst95 = sorted[idx95];
        var worst99 = sorted[idx99];
        // VaR = 组合价值 × |最差收益率| × sqrt(持有天数)
        var sqrtDays = (decimal)Math.Sqrt(holdingDays);
        var var95 = decimal.Round(portfolioValue * Math.Abs(worst95) * sqrtDays, 2, MidpointRounding.ToEven);
        var var99 = decimal.Round(portfolioValue * Math.Abs(worst99) * sqrtDays, 2, MidpointRounding.ToEven);
        return new VarResult(var95, var99, holdingDays, "Historical", portfolioValue);
    }
}

// ===== 投资限制校验 =====

public sealed record InvestmentLimit(string RuleId, string Description, decimal LimitValue, decimal ActualValue, bool Passed);
public sealed record InvestmentRestrictionCheck(IReadOnlyList<InvestmentLimit> Results)
{
    public bool AllPassed => Results.All(x => x.Passed);
}

public static class InvestmentRestrictionChecker
{
    // 单一发行人占比 ≤ 10%；单一券种占比 ≤ 20%；股票类占比 ≤ 40%；流动性资产 ≥ 5%
    public static InvestmentRestrictionCheck Check(
        (decimal SingleIssuer, decimal TotalValue) singleIssuer,
        (decimal SingleBondType, decimal TotalValue) singleBond,
        (decimal Equity, decimal TotalValue) equity,
        (decimal LiquidAsset, decimal TotalValue) liquid)
    {
        var results = new List<InvestmentLimit>
        {
            CheckLimit("R_SINGLE_ISSUER", "单一发行人占比 ≤ 10%", 0.10m, singleIssuer.SingleIssuer, singleIssuer.TotalValue),
            CheckLimit("R_SINGLE_BOND", "单一券种占比 ≤ 20%", 0.20m, singleBond.SingleBondType, singleBond.TotalValue),
            CheckLimit("R_EQUITY_RATIO", "股票类占比 ≤ 40%", 0.40m, equity.Equity, equity.TotalValue),
            CheckLimit("R_LIQUID_MIN", "流动性资产 ≥ 5%", 0.05m, liquid.LiquidAsset, liquid.TotalValue, isMinLimit: true)
        };
        return new InvestmentRestrictionCheck(results);
    }

    private static InvestmentLimit CheckLimit(string ruleId, string desc, decimal limit, decimal actual, decimal total, bool isMinLimit = false)
    {
        var ratio = total > 0 ? actual / total : 0m;
        var passed = isMinLimit ? ratio >= limit : ratio <= limit;
        return new InvestmentLimit(ruleId, desc, limit, ratio, passed);
    }
}

// ===== 超限告警状态机 =====

public enum AlertStatus { Triggered, Notified, Acknowledged, Resolved }
public sealed record RiskAlert(Guid Id, string RuleId, string Description, AlertStatus Status, decimal ExcessAmount, DateTimeOffset TriggeredAt);

public sealed class RiskAlertService
{
    private readonly ConcurrentDictionary<Guid, RiskAlert> _alerts = new();

    public RiskAlert Trigger(string ruleId, string description, decimal excess)
    {
        var alert = new RiskAlert(Guid.NewGuid(), ruleId, description, AlertStatus.Triggered, excess, DateTimeOffset.UtcNow);
        _alerts[alert.Id] = alert;
        return alert;
    }

    public RiskAlert Acknowledge(Guid alertId)
    {
        var alert = Get(alertId);
        return Update(alert with { Status = AlertStatus.Acknowledged });
    }

    public RiskAlert Resolve(Guid alertId)
    {
        var alert = Get(alertId);
        return Update(alert with { Status = AlertStatus.Resolved });
    }

    public IReadOnlyList<RiskAlert> Active() => _alerts.Values.Where(x => x.Status != AlertStatus.Resolved).OrderByDescending(x => x.TriggeredAt).ToArray();
    public RiskAlert Get(Guid id) => _alerts.TryGetValue(id, out var a) ? a : throw new KeyNotFoundException("告警不存在。");
    private RiskAlert Update(RiskAlert a) { _alerts[a.Id] = a; return a; }
}
