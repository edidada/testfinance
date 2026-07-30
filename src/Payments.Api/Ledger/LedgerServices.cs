using System.Collections.Concurrent;

namespace Payments.Api.Ledger;

// ===== 会计科目表（Chart of Accounts） =====

public enum AccountType { Asset, Liability, Equity, Revenue, Expense }
public sealed record LedgerAccount(string Code, string Name, AccountType Type, string Currency, bool IsContra = false)
{
    // 借方科目（资产/费用）增加记借方；贷方科目（负债/权益/收入）增加记贷方
    public bool NormalDebit => Type is AccountType.Asset or AccountType.Expense;
}

public static class ChartOfAccounts
{
    // 标准银行会计科目（Demo 简化）
    private static readonly Dictionary<string, LedgerAccount> _chart = new()
    {
        ["1001"] = new("1001", "现金", AccountType.Asset, "CNY"),
        ["1002"] = new("1002", "银行存款", AccountType.Asset, "CNY"),
        ["1122"] = new("1122", "应收账款", AccountType.Asset, "CNY"),
        ["2202"] = new("2202", "应付账款", AccountType.Liability, "CNY"),
        ["2203"] = new("2203", "预收账款", AccountType.Liability, "CNY"),
        ["4001"] = new("4001", "实收资本", AccountType.Equity, "CNY"),
        ["6001"] = new("6001", "主营业务收入", AccountType.Revenue, "CNY"),
        ["6601"] = new("6601", "销售费用", AccountType.Expense, "CNY"),
        ["6603"] = new("6603", "财务费用", AccountType.Expense, "CNY")
    };

    public static LedgerAccount Get(string code) =>
        _chart.TryGetValue(code, out var a) ? a : throw new KeyNotFoundException($"科目 {code} 不存在。");
    public static IReadOnlyCollection<LedgerAccount> All() => _chart.Values;
}

// ===== 日记账分录（Journal Entry） =====

public sealed record JournalLine(string AccountCode, decimal Debit, decimal Credit, string Currency, string Memo);
public sealed record JournalEntry(Guid Id, string BatchNo, DateTimeOffset PostedAt, IReadOnlyList<JournalLine> Lines)
{
    public decimal TotalDebit => Lines.Sum(x => x.Debit);
    public decimal TotalCredit => Lines.Sum(x => x.Credit);
    public bool IsBalanced => TotalDebit == TotalCredit;
};

// ===== 借贷平衡校验器 =====

public static class DoubleEntryValidator
{
    public static void Validate(JournalEntry entry)
    {
        if (!entry.IsBalanced)
            throw new InvalidOperationException($"借贷不平衡：借方 {entry.TotalDebit} ≠ 贷方 {entry.TotalCredit}");
        foreach (var line in entry.Lines)
        {
            if (line.Debit < 0 || line.Credit < 0)
                throw new ArgumentException("借贷金额不能为负。");
            if (line.Debit > 0 && line.Credit > 0)
                throw new ArgumentException("同一行不能同时有借方和贷方金额。");
        }
    }
}

// ===== 日终记账（EOD Posting） =====

public sealed class JournalService
{
    private readonly ConcurrentDictionary<Guid, JournalEntry> _entries = new();
    private readonly ConcurrentDictionary<string, decimal> _balances = new(); // accountCode -> balance

    public JournalEntry Post(string batchNo, IReadOnlyList<JournalLine> lines)
    {
        var entry = new JournalEntry(Guid.NewGuid(), batchNo, DateTimeOffset.UtcNow, lines);
        DoubleEntryValidator.Validate(entry);
        _entries[entry.Id] = entry;
        // 更新科目余额
        foreach (var line in lines)
        {
            var account = ChartOfAccounts.Get(line.AccountCode);
            var delta = account.NormalDebit ? line.Debit - line.Credit : line.Credit - line.Debit;
            _balances.AddOrUpdate(line.AccountCode, delta, (_, v) => v + delta);
        }
        return entry;
    }

    public decimal Balance(string accountCode) => _balances.TryGetValue(accountCode, out var b) ? b : 0m;
    public IReadOnlyList<JournalEntry> Batch(string batchNo) => _entries.Values.Where(x => x.BatchNo == batchNo).OrderBy(x => x.PostedAt).ToArray();
}

// ===== 多币种折算 =====

public sealed record FxRate(string FromCurrency, string ToCurrency, decimal Rate, DateOnly AsOf);
public sealed record FxConversion(string FromCurrency, string ToCurrency, decimal Amount, decimal Rate, decimal Converted, DateOnly AsOf);

public static class FxConverter
{
    // 多币种折算：通过 USD 中转交叉汇率（Demo 简化）
    private static readonly Dictionary<string, decimal> _toUsd = new()
    {
        ["CNY"] = 0.14m, ["USD"] = 1m, ["EUR"] = 1.08m, ["HKD"] = 0.13m, ["JPY"] = 0.0067m
    };

    public static FxConversion Convert(string from, string to, decimal amount, DateOnly asOf)
    {
        if (amount < 0) throw new ArgumentException("折算金额不能为负。");
        if (!_toUsd.TryGetValue(from, out var fromRate) || !_toUsd.TryGetValue(to, out var toRate))
            throw new ArgumentException($"不支持的币种：{from} -> {to}");
        var rate = decimal.Round(fromRate / toRate, 6, MidpointRounding.ToEven);
        var converted = decimal.Round(amount * rate, 2, MidpointRounding.ToEven);
        return new FxConversion(from, to, amount, rate, converted, asOf);
    }

    public static IReadOnlyDictionary<string, decimal> Rates => _toUsd;
}
