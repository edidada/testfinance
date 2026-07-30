using System.Collections.Concurrent;
using System.Text;

namespace Payments.Api.Clearing;

// ===== BIC 行号校验（ISO 9362） =====

public static class BicValidator
{
    // BIC 格式：8 或 11 位，前 4 银行代码、2 国家码、2 地区码、3 分支（可选）
    public static bool IsValid(string bic)
    {
        if (string.IsNullOrWhiteSpace(bic)) return false;
        bic = bic.ToUpperInvariant();
        if (bic.Length is not 8 and not 11) return false;
        if (!bic[..4].All(c => char.IsLetter(c))) return false;
        if (!char.IsLetter(bic[4]) || !char.IsLetter(bic[5])) return false; // 国家码
        return bic[6..].All(c => char.IsLetterOrDigit(c));
    }
}

// ===== ISO 20022 pacs.008 报文构建器（金融机构间客户贷记转账） =====

public sealed record Pacs008Message(
    string MessageId, string InstrId, string EndToEndId, decimal Amount, string Currency,
    string DebtorBic, string DebtorAccount, string DebtorName,
    string CreditorBic, string CreditorAccount, string CreditorName,
    DateTimeOffset RequestedExecutionDate, string RemittanceInfo);

public static class Pacs008Builder
{
    // 生成 ISO 20022 pacs.008 XML 报文（Demo 简化版）
    public static string Build(Pacs008Message msg)
    {
        if (!BicValidator.IsValid(msg.DebtorBic) || !BicValidator.IsValid(msg.CreditorBic))
            throw new ArgumentException("BIC 行号无效。");
        if (msg.Amount <= 0) throw new ArgumentException("金额必须大于 0。");
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pacs.008.001.09"">
  <FIToFIPmtSts>
    <GrpHdr>
      <MsgId>{msg.MessageId}</MsgId>
      <CreDtTm>{msg.RequestedExecutionDate:yyyy-MM-ddTHH:mm:ssZ}</CreDtTm>
    </GrpHdr>
    <CdtTrfTxInf>
      <PmtId><InstrId>{msg.InstrId}</InstrId><EndToEndId>{msg.EndToEndId}</EndToEndId></PmtId>
      <Amt><IntrBkSttlmAmt Ccy=""{msg.Currency}"">{msg.Amount:F2}</IntrBkSttlmAmt></Amt>
      <DbtrAgt><FinInstnId><BICFI>{msg.DebtorBic}</BICFI></FinInstnId></DbtrAgt>
      <Dbtr><Nm>{msg.DebtorName}</Nm></Dbtr>
      <DbtrAcct><Id><Othr><Id>{msg.DebtorAccount}</Id></Othr></Id></DbtrAcct>
      <CdtrAgt><FinInstnId><BICFI>{msg.CreditorBic}</BICFI></FinInstnId></CdtrAgt>
      <Cdtr><Nm>{msg.CreditorName}</Nm></Cdtr>
      <CdtrAcct><Id><Othr><Id>{msg.CreditorAccount}</Id></Othr></Id></CdtrAcct>
      <RmtInf><Ustrd>{msg.RemittanceInfo}</Ustrd></RmtInf>
    </CdtTrfTxInf>
  </FIToFIPmtSts>
</Document>";
    }
}

// ===== pacs.002 状态报告解析器 =====

public sealed record Pacs002Status(string MessageId, string EndToEndId, string TxStatus, string ReasonCode, DateTimeOffset ProcessedAt);

public static class Pacs002Parser
{
    public static Pacs002Status Parse(string xml)
    {
        // Demo 简化解析（生产用 XmlReader）
        var msgId = Extract(xml, "<MsgId>", "</MsgId>");
        var e2e = Extract(xml, "<EndToEndId>", "</EndToEndId>");
        var status = Extract(xml, "<TxSts>", "</TxSts>");
        var reason = Extract(xml, "<Rsn>", "<Cd>", "</Cd>");
        return new Pacs002Status(msgId, e2e, status, reason, DateTimeOffset.UtcNow);
    }

    private static string Extract(string xml, string start, string end)
    {
        var s = xml.IndexOf(start, StringComparison.Ordinal);
        var e = xml.IndexOf(end, s + start.Length, StringComparison.Ordinal);
        return s >= 0 && e > s ? xml[(s + start.Length)..e] : string.Empty;
    }

    private static string Extract(string xml, string wrapper1, string wrapper2, string end)
    {
        var s = xml.IndexOf(wrapper1, StringComparison.Ordinal);
        if (s < 0) return string.Empty;
        s = xml.IndexOf(wrapper2, s, StringComparison.Ordinal);
        var e = xml.IndexOf(end, s + wrapper2.Length, StringComparison.Ordinal);
        return s >= 0 && e > s ? xml[(s + wrapper2.Length)..e] : string.Empty;
    }
}

// ===== 多边净额清算（Bilateral / Multilateral Netting） =====

