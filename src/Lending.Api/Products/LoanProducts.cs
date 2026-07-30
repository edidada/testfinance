namespace Lending.Api.Products;

// ===== 信贷产品体系（8 类产品配置，复用 LoanApplication） =====

public enum ProductKind
{
    Consumer,        // 消费信贷
    Corporate,       // 对公信贷
    Retail,          // 零售信贷（住房/汽车/消费）
    SupplyChain,     // 供应链金融
    OnlineLoan,      // 网络贷款
    Inclusive,       // 普惠贷
    SME,             // 小微贷
    Agricultural,    // 农贷
    NonBank          // 非银信贷
}

public sealed record ProductDefinition(
    ProductKind Kind,
    string Code,
    string Name,
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermMonths,
    int MaxTermMonths,
    decimal MinRate,
    decimal MaxRate,
    bool RequireCollateral,
    bool RequireGuarantor,
    string RegulatoryTag);

// 信贷产品目录（Demo 配置）
public static class ProductCatalog
{
    private static readonly Dictionary<ProductKind, ProductDefinition> _catalog = new()
    {
        [ProductKind.Consumer] = new(ProductKind.Consumer, "CONSUMER_001", "个人消费贷", 1_000m, 200_000m, 3, 60, 0.0435m, 0.18m, false, false, "RETAIL"),
        [ProductKind.Corporate] = new(ProductKind.Corporate, "CORP_001", "对公流动资金贷款", 100_000m, 50_000_000m, 6, 120, 0.0385m, 0.10m, true, true, "CORPORATE"),
        [ProductKind.Retail] = new(ProductKind.Retail, "RETAIL_HOUSE", "个人住房贷款", 100_000m, 10_000_000m, 12, 360, 0.0325m, 0.065m, true, false, "MORTGAGE"),
        [ProductKind.SupplyChain] = new(ProductKind.SupplyChain, "SCF_001", "应收账款融资", 50_000m, 20_000_000m, 1, 12, 0.045m, 0.12m, false, true, "SCF"),
        [ProductKind.OnlineLoan] = new(ProductKind.OnlineLoan, "ONLINE_001", "互联网消费贷", 500m, 100_000m, 1, 36, 0.05m, 0.24m, false, false, "ONLINE"),
        [ProductKind.Inclusive] = new(ProductKind.Inclusive, "INCLUSIVE_001", "普惠经营贷", 10_000m, 5_000_000m, 6, 60, 0.0385m, 0.075m, false, false, "INCLUSIVE"),
        [ProductKind.SME] = new(ProductKind.SME, "SME_001", "小微企业贷", 50_000m, 10_000_000m, 6, 60, 0.04m, 0.10m, true, true, "SME"),
        [ProductKind.Agricultural] = new(ProductKind.Agricultural, "AGRI_001", "涉农贷款", 5_000m, 3_000_000m, 6, 60, 0.035m, 0.08m, false, false, "AGRI"),
        [ProductKind.NonBank] = new(ProductKind.NonBank, "NB_001", "非银消费贷", 1_000m, 200_000m, 3, 36, 0.06m, 0.30m, false, false, "NON_BANK")
    };

    public static ProductDefinition Get(ProductKind kind) =>
        _catalog.TryGetValue(kind, out var def) ? def : throw new KeyNotFoundException($"未知产品类型：{kind}");

    public static IReadOnlyCollection<ProductDefinition> All() => _catalog.Values;

    // 产品准入校验：金额/期限/利率是否在产品区间内
    public static bool Validate(ProductKind kind, decimal amount, int termMonths, decimal annualRate)
    {
        var def = Get(kind);
        return amount >= def.MinAmount && amount <= def.MaxAmount
            && termMonths >= def.MinTermMonths && termMonths <= def.MaxTermMonths
            && annualRate >= def.MinRate && annualRate <= def.MaxRate;
    }
}

// 供应链金融：核心企业信用传递
public sealed record CoreEnterprise(string Id, string Name, decimal CreditLimit, decimal UsedLimit);
public sealed record Receivable(Guid Id, string CoreEnterpriseId, decimal Amount, DateOnly DueDate, bool Confirmed);

public sealed class SupplyChainService
{
    private readonly Dictionary<string, CoreEnterprise> _cores = new();
    private readonly List<Receivable> _receivables = new();

    public void RegisterCore(CoreEnterprise core) => _cores[core.Id] = core;
    public Receivable UploadReceivable(Receivable r) { _receivables.Add(r); return r; }

    public Receivable Confirm(Guid receivableId, string coreId)
    {
        var r = _receivables.FirstOrDefault(x => x.Id == receivableId) ?? throw new KeyNotFoundException("应收账款不存在。");
        if (!_cores.ContainsKey(coreId)) throw new KeyNotFoundException("核心企业不存在。");
        var confirmed = r with { Confirmed = true };
        var idx = _receivables.FindIndex(x => x.Id == receivableId);
        _receivables[idx] = confirmed;
        return confirmed;
    }

    // 可融资额度 = 应收账款金额 × 折扣率（Demo 80%）
    public decimal FinancableAmount(Guid receivableId)
    {
        var r = _receivables.FirstOrDefault(x => x.Id == receivableId) ?? throw new KeyNotFoundException("应收账款不存在。");
        if (!r.Confirmed) throw new InvalidOperationException("应收账款未确认，不可融资。");
        return decimal.Round(r.Amount * 0.8m, 2, MidpointRounding.ToEven);
    }
}
