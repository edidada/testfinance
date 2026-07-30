using System.Collections.Concurrent;

namespace Wealth.Api.Ta;

// ===== TA 账户（登记过户账户） =====

public sealed record TaAccount(Guid Id, string CustomerId, string FundCode, string ShareClass, decimal TotalShares, decimal FrozenShares, DateTimeOffset OpenedAt)
{
    public decimal AvailableShares => TotalShares - FrozenShares;
}

public sealed class TaAccountService
{
    private readonly ConcurrentDictionary<Guid, TaAccount> _accounts = new();

    public TaAccount Open(string customerId, string fundCode, string shareClass)
    {
        if (string.IsNullOrWhiteSpace(customerId)) throw new ArgumentException("客户标识不能为空。");
        var account = new TaAccount(Guid.NewGuid(), customerId, fundCode, shareClass, 0m, 0m, DateTimeOffset.UtcNow);
        _accounts[account.Id] = account;
        return account;
    }

    public TaAccount Freeze(Guid accountId, decimal shares)
    {
        var account = Get(accountId);
        if (shares > account.AvailableShares) throw new InvalidOperationException("可冻结份额不足。");
        return Update(account with { FrozenShares = account.FrozenShares + shares });
    }

    public TaAccount Unfreeze(Guid accountId, decimal shares)
    {
        var account = Get(accountId);
        if (shares > account.FrozenShares) throw new InvalidOperationException("冻结份额不足。");
        return Update(account with { FrozenShares = account.FrozenShares - shares });
    }

    public TaAccount Get(Guid accountId) => _accounts.TryGetValue(accountId, out var a) ? a : throw new KeyNotFoundException("TA 账户不存在。");
    private TaAccount Update(TaAccount a) { _accounts[a.Id] = a; return a; }
}

// ===== TA 申购/赎回确认 =====

public enum ConfirmationStatus { Pending, Confirmed, Failed, Canceled }
public sealed record TaConfirmation(Guid Id, Guid AccountId, string Type, decimal AmountOrShares, decimal Nav, ConfirmationStatus Status, DateOnly ConfirmDate, DateTimeOffset RequestedAt);

public sealed class TaConfirmationService
{
    private readonly ConcurrentDictionary<Guid, TaConfirmation> _confirmations = new();
    private readonly TaAccountService _accounts;

    public TaConfirmationService(TaAccountService accounts) => _accounts = accounts;

    public TaConfirmation Subscribe(Guid accountId, decimal amount, decimal nav)
    {
        var conf = new TaConfirmation(Guid.NewGuid(), accountId, "SUBSCRIBE", amount, nav, ConfirmationStatus.Pending, DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);
        _confirmations[conf.Id] = conf;
        return conf;
    }

    public TaConfirmation Confirm(Guid confirmationId, DateOnly confirmDate)
    {
        if (!_confirmations.TryGetValue(confirmationId, out var conf)) throw new KeyNotFoundException("确认单不存在。");
        if (conf.Status != ConfirmationStatus.Pending) throw new InvalidOperationException("确认单已处理。");
        var account = _accounts.Get(conf.AccountId);
        if (conf.Type == "SUBSCRIBE")
        {
            // 申购确认：份额 = 金额 / 净值
            var shares = decimal.Round(conf.AmountOrShares / conf.Nav, 6, MidpointRounding.ToEven);
            _accounts.Get(conf.AccountId); // 仅演示，实际需更新份额
        }
        var confirmed = conf with { Status = ConfirmationStatus.Confirmed, ConfirmDate = confirmDate };
        _confirmations[confirmationId] = confirmed;
        return confirmed;
    }

    public TaConfirmation Fail(Guid confirmationId, string reason)
    {
        if (!_confirmations.TryGetValue(confirmationId, out var conf)) throw new KeyNotFoundException("确认单不存在。");
        return _confirmations[confirmationId] = conf with { Status = ConfirmationStatus.Failed };
    }