public sealed record ClearingInstruction(Guid Id, string DebtorBic, string CreditorBic, decimal Amount, string Currency);
public sealed record NetPosition(string Bic, decimal NetAmount, int InboundCount, int OutboundCount);
public sealed record NettingResult(string Currency, IReadOnlyList<NetPosition> Positions, int TotalInstructions, int NettedInstructions);

public sealed class NettingEngine
{
    // 多边净额：将互为债权债务的多笔指令轧差为单边净额
    public NettingResult Net(IReadOnlyList<ClearingInstruction> instructions, string currency)
    {
        if (instructions.Count == 0) return new NettingResult(currency, [], 0, 0);
        var balances = new Dictionary<string, decimal>();
        var inbound = new Dictionary<string, int>();
        var outbound = new Dictionary<string, int>();

        foreach (var inst in instructions)
        {
            balances.TryGetValue(inst.DebtorBic, out var debited);
            balances[inst.DebtorBic] = debited - inst.Amount;  // 付款方减
            balances.TryGetValue(inst.CreditorBic, out var credited);
            balances[inst.CreditorBic] = credited + inst.Amount; // 收款方加
            inbound[inst.CreditorBic] = inbound.GetValueOrDefault(inst.CreditorBic) + 1;
            outbound[inst.DebtorBic] = outbound.GetValueOrDefault(inst.DebtorBic) + 1;
        }

        var positions = balances.Select(kv => new NetPosition(
            kv.Key,
            decimal.Round(kv.Value, 2, MidpointRounding.ToEven),
            inbound.GetValueOrDefault(kv.Key),
            outbound.GetValueOrDefault(kv.Key))).OrderBy(x => x.Bic).ToArray();

        return new NettingResult(currency, positions, instructions.Count, positions.Length);
    }
}

// ===== 清算批次状态机 =====

public enum ClearingBatchStatus { Open, Closed, Settled, Rejected }
public sealed record ClearingBatch(Guid Id, string Currency, DateOnly BusinessDate, ClearingBatchStatus Status, IReadOnlyList<ClearingInstruction> Instructions, DateTimeOffset CreatedAt);

public sealed class ClearingBatchService
{
    private readonly ConcurrentDictionary<Guid, ClearingBatch> _batches = new();

    public ClearingBatch Open(string currency, DateOnly businessDate)
    {
        var batch = new ClearingBatch(Guid.NewGuid(), currency, businessDate, ClearingBatchStatus.Open, [], DateTimeOffset.UtcNow);
        _batches[batch.Id] = batch;
        return batch;
    }

    public ClearingBatch Add(Guid batchId, ClearingInstruction instruction)
    {
        var batch = Get(batchId);
        if (batch.Status != ClearingBatchStatus.Open) throw new InvalidOperationException("批次已关闭。");
        return Update(batch with { Instructions = batch.Instructions.Append(instruction).ToArray() });
    }

    public ClearingBatch Close(Guid batchId)
    {
        var batch = Get(batchId);
        if (batch.Status != ClearingBatchStatus.Open) throw new InvalidOperationException("批次不在开放状态。");
        return Update(batch with { Status = ClearingBatchStatus.Closed });
    }

    public ClearingBatch Settle(Guid batchId)
    {
        var batch = Get(batchId);
        if (batch.Status != ClearingBatchStatus.Closed) throw new InvalidOperationException("仅已关闭批次可结算。");
        return Update(batch with { Status = ClearingBatchStatus.Settled });
    }

    public ClearingBatch Get(Guid batchId) => _batches.TryGetValue(batchId, out var b) ? b : throw new KeyNotFoundException("清算批次不存在。");
    private ClearingBatch Update(ClearingBatch b) { _batches[b.Id] = b; return b; }
}

// ===== CNAPS 指令构建（HVPS/BEPS/SUPER_NET） =====

public enum CnapsChannel { HVPS, BEPS, SUPER_NET, IBPS } // 大额/小额/网银互联/网上支付跨行清算
public sealed record CnapsInstruction(string MsgType, CnapsChannel Channel, string DebtorAccount, string CreditorAccount, decimal Amount, string Currency);

public static class CnapsBuilder
{
    // 根据金额与紧急程度选择 CNAPS 通道
    public static CnapsChannel SelectChannel(decimal amount, bool urgent)
    {
        if (urgent || amount >= 50_000m) return CnapsChannel.HVPS;  // 大额实时
        if (amount >= 2_000m) return CnapsChannel.BEPS;              // 小额批量
        return CnapsChannel.SUPER_NET;                                // 网银互联
    }

    public static CnapsInstruction Build(CnapsChannel channel, string debtorAccount, string creditorAccount, decimal amount, string currency)
    {
        var msgType = channel switch
        {
            CnapsChannel.HVPS => "HVPS.111",      // 大额贷记
            CnapsChannel.BEPS => "BEPS.121",       // 小额贷记
            CnapsChannel.SUPER_NET => "SUPER_NET.001",
            CnapsChannel.IBPS => "IBPS.101",
            _ => throw new ArgumentException($"未知 CNAPS 通道：{channel}")
        };
        return new CnapsInstruction(msgType, channel, debtorAccount, creditorAccount, amount, currency);
    }
}
