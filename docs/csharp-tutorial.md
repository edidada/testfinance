# 面向金融科技开发者的 C# 与 .NET 8 实战教程

本教程以本仓库 `TestFinance` 代码为蓝本，帮助新手快速掌握 C# 语言特性、.NET 8 框架核心概念以及主流三方库的使用。本仓库共 ~2000 行 C# 代码，覆盖支付、信贷、理财三大金融业务域，所有示例均可在本仓库找到对应实现。

---

## 1. C# 语言核心特性

### 1.1 类型系统：值类型 vs 引用类型

C# 中数据分为两类：**值类型 (Value Type)** 和 **引用类型 (Reference Type)**。

- **值类型**：存储在栈上，赋值时拷贝。如 `int`, `decimal`, `bool`, `struct`、`enum`。
- **引用类型**：存储在堆上，变量仅存储引用地址。如 `class`, `string`, `array`、`record`。

> **金融场景应用**：金额计算必须使用 `decimal`（值类型）而非 `float`/`double`，因为前者能精确表示十进制小数，避免浮点误差。参见 [PaymentDomain.cs](../src/Payments.Api/PaymentDomain.cs) 中的 `public sealed record Payment(...)`。

### 1.2 记录类型 (Record) 与 `with` 表达式

`record` 是 C# 9.0 引入的不可变引用类型，非常适合定义 DTO、值对象和事件。

```csharp
// 本仓库示例: 罚息计算结果（不可变值对象）
// 来源: src/Lending.Api/PostLoan/PostLoanServices.cs
public sealed record PenaltyCalculation(
    Guid LoanId,
    int InstallmentSeq,
    decimal OverduePrincipal,
    decimal PenaltyRate,
    int DaysPastDue,
    decimal PenaltyAmount
);
```

`record` 默认实现**值相等比较**——两个实例只要属性相同即视为相等。配合 `with` 表达式可做非破坏性变更：

```csharp
// 本仓库示例: 票据池更新（非破坏性变更）
// 来源: src/Lending.Api/Bills/BillServices.cs
return Update(pool with { FinancedAmount = pool.FinancedAmount + amount });
```

