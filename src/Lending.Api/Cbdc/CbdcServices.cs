using System.Collections.Concurrent;

namespace Lending.Api.Cbdc;

// ===== 数字人民币钱包（对公/个人） =====

public enum WalletType { Personal, Corporate }
public enum WalletLevel { Level1, Level2, Level3, Level4 }
public sealed record CBDCWallet(Guid Id, string OwnerId, WalletType Type, WalletLevel Level, decimal Balance, DateTimeOffset OpenedAt)
{
    public decimal PaymentLimit => Level switch { WalletLevel.Level1 => 1000m, WalletLevel.Level2 => 50000m, WalletLevel.Level3 => 200000m, _ => decimal.MaxValue };
}

public sealed class CBDCWalletService
{
    private readonly ConcurrentDictionary<Guid, CBDCWallet> _wallets = new();

    public CBDCWallet Open(string ownerId, WalletType type, WalletLevel level)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("钱包归属人不能为空。");
        var wallet = new CBDCWallet(Guid.NewGuid(), ownerId, type, level, 0m, DateTimeOffset.UtcNow);
        _wallets[wallet.Id] = wallet;
        return wallet;
    }

    public CBDCWallet TopUp(Guid walletId, decimal amount)
    {
        var wallet = Get(walletId);
        if (amount <= 0) throw new ArgumentException("充值为金额必须大于 0。");
        return Update(wallet with { Balance = wallet.Balance + amount });
    }

    public CBDCWallet Transfer(Guid fromWalletId, Guid toWalletId, decimal amount)
    {
        var source = Get(fromWalletId);
        var to = Get(toWalletId);
        if (amount <= 0) throw new ArgumentException("转账金额必须大于 0。");
        if (amount > source.PaymentLimit) throw new InvalidOperationException($"超出钱包单笔支付限额 {source.PaymentLimit}。");
        if (source.Balance < amount) throw new InvalidOperationException("钱包余额不足。");
        Update(source with { Balance = source.Balance - amount });
        Update(to with { Balance = to.Balance + amount });
        return Get(fromWalletId);
    }

    public CBDCWallet Get(Guid walletId) => _wallets.TryGetValue(walletId, out var w) ? w : throw new KeyNotFoundException("钱包不存在。");
    private CBDCWallet Update(CBDCWallet w) { _wallets[w.Id] = w; return w; }
}

// ===== 智能合约（定向支付、条件支付等可编程资金管控） =====

public enum ContractStatus { Pending, ConditionMet, Executed, Expired, Canceled }
public sealed record SmartContract(
    Guid Id, Guid FromWalletId, Guid ToWalletId, decimal Amount, string ConditionExpression,
    ContractStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ExecutedAt)
{
    // 条件表达式示例："goods_delivered==true" / "date>=2026-12-31"
};

public sealed class SmartContractEngine
{
    private readonly ConcurrentDictionary<Guid, SmartContract> _contracts = new();
    private readonly CBDCWalletService _wallets;

    public SmartContractEngine(CBDCWalletService wallets) => _wallets = wallets;

    public SmartContract Deploy(Guid fromWalletId, Guid toWalletId, decimal amount, string conditionExpression, TimeSpan ttl)
    {
        if (amount <= 0) throw new ArgumentException("合约金额必须大于 0。");
        var contract = new SmartContract(Guid.NewGuid(), fromWalletId, toWalletId, amount, conditionExpression, ContractStatus.Pending, DateTimeOffset.UtcNow, null);
        _contracts[contract.Id] = contract;
        // 演示 TTL 到期自动失效（生产环境由调度器驱动）
        _ = Task.Delay(ttl).ContinueWith(_ => Expire(contract.Id));
        return contract;
    }

    public SmartContract Fulfill(Guid contractId, IDictionary<string, string> facts)
    {
        var contract = Get(contractId);
        if (contract.Status != ContractStatus.Pending) throw new InvalidOperationException("合约不可执行。");
        if (!EvaluateCondition(contract.ConditionExpression, facts))
            throw new InvalidOperationException("条件未满足，合约不可执行。");
        Update(contract with { Status = ContractStatus.ConditionMet });
        // 触发资金划转
        _wallets.Transfer(contract.FromWalletId, contract.ToWalletId, contract.Amount);
        return Update(contract with { Status = ContractStatus.Executed, ExecutedAt = DateTimeOffset.UtcNow });
    }

    public SmartContract Cancel(Guid contractId)
    {
        var contract = Get(contractId);
        if (contract.Status != ContractStatus.Pending) throw new InvalidOperationException("仅待执行合约可撤销。");
        return Update(contract with { Status = ContractStatus.Canceled });
    }

    public SmartContract Get(Guid contractId) => _contracts.TryGetValue(contractId, out var c) ? c : throw new KeyNotFoundException("智能合约不存在。");
    private SmartContract Update(SmartContract c) { _contracts[c.Id] = c; return c; }
    private void Expire(Guid id) { if (_contracts.TryGetValue(id, out var c) && c.Status == ContractStatus.Pending) Update(c with { Status = ContractStatus.Expired }); }

    // 简易条件求值：支持 "key==value" 与 "date>=YYYY-MM-DD"
    private static bool EvaluateCondition(string expr, IDictionary<string, string> facts)
    {
        if (string.IsNullOrWhiteSpace(expr)) return true;
        if (expr.Contains("=="))
        {
            var parts = expr.Split("==");
            return facts.TryGetValue(parts[0].Trim(), out var v) && v == parts[1].Trim();
        }
        if (expr.Contains(">="))
        {
            var parts = expr.Split(">=");
            if (!facts.TryGetValue(parts[0].Trim(), out var v)) return false;
            return string.Compare(v, parts[1].Trim(), StringComparison.Ordinal) >= 0;
        }
        return true;
    }
}
