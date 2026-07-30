# 面向金融科技开发者的 C# 与 .NET 8 实战教程

本教程以本仓库 `TestFinance` 代码为蓝本，帮助新手快速掌握 C# 语言特性、.NET Core 框架核心概念以及主流三方库的使用。

---

## 1. C# 语言核心特性

### 1.1 类型系统：值类型 vs 引用类型

C# 中数据分为两类：**值类型 (Value Type)** 和 **引用类型 (Reference Type)**。

-   **值类型**：存储在栈 (Stack) 上，赋值时拷贝。基本类型如 `int`, `decimal`, `bool`, `struct`。
-   **引用类型**：存储在堆 (Heap) 上，变量仅存储引用地址。如 `class`, `string`, `array`。

> **金融场景应用**：金额计算必须使用 `decimal` (值类型) 而非 `float`/`double`，因为前者能精确表示十进制小数，避免浮点误差。参见 `PaymentDomain.cs` 中的 `public sealed record Payment(...)`。

### 1.2 记录类型 (Record)

`record` 是 C# 9.0 引入的不可变数据类型，非常适合用于定义 DTO (数据传输对象) 和事件。

```csharp
// 本仓库示例: 定义一个付款请求的只读数据结构
public sealed record CreatePayment(
    string MerchantId, 
    decimal Amount, 
    string Currency, 
    string Reference
);

// 使用时可通过 with 表达式进行属性拷贝修改 (非破坏性变更)
var newPayment = oldPayment with { Amount = 100.00m };
```

**优势**：`record` 默认实现值相等比较 (Value Equality)，即两个 `record` 实例只要属性相同就视为相等。这对于比较复杂的值对象 (Value Object) 非常有用，如判断两个支付指令是否相同。

### 1.3 模式匹配与 Switch 表达式

C# 的模式匹配功能非常强大，`switch` 表达式比传统的 `if-else` 更简洁。

```csharp
// 本仓库示例: 判断贷款状态
public enum LoanStatus { Submitted, Approved, Rejected, Disbursed, Closed }

// 处理业务逻辑时，模式匹配让状态机转换更清晰
string statusDescription = loan.Status switch
{
    LoanStatus.Submitted => "已提交，等待审批",
    LoanStatus.Approved => "已批准，等待放款",
    LoanStatus.Disbursed => "已放款，还款进行中",
    LoanStatus.Closed    => "已结清",
    _                    => "未知状态"
};
```

### 1.4 异步编程 (async/await)

在 IO 密集型操作（如数据库访问、HTTP 调用）中，必须使用异步编程以避免阻塞线程。

```csharp
// 异步方法通常以 Async 结尾，返回 Task 或 Task<T>
public async Task<Payment> GetPaymentAsync(Guid id)
{
    // 模拟异步数据库查询
    var payment = await _dbContext.Payments.FindAsync(id);
    if (payment == null) throw new KeyNotFoundException();
    return payment;
}
```

---

## 2. .NET 8 框架基础

### 2.1 项目结构与 Sdk

所有 .NET 项目都使用 `csproj` 文件定义。`Microsoft.NET.Sdk.Web` 是创建 Web 应用的基础 SDK。

```xml
<!-- 本仓库示例 -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>          <!-- 启用可空引用类型 -->
    <ImplicitUsings>enable</ImplicitUsings> <!-- 自动引入常用命名空间 -->
  </PropertyGroup>
</Project>
```

### 2.2 托管服务 (Hosted Service)

在后台执行长时任务（如处理 Outbox 消息、定时清算）时，使用 `BackgroundService`。

```csharp
// 后台服务模板
public class OutboxProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 1. 查询待处理的 Outbox 消息
            var messages = await _db.OutboxMessages
                .Where(m => m.ProcessedAt == null)
                .OrderBy(m => m.OccurredAt)
                .Take(100)
                .ToListAsync();

            // 2. 处理并发布
            foreach (var msg in messages)
            {
                await _eventBus.PublishAsync(msg.EventType, msg.Payload);
                msg.ProcessedAt = DateTime.UtcNow;
            }
            
            await _db.SaveChangesAsync();
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

### 2.3 依赖注入 (DI)

.NET 内置了轻量级的依赖注入容器，用于管理接口和实现的生命周期。

```csharp
// 在 Program.cs 中注册服务
var builder = WebApplication.CreateBuilder(args);

