using System.Collections.Concurrent;

namespace Wealth.Api;

public enum KycStatus { Pending, Verified, Rejected }
public sealed record Product(string Code, string Name, string Currency, decimal Nav, decimal MinimumSubscription, bool IsOpen);
public sealed record InvestmentAccount(Guid Id, string CustomerId, KycStatus Kyc, IReadOnlyDictionary<string, decimal> Units);
public sealed record Order(Guid Id, Guid AccountId, string ProductCode, string Type, decimal Units, decimal Nav, DateTimeOffset CreatedAt);

public sealed class WealthService
{
    private readonly ConcurrentDictionary<string, Product> _products = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, InvestmentAccount> _accounts = new();
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();
    private readonly object _gate = new();

    public WealthService()
    {
        _products["CASH-CNY"] = new Product("CASH-CNY", "现金管理", "CNY", 1m, 1m, true);
        _products["BOND-001"] = new Product("BOND-001", "稳健债券", "CNY", 1.0432m, 100m, true);
    }

    public InvestmentAccount OpenAccount(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId)) throw new ArgumentException("客户标识不能为空。");
        var account = new InvestmentAccount(Guid.NewGuid(), customerId, KycStatus.Pending, new Dictionary<string, decimal>());
        _accounts[account.Id] = account;
        return account;
    }

    public InvestmentAccount VerifyKyc(Guid id, bool accepted)
    {
        lock (_gate)
        {
            var account = GetAccount(id);
            if (account.Kyc != KycStatus.Pending) throw new InvalidOperationException("KYC 状态不可重复变更。");
            var changed = account with { Kyc = accepted ? KycStatus.Verified : KycStatus.Rejected };
            _accounts[id] = changed;
            return changed;
        }
    }

    public Order Subscribe(Guid accountId, string productCode, decimal amount)
    {
        lock (_gate)
        {
            var account = RequireVerified(accountId);
            var product = GetProduct(productCode);
            if (!product.IsOpen || amount < product.MinimumSubscription) throw new InvalidOperationException("产品未开放或金额低于起投额。");
            var units = decimal.Round(amount / product.Nav, 6, MidpointRounding.ToEven);
            return ApplyOrder(account, product, "subscribe", units);
        }
    }

    public Order Redeem(Guid accountId, string productCode, decimal units)
    {
        lock (_gate)
        {
            var account = RequireVerified(accountId);
            var product = GetProduct(productCode);
            if (!product.IsOpen || units <= 0 || !account.Units.TryGetValue(product.Code, out var held) || held < units) throw new InvalidOperationException("赎回份额不足或产品未开放。");
            return ApplyOrder(account, product, "redeem", units);
        }
    }

    public InvestmentAccount GetAccount(Guid id) => _accounts.TryGetValue(id, out var account) ? account : throw new KeyNotFoundException("投资账户不存在。");
    public IReadOnlyCollection<Product> Products() => _products.Values.OrderBy(x => x.Code).ToArray();
    public decimal Valuation(Guid accountId) { var account = GetAccount(accountId); return account.Units.Sum(x => x.Value * GetProduct(x.Key).Nav); }
    private InvestmentAccount RequireVerified(Guid id) { var account = GetAccount(id); if (account.Kyc != KycStatus.Verified) throw new InvalidOperationException("账户尚未完成 KYC。"); return account; }
    private Product GetProduct(string code) => _products.TryGetValue(code, out var product) ? product : throw new KeyNotFoundException("产品不存在。");
    private Order ApplyOrder(InvestmentAccount account, Product product, string type, decimal units)
    {
        var positions = account.Units.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        positions.TryGetValue(product.Code, out var current);
        positions[product.Code] = type == "subscribe" ? current + units : current - units;
        _accounts[account.Id] = account with { Units = positions };
        var order = new Order(Guid.NewGuid(), account.Id, product.Code, type, units, product.Nav, DateTimeOffset.UtcNow);
        _orders[order.Id] = order;
        return order;
    }
}
