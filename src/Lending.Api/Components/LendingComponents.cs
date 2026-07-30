using System.Collections.Concurrent;
using Finance.BuildingBlocks;

namespace Lending.Api.Components;

// ===== Outbox 事务消息（内存队列 + 后台 Worker 可靠投递） =====

public interface IOutbox
{
    void Enqueue(string aggregateType, Guid aggregateId, string eventType, object payload, RequestContext? context = null);
    IReadOnlyList<OutboxMessage> Pending(int limit = 100);
    void MarkProcessed(long id, string? error = null);
}

public sealed class InMemoryOutbox : IOutbox
{
    private long _seq;
    private readonly ConcurrentDictionary<long, OutboxMessage> _store = new();

    public void Enqueue(string aggregateType, Guid aggregateId, string eventType, object payload, RequestContext? context = null)
    {
        var id = Interlocked.Increment(ref _seq);
        _store[id] = new OutboxMessage
        {
            Id = id,
            TenantId = context?.TenantId ?? Guid.Empty,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
            Headers = System.Text.Json.JsonSerializer.Serialize(new { correlation_id = context?.CorrelationId ?? Guid.NewGuid(), request_id = context?.RequestId })
        };
    }

    public IReadOnlyList<OutboxMessage> Pending(int limit = 100) =>
        _store.Values.Where(x => x.ProcessedAt is null).OrderBy(x => x.OccurredAt).Take(limit).ToArray();

    public void MarkProcessed(long id, string? error = null)
    {
        if (_store.TryGetValue(id, out var msg))
        {
            msg.ProcessedAt = DateTimeOffset.UtcNow;
            msg.Attempts++;
            msg.LastError = error;
        }
    }
}

// 后台 Worker 轮询 Outbox（生产环境替换为 MQ 投递）
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IOutbox _outbox;
    public OutboxDispatcher(IOutbox outbox) => _outbox = outbox;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var msg in _outbox.Pending())
                _outbox.MarkProcessed(msg.Id); // Demo：直接标记成功；生产环境调用 MQ/HTTP 投递
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

// ===== 审计日志（内存） =====

public interface IAuditTrail
{
    void Record(string entityType, Guid entityId, string action, object? before, object after, RequestContext? context = null, string? reasonCode = null);
    IReadOnlyList<AuditLog> Query(Guid entityId);
}

public sealed class InMemoryAuditTrail : IAuditTrail
{
    private long _seq;
    private readonly ConcurrentDictionary<long, AuditLog> _store = new();

    public void Record(string entityType, Guid entityId, string action, object? before, object after, RequestContext? context = null, string? reasonCode = null)
    {
        var id = Interlocked.Increment(ref _seq);
        _store[id] = new AuditLog
        {
            Id = id,
            TenantId = context?.TenantId ?? Guid.Empty,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            BeforeState = before is null ? null : System.Text.Json.JsonSerializer.Serialize(before),
            AfterState = System.Text.Json.JsonSerializer.Serialize(after),
            Operator = context?.Actor,
            ReasonCode = reasonCode,
            CorrelationId = context?.CorrelationId ?? Guid.NewGuid(),
            RequestId = context?.RequestId
        };
    }

    public IReadOnlyList<AuditLog> Query(Guid entityId) => _store.Values.Where(x => x.EntityId == entityId).OrderBy(x => x.OccurredAt).ToArray();
}

// ===== 工作流引擎（对标 Echain：节点/会签/串签/超时升级） =====

public enum WorkflowNodeKind { Serial, Countersign, Auto }
public sealed record WorkflowNode(string Id, string Name, WorkflowNodeKind Kind, string[] Approvers, int TimeoutMinutes);
public sealed record WorkflowInstance(Guid Id, string DefinitionId, string BusinessKey, string CurrentNodeId, string Status, DateTimeOffset CreatedAt, IReadOnlyList<WorkflowApproval> Approvals);
public sealed record WorkflowApproval(string NodeId, string Approver, bool Approved, string? Comment, DateTimeOffset At);

