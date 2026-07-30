using Lending.Api.Bills;
using Lending.Api.Cbdc;
using Lending.Api.Components;
using Lending.Api.InLoan;
using Lending.Api.PostLoan;
using Lending.Api.PreLoan;
using Lending.Api.Products;
using Lending.Api.Reporting;
using Xunit;

namespace Lending.Tests;

public sealed class LendingFeatureTests
{
    // ===== 五级分类 =====
    [Theory]
    [InlineData(0, AssetClassification.Normal)]
    [InlineData(15, AssetClassification.SpecialMention)]
    [InlineData(31, AssetClassification.Substandard)]
    [InlineData(65, AssetClassification.Doubtful)]
    [InlineData(91, AssetClassification.Loss)]
    public void Five_tier_classifier_maps_days_correctly(int dpd, AssetClassification expected)
        => Assert.Equal(expected, FiveTierClassifier.Classify(dpd));

    [Fact]
    public void Substandard_and_above_are_non_performing()
    {
        Assert.True(FiveTierClassifier.IsNonPerforming(AssetClassification.Substandard));
        Assert.False(FiveTierClassifier.IsNonPerforming(AssetClassification.SpecialMention));
    }

    // ===== 罚息计算 =====
    [Fact]
    public void Penalty_uses_150_percent_rate_over_360_days()
    {
        // 10000 × (0.08 × 1.5 / 360) × 30 = 100
        var p = PenaltyCalculator.Calculate(Guid.NewGuid(), 1, 10000m, 0.08m, 30);
        Assert.Equal(100m, p.PenaltyAmount);
        Assert.Equal(0.12m, p.PenaltyRate);
    }

    // ===== 财务指标 =====
    [Fact]
    public void Financial_metrics_compute_debt_ratio_and_ebit()
    {
        var s = new FinancialStatement(TotalAssets: 100m, TotalLiabilities: 40m, CurrentAssets: 60m, CurrentLiabilities: 30m, Revenue: 200m, OperatingCosts: 150m, InterestExpense: 10m, TaxExpense: 5m);
        var m = FinancialMetricsCalculator.Compute(s);
        Assert.Equal(0.4m, m.DebtToAssetRatio);
        Assert.Equal(2m, m.CurrentRatio);
        Assert.Equal(50m, m.EBIT);
        Assert.Equal(5m, m.InterestCoverageRatio);
    }

    // ===== 内部评级 IRB =====
    [Fact]
    public void Irb_unsecured_loan_uses_45_percent_lgd()
    {
        // PD=0.15%, LGD=0.45, EAD=100000 → EL = 0.0015 × 0.45 × 100000 = 67.5
        var r = InternalRatingEngine.Rate(Guid.NewGuid(), "BBB", 100000m, 0m);
        Assert.Equal(0.15m, r.PD);
        Assert.Equal(0.45m, r.LGD);
        Assert.Equal(67.5m, r.ExpectedLoss);
    }

    // ===== 提前还款 =====
    [Fact]
    public void Early_repayment_full_charges_penalty_when_term_short()
    {
        var svc = new EarlyRepaymentService();
        var req = new EarlyRepaymentRequest(Guid.NewGuid(), EarlyRepaymentKind.Full, 50000m, DateTimeOffset.UtcNow);
        var r = svc.Settle(req, remainingPrincipal: 50000m, remainingInstallments: 3, annualRate: 0.08m);
        Assert.Equal(50000m, r.PrincipalPaid);
        // 剩余 3 期 < 6 → 违约金率 2%
        Assert.Equal(1000m, r.PenaltyFee);
        Assert.Equal(51000m, r.TotalDue);
    }

    // ===== 票据贴现 =====
    [Fact]
    public void Discount_interest_uses_360_day_basis()
    {
        // 100000 × 0.04 × 90 / 360 = 1000
        var d = new DiscountService().Discount(Guid.NewGuid(), "holder", 100000m, 0.04m, 90);
        Assert.Equal(1000m, d.DiscountInterest);
        Assert.Equal(99000m, d.NetProceeds);
    }

