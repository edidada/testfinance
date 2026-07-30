using Lending.Api;
using Xunit;

namespace Lending.Tests;

public sealed class LendingServiceTests
{
    [Fact]
    public void Strong_affordable_application_is_approved_and_disbursed()
    {
        var service = new LendingService();
        var loan = service.Submit(new LoanApplication("customer", 20000m, 1000m, 12000m, 12, 850));
        Assert.Equal(LoanStatus.Approved, service.Decide(loan.Id).Status);
        service.CreateContract(loan.Id, "contract-1");
        service.SignContract(loan.Id);
        Assert.Equal(12, service.Disburse(loan.Id).Schedule.Count);
    }

    [Fact]
    public void Cannot_disburse_before_approval()
    {
        var service = new LendingService();
        var loan = service.Submit(new LoanApplication("customer", 10000m, 0m, 1000m, 12, 800));
        Assert.Throws<InvalidOperationException>(() => service.Disburse(loan.Id));
    }
}
