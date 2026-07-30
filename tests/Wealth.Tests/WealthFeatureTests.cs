using Wealth.Api.Clearing;
using Wealth.Api.Compliance;
using Wealth.Api.Fee;
using Wealth.Api.Products;
using Wealth.Api.RiskControl;
using Wealth.Api.Ta;
using Wealth.Api.Trading;
using Wealth.Api.Valuation;
using Xunit;

namespace Wealth.Tests;

public sealed class WealthFeatureTests
{
    // ===== 净值计算 =====
    [Fact]
    public void Nav_calculator_computes_unit_and_accumulated()
    {
        // 总资产 1100 万，总负债 100 万，总份额 1000 万份
        var nav = NavCalculator.Calculate("FUND001", new DateOnly(2026, 7, 31), 11_000_000m, 1_000_000m, 10_000_000m, 1.05m, 0.02m);
        Assert.Equal(1.0000m, nav.UnitNav);         // (1100-100)/1000 = 1.0
        Assert.Equal(1.0200m, nav.AccumulatedNav);  // 1.0 + 0.02
        Assert.True(nav.DailyReturn < 0);           // 1.0 - 1.05 < 0
    }

    // ===== 估值表生成 =====
    [Fact]
    public void Valuation_sheet_sums_market_value()
    {
        var lines = new[]
        {
            new ValuationLine("BOND001", "国债", 1000m, 100m, 100_000m, 98_000m, 2_000m, ValuationMethod.AmortizedCost),
            new ValuationLine("STOCK001", "股票", 500m, 20m, 10_000m, 12_000m, -2_000m, ValuationMethod.FairValue)
        };
        var sheet = ValuationSheetGenerator.Generate("FUND001", new DateOnly(2026, 7, 31), lines);
        Assert.Equal(110_000m, sheet.TotalMarketValue);
        Assert.Equal(0m, sheet.TotalUnrealizedPnL);  // 2000 - 2000 = 0
    }

    // ===== 最大回撤 =====
    [Fact]
    public void Max_drawdown_finds_peak_to_trough()
    {
        var svc = new NavHistoryService();
        svc.Record("FUND001", new DateOnly(2026, 7, 1), 1.00m, 1.00m);
        svc.Record("FUND001", new DateOnly(2026, 7, 2), 1.10m, 1.10m);  // peak
        svc.Record("FUND001", new DateOnly(2026, 7, 3), 0.99m, 1.09m);  // trough
        svc.Record("FUND001", new DateOnly(2026, 7, 4), 1.05m, 1.15m);
        var dd = svc.MaxDrawdown("FUND001");
        // (1.10 - 0.99) / 1.10 ≈ 0.1
        Assert.True(dd > 0.09m && dd < 0.11m);
    }

    // ===== FX 重估 =====
    [Fact]
    public void Fx_revaluation_computes_gain()
    {
        var positions = new[]
        {
            new FxPosition("USD", 100_000m, 7.0m, 7.1m),   // 100000 × (7.1-7.0) = 10000
            new FxPosition("EUR", 50_000m, 7.5m, 7.4m)      // 50000 × (7.4-7.5) = -5000
        };
        var results = FxRevaluationEngine.Revalue(positions);
        Assert.Equal(10_000m, results[0].UnrealizedFxGain);
        Assert.Equal(-5_000m, results[1].UnrealizedFxGain);
    }

    // ===== 管理费计提 =====
    [Fact]
    public void Management_fee_accrues_daily()
    {
        // 1 亿规模，年管理费 1.5%，日计提 = 1亿 × 1.5% / 365
        var fee = FeeAccrualCalculator.ManagementFee("FUND001", new DateOnly(2026, 7, 31), 1.0m, 100_000_000m, 0.015m);
        Assert.Equal("MANAGEMENT", fee.FeeType);
        // 1 × 1亿 × 0.015 / 365 ≈ 4109.59
        Assert.True(fee.DailyFee > 4100m && fee.DailyFee < 4110m);
    }

    // ===== 申购费阶梯 =====
    [Fact]
    public void Subscription_fee_uses_tiered_rate()
    {
        // 5 万元 → 1.5% 费率（0-10 万档）
        var small = SubscriptionFeeCalculator.Calculate(50_000m);
        Assert.Equal(0.015m, small.FeeRate);
        Assert.Equal(750m, small.Fee);

        // 50 万元 → 1.2% 费率（10 万-100 万档）
        var medium = SubscriptionFeeCalculator.Calculate(500_000m);
        Assert.Equal(0.012m, medium.FeeRate);

        // 200 万元 → 0.8% 费率（100 万-500 万档）
        var large = SubscriptionFeeCalculator.Calculate(2_000_000m);
        Assert.Equal(0.008m, large.FeeRate);
    }