public interface IWorkflowEngine
{
    WorkflowInstance Start(string definitionId, string businessKey, IReadOnlyList<WorkflowNode> nodes);
    WorkflowInstance Approve(Guid instanceId, string approver, bool approved, string? comment);
    WorkflowInstance? Find(string businessKey);
}

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly ConcurrentDictionary<Guid, WorkflowInstance> _instances = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<WorkflowNode>> _definitions = new();

    public WorkflowInstance Start(string definitionId, string businessKey, IReadOnlyList<WorkflowNode> nodes)
    {
        if (nodes.Count == 0) throw new ArgumentException("工作流节点不能为空。");
        var inst = new WorkflowInstance(Guid.NewGuid(), definitionId, businessKey, nodes[0].Id, "RUNNING", DateTimeOffset.UtcNow, []);
        _instances[inst.Id] = inst;
        _definitions[inst.Id] = nodes;
        return inst;
    }

    public WorkflowInstance Approve(Guid instanceId, string approver, bool approved, string? comment)
    {
        if (!_instances.TryGetValue(instanceId, out var inst) || !_definitions.TryGetValue(instanceId, out var nodes))
            throw new KeyNotFoundException("工作流实例不存在。");
        if (inst.Status != "RUNNING") throw new InvalidOperationException("工作流已结束。");
        var node = nodes.First(n => n.Id == inst.CurrentNodeId);
        var approval = new WorkflowApproval(node.Id, approver, approved, comment, DateTimeOffset.UtcNow);
        var approvals = inst.Approvals.Append(approval).ToArray();

        // 不通过 → 终止
        if (!approved)
            return Update(instanceId, inst with { Status = "REJECTED", Approvals = approvals });

        // 会签节点：需全部审批人通过
        if (node.Kind == WorkflowNodeKind.Countersign)
        {
            var nodeApprovals = approvals.Where(a => a.NodeId == node.Id && a.Approved).Select(a => a.Approver).ToHashSet();
            if (!node.Approvers.All(nodeApprovals.Contains))
                return Update(instanceId, inst with { Approvals = approvals });
        }

        // 推进到下一节点
        var idx = 0;
        for (; idx < nodes.Count; idx++) { if (nodes[idx].Id == node.Id) break; }
        if (idx + 1 >= nodes.Count)
            return Update(instanceId, inst with { Status = "APPROVED", Approvals = approvals });
        var next = nodes[idx + 1];
        return Update(instanceId, inst with { CurrentNodeId = next.Id, Approvals = approvals });
    }

    public WorkflowInstance? Find(string businessKey) => _instances.Values.FirstOrDefault(x => x.BusinessKey == businessKey);
    private WorkflowInstance Update(Guid id, WorkflowInstance inst) { _instances[id] = inst; return inst; }
}

// ===== 规则引擎（对标 Shuffle：规则与代码解耦） =====

public sealed record Rule(string Id, string Name, Func<IDictionary<string, object>, bool> Predicate, string Action, string? RejectReason);
public sealed record RuleEvaluationResult(bool Passed, IReadOnlyList<string> HitRules, IReadOnlyList<string> RejectReasons);

public interface IRuleEngine
{
    void Register(Rule rule);
    RuleEvaluationResult Evaluate(IDictionary<string, object> facts);
}

public sealed class RuleEngine : IRuleEngine
{
    private readonly ConcurrentDictionary<string, Rule> _rules = new();
    public void Register(Rule rule) => _rules[rule.Id] = rule;
    public RuleEvaluationResult Evaluate(IDictionary<string, object> facts)
    {
        var hit = new List<string>();
        var rejects = new List<string>();
        foreach (var r in _rules.Values)
        {
            if (r.Predicate(facts))
            {
                hit.Add(r.Id);
                if (r.Action == "REJECT" && r.RejectReason is not null) rejects.Add(r.RejectReason);
            }
        }
        return new RuleEvaluationResult(rejects.Count == 0, hit, rejects);
    }
}

// ===== 统一调度平台（对标 USE：任务依赖与定时） =====

public sealed record ScheduledTask(string Id, string Name, Func<CancellationToken, Task> Action, string[] DependsOn, DateTimeOffset NextRun);
public interface IScheduler
{
    void Schedule(ScheduledTask task);
    IReadOnlyList<ScheduledTask> Due(DateTimeOffset asOf);
}

public sealed class InMemoryScheduler : IScheduler
{
    private readonly ConcurrentDictionary<string, ScheduledTask> _tasks = new();
    public void Schedule(ScheduledTask task) => _tasks[task.Id] = task;
    public IReadOnlyList<ScheduledTask> Due(DateTimeOffset asOf) =>
        _tasks.Values.Where(t => t.NextRun <= asOf).ToArray();
}

// ===== 影像平台（电子影像采集/传输/生命周期） =====

public sealed record ImageObject(string ObjectKey, string MediaType, long SizeBytes, string Status, DateTimeOffset UploadedAt, DateTimeOffset? PurgedAt);
public interface IImagePlatform
{
    ImageObject Upload(string objectKey, string mediaType, long sizeBytes);
    ImageObject? Get(string objectKey);
    ImageObject Purge(string objectKey);
}

public sealed class InMemoryImagePlatform : IImagePlatform
{
    private readonly ConcurrentDictionary<string, ImageObject> _store = new();
    public ImageObject Upload(string objectKey, string mediaType, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) throw new ArgumentException("对象键不能为空。");
        var img = new ImageObject(objectKey, mediaType, sizeBytes, "ACTIVE", DateTimeOffset.UtcNow, null);
        _store[objectKey] = img;
        return img;
    }
    public ImageObject? Get(string objectKey) => _store.TryGetValue(objectKey, out var img) ? img : null;
    public ImageObject Purge(string objectKey)
    {
        if (!_store.TryGetValue(objectKey, out var img)) throw new KeyNotFoundException("影像不存在。");
        var purged = img with { Status = "PURGED", PurgedAt = DateTimeOffset.UtcNow };
        _store[objectKey] = purged;
        return purged;
    }
}
