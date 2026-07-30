using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Payments.Api;

public enum PaymentStatus { Authorized, Captured, Refunded, Failed }
public sealed record CreatePayment(string MerchantId, decimal Amount, string Currency, string Reference);
public sealed record Payment(Guid Id, string MerchantId, decimal Amount, string Currency, string Reference, PaymentStatus Status, decimal RefundedAmount, DateTimeOffset CreatedAt);
public sealed record LedgerEntry(Guid PaymentId, string Type, decimal Amount, DateTimeOffset OccurredAt);

public sealed class PaymentService
{
    private readonly ConcurrentDictionary<Guid, Payment> _payments = new();
    private readonly ConcurrentDictionary<string, (string Hash, Guid PaymentId)> _idempotency = new();
    private readonly ConcurrentQueue<LedgerEntry> _ledger = new();
    private readonly object _gate = new();

    public Payment Create(CreatePayment request, string idempotencyKey)
    {
        Validate(request, idempotencyKey);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.MerchantId}|{request.Amount}|{request.Currency}|{request.Reference}")));
        lock (_gate)
        {
            if (_idempotency.TryGetValue(idempotencyKey, out var existing))
            {
                if (existing.Hash != hash) throw new InvalidOperationException("Idempotency-Key 已用于不同请求。");
                return _payments[existing.PaymentId];
            }
            var payment = new Payment(Guid.NewGuid(), request.MerchantId, request.Amount, request.Currency.ToUpperInvariant(), request.Reference, PaymentStatus.Authorized, 0m, DateTimeOffset.UtcNow);
            _payments[payment.Id] = payment;
            _idempotency[idempotencyKey] = (hash, payment.Id);
            _ledger.Enqueue(new LedgerEntry(payment.Id, "authorization", payment.Amount, DateTimeOffset.UtcNow));
            return payment;
        }
    }

    public Payment Capture(Guid id) => Transition(id, p => p.Status == PaymentStatus.Authorized,
        p => p with { Status = PaymentStatus.Captured }, "capture", p => p.Amount);

    public Payment Refund(Guid id, decimal amount) => Transition(id,
        p => p.Status == PaymentStatus.Captured && amount > 0 && p.RefundedAmount + amount <= p.Amount,
        p => p with { RefundedAmount = p.RefundedAmount + amount, Status = p.RefundedAmount + amount == p.Amount ? PaymentStatus.Refunded : PaymentStatus.Captured },
        "refund", _ => amount);

    public Payment Get(Guid id) => _payments.TryGetValue(id, out var payment) ? payment : throw new KeyNotFoundException("支付单不存在。");
    public IReadOnlyCollection<LedgerEntry> Ledger(Guid id) => _ledger.Where(x => x.PaymentId == id).ToArray();

    private Payment Transition(Guid id, Func<Payment, bool> allowed, Func<Payment, Payment> update, string entryType, Func<Payment, decimal> amount)
    {
        lock (_gate)
        {
            var old = Get(id);
            if (!allowed(old)) throw new InvalidOperationException("当前支付状态不允许此操作。");
            var changed = update(old);
            _payments[id] = changed;
            _ledger.Enqueue(new LedgerEntry(id, entryType, amount(old), DateTimeOffset.UtcNow));
            return changed;
        }
    }

    private static void Validate(CreatePayment request, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new ArgumentException("缺少或无效的 Idempotency-Key。");
        if (string.IsNullOrWhiteSpace(request.MerchantId) || request.Amount <= 0 || request.Currency.Length != 3) throw new ArgumentException("支付参数无效。");
    }
}