    // ===== 赎回费按持有期限 =====
    [Fact]
    public void Redemption_fee_decreases_with_holding_period()
    {
        // 持有 3 天 → 1.5%
        var shortHolding = RedemptionFeeCalculator.Calculate(1000m, 1.0m, 3);
        Assert.Equal(0.015m, shortHolding.FeeRate);

        // 持有 400 天 → 0.25%
        var longHolding = RedemptionFeeCalculator.Calculate(1000m, 1.0m, 400);
        Assert.Equal(0.0025m, longHolding.FeeRate);

        // 持有 800 天 → 0%
        var veryLong = RedemptionFeeCalculator.Calculate(1000m, 1.0m, 800);
        Assert.Equal(0m, veryLong.FeeRate);
    }

    // ===== 撮合引擎 =====
    [Fact]
    public void Matching_engine_matches_buy_and_sell()
    {
        var engine = new MatchingEngine();
        var buy = new TradeOrder { ProductCode = "FUND001", Side = OrderSide.Buy, Price = 1.05m, Quantity = 1000m, Status = OrderStatus.Pending, FilledQuantity = 0m, PlacedAt = DateTimeOffset.UtcNow };
        var sell = new TradeOrder { ProductCode = "FUND001", Side = OrderSide.Sell, Price = 1.00m, Quantity = 800m, Status = OrderStatus.Pending, FilledQuantity = 0m, PlacedAt = DateTimeOffset.UtcNow.AddSeconds(1) };
        var trades = engine.Match(new[] { buy }, new[] { sell });
        Assert.Single(trades);
        Assert.Equal(800m, trades[0].Quantity);
    }

    // ===== 比例配售 =====
    [Fact]
    public void Pro_rata_allocator_scales_proportionally()
    {
        var request = new AllocationRequest("FUND001", 1000m, new[]
        {
            (Guid.NewGuid(), 600m),
            (Guid.NewGuid(), 400m)
        });
        var results = ProRataAllocator.Allocate(request);
        // 总需求 1000 = 总供给 1000，比例 1.0
        Assert.Equal(600m, results[0].AllocatedAmount);
        Assert.Equal(400m, results[1].AllocatedAmount);
    }

    [Fact]
    public void Pro_rata_allocator_scales_when_oversubscribed()
    {
        var request = new AllocationRequest("FUND001", 500m, new[]
        {
            (Guid.NewGuid(), 600m),
            (Guid.NewGuid(), 400m)
        });
        var results = ProRataAllocator.Allocate(request);
        // 比例 0.5
        Assert.Equal(300m, results[0].AllocatedAmount);
        Assert.Equal(200m, results[1].AllocatedAmount);
    }

    // ===== 分红再投 =====
    [Fact]
    public void Dividend_reinvestment_converts_to_shares()
    {
        var declaration = new DividendDeclaration("FUND001", new DateOnly(2026, 7, 31), new DateOnly(2026, 7, 31), 0.05m, DividendMethod.Reinvestment);
        var result = DividendProcessor.Process(Guid.NewGuid(), 10000m, declaration, 1.20m);
        // 现金分红 = 10000 × 0.05 = 500；再投份额 = 500 / 1.20 ≈ 416.67
        Assert.Equal(0m, result.CashDividend);
        Assert.True(result.ReinvestedShares > 416m && result.ReinvestedShares < 417m);
    }

    // ===== 基金转换 =====
    [Fact]
    public void Fund_conversion_charges_fee()
    {
        var conversion = FundConversionService.Convert(Guid.NewGuid(), "FUND_A", "FUND_B", 1000m, 1.50m, 1.00m, 0.001m);
        // 转出金额 = 1000 × 1.50 = 1500；转换费 = 1500 × 0.001 = 1.5；转入份额 = (1500-1.5)/1.00 = 1498.5
        Assert.Equal(1.5m, conversion.ConversionFee);
        Assert.Equal(1498.5m, conversion.ToShares);
    }

    // ===== 集中度校验 =====
    [Fact]
    public void Concentration_check_fails_when_over_limit()
    {
        var result = ConcentrationChecker.Check("单一发行人", 150_000m, 1_000_000m, 0.10m);
        Assert.False(result.Passed);  // 15% > 10%
        Assert.Contains("超限", result.Reason);
    }

