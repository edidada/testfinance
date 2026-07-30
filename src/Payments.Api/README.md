# 支付服务 (Payments.Api)

**工业级核心交易与资金流转服务**。本服务旨在实现高并发、强一致性的支付指令处理，确保每一笔资金变动都具备审计性与幂等性，严格遵循 PCI DSS 等金融安全标准。

## 核心业务能力

1.  **全流程支付管理**：覆盖 `Authorized` (授权) → `Captured` (捕获) → `Refunded` (退款) 完整生命周期，支持多渠道、多币种。
2.  **高可用幂等处理**：提供强大的幂等机制，确保在网络重试、超时场景下支付指令仅执行一次。
3.  **复式记账本**：内置 **ACID 复式账本**，每一笔支付动作都对应借贷平衡的会计分录，保证资金可追溯。
4.  **清算与结算体系**：覆盖 CNAPS/ISO 20022 报文、多边净额清算、RTGS 大额实时支付、结算账户与资金划拨。
5.  **实时风控与对账**：交易限额、黑白名单、异常识别、规则引擎，配合 T+0/T+1 自动对账与差错处理。

## 架构设计与模式

基于 **DDD (领域驱动设计)** 与 **事件驱动架构** 构建。

- **聚合根**：`Payment` 作为核心聚合根，封装支付金额、状态、账本条目与状态迁移规则。
- **双分录模型**：资金变动通过 `JournalLine` (借方/贷方) 记录，严禁单式记账。
- **一致性保障**：
    -   **强一致性**：支付创建、捕获、退款等操作必须与账本写入在**同一数据库事务**中完成。
    -   **最终一致性**：对外发布事件采用 **Outbox 模式**，保证事务性发件，由后台 Worker 可靠投递。
- **幂等性**：通过 `Idempotency-Key` 与请求体哈希（Hash）结合的方式，拒绝参数篡改的重复请求。
- **状态机**：清算批次、RTGS 指令、结算批次、差错单均采用显式状态机控制流转。

## 关键技术实现

### 1. 支付渠道集成
- **多渠道路由**：支持对接第三方支付（微信、支付宝、银联）或自建支付网关，根据金额、费率、成功率动态路由。
- **回调验签**：所有异步回调必须进行严格的签名验证（HMAC-SHA256 / RSA-SHA256），防止伪造支付成功通知。
- **对账系统**：T+0/T+1 自动拉取渠道对账文件，进行自动比对与差异处理。

### 2. 清算与结算
- **ISO 20022 报文**：`pacs.008` 贷记转账报文构建、`pacs.002` 状态报告解析。
- **CNAPS 通道**：按金额与紧急度选择 HVPS（大额实时）/BEPS（小额批量）/SUPER_NET（网银互联）。
- **多边净额清算**：将互为债权债务的多笔指令轧差为单边净额头寸。
- **RTGS 大额实时支付**：优先级队列、流动性预留、日终轧账。

### 3. 数据一致性
- **本地消息表**：`outbox_messages` 确保业务操作与事件发布的原子性。
- **分布式幂等**：基于数据库唯一索引实现跨实例的幂等控制。
- **金融报文队列**：对标东方通 TongLINK/Q，支持死信队列与指数退避重试。

### 4. 安全与合规
- **敏感数据处理**：完整号、CVV 等信息严禁落库，仅保留必要的脱敏标识（如 Tokenization）。
- **审计日志**：所有操作（成功/失败）均记录详细审计信息，包括操作者、IP、设备、请求体快照。
- **风险控制**：集成实时风控规则，对异常交易（如高频小额、大额跳变）进行实时拦截或人工审核。

## 数据模型概览 (参考)

详细设计请参见 [database-design.md](../../docs/database-design.md)。
- **`payments`**：支付订单主表。
- **`ledger_entries`**：复式账本明细（含方向、金额、关联订单）。
- **`outbox_messages`**：事务消息表。
- **`audit_log`**：操作日志表。

## API 契约

详细出入参定义请参见 [network-api.md](../../docs/network-api.md)。核心主流程端点：
- `POST /v1/payments` (需 Idempotency-Key)
- `POST /v1/payments/{id}/capture`
- `POST /v1/payments/{id}/refunds`
- `GET /v1/payments/{id}/ledger`

