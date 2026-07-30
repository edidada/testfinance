using Payments.Api;
using Xunit;

namespace Payments.Tests;

public sealed class PaymentServiceTests
{
    [Fact]
    public void Same_idempotency_key_returns_original_payment()
    {
        var service = new PaymentService();
        var request = new CreatePayment("merchant", 100m, "CNY", "order-1");
        var first = service.Create(request, "key-1");
        var second = service.Create(request, "key-1");
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Refund_cannot_exceed_captured_amount()
    {
        var service = new PaymentService();
        var payment = service.Capture(service.Create(new CreatePayment("merchant", 100m, "CNY", "order-2"), "key-2").Id);
        Assert.Throws<InvalidOperationException>(() => service.Refund(payment.Id, 101m));
        Assert.Equal(PaymentStatus.Captured, service.Get(payment.Id).Status);
    }
}
