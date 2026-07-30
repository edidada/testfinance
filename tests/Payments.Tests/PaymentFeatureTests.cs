using Payments.Api.Channels;
using Payments.Api.Clearing;
using Payments.Api.Ledger;
using Payments.Api.Reconciliation;
using Payments.Api.RiskControl;
using Payments.Api.Rtgs;
using Payments.Api.Settlement;
using Payments.Api.Transport;
using Xunit;

namespace Payments.Tests;

public sealed class PaymentFeatureTests
{
    // ===== BIC 行号校验 =====
    [Theory]
    [InlineData("ICBKCNBJ", true)]        // 8 位合法
    [InlineData("ICBKCNBJXXX", true)]      // 11 位合法
    [InlineData("ICBKCN", false)]          // 太短
    [InlineData("1234CNBJ", false)]        // 前 4 位非字母
    public void Bic_validator_checks_format(string bic, bool expected)
        => Assert.Equal(expected, BicValidator.IsValid(bic));

    // ===== 借贷平衡校验 =====
    [Fact]
    public void Double_entry_rejects_unbalanced()
    {
        var unbalanced = new JournalEntry(Guid.NewGuid(), "B001", DateTimeOffset.UtcNow, new[]
        {
            new JournalLine("1001", 100m, 0m, "CNY", "借现金"),
            new JournalLine("2202", 0m, 99m, "CNY", "贷应付")  // 不平衡
        });
        Assert.Throws<InvalidOperationException>(() => DoubleEntryValidator.Validate(unbalanced));
    }

    [Fact]
    public void Double_entry_accepts_balanced()
    {
        var balanced = new JournalEntry(Guid.NewGuid(), "B001", DateTimeOffset.UtcNow, new[]
        {
            new JournalLine("1001", 100m, 0m, "CNY", "借现金"),
            new JournalLine("2202", 0m, 100m, "CNY", "贷应付")
        });
        DoubleEntryValidator.Validate(balanced); // 不抛异常即通过
        Assert.Equal(100m, balanced.TotalDebit);
        Assert.Equal(100m, balanced.TotalCredit);
    }

    // ===== 多币种折算 =====
    [Fact]
    public void Fx_convert_uses_usd_intermediary()
    {
        var result = FxConverter.Convert("CNY", "USD", 1000m, new DateOnly(2026, 7, 31));
        Assert.Equal("CNY", result.FromCurrency);
        Assert.Equal("USD", result.ToCurrency);
        Assert.Equal(140m, result.Converted);  // 1000 × 0.14
    }

    // ===== 多边净额清算 =====
    [Fact]
    public void Netting_reduces_bilateral_to_net()
    {
        var engine = new NettingEngine();
        var instructions = new List<ClearingInstruction>
        {
            new(Guid.NewGuid(), "BANKA", "BANKB", 100m, "CNY"),
            new(Guid.NewGuid(), "BANKB", "BANKA", 60m, "CNY"),
            new(Guid.NewGuid(), "BANKA", "BANKC", 50m, "CNY")
        };
        var result = engine.Net(instructions, "CNY");
        // BANKA: -100 +60 -50 = -90；BANKB: +100 -60 = +40；BANKC: +50
        var bankA = result.Positions.First(p => p.Bic == "BANKA");
        Assert.Equal(-90m, bankA.NetAmount);
        Assert.Equal(3, result.TotalInstructions);
        Assert.Equal(3, result.NettedInstructions);
    }

    // ===== pacs.008 报文构建 =====
    [Fact]
    public void Pacs008_builds_valid_xml()
    {
        var msg = new Pacs008Message("MSG001", "INST001", "E2E001", 10000m, "CNY",
            "ICBKCNBJ", "622200001", "张三", "BKCHCNBJ", "622200002", "李四",
            DateTimeOffset.UtcNow, "工资");
        var xml = Pacs008Builder.Build(msg);
        Assert.Contains("pacs.008", xml);
        Assert.Contains("MSG001", xml);
        Assert.Contains("10000.00", xml);
    }

    // ===== CNAPS 通道选择 =====
    [Theory]
    [InlineData(50_000m, false, CnapsChannel.HVPS)]      // 5 万以上走大额
    [InlineData(3_000m, false, CnapsChannel.BEPS)]        // 2000-5 万走小额
    [InlineData(1_000m, false, CnapsChannel.SUPER_NET)]   // 2000 以下走网银互联
    [InlineData(1_000m, true, CnapsChannel.HVPS)]         // 紧急走大额
    public void Cnaps_selects_channel_by_amount_and_urgency(decimal amount, bool urgent, CnapsChannel expected)
        => Assert.Equal(expected, CnapsBuilder.SelectChannel(amount, urgent));

    // ===== RTGS 优先级队列 =====
    [Fact]
    public void Rtgs_queue_dequeues_by_priority()
    {
        var queue = new RtgsQueue();
        queue.Enqueue(new RtgsInstruction(Guid.NewGuid(), "I1", "A", "B", 100m, "CNY", Priority.Low, RtgsStatus.Queued, DateTimeOffset.UtcNow, null));
        queue.Enqueue(new RtgsInstruction(Guid.NewGuid(), "I2", "C", "D", 200m, "CNY", Priority.High, RtgsStatus.Queued, DateTimeOffset.UtcNow.AddSeconds(1), null));
        var batch = queue.DequeueBatch(10);
        Assert.Equal("I2", batch[0].InstrId);  // High 优先出队
        Assert.Equal(RtgsStatus.Submitted, batch[0].Status);
    }

