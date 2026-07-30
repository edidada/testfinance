# 金融科技 .NET 8 工业级代码

csharp
testfinance

腾讯利用C#开发了一系列的金融科技平台，如支付系统、信贷系统、投资理财平台等。这些系统通常需要高性能、高安全性和高可扩展性，C#在这些方面的表现优异，特别是在.NET框架下，提供了强大的开发工具和库，便于构建复杂的金融应用。

本仓库包含三个可独立部署的 ASP.NET Core 服务：支付、信贷及投资理财。它们刻意将业务规则、状态迁移和 HTTP 层放在少量无框架依赖的代码中，便于审计和替换基础设施。

| 服务 | 端口 | 关键能力 |
| --- | --- | --- |
| `Payments.Api` | 5101 | 幂等支付、捕获、退款、账本审计 |
| `Lending.Api` | 5102 | 申请、可解释评分、审批、放款、还款计划 |
| `Wealth.Api` | 5103 | KYC 门禁、产品申购赎回、份额与净值估值 |

## 快速开始

安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 后执行：

```bash
dotnet restore TestFinance.sln
dotnet test TestFinance.sln --configuration Release
dotnet run --project src/Payments.Api --urls http://localhost:5101
```

每个服务提供 `/health`；示例请求与领域限制位于各服务目录的 `README.md`。

## 工程与安全边界

- 金额统一以 `decimal` 和 ISO 货币代码传递；生产环境应将货币精度校验下沉至共享 Money 值对象。
- 写操作要求 `Idempotency-Key`；同一键且不同载荷将被拒绝。
- 内存仓储只用于本地演示。生产环境应实现事务性 SQL 仓储、Outbox、分布式幂等表和消息总线。
- 接入层应由 API Gateway 完成 OIDC/JWT 验签、mTLS、WAF、限流、PII 脱敏和密钥轮换；业务服务只信任已验证的身份声明。
- 金融产品、授信决策、AML/KYC 和会计分录必须经持牌机构、合规与风控审批后才能上线。

## 质量门禁

CI 推荐执行 `dotnet format --verify-no-changes`、`dotnet build -warnaserror`、`dotnet test`、依赖漏洞扫描和 SAST。每个聚合根的关键状态转移已有单元测试；上线前需补充数据库、消息重试、并发、授权和契约测试。