    // ===== 票据池 =====
    [Fact]
    public void Bill_pool_financing_capped_at_90_percent()
    {
        var svc = new BillPoolService();
        var pool = svc.Open("owner-1");
        svc.Deposit(pool.Id, Guid.NewGuid(), 100000m, new DateOnly(2026, 12, 31));
        var financed = svc.Finance(pool.Id, 90000m);
        Assert.Equal(90000m, financed.FinancedAmount);
        Assert.Throws<InvalidOperationException>(() => svc.Finance(pool.Id, 1m)); // 已达上限
    }

    // ===== CBDC 钱包 + 智能合约 =====
    [Fact]
    public void Cbdc_wallet_transfer_respects_payment_limit()
    {
        var svc = new CBDCWalletService();
        var w1 = svc.Open("u1", WalletType.Personal, WalletLevel.Level1); // 限额 1000
        var w2 = svc.Open("u2", WalletType.Personal, WalletLevel.Level2);
        svc.TopUp(w1.Id, 5000m);
        // 超限额
        Assert.Throws<InvalidOperationException>(() => svc.Transfer(w1.Id, w2.Id, 1001m));
        // 限额内成功
        svc.Transfer(w1.Id, w2.Id, 1000m);
        Assert.Equal(4000m, svc.Get(w1.Id).Balance);
        Assert.Equal(1000m, svc.Get(w2.Id).Balance);
    }

    [Fact]
    public void Smart_contract_executes_only_when_condition_met()
    {
        var wallets = new CBDCWalletService();
        var from = wallets.Open("u1", WalletType.Corporate, WalletLevel.Level4);
        var to = wallets.Open("u2", WalletType.Corporate, WalletLevel.Level4);
        wallets.TopUp(from.Id, 1000m);
        var engine = new SmartContractEngine(wallets);
        var contract = engine.Deploy(from.Id, to.Id, 500m, "goods_delivered==true", TimeSpan.FromMinutes(5));

        // 条件未满足
        Assert.Throws<InvalidOperationException>(() => engine.Fulfill(contract.Id, new Dictionary<string, string> { ["goods_delivered"] = "false" }));

        // 条件满足 → 执行
        var executed = engine.Fulfill(contract.Id, new Dictionary<string, string> { ["goods_delivered"] = "true" });
        Assert.Equal(ContractStatus.Executed, executed.Status);
        Assert.Equal(500m, wallets.Get(to.Id).Balance);
    }

    // ===== 工作流引擎（会签节点需全部通过） =====
    [Fact]
    public void Workflow_countersign_requires_all_approvers()
    {
        var engine = new WorkflowEngine();
        var nodes = new[]
        {
            new WorkflowNode("N1", "初审", WorkflowNodeKind.Serial, new[] { "officer" }, 60),
            new WorkflowNode("N2", "会签", WorkflowNodeKind.Countersign, new[] { "risk1", "risk2" }, 120),
            new WorkflowNode("N3", "终审", WorkflowNodeKind.Serial, new[] { "approver" }, 240)
        };
        var inst = engine.Start("DEF", "biz-1", nodes);
        Assert.Equal("N1", inst.CurrentNodeId);

        inst = engine.Approve(inst.Id, "officer", true, null);
        Assert.Equal("N2", inst.CurrentNodeId);

        // 仅 risk1 通过，仍停留在 N2
        inst = engine.Approve(inst.Id, "risk1", true, null);
        Assert.Equal("N2", inst.CurrentNodeId);

        // risk2 通过后推进到 N3
        inst = engine.Approve(inst.Id, "risk2", true, null);
        Assert.Equal("N3", inst.CurrentNodeId);

        inst = engine.Approve(inst.Id, "approver", true, null);
        Assert.Equal("APPROVED", inst.Status);
    }