    // ===== VaR 计算 =====
    [Fact]
    public void Var_calculator_uses_historical_percentile()
    {
        // 100 个收益率，大部分在 -0.01 到 0.01，最差的 5% 约 -0.03
        var random = new Random(42);
        var returns = Enumerable.Range(0, 100).Select(_ => (decimal)(random.NextDouble() * 0.04 - 0.02)).ToArray();
        var var = VarCalculator.Historical(returns, 1_000_000m);
        Assert.True(var.Confidence95 > 0);
        Assert.True(var.Confidence99 >= var.Confidence95);
    }

    // ===== 投资限制校验 =====
    [Fact]
    public void Investment_restriction_checks_all_rules()
    {
        var result = InvestmentRestrictionChecker.Check(
            singleIssuer: (150_000m, 1_000_000m),     // 15% > 10% 超限
            singleBond: (150_000m, 1_000_000m),        // 15% < 20% 通过
            equity: (300_000m, 1_000_000m),            // 30% < 40% 通过
            liquid: (60_000m, 1_000_000m));            // 6% > 5% 通过
        Assert.False(result.AllPassed);
        Assert.Contains(result.Results, r => r.RuleId == "R_SINGLE_ISSUER" && !r.Passed);
    }

    // ===== 适当性匹配 =====
    [Fact]
    public void Suitability_matcher_requires_customer_risk_above_product()
    {
        var matched = SuitabilityMatcher.Match(3, 2);
        Assert.True(matched.Matched);

        var mismatched = SuitabilityMatcher.Match(2, 4);
        Assert.False(mismatched.Matched);
    }

    // ===== AML 大额现金 =====
    [Fact]
    public void Aml_detects_large_cash_transaction()
    {
        var engine = new AmlRuleEngine();
        var alert = engine.CheckLargeCash("C001", 60_000m, "CASH");
        Assert.NotNull(alert);
        Assert.Equal(AmlAlertType.LargeCash, alert!.Type);
    }

    [Fact]
    public void Aml_ignores_small_non_cash()
    {
        var engine = new AmlRuleEngine();
        var alert = engine.CheckLargeCash("C001", 10_000m, "TRANSFER");
        Assert.Null(alert);
    }

    // ===== 产品生命周期 =====
    [Fact]
    public void Product_lifecycle_transitions_validly()
    {
        var svc = new ProductLifecycleService();
        svc.Create(new FundProduct("FUND001", "测试基金", "A", 3, ProductPhase.Raising, 1000m, 0.015m, 0.002m, new DateOnly(2026, 1, 1), null));
        var open = svc.Transition("FUND001", ProductPhase.ClosedForRaising);
        Assert.Equal(ProductPhase.ClosedForRaising, open.Phase);
        var opened = svc.Transition("FUND001", ProductPhase.Open);
        Assert.Equal(ProductPhase.Open, opened.Phase);
    }

    [Fact]
    public void Product_lifecycle_rejects_invalid_transition()
    {
        var svc = new ProductLifecycleService();
        svc.Create(new FundProduct("FUND002", "测试基金2", "A", 3, ProductPhase.Raising, 1000m, 0.015m, 0.002m, new DateOnly(2026, 1, 1), null));
        Assert.Throws<InvalidOperationException>(() => svc.Transition("FUND002", ProductPhase.Open));  // 不能从 Raising 直接到 Open
    }

    // ===== 资金清算 =====
    [Fact]
    public void Fund_clearing_computes_net_flow()
    {
        var svc = new FundClearingService();
        var record = svc.Clear("FUND001", new DateOnly(2026, 7, 31), 1_000_000m, 600_000m);
        Assert.Equal(400_000m, record.NetFlow);  // 申购 - 赎回
        Assert.Equal("CLEARED", record.Status);
    }

    // ===== TA 账户冻结 =====
    [Fact]
    public void Ta_account_freeze_deducts_available()
    {
        var svc = new TaAccountService();
        var account = svc.Open("C001", "FUND001", "A");
        Assert.Equal(0m, account.AvailableShares);
    }

    // ===== 对账差异识别 =====
    [Fact]
    public void Clearing_diff_detects_mismatch()
    {
        var svc = new ClearingDiffService();
        var diff = svc.Detect("FUND001", ClearingDiffType.Balance, 1_000_000m, 999_500m);
        Assert.Equal(500m, diff.Difference);
        Assert.Throws<InvalidOperationException>(() => svc.Detect("FUND002", ClearingDiffType.Balance, 100m, 100m));  // 无差异
    }
}
