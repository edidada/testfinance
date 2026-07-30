using System.Collections.Concurrent;

namespace Wealth.Api.Products;

// ===== 产品生命周期状态机 =====

public enum ProductPhase { Raising, ClosedForRaising, Open, Suspended, Liquidating, Liquidated }
public sealed record FundProduct(string Code, string Name, string ShareClass, int RiskLevel, ProductPhase Phase, decimal MinSubscription, decimal ManagementFeeRate, decimal CustodyFeeRate, DateOnly InceptionDate, DateOnly? MaturityDate);

public sealed class ProductLifecycleService
{
    private readonly ConcurrentDictionary<string, FundProduct> _products = new(StringComparer.OrdinalIgnoreCase);

    public FundProduct Create(FundProduct product)
    {
        _products[product.Code] = product;
        return product;
    }

    public FundProduct Transition(string code, ProductPhase newPhase)
    {
        var product = Get(code);
        var valid = (product.Phase, newPhase) switch
        {
            (ProductPhase.Raising, ProductPhase.ClosedForRaising) => true,
            (ProductPhase.ClosedForRaising, ProductPhase.Open) => true,
            (ProductPhase.Open, ProductPhase.Suspended) => true,
            (ProductPhase.Suspended, ProductPhase.Open) => true,
            (ProductPhase.Open, ProductPhase.Liquidating) => true,
            (ProductPhase.Suspended, ProductPhase.Liquidating) => true,
            (ProductPhase.Liquidating, ProductPhase.Liquidated) => true,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"{product.Phase} → {newPhase} 状态转换非法。");
        var updated = product with { Phase = newPhase };
        _products[code] = updated;
        return updated;
    }

    public FundProduct Get(string code) => _products.TryGetValue(code, out var p) ? p : throw new KeyNotFoundException("产品不存在。");
    public IReadOnlyCollection<FundProduct> All() => _products.Values.OrderBy(x => x.Code).ToArray();
}

// ===== 产品目录（份额类别 A/B/C 类） =====

public enum ShareClass { A, B, C } // A 前端收费、B 后端收费、C 销售服务费
public sealed record ShareClassConfig(ShareClass Class, string Suffix, string Description, decimal SubscriptionFeeRate, decimal RedemptionFeeRate, decimal SalesServiceFeeRate);

public static class ShareClassCatalog
{
    private static readonly Dictionary<ShareClass, ShareClassConfig> _configs = new()
    {
        [ShareClass.A] = new(ShareClass.A, "A", "前端收费：申购时收申购费", 0.015m, 0.005m, 0m),
        [ShareClass.B] = new(ShareClass.B, "B", "后端收费：赎回时收申购费，按持有期递减", 0m, 0.008m, 0m),
        [ShareClass.C] = new(ShareClass.C, "C", "销售服务费：无申购赎回费，按日计提销售费", 0m, 0m, 0.004m)
    };

    public static ShareClassConfig Get(ShareClass cls) => _configs.TryGetValue(cls, out var c) ? c : throw new KeyNotFoundException($"份额类别 {cls} 不存在。");
    public static IReadOnlyCollection<ShareClassConfig> All() => _configs.Values;
}

// ===== 产品费率配置 =====

public sealed record ProductFeeConfig(string ProductCode, decimal ManagementFeeRate, decimal CustodyFeeRate, decimal SalesServiceFeeRate, decimal SubscriptionFeeRate, decimal RedemptionFeeRate);

public sealed class ProductFeeService
{
    private readonly ConcurrentDictionary<string, ProductFeeConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public ProductFeeConfig Configure(ProductFeeConfig config)
    {
        _configs[config.ProductCode] = config;
        return config;
    }

    public ProductFeeConfig Get(string productCode) => _configs.TryGetValue(productCode, out var c) ? c : throw new KeyNotFoundException("产品费率未配置。");
}

// ===== 产品风险等级映射 =====

public enum AssetClass { Equity, Bond, MoneyMarket, Mixed, Alternative }
public static class ProductRiskMapper
{
    // 资产类别 → 默认风险等级（1 低 - 5 高）
    public static int DefaultRiskLevel(AssetClass assetClass) => assetClass switch
    {
        AssetClass.MoneyMarket => 1,
        AssetClass.Bond => 2,
        AssetClass.Mixed => 3,
        AssetClass.Equity => 4,
        AssetClass.Alternative => 5,
        _ => 3
    };
}