> **注意**：`with` 仅适用于 `record` 或 `struct`。对普通 `class`（如 [OutboxMessage](../src/Finance.BuildingBlocks/Persistence.cs#L32)）只能直接修改属性。

### 1.3 模式匹配与 Switch 表达式

C# 的 `switch` 表达式支持**关系模式**和**范围匹配**，比 `if-else` 更简洁。

```csharp
// 本仓库示例: 银行贷款风险五级分类
// 来源: src/Lending.Api/PostLoan/PostLoanServices.cs
public static AssetClassification Classify(int daysPastDue) => daysPastDue switch
{
    0       => AssetClassification.Normal,
    <= 30   => AssetClassification.SpecialMention,
    <= 60   => AssetClassification.Substandard,
    <= 90   => AssetClassification.Doubtful,
    _       => AssetClassification.Loss
};
```

**枚举比较**也很优雅：

```csharp
// 次级及以下为不良贷款
public static bool IsNonPerforming(AssetClassification c) => c >= AssetClassification.Substandard;
```

### 1.4 `init` 与 `required` 属性

C# 9 的 `init` 和 C# 11 的 `required` 让对象初始化更安全：

```csharp
// 本仓库示例: 聚合根基类
// 来源: src/Finance.BuildingBlocks/Persistence.cs
public abstract class AggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();        // init: 仅构造时赋值
    public Guid TenantId { get; init; }
    public int Version { get; set; }                       // set: 可变
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class OutboxMessage
{
    public long Id { get; set; }
    public required string EventType { get; set; }         // required: 必须初始化
    public required string Payload { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
```

### 1.5 异步编程 (async/await)

IO 密集型操作（数据库、HTTP 调用）必须用异步避免阻塞线程：

```csharp
// 本仓库示例: 征信查询网关接口
// 来源: src/Lending.Api/PreLoan/PreLoanServices.cs
public interface ICreditBureauGateway
{
    Task<CreditBureauReport> QueryAsync(CreditBureauQuery query, CancellationToken ct = default);
}

// Fake 实现
public sealed class FakeCreditBureauGateway : ICreditBureauGateway
{
    public Task<CreditBureauReport> QueryAsync(CreditBureauQuery query, CancellationToken ct = default)
    {
        var report = new CreditBureauReport(query.CustomerId, Score: 720, OverdueCount30d: 0,
            OverdueCount90d: 0, InquiryCount90d: 2, TotalOutstanding: 50000m, DateTimeOffset.UtcNow);
        return Task.FromResult(report);
    }
}
```

> **CancellationToken**：异步方法应始终接收并传递 `ct`，支持超时取消。

### 1.6 集合初始化器与 LINQ

```csharp
// 本仓库示例: 信贷产品目录
// 来源: src/Lending.Api/Products/LoanProducts.cs
private static readonly Dictionary<ProductKind, ProductDefinition> _catalog = new()
{
    [ProductKind.Consumer] = new(ProductKind.Consumer, "CONSUMER_001", "个人消费贷",
        MinAmount: 1_000m, MaxAmount: 200_000m, MinTermMonths: 3, MaxTermMonths: 60,
        MinRate: 0.0435m, MaxRate: 0.18m, RequireCollateral: false, RequireGuarantor: false, RegulatoryTag: "RETAIL"),
    [ProductKind.Corporate] = new(ProductKind.Corporate, "CORP_001", "对公流动资金贷款",
        MinAmount: 100_000m, MaxAmount: 50_000_000m, /* ... */)
};

// LINQ 查询待处理消息
public IReadOnlyList<OutboxMessage> Pending(int limit = 100) =>
    _store.Values.Where(x => x.ProcessedAt is null)
                 .OrderBy(x => x.OccurredAt)
                 .Take(limit)
                 .ToArray();
```

> **数字分隔符** `1_000m` 提升可读性，金融代码常用。

---

## 2. .NET 8 框架核心

### 2.1 项目结构与 SDK

所有 .NET 项目用 `csproj` 定义。本仓库采用中央包版本管理（[Directory.Packages.props](../Directory.Packages.props)）。

```xml
<!-- 本仓库示例: src/Lending.Api/Lending.Api.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <AssemblyName>Lending.Api</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Finance.BuildingBlocks/Finance.BuildingBlocks.csproj" />
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Swashbuckle.AspNetCore" />
  </ItemGroup>
</Project>
```

> **中央版本管理**：版本号集中在 [Directory.Packages.props](../Directory.Packages.props) 定义，各 `csproj` 只写包名不写版本。

### 2.2 Minimal API

ASP.NET Core 8 的 Minimal API 用一行代码定义端点：

```csharp
// 本仓库示例: src/Lending.Api/Program.cs
app.MapPost("/v1/loan-applications", (LoanApplication request, LendingService service) =>
{
    try
    {
        var loan = service.Submit(request);
        return Results.Created($"/v1/loans/{loan.Id}", loan);
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

// 统一异常处理包装
static IResult Execute(Func<object> action)
{
    try { return Results.Ok(action()); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
    catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}
```

### 2.3 依赖注入 (DI)

.NET 内置轻量级 DI 容器，管理接口与实现的生命周期：

```csharp
// 本仓库示例: src/Lending.Api/Program.cs
// 单例 (Singleton): 全应用共享一个实例
builder.Services.AddSingleton<LendingService>();
builder.Services.AddSingleton<IOutbox, InMemoryOutbox>();
builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
builder.Services.AddSingleton<IRuleEngine, RuleEngine>();

// 后台服务 (Hosted Service): 应用启动即运行
builder.Services.AddHostedService<OutboxDispatcher>();

// 工厂注册: 需要自定义构造逻辑时
builder.Services.AddSingleton<ICreditBureauGateway>(_ =>
    new ResilientCreditBureauGateway(new FakeCreditBureauGateway()));
builder.Services.AddSingleton<CreditRuleSet>(_ =>
{
    var rs = new CreditRuleSet(_.GetRequiredService<IRuleEngine>());
    rs.RegisterDefaults();
    return rs;
});
```

三种生命周期：
- **Singleton**：全应用一个实例，适合无状态服务
- **Scoped**：每个请求一个实例，适合 DbContext
- **Transient**：每次注入都新建，适合轻量无状态服务

### 2.4 后台服务 (BackgroundService)

长时任务（Outbox 投递、定时清算）用 `BackgroundService`：

```csharp
// 本仓库示例: Outbox 后台投递器
// 来源: src/Lending.Api/Components/LendingComponents.cs
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IOutbox _outbox;
    public OutboxDispatcher(IOutbox outbox) => _outbox = outbox;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var msg in _outbox.Pending())
                _outbox.MarkProcessed(msg.Id);  // Demo: 直接标记成功；生产环境调用 MQ/HTTP
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
```

### 2.5 线程安全集合与原子操作

```csharp
// 本仓库示例: 线程安全的内存仓储
// 来源: src/Lending.Api/Components/LendingComponents.cs
public sealed class InMemoryOutbox : IOutbox
{
    private long _seq;                                          // 原子自增序列
    private readonly ConcurrentDictionary<long, OutboxMessage> _store = new();

    public void Enqueue(string aggregateType, Guid aggregateId, string eventType, object payload, RequestContext? context = null)
    {
        var id = Interlocked.Increment(ref _seq);              // 原子操作
        _store[id] = new OutboxMessage { /* ... */ };
    }
}
```

> **`ConcurrentDictionary`**：多线程安全的字典，金融系统高频并发场景必备。

### 2.6 中间件与请求上下文

```csharp
// 本仓库示例: 从 HTTP 头提取租户与链路追踪 ID
// 来源: src/Finance.BuildingBlocks/Persistence.cs
public static class RequestContextExtensions
{
    public static RequestContext CurrentContext(this HttpContext http)
    {
        var tenant = Guid.TryParse(http.Request.Headers["X-Tenant-Id"], out var parsed) ? parsed : Guid.Empty;
        var correlation = Guid.TryParse(http.Request.Headers["X-Correlation-Id"], out var correlationId) ? correlationId : Guid.NewGuid();
        return new RequestContext(tenant, correlation, http.TraceIdentifier, http.User?.Identity?.Name);
    }
}
```

---

## 3. 主流三方库概览 (2026年)

本仓库采用极简依赖设计，但生产环境需引入以下库。

### 3.1 本仓库实际使用的库

| 库 | 用途 | 本仓库引用位置 |
| :--- | :--- | :--- |
| **Serilog.AspNetCore** | 结构化日志 | [Lending.Api.csproj](../src/Lending.Api/Lending.Api.csproj)、[Payments.Api.csproj](../src/Payments.Api/Payments.Api.csproj) |
| **Swashbuckle.AspNetCore** | Swagger/OpenAPI 文档生成 | 三个 API 项目 |
| **Microsoft.EntityFrameworkCore.Sqlite** | EF Core + SQLite 持久化 | [Finance.BuildingBlocks](../src/Finance.BuildingBlocks/Persistence.cs) |
| **xunit** | 单元测试框架 | [tests/](../tests/) 目录 |

### 3.2 数据访问 (ORM)

* **Entity Framework Core (EF Core)**
  - 微软官方 ORM，支持 LINQ 查询、迁移、DbContext。
  - 本仓库 [Persistence.cs](../src/Finance.BuildingBlocks/Persistence.cs) 定义了 `FinanceDbContext` 与实体映射（`ToTable`、`HasIndex` 等）。

* **Dapper**
  - StackOverflow 出品的轻量级 ORM，适合手写 SQL、追求极致性能的报表场景。
  - API 极简：`connection.Query<T>(sql)`。

### 3.3 日志与可观测性

* **Serilog**
  - 最流行的结构化日志库，支持 Sink 到 Seq、Elasticsearch、CloudWatch。
  - 本仓库用法：`builder.Services.AddSerilog();`

```csharp
// 配置示例
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Seq("http://localhost:5341")
    .CreateLogger();
```

* **OpenTelemetry (OTel)**
  - CNCF 可观测性标准，统一指标、追踪、日志采集，与 Prometheus、Jaeger 无缝集成。

### 3.4 可靠性

* **Polly**
  - .NET 生态最著名的容错库，实现重试、熔断、超时、限流。
  - 本仓库 [PreLoanServices.cs](../src/Lending.Api/PreLoan/PreLoanServices.cs) 的 `ResilientCreditBureauGateway` 手写实现了简易熔断（3 次失败熔断 30 秒），生产环境用 Polly 替代：

```csharp
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, retryAttempt =>
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

await retryPolicy.ExecuteAsync(() => _httpClient.GetAsync(url));
```

* **Refit**
  - 类型安全的 HTTP 客户端库，通过接口定义自动生成调用代码。

### 3.5 序列化与校验

* **System.Text.Json**
  - 微软官方 JSON 库，默认集成在 ASP.NET Core。
  - 本仓库多处使用：`JsonSerializer.Serialize(payload)`、`JsonSerializer.Serialize(new { correlation_id = ... })`。

* **FluentValidation**
  - 流行的校验库，用 Fluent API 定义规则。

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

> 本仓库采用 `ArgumentException` 做校验（极简），生产环境推荐 FluentValidation。

### 3.6 消息队列

* **MassTransit / Rebus**
  - .NET 主流消息总线框架，封装 RabbitMQ、Kafka、Azure Service Bus。
  - 本仓库 [OutboxDispatcher](../src/Lending.Api/Components/LendingComponents.cs#L50) 是 Outbox 模式的内存实现，生产环境替换为 MassTransit 投递到 MQ。

---

## 4. 金融业务算法实战

本仓库实现了多个金融核心算法，是学习 C# 数值计算的最佳示例。

### 4.1 复式记账与金额精度

```csharp
// 本仓库示例: 金额精度控制
// 来源: src/Lending.Api/PreLoan/PreLoanServices.cs
var debtRatio = decimal.Round(s.TotalLiabilities / s.TotalAssets, 4, MidpointRounding.ToEven);
```

> **`MidpointRounding.ToEven`**（银行家舍入）：金融标准舍入法，0.5 向最近偶数舍入，避免系统性偏差。

### 4.2 巴塞尔协议内部评级法 (IRB)

```csharp
// 本仓库示例: PD/LGD/EAD 计算
// 来源: src/Lending.Api/PreLoan/PreLoanServices.cs
public static InternalRating Rate(Guid loanId, string ratingGrade, decimal exposure, decimal collateralValue)
{
    if (!GradeToPD.TryGetValue(ratingGrade, out var pd))
        throw new ArgumentException($"未知评级等级：{ratingGrade}");

    // LGD = (敞口 - 抵押物回收值) / 敞口；无抵押则 LGD=45%
    var recovery = Math.Min(collateralValue, exposure);
    var lgd = collateralValue > 0
        ? decimal.Round((exposure - recovery) / exposure, 4, MidpointRounding.ToEven)
        : 0.45m;
    lgd = Math.Clamp(lgd, 0m, 1m);                  // 钳制到 [0, 1]

    var ead = exposure;
    var el = decimal.Round(pd / 100m * lgd * ead, 2, MidpointRounding.ToEven);  // 预期损失 EL = PD × LGD × EAD
    return new InternalRating(Guid.NewGuid(), loanId, ratingGrade, pd, lgd, ead, el, DateTimeOffset.UtcNow);
}
```

### 4.3 票据贴现息计算

```csharp
// 本仓库示例: 贴现息 = 票面 × 贴现率 × 到期天数 / 360
// 来源: src/Lending.Api/Bills/BillServices.cs
public DiscountRecord Discount(Guid billId, string holder, decimal faceAmount,
                                decimal annualDiscountRate, int daysToMaturity)
{
    var interest = decimal.Round(faceAmount * annualDiscountRate * daysToMaturity / 360m,
                                  2, MidpointRounding.ToEven);
    var net = faceAmount - interest;                // 实得金额 = 票面 - 贴现息
    return new DiscountRecord(Guid.NewGuid(), billId, holder, faceAmount,
                              annualDiscountRate, daysToMaturity, interest, net, DateTimeOffset.UtcNow);
}
```

### 4.4 工作流引擎（会签/串签）

本仓库手写了一个简易工作流引擎，演示状态机设计：

```csharp
// 本仓库示例: 会签节点需全部审批人通过
// 来源: src/Lending.Api/Components/LendingComponents.cs
if (node.Kind == WorkflowNodeKind.Countersign)
{
    var nodeApprovals = approvals
        .Where(a => a.NodeId == node.Id && a.Approved)
        .Select(a => a.Approver)
        .ToHashSet();
    if (!node.Approvers.All(nodeApprovals.Contains))
        return Update(instanceId, inst with { Approvals = approvals });  // 仍停留在当前节点
}
```

### 4.5 数字人民币智能合约

```csharp
// 本仓库示例: 条件支付合约
// 来源: src/Lending.Api/Cbdc/CbdcServices.cs
public SmartContract Fulfill(Guid contractId, IDictionary<string, string> facts)
{
    var contract = Get(contractId);
    if (contract.Status != ContractStatus.Pending)
        throw new InvalidOperationException("合约不可执行。");
    if (!EvaluateCondition(contract.ConditionExpression, facts))
        throw new InvalidOperationException("条件未满足，合约不可执行。");

    Update(contract with { Status = ContractStatus.ConditionMet });
    _wallets.Transfer(contract.FromWalletId, contract.ToWalletId, contract.Amount);  // 触发资金划转
    return Update(contract with { Status = ContractStatus.Executed, ExecutedAt = DateTimeOffset.UtcNow });
}

// 简易条件求值: "goods_delivered==true"
private static bool EvaluateCondition(string expr, IDictionary<string, string> facts)
{
    if (expr.Contains("=="))
    {
        var parts = expr.Split("==");
        return facts.TryGetValue(parts[0].Trim(), out var v) && v == parts[1].Trim();
    }
    return true;
}
```

---

## 5. 单元测试实战

本仓库用 **xUnit** 编写测试，[tests/Lending.Tests/LendingFeatureTests.cs](../tests/Lending.Tests/LendingFeatureTests.cs) 包含 25 个测试。

### 5.1 理论测试 (Theory)

`[Theory]` + `[InlineData]` 适合参数化测试：

```csharp
// 本仓库示例: 五级分类边界测试
[Theory]
[InlineData(0, AssetClassification.Normal)]
[InlineData(15, AssetClassification.SpecialMention)]
[InlineData(31, AssetClassification.Substandard)]
[InlineData(65, AssetClassification.Doubtful)]
[InlineData(91, AssetClassification.Loss)]
public void Five_tier_classifier_maps_days_correctly(int dpd, AssetClassification expected)
    => Assert.Equal(expected, FiveTierClassifier.Classify(dpd));
```

### 5.2 事实测试 (Fact)

`[Fact]` 用于无参数的单点验证：

```csharp
[Fact]
public void Penalty_uses_150_percent_rate_over_360_days()
{
    // 10000 × (0.08 × 1.5 / 360) × 30 = 100
    var p = PenaltyCalculator.Calculate(Guid.NewGuid(), 1, 10000m, 0.08m, 30);
    Assert.Equal(100m, p.PenaltyAmount);
    Assert.Equal(0.12m, p.PenaltyRate);
}
```

### 5.3 异常断言

```csharp
[Fact]
public void Bill_pool_financing_capped_at_90_percent()
{
    var svc = new BillPoolService();
    var pool = svc.Open("owner-1");
    svc.Deposit(pool.Id, Guid.NewGuid(), 100000m, new DateOnly(2026, 12, 31));
    var financed = svc.Finance(pool.Id, 90000m);
    Assert.Equal(90000m, financed.FinancedAmount);
    Assert.Throws<InvalidOperationException>(() => svc.Finance(pool.Id, 1m));  // 已达上限
}
```

### 5.4 运行测试

```bash
# 安装 .NET 8 SDK 后（见根目录 README）
dotnet test TestFinance.sln -c Release
# 输出: Passed! - Failed: 0, Passed: 29, Skipped: 0, Total: 29
```

---

## 6. 本仓库代码演示的模式总览

| 代码位置 | 演示的技术点 |
| :--- | :--- |
| [PaymentDomain.cs](../src/Payments.Api/PaymentDomain.cs) | `record` 类型、`ConcurrentDictionary`、`SHA256` 哈希、复式记账 |
| [Payments.Persistence.cs](../src/Payments.Api/Payments.Persistence.cs) | EF Core `DbContext`、`DbSet`、实体映射 |
| [LendingDomain.cs](../src/Lending.Api/LendingDomain.cs) | 聚合根、状态机、`decimal` 金融计算、`enum` |
| [Components/LendingComponents.cs](../src/Lending.Api/Components/LendingComponents.cs) | `BackgroundService`、`ConcurrentDictionary`、`Interlocked`、接口设计、工作流引擎、规则引擎 |
| [PreLoan/PreLoanServices.cs](../src/Lending.Api/PreLoan/PreLoanServices.cs) | 静态方法、`Math.Clamp`、`MidpointRounding.ToEven`、接口+Fake 实现、熔断降级 |
| [InLoan/InLoanServices.cs](../src/Lending.Api/InLoan/InLoanServices.cs) | 策略模式（受托支付）、依赖注入、规则注册 |
| [PostLoan/PostLoanServices.cs](../src/Lending.Api/PostLoan/PostLoanServices.cs) | `switch` 关系模式、`record with`、状态机、LINQ |
| [Products/LoanProducts.cs](../src/Lending.Api/Products/LoanProducts.cs) | `enum`、字典初始化器、静态工厂、数字分隔符 |
| [Bills/BillServices.cs](../src/Lending.Api/Bills/BillServices.cs) | 票据聚合、贴现息公式、`record with` |
| [Cbdc/CbdcServices.cs](../src/Lending.Api/Cbdc/CbdcServices.cs) | 钱包聚合、智能合约、`Task.Delay` + `ContinueWith`、条件求值 |
| [Reporting/ReportingServices.cs](../src/Lending.Api/Reporting/ReportingServices.cs) | 泛型接口 `IDataPlatform`、`OfType<T>`、监管报表 |
| [Persistence.cs](../src/Finance.BuildingBlocks/Persistence.cs) | `abstract class`、`init`/`required`、`HttpContext` 扩展方法 |
| [Program.cs](../src/Lending.Api/Program.cs) (三个服务) | Minimal API、DI 注册、`Results.Created/BadRequest/NotFound`、DTO `record` |
| [LendingFeatureTests.cs](../tests/Lending.Tests/LendingFeatureTests.cs) | xUnit `[Theory]`/`[Fact]`、`Assert.Throws`、25 个测试 |

---

## 7. 实战演练

### 7.1 跑通工程

```bash
# 1. 安装 .NET 8 SDK（参见根目录 README）
# 2. 编译
dotnet build TestFinance.sln -c Release
# 3. 运行测试
dotnet test TestFinance.sln -c Release
# 4. 启动信贷服务
dotnet run --project src/Lending.Api -c Release
# 访问 http://localhost:5102/swagger 查看 API 文档
```

### 7.2 推荐学习路径

1. **入门**：读 [PaymentDomain.cs](../src/Payments.Api/PaymentDomain.cs)（~100 行），理解 `record`、`ConcurrentDictionary`、状态机。
2. **进阶**：读 [LendingDomain.cs](../src/Lending.Api/LendingDomain.cs)（~100 行），理解聚合根与金融计算。
3. **实战**：读 [Components/LendingComponents.cs](../src/Lending.Api/Components/LendingComponents.cs)（~240 行），理解 `BackgroundService`、工作流引擎、规则引擎。
4. **深入**：读 [PreLoan/PreLoanServices.cs](../src/Lending.Api/PreLoan/PreLoanServices.cs) 与 [PostLoan/PostLoanServices.cs](../src/Lending.Api/PostLoan/PostLoanServices.cs)，理解 IRB、五级分类、熔断降级。
5. **测试**：读 [LendingFeatureTests.cs](../tests/Lending.Tests/LendingFeatureTests.cs)，学习如何为金融算法写单元测试。

### 7.3 练习题

1. 为 [CbdcServices.cs](../src/Lending.Api/Cbdc/CbdcServices.cs) 的 `SmartContractEngine` 补充"部分支付"合约类型。
2. 为 [Products/LoanProducts.cs](../src/Lending.Api/Products/LoanProducts.cs) 新增"绿色信贷"产品类型，并配置参数。
3. 用 Polly 替换 [ResilientCreditBureauGateway](../src/Lending.Api/PreLoan/PreLoanServices.cs) 的手写熔断逻辑。
4. 为 [Bills/BillServices.cs](../src/Lending.Api/Bills/BillServices.cs) 的 `RediscountService` 补充单元测试。

---

> **延伸阅读**：架构设计见 [docs/code-design.md](./code-design.md)，数据库设计见 [docs/database-design.md](./database-design.md)，API 契约见 [docs/network-api.md](./network-api.md)。
