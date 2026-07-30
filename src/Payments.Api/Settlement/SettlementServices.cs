using System.Collections.Concurrent;

namespace Payments.Api.Settlement;

// ===== 结算账户 =====

public sealed record SettlementAccount(string Bic, string Name, string Currency, decimal Balance, DateTimeOffset UpdatedAt);

public sealed class SettlementAccountService
{
    private readonly ConcurrentDictionary<string, SettlementAccount> _accounts = new();

    public SettlementAccount Open(string bic, string name, string currency)
    {
        var account = new SettlementAccount(bic, name, currency, 0m, DateTimeOffset.UtcNow);
        _accounts[bic] = account;
        return account;
    }

    public SettlementAccount TopUp(string bic, decimal amount)
    {
        var account = Get(bic);
        if (amount <= 0) throw new ArgumentException("充值金额必须大于 0。");
        return Update(account with { Balance = account.Balance + amount, UpdatedAt = DateTimeOffset.UtcNow });
    }

    public SettlementAccount Withdraw(string bic, decimal amount)
    {
        var account = Get(bic);
        if (amount <= 0) throw new ArgumentException("扣款金额必须大于 0。");
        if (account.Balance < amount) throw new InvalidOperationException("余额不足。");
        return Update(account with { Balance = account.Balance - amount, UpdatedAt = DateTimeOffset.UtcNow });
    }

    public SettlementAccount Get(string bic) => _accounts.TryGetValue(bic, out var a) ? a : throw new KeyNotFoundException("结算账户不存在。");
    private SettlementAccount Update(SettlementAccount a) { _accounts[a.Bic] = a; return a; }
}

// ===== 资金划拨指令 =====

public enum TransferStatus { Initiated, Reserved, Settled, Failed, Reversed }
public sealed record TransferInstruction(
    Guid Id, string FromBic, string ToBic, decimal Amount, string Currency,
    string Purpose, TransferStatus Status, DateTimeOffset InitiatedAt, DateTimeOffset? SettledAt);

public sealed class SettlementService
{
    private readonly ConcurrentDictionary<Guid, TransferInstruction> _transfers = new();
    private readonly SettlementAccountService _accounts;

    public SettlementService(SettlementAccountService accounts) => _accounts = accounts;

    public TransferInstruction Initiate(string fromBic, string toBic, decimal amount, string currency, string purpose)
    {
        var from = _accounts.Get(fromBic);
        if (from.Balance < amount) throw new InvalidOperationException($"余额不足：{from.Balance} < {amount}");
        var transfer = new TransferInstruction(Guid.NewGuid(), fromBic, toBic, amount, currency, purpose, TransferStatus.Initiated, DateTimeOffset.UtcNow, null);
        _transfers[transfer.Id] = transfer;
        return transfer;
    }

    public TransferInstruction Settle(Guid transferId)
    {
        var transfer = Get(transferId);
        if (transfer.Status != TransferStatus.Initiated) throw new InvalidOperationException("划拨不可结算。");
        var from = _accounts.Get(transfer.FromBic);
        var to = _accounts.Get(transfer.ToBic);
        if (from.Balance < transfer.Amount) throw new InvalidOperationException("结算时余额不足。");
        _accounts.Withdraw(transfer.FromBic, transfer.Amount); // 扣款
        _accounts.TopUp(transfer.ToBic, transfer.Amount);      // 入账
        var settled = transfer with { Status = TransferStatus.Settled, SettledAt = DateTimeOffset.UtcNow };
        _transfers[transferId] = settled;
        return settled;
    }

    public TransferInstruction Get(Guid transferId) => _transfers.TryGetValue(transferId, out var t) ? t : throw new KeyNotFoundException("划拨指令不存在。");
}

// ===== 轧差头寸计算 =====

public sealed record SettlementPosition(string Bic, decimal TotalInflow, decimal TotalOutflow, decimal NetPosition)
{
    public decimal NetPosition2 => TotalInflow - TotalOutflow;
}

public sealed class SettlementPositionCalculator
{
    public SettlementPosition Calculate(string bic, IEnumerable<(decimal Amount, bool IsInflow)> flows)
    {
        var inflow = flows.Where(x => x.IsInflow).Sum(x => x.Amount);
        var outflow = flows.Where(x => !x.IsInflow).Sum(x => x.Amount);
        return new SettlementPosition(bic, decimal.Round(inflow, 2, MidpointRounding.ToEven),
            decimal.Round(outflow, 2, MidpointRounding.ToEven),
            decimal.Round(inflow - outflow, 2, MidpointRounding.ToEven));
    }
}

// ===== 结算批次状态机 =====

public enum SettlementBatchStatus { Open, Processing, Completed, Failed }
public sealed record SettlementBatch(Guid Id, string Currency, DateOnly BusinessDate, SettlementBatchStatus Status, IReadOnlyList<Guid> TransferIds, DateTimeOffset CreatedAt);

public sealed class SettlementBatchService
{
    private readonly ConcurrentDictionary<Guid, SettlementBatch> _batches = new();

    public SettlementBatch Open(string currency, DateOnly businessDate)
    {
        var batch = new SettlementBatch(Guid.NewGuid(), currency, businessDate, SettlementBatchStatus.Open, [], DateTimeOffset.UtcNow);
        _batches[batch.Id] = batch;
        return batch;
    }

    public SettlementBatch Add(Guid batchId, Guid transferId)
    {
        var batch = Get(batchId);
        if (batch.Status != SettlementBatchStatus.Open) throw new InvalidOperationException("批次已关闭。");
        return Update(batch with { TransferIds = batch.TransferIds.Append(transferId).ToArray() });
    }

    public SettlementBatch Process(Guid batchId)
    {
        var batch = Get(batchId);
        return Update(batch with { Status = SettlementBatchStatus.Processing });
    }

    public SettlementBatch Complete(Guid batchId)
    {
        var batch = Get(batchId);
        return Update(batch with { Status = SettlementBatchStatus.Completed });
    }

    public SettlementBatch Get(Guid batchId) => _batches.TryGetValue(batchId, out var b) ? b : throw new KeyNotFoundException("结算批次不存在。");
    private SettlementBatch Update(SettlementBatch b) { _batches[b.Id] = b; return b; }
}
