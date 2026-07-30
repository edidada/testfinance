using System.Collections.Concurrent;

namespace Payments.Api.Transport;

// ===== 金融报文队列抽象（对标东方通 TongLINK/Q） =====

public interface IFinancialMessageQueue
{
    void Publish(string topic, string messageId, string payload, Dictionary<string, string>? headers = null);
    string? Consume(string topic);
    IReadOnlyList<string> Pending(string topic);
    void Ack(string topic, string messageId);
}

public sealed class InMemoryMessageQueue : IFinancialMessageQueue
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(string Id, string Payload, Dictionary<string, string> Headers)>> _queues = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _acknowledged = new();

    public void Publish(string topic, string messageId, string payload, Dictionary<string, string>? headers = null)
    {
        var queue = _queues.GetOrAdd(topic, _ => new ConcurrentQueue<(string, string, Dictionary<string, string>)>());
        queue.Enqueue((messageId, payload, headers ?? new()));
    }

    public string? Consume(string topic)
    {
        if (!_queues.TryGetValue(topic, out var queue)) return null;
        while (queue.TryDequeue(out var msg))
        {
            var ackSet = _acknowledged.GetOrAdd(topic, _ => new HashSet<string>());
            lock (ackSet) { if (ackSet.Contains(msg.Id)) continue; }
            return msg.Payload;
        }
        return null;
    }

    public IReadOnlyList<string> Pending(string topic) =>
        _queues.TryGetValue(topic, out var q) ? q.Select(x => x.Id).ToArray() : Array.Empty<string>();

    public void Ack(string topic, string messageId)
    {
        var ackSet = _acknowledged.GetOrAdd(topic, _ => new HashSet<string>());
        lock (ackSet) { ackSet.Add(messageId); }
    }
}

// ===== TongLINK/Q 网关适配（接口骨架） =====

public interface ITongLinkGateway
{
    Task<bool> SendAsync(string destination, string message, CancellationToken ct = default);
    Task<string?> ReceiveAsync(string source, CancellationToken ct = default);
}

public sealed class FakeTongLinkGateway : ITongLinkGateway
{
    public Task<bool> SendAsync(string destination, string message, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<string?> ReceiveAsync(string source, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

// ===== 死信队列（Dead Letter Queue） =====

public sealed record DeadLetter(string Topic, string MessageId, string Payload, string Reason, int Attempts, DateTimeOffset MovedAt);

public sealed class DeadLetterQueue
{
    private readonly ConcurrentQueue<DeadLetter> _dlq = new();

    public void Move(string topic, string messageId, string payload, string reason, int attempts)
    {
        _dlq.Enqueue(new DeadLetter(topic, messageId, payload, reason, attempts, DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<DeadLetter> All() => _dlq.ToArray();
    public int Count => _dlq.Count;
}

// ===== 消息幂等重试 =====

public sealed class MessageRetryPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan[] _backoff;

    public MessageRetryPolicy(int maxAttempts = 3)
    {
        _maxAttempts = maxAttempts;
        _backoff = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30) };
    }

    public bool ShouldRetry(int attempt) => attempt < _maxAttempts;
    public TimeSpan GetDelay(int attempt) => _backoff[Math.Min(attempt, _backoff.Length - 1)];
}

// ===== CNAPS 报文传输适配 =====

public sealed class CnapsMessageBus
{
    private readonly IFinancialMessageQueue _mq;
    private readonly DeadLetterQueue _dlq;
    private readonly MessageRetryPolicy _retry;

    public CnapsMessageBus(IFinancialMessageQueue mq, DeadLetterQueue dlq, MessageRetryPolicy retry)
    {
        _mq = mq; _dlq = dlq; _retry = retry;
    }

    public void SendCnapsMessage(string msgType, string messageId, string payload)
    {
        var topic = $"CNAPS.{msgType}";
        _mq.Publish(topic, messageId, payload);
    }

    public string? ReceiveCnapsMessage(string msgType)
    {
        return _mq.Consume($"CNAPS.{msgType}");
    }
}
