using System.Collections.Concurrent;

namespace Wealth.Api.Valuation;

// ===== 净值计算器（单位净值/累计净值） =====

public sealed record NavCalculation(string ProductCode, DateOnly NavDate, decimal TotalAssets, decimal TotalLiabilities, decimal TotalUnits, decimal UnitNav, decimal AccumulatedNav, decimal DailyReturn)
{
    // 单位净值 = (总资产 - 总负债) / 总份额
    // 累计净值 = 单位净值 + 历史累计分红 / 总份额
};

public static class NavCalculator
{
    public static NavCalculation Calculate(string productCode, DateOnly navDate, decimal totalAssets, decimal totalLiabilities, decimal totalUnits, decimal previousNav, decimal accumulatedDividendPerUnit = 0m)
    {
        if (totalUnits <= 0) throw new ArgumentException("总份额必须大于 0。");
        if (totalAssets < 0 || totalLiabilities < 0) throw new ArgumentException("资产/负债不能为负。");
        var netAssets = totalAssets - totalLiabilities;
        var unitNav = decimal.Round(netAssets / totalUnits, 4, MidpointRounding.ToEven);
        var accumulatedNav = decimal.Round(unitNav + accumulatedDividendPerUnit, 4, MidpointRounding.ToEven);
        var dailyReturn = previousNav > 0 ? decimal.Round((unitNav - previousNav) / previousNav, 6, MidpointRounding.ToEven) : 0m;
        return new NavCalculation(productCode, navDate, totalAssets, totalLiabilities, totalUnits, unitNav, accumulatedNav, dailyReturn);
    }
}

// ===== 估值表生成 =====

public enum ValuationMethod { AmortizedCost, FairValue }
public sealed record ValuationLine(string AssetCode, string AssetName, decimal Quantity, decimal Price, decimal MarketValue, decimal Cost, decimal UnrealizedPnL, ValuationMethod Method);
public sealed record ValuationSheet(string ProductCode, DateOnly ValuationDate, IReadOnlyList<ValuationLine> Lines, decimal TotalMarketValue, decimal TotalCost, decimal TotalUnrealizedPnL)
{
    public decimal UnrealizedPnLRatio => TotalCost > 0 ? decimal.Round(TotalUnrealizedPnL / TotalCost, 4, MidpointRounding.ToEven) : 0m;
};

public static class ValuationSheetGenerator
{
    public static ValuationSheet Generate(string productCode, DateOnly valuationDate, IReadOnlyList<ValuationLine> lines)
    {
        var totalMarket = lines.Sum(x => x.MarketValue);
        var totalCost = lines.Sum(x => x.Cost);
        var totalPnL = lines.Sum(x => x.UnrealizedPnL);
        return new ValuationSheet(productCode, valuationDate, lines, decimal.Round(totalMarket, 2, MidpointRounding.ToEven),
            decimal.Round(totalCost, 2, MidpointRounding.ToEven), decimal.Round(totalPnL, 2, MidpointRounding.ToEven));
    }
}

// ===== 历史净值序列 =====

public sealed class NavHistoryService
{
    private readonly ConcurrentDictionary<string, List<NavSnapshotEx>> _history = new(StringComparer.OrdinalIgnoreCase);

    public NavSnapshotEx Record(string productCode, DateOnly navDate, decimal unitNav, decimal accumulatedNav)
    {
        if (unitNav <= 0) throw new ArgumentException("单位净值必须大于 0。");
        var snapshot = new NavSnapshotEx(productCode, navDate, unitNav, accumulatedNav);
        var list = _history.GetOrAdd(productCode, _ => new List<NavSnapshotEx>());
        lock (list)
        {
            list.Add(snapshot);
            list.Sort((a, b) => a.NavDate.CompareTo(b.NavDate));
        }
        return snapshot;
    }

    public IReadOnlyList<NavSnapshotEx> Series(string productCode, DateOnly? from = null, DateOnly? to = null)
    {
        if (!_history.TryGetValue(productCode, out var list)) return Array.Empty<NavSnapshotEx>();
        return list.Where(x => (from is null || x.NavDate >= from) && (to is null || x.NavDate <= to)).ToArray();
    }

    // 最大回撤（Max Drawdown）
    public decimal MaxDrawdown(string productCode)
    {
        var series = Series(productCode);
        if (series.Count < 2) return 0m;
        var peak = series[0].UnitNav;
        var maxDd = 0m;
        foreach (var s in series)
        {
            if (s.UnitNav > peak) peak = s.UnitNav;
            var dd = (peak - s.UnitNav) / peak;
            if (dd > maxDd) maxDd = dd;
        }
        return decimal.Round(maxDd, 4, MidpointRounding.ToEven);
    }
}

// 重新定义 NavSnapshot（与 WealthDomain 一致但含累计净值）
public sealed record NavSnapshotEx(string ProductCode, DateOnly NavDate, decimal UnitNav, decimal AccumulatedNav);

// ===== 多币种 FX 重估 =====

public sealed record FxPosition(string Currency, decimal Amount, decimal BookRate, decimal MarketRate);
public sealed record FxRevaluation(string Currency, decimal Amount, decimal BookRate, decimal MarketRate, decimal UnrealizedFxGain)
{
    // 汇兑损益 = 持仓 × (市场汇率 - 账面汇率)
};

public static class FxRevaluationEngine
{
    public static IReadOnlyList<FxRevaluation> Revalue(IReadOnlyList<FxPosition> positions)
    {
        return positions.Select(p =>
        {
            var gain = decimal.Round(p.Amount * (p.MarketRate - p.BookRate), 2, MidpointRounding.ToEven);
            return new FxRevaluation(p.Currency, p.Amount, p.BookRate, p.MarketRate, gain);
        }).ToArray();
    }
}

// ===== 估值口径（摊余成本 vs 公允价值） =====

public static class ValuationMethodPolicy
{
    // 货币基金/固定收益类可用摊余成本法；权益类必须公允价值
    public static ValuationMethod Decide(string productType, bool hasActiveMarket)
    {
        if (productType is "MONEY_MARKET" or "FIXED_INCOME" && !hasActiveMarket) return ValuationMethod.AmortizedCost;
        return ValuationMethod.FairValue;
    }
}
