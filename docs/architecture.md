# 架构与上线清单

## 服务边界

```text
Client -> Gateway (OIDC, mTLS, rate limit) -> Payments / Lending / Wealth
                                            -> SQL ledger + Outbox -> Event bus
                                            -> KYC / AML / Credit bureau / Custodian
```

三个 API 项目是独立部署单元。每个写模型拥有自己的数据库与 Outbox；禁止跨服务共享表或同步事务。支付服务应发布 `PaymentCaptured`/`PaymentRefunded`，信贷服务消费放款命令，理财服务消费入金确认。所有消费者以事件 ID 去重。

## 生产数据模型

1. 为每个聚合根使用主键、乐观并发版本、创建/更新时间和操作者。
2. 金额列使用 `decimal(19,4)`（或遵从币种精度），绝不使用 `float`/`double`。
3. 幂等表以 `(tenant_id, operation, idempotency_key)` 唯一约束保存请求摘要、响应体与过期时间。
4. 账本使用只追加的双分录模型；业务状态不能替代会计分录。
5. PII 加密存储、最小化日志字段，设置保留期和主体访问/删除流程。

## 可靠性与审计

- 数据库事务内写领域数据和 Outbox；后台投递器至少一次投递，消费者幂等。
- HTTP 调用设定超时、重试预算、熔断与 correlation ID；不可重试的支付渠道错误应显式映射。
- 每项决策和资金状态变迁记录操作者、时间、原因码、前后值、关联请求 ID。
- 为余额、拒绝率、放款失败、净值过期、Outbox 堆积设置指标、告警和 runbook。

## 发布门禁

除单元测试外，还应执行：并发幂等集成测试、支付渠道契约测试、迁移回滚演练、灾备恢复演练、渗透测试、依赖/SBOM 扫描及合规审批。敏感配置只能通过受管密钥系统注入，不能提交到代码库。