    // ===== 流动性预留 =====
    [Fact]
    public void Liquidity_reserve_deducts_from_usable()
    {
        var mgr = new LiquidityManager();
        mgr.SetBalance("BANKA", 1_000_000m);
        var reserved = mgr.Reserve("BANKA", 300_000m);
        Assert.Equal(700_000m, reserved.Usable);
    }

    // ===== 对账差异识别 =====
    [Fact]
    public void Diff_detector_finds_missing_and_mismatch()
    {
        var local = new List<ReconciliationRecord>
        {
            new("TX001", 100m, "CNY", "SUCCESS", DateTimeOffset.UtcNow),
            new("TX002", 200m, "CNY", "SUCCESS", DateTimeOffset.UtcNow),
            new("TX003", 300m, "CNY", "SUCCESS", DateTimeOffset.UtcNow)
        };
        var channel = new List<ReconciliationRecord>
        {
            new("TX001", 100m, "CNY", "SUCCESS", DateTimeOffset.UtcNow),
            new("TX002", 199m, "CNY", "SUCCESS", DateTimeOffset.UtcNow),  // 金额不符
            new("TX004", 400m, "CNY", "SUCCESS", DateTimeOffset.UtcNow)   // 本地无
        };
        var diffs = DiffDetector.Detect(local, channel);
        Assert.Equal(3, diffs.Count);  // TX002 金额不符 + TX003 本地有渠道无 + TX004 渠道有本地无
        Assert.Contains(diffs, d => d.Type == DiffType.AmountMismatch && d.TransactionId == "TX002");
        Assert.Contains(diffs, d => d.Type == DiffType.MissingInChannel && d.TransactionId == "TX003");
        Assert.Contains(diffs, d => d.Type == DiffType.MissingInLocal && d.TransactionId == "TX004");
    }

    // ===== 限额校验 =====
    [Fact]
    public void Limit_checker_blocks_single_over_limit()
    {
        var checker = new LimitChecker();
        checker.Configure(new LimitRule("M001", 100_000m, 10_000m, 10));
        var result = checker.Check("M001", 15_000m);
        Assert.False(result.Passed);
        Assert.Contains("单笔超限", result.Reason);
    }

    // ===== 黑名单 =====
    [Fact]
    public void Blacklist_blocks_blacklisted_customer()
    {
        var svc = new BlacklistService();
        svc.Add("BAD_CUSTOMER", ListType.Blacklist, "欺诈");
        Assert.True(svc.IsBlacklisted("BAD_CUSTOMER"));
        Assert.False(svc.IsBlacklisted("GOOD_CUSTOMER"));
    }

    // ===== 风控规则引擎 =====
    [Fact]
    public void Risk_engine_blocks_night_large_transaction()
    {
        var engine = new PaymentRiskEngine();
        engine.RegisterDefaults();
        var ctx = new RiskContext("C001", "M001", 60_000m, "CNY", "192.168.1.1", 23);  // 23 点 6 万
        var result = engine.Evaluate(ctx);
        Assert.False(result.Approved);
        Assert.Contains("夜间", result.Reasons[0]);
    }

    // ===== 结算划拨 =====
    [Fact]
    public void Settlement_transfer_moves_balance()
    {
        var accounts = new SettlementAccountService();
        accounts.Open("BANKA", "银行A", "CNY");
        accounts.Open("BANKB", "银行B", "CNY");
        accounts.TopUp("BANKA", 100_000m);

        var svc = new SettlementService(accounts);
        var transfer = svc.Initiate("BANKA", "BANKB", 30_000m, "CNY", "清算");
        var settled = svc.Settle(transfer.Id);

        Assert.Equal(TransferStatus.Settled, settled.Status);
        Assert.Equal(70_000m, accounts.Get("BANKA").Balance);
        Assert.Equal(30_000m, accounts.Get("BANKB").Balance);
    }

    // ===== 渠道路由 =====
    [Fact]
    public void Channel_router_selects_lowest_cost()
    {
        var selected = ChannelRouter.Select(1_000_000m);
        // 大额应选 BankTransfer（费率最低 0.003）
        Assert.Equal(ChannelType.BankTransfer, selected.Type);
    }

    // ===== HMAC 签名验签 =====
    [Fact]
    public void Hmac_signature_round_trips()
    {
        var payload = "amount=100&currency=CNY";
        var secret = "my_secret_key";
        var signature = SignatureVerifier.SignHmac(payload, secret);
        Assert.True(SignatureVerifier.VerifyHmac(payload, signature, secret));
        Assert.False(SignatureVerifier.VerifyHmac("tampered", signature, secret));
    }

    // ===== 消息队列 =====
    [Fact]
    public void Message_queue_publish_and_consume()
    {
        var mq = new InMemoryMessageQueue();
        mq.Publish("CNAPS.HVPS", "MSG001", "<xml>payload</xml>");
        var consumed = mq.Consume("CNAPS.HVPS");
        Assert.Equal("<xml>payload</xml>", consumed);
    }

    // ===== 对账文件解析 =====
    [Fact]
    public void Csv_parser_parses_reconciliation_file()
    {
        var csv = "txId,amount,currency,status,time\nTX001,100.50,CNY,SUCCESS,2026-07-31T10:00:00Z\nTX002,200.00,CNY,SUCCESS,2026-07-31T11:00:00Z";
        var records = ReconciliationFileParser.ParseCsv(csv);
        Assert.Equal(2, records.Count);
        Assert.Equal("TX001", records[0].TransactionId);
        Assert.Equal(100.50m, records[0].Amount);
    }
}
