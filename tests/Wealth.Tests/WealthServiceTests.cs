using Wealth.Api;
using Xunit;

namespace Wealth.Tests;

public sealed class WealthServiceTests
{
    [Fact]
    public void Kyc_is_required_before_subscription()
    {
        var service = new WealthService();
        var account = service.OpenAccount("customer");
        Assert.Throws<InvalidOperationException>(() => service.Subscribe(account.Id, "CASH-CNY", 10m));
        service.VerifyKyc(account.Id, true);
        var order = service.Subscribe(account.Id, "CASH-CNY", 10m);
        Assert.Equal("subscribe", order.Type);
        Assert.Equal(10m, service.Valuation(account.Id));
    }

    [Fact]
    public void Redemption_cannot_exceed_held_units()
    {
        var service = new WealthService();
        var account = service.OpenAccount("customer");
        service.VerifyKyc(account.Id, true);
        Assert.Throws<InvalidOperationException>(() => service.Redeem(account.Id, "BOND-001", 1m));
    }
}