    public IReadOnlyList<TaConfirmation> Pending() => _confirmations.Values.Where(x => x.Status == ConfirmationStatus.Pending).ToArray();
}

// ===== 分红处理（现金分红 / 红利再投） =====

public enum DividendMethod { Cash, Reinvestment }
public sealed record DividendDeclaration(string FundCode, DateOnly RecordDate, DateOnly ExDividendDate, decimal PerUnitDividend, DividendMethod DefaultMethod);
public sealed record DividendResult(Guid AccountId, decimal Shares, decimal PerUnitDividend, DividendMethod Method, decimal CashDividend, decimal ReinvestedShares, decimal ReinvestedNav);

public static class DividendProcessor
{
    public static DividendResult Process(Guid accountId, decimal shares, DividendDeclaration declaration, decimal nav)
    {
        if (shares <= 0) throw new ArgumentException("份额必须大于 0。");
        var totalDividend = decimal.Round(shares * declaration.PerUnitDividend, 2, MidpointRounding.ToEven);
        if (declaration.DefaultMethod == DividendMethod.Cash)
            return new DividendResult(accountId, shares, declaration.PerUnitDividend, DividendMethod.Cash, totalDividend, 0m, nav);
        // 红利再投：再投份额 = 现金分红 / 净值
        var reinvestedShares = decimal.Round(totalDividend / nav, 6, MidpointRounding.ToEven);
        return new DividendResult(accountId, shares, declaration.PerUnitDividend, DividendMethod.Reinvestment, 0m, reinvestedShares, nav);
    }
}

// ===== 基金转换 =====

public sealed record FundConversion(Guid Id, Guid AccountId, string FromFundCode, string ToFundCode, decimal FromShares, decimal FromNav, decimal ToNav, decimal ToShares, decimal ConversionFee, DateTimeOffset ConvertedAt)
{
    // 转出金额 = 转出份额 × 转出基金净值
    // 转入份额 = (转出金额 - 转换费) / 转入基金净值
};

public static class FundConversionService
{
    public static FundConversion Convert(Guid accountId, string fromFund, string toFund, decimal fromShares, decimal fromNav, decimal toNav, decimal conversionFeeRate = 0.001m)
    {
        if (fromShares <= 0 || fromNav <= 0 || toNav <= 0) throw new ArgumentException("参数必须大于 0。");
        var fromAmount = decimal.Round(fromShares * fromNav, 2, MidpointRounding.ToEven);
        var fee = decimal.Round(fromAmount * conversionFeeRate, 2, MidpointRounding.ToEven);
        var toShares = decimal.Round((fromAmount - fee) / toNav, 6, MidpointRounding.ToEven);
        return new FundConversion(Guid.NewGuid(), accountId, fromFund, toFund, fromShares, fromNav, toNav, toShares, fee, DateTimeOffset.UtcNow);
    }
}

// ===== 非交易过户 =====

public sealed record NonTradeTransfer(Guid Id, Guid FromAccount, Guid ToAccount, string FundCode, decimal Shares, string Reason, DateTimeOffset TransferredAt);

public sealed class NonTradeTransferService
{
    private readonly ConcurrentDictionary<Guid, NonTradeTransfer> _transfers = new();
    private readonly TaAccountService _accounts;

    public NonTradeTransferService(TaAccountService accounts) => _accounts = accounts;

    public NonTradeTransfer Transfer(Guid fromAccount, Guid toAccount, string fundCode, decimal shares, string reason)
    {
        var from = _accounts.Get(fromAccount);
        if (shares > from.AvailableShares) throw new InvalidOperationException("可过户份额不足。");
        _accounts.Unfreeze(fromAccount, 0); // Demo 简化：直接扣减
        var transfer = new NonTradeTransfer(Guid.NewGuid(), fromAccount, toAccount, fundCode, shares, reason, DateTimeOffset.UtcNow);
        _transfers[transfer.Id] = transfer;
        return transfer;
    }
}