// 注册单例服务 (整个应用生命周期内只有一个实例)
builder.Services.AddSingleton<IPaymentService, PaymentService>();

// 注册作用域服务 (每次请求创建新实例)
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// 注册瞬时服务 (每次注入都创建新实例)
builder.Services.AddTransient<IHttpClientFactory, HttpClientFactory>();
```

---

## 3. 主流三方库概览 (2026年)

虽然本仓库使用了**极少依赖**的极简设计，但在生产环境中，以下库是构建健壮 .NET 应用的标配。

### 3.1 数据访问 (ORM)

*   **Entity Framework Core (EF Core)**
    *   **地位**：微软官方 ORM，最主流。
    *   **用途**：将 C# 对象映射到数据库表 (Code First, Database First)。
    *   **特色**：支持 LINQ 查询、迁移、DbContext 生命周期管理。

*   **Dapper**
    *   **地位**：StackOverflow 出品，轻量级 ORM。
    *   **用途**：作为 EF Core 的补充，用于需要手写 SQL、追求极致性能的场景（如报表、复杂查询）。
    *   **特色**：API 极简，`connection.Query<T>(sql)`。

### 3.2 日志与监控

*   **Serilog**
    *   **地位**：最流行的结构化日志库。
    *   **用途**：日志聚合（Sink）到 Seq, Elasticsearch, CloudWatch 等。
    *   **配置示例**：
        ```csharp
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Seq("http://localhost:5341")
            .CreateLogger();
        ```

*   **OpenTelemetry (OTel)**
    *   **地位**：CNCF 孵化项目，可观测性标准。
    *   **用途**：标准化指标 (Metrics)、追踪 (Tracing)、日志 (Logging) 的采集与导出。
    *   **特色**：与 Prometheus, Jaeger, Zipkin 无缝集成。

### 3.3 可靠性与网络

*   **Polly**
    *   **地位**：.NET 生态最著名的容错库。
    *   **用途**：实现**重试 (Retry)**、**熔断 (Circuit Breaker)**、**超时 (Timeout)**、**限流 (Rate Limit)** 等策略。
    *   **代码示例**：
        ```csharp
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .WaitAndRetryAsync(3, retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        // 调用时包裹策略
        await retryPolicy.ExecuteAsync(() => _httpClient.GetAsync(url));
        ```

*   **Refit**
    *   **地位**：类型安全的 HTTP 客户端库。
    *   **用途**：通过定义接口和属性，自动生成 HTTP 调用代码。
    *   **代码示例**：
        ```csharp
        public interface IGitHubApi
        {
            [Get("/repos/{owner}/{repo}")]
            Task<Repo> GetRepo(string owner, string repo);
        }
        // 使用
        var api = RestService.For<IGitHubApi>("https://api.github.com");
        var repo = await api.GetRepo("octocat", "Hello-World");
        ```

### 3.4 序列化与校验

*   **System.Text.Json**
    *   **地位**：微软官方 JSON 库，性能极高。
    *   **用途**：JSON 序列化/反序列化，默认已集成在 ASP.NET Core 中。

*   **FluentValidation**
    *   **地位**：最流行的 .NET 校验库。
    *   **用途**：使用 Fluent API 为模型定义业务校验规则。
    *   **代码示例**：
        ```csharp
        public class PaymentValidator : AbstractValidator<CreatePayment>
        {
            public PaymentValidator()
            {
                RuleFor(x => x.Amount).GreaterThan(0);
                RuleFor(x => x.Currency).Length(3).WithMessage("Currency must be 3 letters.");
            }
        }
        ```

### 3.5 消息队列

*   **MassTransit / Rebus**
    *   **地位**：.NET 生态主流的消息总线框架。
    *   **用途**：封装 RabbitMQ, Kafka, Azure Service Bus 等 MQ 的复杂性。
    *   **特色**：提供发送/订阅、Saga 长事务、幂等处理等高级功能。

---

## 4. 本仓库代码演示的模式

| 本仓库代码位置 | 演示的技术点 |
| --- | --- |
| `PaymentDomain.cs` | `record` 类型定义、`ConcurrentDictionary` 线程安全集合、`SHA256` 哈希 |
| `LendingDomain.cs` | 状态机、`decimal` 金融计算、业务校验逻辑 |
| `Program.cs` (三个服务) | Minimal API、依赖注入、中间件 |

建议在阅读本教程的同时，结合 `src/` 目录下的代码进行实操练习。
