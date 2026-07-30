using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Finance.BuildingBlocks;

public abstract class AggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class IdempotencyKey
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Operation { get; set; }
    public required string Key { get; set; }
    public required string RequestHash { get; set; }
    public int ResponseCode { get; set; }
    public required string ResponseBody { get; set; }
    public Guid? ResourceId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class OutboxMessage
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public required string AggregateType { get; set; }
    public Guid AggregateId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public required string Headers { get; set; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Action { get; set; }
    public string? BeforeState { get; set; }
    public required string AfterState { get; set; }
    public string? Operator { get; set; }
    public string? ReasonCode { get; set; }
    public Guid CorrelationId { get; set; }
    public string? RequestId { get; set; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public abstract class FinanceDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected void ConfigureFinancialControls(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyKey>(b =>
        {
            b.ToTable("idempotency_keys"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.Operation, x.Key }).IsUnique();
            b.Property(x => x.Key).HasMaxLength(128); b.Property(x => x.RequestHash).HasMaxLength(64);
        });
        modelBuilder.Entity<OutboxMessage>(b => { b.ToTable("outbox_messages"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.ProcessedAt, x.OccurredAt }); });
        modelBuilder.Entity<AuditLog>(b => { b.ToTable("audit_log"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt }); });
    }

    public void AddAudit(string entityType, Guid entityId, string action, object? before, object after, RequestContext context, string? reasonCode = null) =>
        AuditLogs.Add(new AuditLog { TenantId = context.TenantId, EntityType = entityType, EntityId = entityId, Action = action, BeforeState = before is null ? null : JsonSerializer.Serialize(before), AfterState = JsonSerializer.Serialize(after), Operator = context.Actor, ReasonCode = reasonCode, CorrelationId = context.CorrelationId, RequestId = context.RequestId });

    public void AddOutbox(string aggregateType, Guid aggregateId, string eventType, object payload, RequestContext context) =>
        OutboxMessages.Add(new OutboxMessage { TenantId = context.TenantId, AggregateType = aggregateType, AggregateId = aggregateId, EventType = eventType, Payload = JsonSerializer.Serialize(payload), Headers = JsonSerializer.Serialize(new { correlation_id = context.CorrelationId, request_id = context.RequestId }) });
}

public sealed record RequestContext(Guid TenantId, string? Actor, Guid CorrelationId, string? RequestId)
{
    public static RequestContext FromHttp(HttpContext http)
    {
        var tenant = Guid.TryParse(http.Request.Headers["X-Tenant-Id"], out var parsed) ? parsed : Guid.Empty;
        var correlation = Guid.TryParse(http.Request.Headers["X-Correlation-Id"], out var correlationId) ? correlationId : Guid.NewGuid();
        return new RequestContext(tenant, http.User.Identity?.Name, correlation, http.TraceIdentifier);
    }
}

public static class RequestHash
{
    public static string Of<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
