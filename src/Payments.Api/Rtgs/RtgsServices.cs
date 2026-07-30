using System.Collections.Concurrent;

namespace Payments.Api.Rtgs;

// ===== RTGS 指令状态机（大额实时支付） =====

public enum RtgsStatus { Queued, Submitted, Settled, Rejected, Returned }
public enum Priority { High, Normal, Low }
public sealed record RtgsInstruction(
    Guid Id, string InstrId, string DebtorBic, string CreditorBic,
    decimal Amount, string Currency, Priority Priority, RtgsStatus Status,
    DateTimeOffset QueuedAt, DateTimeOffset? SettledAt);

// ===== 优先级队列（按优先级 + 提交时间排序） =====

public sealed class RtgsQueue
{
    private readonly ConcurrentDictionary<Guid, RtgsInstruction> _queue = new();

    public RtgsInstruction Enqueue(RtgsInstruction instruction)
    {
        if (instruction.Status != RtgsStatus.Queued) throw new ArgumentException("指令必须为排队状态。");
        _queue[instruction.Id] = instruction;
        return instruction;
    }

    // 按优先级出队（High > Normal > Low，同优先级按排队时间）
    public IReadOnlyList<RtgsInstruction> DequeueBatch(int limit)
    {
        return _queue.Values
            .Where(x => x.Status == RtgsStatus.Queued)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.QueuedAt)
            .Take(limit)
            .Select(x => x with { Status = RtgsStatus.Submitted })
            .Select(x => { _queue[x.Id] = x; return x; })
            .ToArray();
    }

    public RtgsInstruction Settle(Guid id)
    {
        if (!_queue.TryGetValue(id, out var inst)) throw new KeyNotFoundException("RTGS 指令不存在。");
        if (inst.Status != RtgsStatus.Submitted) throw new InvalidOperationException("仅已提交指令可结算。");
        var settled = inst with { Status = RtgsStatus.Settled, SettledAt = DateTimeOffset.UtcNow };
        _queue[id] = settled;
        return settled;
    }

    public IReadOnlyList<RtgsInstruction> Pending() => _queue.Values.Where(x => x.Status == RtgsStatus.Queued).ToArray();
}

// ===== 流动性查询与预留 =====

public sealed record LiquidityPosition(string Bic, decimal AvailableBalance, decimal ReservedAmount, decimal NetPosition)
{
    public decimal Usable => AvailableBalance - ReservedAmount;
}

public sealed class LiquidityManager
{
    private readonly ConcurrentDictionary<string, LiquidityPosition> _positions = new();

    public void SetBalance(string bic, decimal balance) =>
        _positions[bic] = new LiquidityPosition(bic, balance, 0m, 0m);

    public LiquidityPosition Reserve(string bic, decimal amount)
    {
        var pos = Get(bic);
        if (amount > pos.Usable) throw new InvalidOperationException($"可用流动性不足：{pos.Usable} < {amount}");
        var updated = pos with { ReservedAmount = pos.ReservedAmount + amount };
        _positions[bic] = updated;
        return updated;
    }

    public LiquidityPosition Consume(string bic, decimal amount)
    {
        var pos = Get(bic);
        var updated = pos with { AvailableBalance = pos.AvailableBalance - amount, ReservedAmount = Math.Max(0, pos.ReservedAmount - amount) };
        _positions[bic] = updated;
        return updated;
    }

    public LiquidityPosition Get(string bic) =>
        _positions.TryGetValue(bic, out var p) ? p : throw new KeyNotFoundException($"机构 {bic} 流动性头寸不存在。");
}

// ===== 日终轧账（End of Day Net Position） =====

public sealed record EodSettlement(string Bic, decimal TotalDebit, decimal TotalCredit, decimal NetPosition, string Status, DateTimeOffset SettledAt)
{
    // NetPosition > 0 为净收款，< 0 为净付款
};

public sealed class EodSettlementService
{
    public EodSettlement Settle(string bic, decimal totalDebit, decimal totalCredit)
    {
        var net = decimal.Round(totalCredit - totalDebit, 2, MidpointRounding.ToEven);
        var status = net >= 0 ? "NET_CREDIT" : "NET_DEBIT";
        return new EodSettlement(bic, totalDebit, totalCredit, net, status, DateTimeOffset.UtcNow);
    }
}
