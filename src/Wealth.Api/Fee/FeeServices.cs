namespace Wealth.Api.Fee;

// ===== 管理费/托管费/销售服务费（按日计提） =====

public sealed record FeeAccrual(string ProductCode, DateOnly AccrualDate, decimal Nav, decimal TotalUnits, decimal FeeRate, decimal DailyFee, string FeeType)
{
    // 日计提 = 前一日净值 × 总份额 × 年费率 / 365
};

public static class FeeAccrualCalculator
{
    // 管理费：年费率通常 0.5%-1.5%
    public static FeeAccrual ManagementFee(string productCode, DateOnly accrualDate, decimal nav, decimal totalUnits, decimal annualRate)
    {
        var daily = decimal.Round(nav * totalUnits * annualRate / 365m, 4, MidpointRounding.ToEven);
        return new FeeAccrual(productCode, accrualDate, nav, totalUnits, annualRate, daily, "MANAGEMENT");
    }

    // 托管费：年费率通常 0.1%-0.25%
    public static FeeAccrual CustodyFee(string productCode, DateOnly accrualDate, decimal nav, decimal totalUnits, decimal annualRate)
    {
        var daily = decimal.Round(nav * totalUnits * annualRate / 365m, 4, MidpointRounding.ToEven);
        return new FeeAccrual(productCode, accrualDate, nav, totalUnits, annualRate, daily, "CUSTODY");
    }

    // 销售服务费：年费率通常 0.1%-0.6%
    public static FeeAccrual SalesServiceFee(string productCode, DateOnly accrualDate, decimal nav, decimal totalUnits, decimal annualRate)
    {
        var daily = decimal.Round(nav * totalUnits * annualRate / 365m, 4, MidpointRounding.ToEven);
        return new FeeAccrual(productCode, accrualDate, nav, totalUnits, annualRate, daily, "SALES_SERVICE");
    }

    // 批量计提一段时间的管理费
    public static IReadOnlyList<FeeAccrual> AccrueRange(string productCode, decimal nav, decimal totalUnits, decimal annualRate, DateOnly from, DateOnly to)
    {
        var list = new List<FeeAccrual>();
        for (var d = from; d <= to; d = d.AddDays(1))
            list.Add(ManagementFee(productCode, d, nav, totalUnits, annualRate));
        return list;
    }
}

// ===== 申购费阶梯费率 =====

public sealed record SubscriptionFeeTier(decimal MinAmount, decimal MaxAmount, decimal FeeRate, decimal MaxFee);
public sealed record SubscriptionFeeCalculation(decimal Amount, decimal FeeRate, decimal Fee, decimal NetInvestment);

public static class SubscriptionFeeCalculator
{
    private static readonly SubscriptionFeeTier[] _tiers =
    {
        new(0m, 100_000m, 0.015m, 1500m),        // 100 万以下 1.5%
        new(100_000m, 1_000_000m, 0.012m, 12000m), // 100 万-1000 万 1.2%
        new(1_000_000m, 5_000_000m, 0.008m, 40000m), // 1000 万-5000 万 0.8%
        new(5_000_000m, decimal.MaxValue, 0.001m, 1000m) // 5000 万以上 0.1%，封顶 1000
    };

    public static SubscriptionFeeCalculation Calculate(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("申购金额必须大于 0。");
        var tier = _tiers.First(t => amount >= t.MinAmount && amount < t.MaxAmount);
        var fee = decimal.Round(amount * tier.FeeRate, 2, MidpointRounding.ToEven);
        fee = Math.Min(fee, tier.MaxFee);
        return new SubscriptionFeeCalculation(amount, tier.FeeRate, fee, amount - fee);
    }
}

// ===== 赎回费阶梯费率（按持有期限） =====

public sealed record RedemptionFeeTier(int MinHoldingDays, int MaxHoldingDays, decimal FeeRate);
public sealed record RedemptionFeeCalculation(decimal Units, decimal Nav, int HoldingDays, decimal FeeRate, decimal Fee, decimal NetProceeds);

public static class RedemptionFeeCalculator
{
    private static readonly RedemptionFeeTier[] _tiers =
    {
        new(0, 7, 0.015m),         // < 7 天 1.5%
        new(7, 30, 0.0075m),       // 7-30 天 0.75%
        new(30, 365, 0.005m),      // 30 天-1 年 0.5%
        new(365, 730, 0.0025m),    // 1-2 年 0.25%
        new(730, int.MaxValue, 0m)  // > 2 年 0%
    };

    public static RedemptionFeeCalculation Calculate(decimal units, decimal nav, int holdingDays)
    {
        if (units <= 0 || nav <= 0) throw new ArgumentException("份额与净值必须大于 0。");
        if (holdingDays < 0) throw new ArgumentException("持有天数不能为负。");
        var tier = _tiers.First(t => holdingDays >= t.MinHoldingDays && holdingDays < t.MaxHoldingDays);
        var gross = units * nav;
        var fee = decimal.Round(gross * tier.FeeRate, 2, MidpointRounding.ToEven);
        return new RedemptionFeeCalculation(units, nav, holdingDays, tier.FeeRate, fee, gross - fee);
    }
}