    [Fact]
    public void Workflow_rejection_terminates_instance()
    {
        var engine = new WorkflowEngine();
        var nodes = new[] { new WorkflowNode("N1", "初审", WorkflowNodeKind.Serial, new[] { "officer" }, 60) };
        var inst = engine.Start("DEF", "biz-2", nodes);
        inst = engine.Approve(inst.Id, "officer", false, "资料不全");
        Assert.Equal("REJECTED", inst.Status);
    }

    // ===== 规则引擎 =====
    [Fact]
    public void Rule_engine_rejects_blacklisted_customer()
    {
        var inner = new RuleEngine();
        var rs = new CreditRuleSet(inner);
        rs.RegisterDefaults();
        var result = rs.Evaluate(new Dictionary<string, object> { ["blacklisted"] = true });
        Assert.False(result.Passed);
        Assert.Contains("客户命中黑名单", result.RejectReasons);
    }

    [Fact]
    public void Rule_engine_passes_clean_customer()
    {
        var inner = new RuleEngine();
        var rs = new CreditRuleSet(inner);
        rs.RegisterDefaults();
        var result = rs.Evaluate(new Dictionary<string, object> { ["blacklisted"] = false, ["inquiry_90d"] = 1, ["age"] = 30, ["dti"] = 0.3m });
        Assert.True(result.Passed);
    }

    // ===== 受托支付策略 =====
    [Fact]
    public void Entrusted_payment_required_above_threshold()
    {
        Assert.Equal(PaymentChannel.Direct, PaymentChannelPolicy.Decide(100_000m, "GENERAL"));
        Assert.Equal(PaymentChannel.Entrusted, PaymentChannelPolicy.Decide(300_000m, "GENERAL"));
        Assert.Equal(PaymentChannel.Entrusted, PaymentChannelPolicy.Decide(50_000m, "ENTRUSTED"));
    }

    // ===== 产品校验 =====
    [Fact]
    public void Product_catalog_validates_amount_and_term()
    {
        Assert.True(ProductCatalog.Validate(ProductKind.Consumer, 50000m, 12, 0.06m));
        Assert.False(ProductCatalog.Validate(ProductKind.Consumer, 500000m, 12, 0.06m)); // 超出消费贷上限
    }

    // ===== 供应链金融 =====
    [Fact]
    public void Supply_chain_financable_amount_is_80_percent_of_confirmed_receivable()
    {
        var svc = new SupplyChainService();
        svc.RegisterCore(new CoreEnterprise("C1", "核心企业", 10_000_000m, 0m));
        var r = svc.UploadReceivable(new Receivable(Guid.NewGuid(), "C1", 100000m, new DateOnly(2026, 12, 31), false));
        // 未确认不可融资
        Assert.Throws<InvalidOperationException>(() => svc.FinancableAmount(r.Id));
        svc.Confirm(r.Id, "C1");
        Assert.Equal(80000m, svc.FinancableAmount(r.Id));
    }

    // ===== 资产质量报送 =====
    [Fact]
    public void Asset_quality_npl_ratio_computed_correctly()
    {
        var snap = new AssetReportService().Snapshot("2026-06", totalLoanBalance: 100_000_000m, nonPerformingBalance: 2_000_000m, provisionBalance: 4_000_000m);
        Assert.Equal(0.02m, snap.NPLRatio);
        Assert.Equal(2m, snap.ProvisionCoverage); // 400万/200万
    }

    // ===== Outbox 内存队列 =====
    [Fact]
    public void Outbox_tracks_pending_and_processed()
    {
        var outbox = new InMemoryOutbox();
        outbox.Enqueue("Loan", Guid.NewGuid(), "LoanDisbursed", new { Amount = 1000m });
        Assert.Single(outbox.Pending());
        var msg = outbox.Pending()[0];
        outbox.MarkProcessed(msg.Id);
        Assert.Empty(outbox.Pending());
    }
}