完整端点列表见 [Program.cs](./Program.cs)，按子域扩展：
- **支付主流程**：`/v1/payments`、`/v1/payments/{id}/capture`、`/v1/payments/{id}/refunds`、`/v1/payments/{id}/refund-requests`、`/v1/payments/{id}/close`、`/v1/payments/channel-notifications`、`/v1/payments/reconciliation/{date}`
- **复式记账**：`/v1/ledger/accounts`、`/v1/ledger/journal-entries`、`/v1/ledger/accounts/{code}/balance`、`/v1/ledger/fx-convert`
- **清算**：`/v1/clearing/batches`、`/v1/clearing/netting`、`/v1/clearing/pacs008`、`/v1/clearing/cnaps`、`/v1/clearing/bic/{bic}/validate`
- **RTGS**：`/v1/rtgs/queue`、`/v1/rtgs/queue/dequeue`、`/v1/rtgs/{id}/settle`、`/v1/rtgs/liquidity/{bic}/reserve`、`/v1/rtgs/eod-settlement`
- **对账**：`/v1/reconciliation/parse-csv`、`/v1/reconciliation/diff`、`/v1/reconciliation/diffs`、`/v1/reconciliation/diffs/{id}/resolve`
- **风控**：`/v1/risk/limits`、`/v1/risk/check`、`/v1/risk/blacklist/{value}`、`/v1/risk/evaluate`
- **结算**：`/v1/settlement/accounts`、`/v1/settlement/accounts/{bic}/topup`、`/v1/settlement/transfers`、`/v1/settlement/transfers/{id}/settle`
- **渠道**：`/v1/channels`、`/v1/channels/select`、`/v1/channels/verify-signature`
- **传输**：`/v1/transport/publish`、`/v1/transport/consume`、`/v1/transport/dlq`

---

## 支付系统功能点清单（C# 实现映射）

> 以下功能点清单参考国内主流支付清算系统（如宇信科技支付平台、沐融科技支付网关）的能力体系，**剔除 AI 类功能**，聚焦可由 C# 代码落地的业务与技术功能。每个功能点均已以 C# 落地，实现位于 [PaymentDomain.cs](./PaymentDomain.cs) 与各子域目录（`Ledger/`、`Clearing/`、`Rtgs/`、`Reconciliation/`、`RiskControl/`、`Settlement/`、`Channels/`、`Transport/`），通过 [Program.cs](./Program.cs) 暴露 HTTP 端点。

### 功能点分类总览

| 分类 | 功能模块数 | 已实现 | 待实现 |
| :--- | :---: | :---: | :---: |
| 一、支付主流程 | 8 | 8 | 0 |
| 二、复式记账 | 5 | 5 | 0 |
| 三、清算与报文 | 6 | 6 | 0 |
| 四、大额实时支付 (RTGS) | 4 | 4 | 0 |
| 五、对账与差错 | 4 | 4 | 0 |
| 六、风险控制 | 5 | 5 | 0 |
| 七、结算 | 4 | 4 | 0 |
| 八、渠道路由 | 4 | 4 | 0 |
| 九、报文传输 | 4 | 4 | 0 |
| **合计** | **44** | **44** | **0** |

---

### 一、支付主流程

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 支付授权 | ✅ | `PaymentService.Create()` / `POST /v1/payments` | 创建授权态支付，校验商户、金额、币种 |
| 幂等控制 | ✅ | `Idempotency-Key` + SHA256 请求体哈希 | 同 Key 同请求返回原支付，参数篡改则拒绝 |
| 支付捕获 | ✅ | `Capture()` / `POST /v1/payments/{id}/capture` | 授权态 → 捕获态，记入账本 |
| 退款处理 | ✅ | `Refund()` / `POST /v1/payments/{id}/refunds` | 支持部分/全额退款，全额退自动转 Refunded |
| 退款请求单 | ✅ | `RequestRefund()` / `POST /v1/payments/{id}/refund-requests` | 商户退款单号去重，关联退款记录 |
| 关单 | ✅ | `Close()` / `POST /v1/payments/{id}/close` | 授权态关闭，终止交易 |
| 渠道回调处理 | ✅ | `ApplyChannelNotification()` | 回调去重，SUCCESS 自动捕获，CLOSED 转失败 |
| 日终对账汇总 | ✅ | `Reconcile()` / `GET /v1/payments/reconciliation/{date}` | 按日汇总笔数、捕获金额、退款敞口 |

### 二、复式记账

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 会计科目表 | ✅ | `ChartOfAccounts` / `GET /v1/ledger/accounts` | 标准银行科目（资产/负债/权益/收入/费用） |
| 日记账分录 | ✅ | `JournalService.Post()` / `POST /v1/ledger/journal-entries` | 批号记账，自动更新科目余额 |
| 借贷平衡校验 | ✅ | `DoubleEntryValidator` | 借贷必相等，禁止单行同借同贷、负金额 |
| 科目余额查询 | ✅ | `JournalService.Balance()` / `GET /v1/ledger/accounts/{code}/balance` | 按科目方向累计余额 |
| 多币种折算 | ✅ | `FxConverter` / `POST /v1/ledger/fx-convert` | USD 中转交叉汇率，支持 CNY/USD/EUR/HKD/JPY |

### 三、清算与报文

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| BIC 行号校验 | ✅ | `BicValidator` (ISO 9362) / `GET /v1/clearing/bic/{bic}/validate` | 8/11 位、银行码+国家码+地区码校验 |
| pacs.008 报文构建 | ✅ | `Pacs008Builder` / `POST /v1/clearing/pacs008` | ISO 20022 金融机构间客户贷记转账 XML |
| pacs.002 状态解析 | ✅ | `Pacs002Parser` | 解析支付状态报告（MsgId/EndToEndId/TxSts） |
| 多边净额清算 | ✅ | `NettingEngine` / `POST /v1/clearing/netting` | 双边/多边轧差为单边净额头寸 |
| 清算批次状态机 | ✅ | `ClearingBatchService` / `POST /v1/clearing/batches` | Open → Closed → Settled，挂载指令 |
| CNAPS 通道选择 | ✅ | `CnapsBuilder` / `POST /v1/clearing/cnaps` | HVPS/BEPS/SUPER_NET 按金额与紧急度路由 |

### 四、大额实时支付 (RTGS)

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 优先级队列 | ✅ | `RtgsQueue.Enqueue/DequeueBatch` | High > Normal > Low，同优先级按排队时间 |
| RTGS 状态机 | ✅ | `RtgsInstruction` | Queued → Submitted → Settled，非法转换拒绝 |
| 流动性查询与预留 | ✅ | `LiquidityManager` / `POST /v1/rtgs/liquidity/{bic}/reserve` | 预留后扣减可用余额，超额拒绝 |
| 日终轧账 | ✅ | `EodSettlementService` / `POST /v1/rtgs/eod-settlement` | 净额轧差，标记 NET_CREDIT/NET_DEBIT |

### 五、对账与差错

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 对账文件解析 | ✅ | `ReconciliationFileParser` / `POST /v1/reconciliation/parse-csv` | 支持 CSV 与定长格式解析渠道流水 |
| 差异识别 | ✅ | `DiffDetector` / `POST /v1/reconciliation/diff` | 识别本地缺失/渠道缺失/金额不符/状态不符 |
| 差错处理状态机 | ✅ | `DiffResolutionService` | Identified → Investigating → Resolved/WrittenOff |
| 差错挂账 | ✅ | `DiffResolutionService.WriteOff()` | 无法调平的差异挂账处理 |

### 六、风险控制

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 交易限额校验 | ✅ | `LimitChecker` / `POST /v1/risk/check` | 单笔/日累计/日笔数三维限额，按商户配置 |
| 黑白名单 | ✅ | `BlacklistService` / `POST /v1/risk/blacklist/{value}` | 黑/白/灰名单，命中即拦截 |
| 异常交易识别 | ✅ | `AnomalyDetector` | 高频小额（10 分钟 ≥20 笔）、大额跳变（均值 ×10） |
| 风控规则引擎 | ✅ | `PaymentRiskEngine` / `POST /v1/risk/evaluate` | 规则与代码解耦，BLOCK/REVIEW 动作 |
| 默认风控规则 | ✅ | `PaymentRiskEngine.RegisterDefaults()` | 夜间大额、高风险商户、境外 IP 三条内置规则 |

### 七、结算

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 结算账户 | ✅ | `SettlementAccountService` / `POST /v1/settlement/accounts` | 按 BIC 开户、充值、扣款，余额不足拒绝 |
| 资金划拨 | ✅ | `SettlementService` / `POST /v1/settlement/transfers` | Initiated → Settled，原子扣款与入账 |
| 轧差头寸计算 | ✅ | `SettlementPositionCalculator` | 按流入/流出汇总净头寸 |
| 结算批次状态机 | ✅ | `SettlementBatchService` | Open → Processing → Completed，批量结算 |

### 八、渠道路由

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 智能渠道路由 | ✅ | `ChannelRouter` / `POST /v1/channels/select` | 成本+成功率+优先级综合打分选渠道 |
| 回调验签 | ✅ | `SignatureVerifier` / `POST /v1/channels/verify-signature` | HMAC-SHA256 与 RSA-SHA256 双算法 |
| 渠道网关适配 | ✅ | `IChannelGateway` + `WechatPayGateway`/`AlipayGateway` | 微信/支付宝下单、查单、退款接口骨架 |
| 渠道限额管理 | ✅ | `ChannelLimitService` | 按渠道单笔上限与日累计限额校验 |

### 九、报文传输

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 金融报文队列 | ✅ | `IFinancialMessageQueue` / `POST /v1/transport/publish` | 对标 TongLINK/Q，主题发布/消费/ACK |
| TongLINK/Q 网关 | ✅ | `ITongLinkGateway` + `FakeTongLinkGateway` | 东方通 TongLINK/Q 接入骨架 |
| 死信队列 | ✅ | `DeadLetterQueue` / `GET /v1/transport/dlq` | 重试耗尽报文转储，支持人工干预 |
| 消息幂等重试 | ✅ | `MessageRetryPolicy` + `CnapsMessageBus` | 指数退避（1s/5s/30s），CNAPS 报文专用通道 |

---

### 现有 C# 领域模型一览

| 类型 | 角色 | 关键字段 |
| :--- | :--- | :--- |
| `Payment` | 聚合根 | Id, MerchantId, Amount, Currency, Status, RefundedAmount |
| `PaymentStatus` | 状态枚举 | Authorized → Captured → Refunded / Failed / Closed |
| `Refund` | 退款记录 | PaymentId, MerchantRefundNo, Amount, Reason |
| `LedgerEntry` | 账本条目 | PaymentId, Type, Amount, OccurredAt |
| `JournalEntry` | 日记账分录 | BatchNo, Lines, TotalDebit, TotalCredit, IsBalanced |
| `ClearingBatch` | 清算批次 | Currency, BusinessDate, Status, Instructions |
| `RtgsInstruction` | RTGS 指令 | DebtorBic, CreditorBic, Amount, Priority, Status |
| `SettlementAccount` | 结算账户 | Bic, Name, Currency, Balance |
| `TransferInstruction` | 资金划拨 | FromBic, ToBic, Amount, Purpose, Status |

### 状态机流转

```
支付主流程：
Authorized ──Capture()──▶ Captured ──Refund()──▶ Refunded
     │                       
     ├──Close()──▶ Closed
     └──ChannelFailure──▶ Failed

清算批次：Open ──Close()──▶ Closed ──Settle()──▶ Settled
RTGS：    Queued ──Dequeue()──▶ Submitted ──Settle()──▶ Settled
结算：    Initiated ──Settle()──▶ Settled
差错：    Identified ──Investigate()──▶ Investigating ──Resolve()/WriteOff()──▶ Resolved/WrittenOff
```

> **说明**：上述 44 个功能点已全部以 C# 落地，覆盖支付主流程/复式记账/清算报文/RTGS/对账差错/风控/结算/渠道路由/报文传输九大子域（外部对接类如渠道网关、TongLINK/Q、托管接口以接口 + 内存实现提供可调用骨架）。新增功能由 26 个单元测试覆盖（见 [tests/Payments.Tests/PaymentFeatureTests.cs](../../tests/Payments.Tests/PaymentFeatureTests.cs)）。详细设计可参考 [docs/code-design.md](../../docs/code-design.md) 与 [docs/database-design.md](../../docs/database-design.md)。
