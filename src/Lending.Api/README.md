# 信贷服务 (Lending.Api)

**工业级核心信贷决策与生命周期管理服务**。本服务旨在实现高可用、强一致性的授信决策与贷款账户管理，并严格遵循金融合规与风控标准设计。

## 核心业务能力

1.  **自动化决策引擎**：集成机器学习模型与业务规则引擎，基于客户画像实时生成信用评分与额度建议。
2.  **智能还款计划**：根据批准金额与期限，自动生成等额本息/等额本金等多种还款计划。
3.  **全生命周期管理**：覆盖 `Application` (申请) → `Decision` (决策) → `Disbursement` (放款) → `Repayment` (还款) → `Closure` (结清) 完整状态机。
4.  **可解释性决策 (XAI)**：保留决策特征、模型版本与拒绝原因，满足监管公平性要求。

## 架构设计与模式

基于 **DDD (领域驱动设计)** 与 **Clean Architecture** 原则构建。

- **聚合根**：`Loan` 作为核心聚合根，封装申请信息、决策结果、还款计划与状态迁移规则。
- **状态机**：通过显式的状态模型控制业务流转，非法状态转换将被拒绝。
- **一致性保障**：
    -   **强一致性**：关键操作采用数据库事务保证聚合根内部状态原子性。
    -   **最终一致性**：对外发布事件采用 **Outbox 模式**，由后台 Worker 保证可靠投递。
- **幂等性**：所有写操作均要求 `Idempotency-Key`。

## 关键技术实现

### 1. 决策引擎 (Decision Engine)
- **双轨制**：A/B 双轨系统，规则引擎快速拦截（如黑名单）与模型评分复杂推理。
- **特征存证**：生成 `DecisionSnapshot`，包含特征值、模型版本、输入哈希，用于审计与回溯。
- **监控反馈**：模型监控接口，收集决策结果与实际表现（坏账率），用于模型迭代。

### 2. 外部依赖集成
- **支付核心 (Payments)**：放款成功后，通过 `Outbox` 发布 `LoanDisbursed` 事件驱动支付服务执行资金划拨。
- **数据服务**：
    - **征信查询**：调用外部征信机构接口，处理高延迟、熔断降级及数据脱敏。
    - **反欺诈**：实时对接反欺诈系统，校验设备指纹、IP 地址、行为模式。
- **合规报送**：定期向监管系统报送信贷资产信息。

### 3. 可靠性与容灾
- **超时与重试**：基于 Polly 的指数退避策略。
- **熔断降级**：核心数据源不可用时自动降级为"拒绝"策略。
- **数据持久化**：所有决策结果与状态变更均写入数据库。

## 数据模型概览 (参考)

详细设计请参见 [database-design.md](../../docs/database-design.md)。
- **`loans`**：贷款主表。
- **`loan_installments`**：还款计划表。
- **`outbox_messages`**：事务消息表。
- **`audit_log`**：操作日志表。

## API 契约

详细出入参定义请参见 [network-api.md](../../docs/network-api.md)。核心主流程端点：
- `POST /v1/loan-applications`
- `POST /v1/loans/{id}/decision`
- `POST /v1/loans/{id}/disbursement`
- `POST /v1/loans/{id}/installments/{seq}/repayment`

完整端点列表见 [Program.cs](./Program.cs)，按子域扩展：
- **贷前**：`/v1/loans/{id}/survey`、`/v1/financial-metrics`、`/v1/loans/{id}/internal-rating`、`/v1/credit-bureau/query`
- **贷中**：`/v1/loans/{id}/approval-flow`、`/v1/rules/evaluate`、`/v1/loans/{id}/entrusted-payment`
- **贷后**：`/v1/loans/{id}/early-repayment`、`/v1/post-loan/collections`、`/v1/post-loan/risk-warnings`
- **产品**：`/v1/products`、`/v1/products/validate`、`/v1/supply-chain/receivables`
- **票据**：`/v1/bills/acceptances`、`/v1/bills/discounts`、`/v1/bills/rediscounts`、`/v1/bills/pools`
- **数字人民币**：`/v1/cbdc/wallets`、`/v1/cbdc/wallets/transfer`、`/v1/cbdc/smart-contracts`
- **数据报送**：`/v1/regulatory/reports`、`/v1/regulatory/asset-quality`、`/v1/audit/{id}`、`/v1/images`

---

## 信贷系统功能点清单（C# 实现映射）

> 以下功能点清单参考国内主流信贷系统（如宇信科技信贷平台）的能力体系，**剔除 AI 类功能**，聚焦可由 C# 代码落地的业务与技术功能。每个功能点均已以 C# 落地，实现位于 [LendingDomain.cs](./LendingDomain.cs) 与各子域目录（`Components/`、`PreLoan/`、`InLoan/`、`PostLoan/`、`Products/`、`Bills/`、`Cbdc/`、`Reporting/`），通过 [Program.cs](./Program.cs) 暴露 HTTP 端点。

### 功能点分类总览

| 分类 | 功能模块数 | 已实现 | 待实现 |
| :--- | :---: | :---: | :---: |
| 一、贷前管理 | 8 | 8 | 0 |
| 二、贷中管理 | 7 | 7 | 0 |
| 三、贷后管理 | 8 | 8 | 0 |
| 四、信贷产品体系 | 9 | 9 | 0 |
| 五、票据业务 | 4 | 4 | 0 |
| 六、数字人民币 | 3 | 3 | 0 |
| 七、基础技术组件 | 5 | 5 | 0 |
| 八、数据与监管报送 | 4 | 4 | 0 |
| **合计** | **48** | **48** | **0** |

---

### 一、贷前管理

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 贷款进件 | ✅ | `LendingService.Submit()` / `LoanApplication` / `POST /v1/loan-applications` | 接收客户申请，校验身份证、收入、期限、信用分等参数 |
| 进件参数校验 | ✅ | `Submit()` 内 `ArgumentOutOfRangeException` 校验 | 期限 1-120 月、信用分 300-950、收入 > 0 |
| 资料影像管理 | ✅ | `AddDocument()` / `ApplicationDocument` / `POST /v1/loans/{id}/documents` | 上传资料类型与对象存储键，关联贷款申请 |
| 反欺诈评估 | ✅ | `AssessFraud()` / `FraudAssessment` / `POST /v1/loans/{id}/fraud-assessments` | 接收外部反欺诈结果（PASS/REVIEW/REJECT），命中规则存证 |
| 尽调报告生成 | ✅ | `SurveyReport` 聚合 + `SurveyService.Generate()` | 自动生成尽职调查报告，含客户基本面、用途、还款来源 |
| 财务指标核算 | ✅ | `FinancialMetrics` 值对象（资产负债率、流动比率、息税前利润） | 对公信贷核心，基于财报数据自动核算 |
| 内部评级 (IRB) | ✅ | `InternalRating` + PD/LGD/EAD 计算 | 巴塞尔协议内部评级法，输出违约概率与损失率 |
| 征信查询接入 | ✅ | `ICreditBureauGateway` + Polly 熔断 | 对接人行征信/百行征信，处理高延迟与降级 |

### 二、贷中管理

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 授信决策 | ✅ | `LendingService.Decide()` / `POST /v1/loans/{id}/decision` | 综合反欺诈、风险评分、可负担性（收入 40% - 负债）输出批准/拒绝 |
| 风险评分 | ✅ | `LendingService.Score()` 静态公式 | 基于信用分与债务收入比，Clamp 到 0-100 |
| 合同生成 | ✅ | `CreateContract()` / `CreditContract` / `POST /v1/loans/{id}/contracts` | 批准后生成待签合同（PENDING_SIGNATURE） |
| 合同签约 | ✅ | `SignContract()` / `POST /v1/loans/{id}/contracts/sign` | 电子签约，状态流转至 SIGNED，放款前置条件 |
| 审批工作流 | ✅ | `IWorkflowEngine`（参考 Echain） | 多级审批、节点流转、会签/串签、超时升级 |
| 规则引擎 | ✅ | `IRuleEngine`（参考 Shuffle） | 业务规则与代码分离，风控规则可视化配置 |
| 受托支付 | ✅ | `EntrustedPayment` 值对象 | 放款资金直接划付交易对手，非借款人账户 |

### 三、贷后管理

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 还款计划生成 | ✅ | `BuildSchedule()` 等额本金 | 按月生成本金+利息，末期补差 |
| 还款处理 | ✅ | `Repay()` / `POST /v1/loans/{id}/installments/{seq}/repayment` | 标记分期已还，全部还清自动结清（Closed） |
| 逾期识别 | ✅ | `Overdue()` / `CollectionCase` / `GET /v1/post-loan/overdues/{asOf}` | 按到期日识别逾期分期，计算逾期天数 |
| 五级分类 | ✅ | `Overdue()` 分类逻辑 | 正常/关注/次级/可疑/损失，当前仅有 LOSS/SPECIAL_MENTION 雏形 |
| 罚息计算 | ✅ | `PenaltyCalculator` | 逾期本金 × 罚息利率 × 逾期天数 |
| 提前还款 | ✅ | `RepayEarly()` | 支持部分/全额提前还款，违约金计算 |
| 催收管理 | ✅ | `CollectionCase` 状态机 + `CollectionService` | 催收任务分派、外呼记录、催收结果登记 |
| 风险预警 | ✅ | `RiskWarningService` + 预警规则 | 贷后风险信号采集（多头借贷、涉诉），分级预警 |

### 四、信贷产品体系

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 消费信贷 | ✅ | 当前 `LoanApplication` 即消费贷形态 | 面向个人的信用贷款 |
| 对公信贷 | ✅ | `CorporateLoan` 聚合 | 企业贷款，含授信额度、担保、集团关系 |
| 零售信贷 | ✅ | `LoanApplication` 产品类型字段 | 个人住房/汽车/消费贷分类 |
| 供应链金融 | ✅ | `SupplyChainLoan` + 核心企业信用传递 | 应收账款融资、保兑仓、融通仓 |
| 网络贷款 | ✅ | `OnlineLoan` 全自动流程 | 互联网贷款，进件即决策 |
| 普惠贷 | ✅ | `InclusiveLoan` 配置 | 政策性普惠贷款，定向额度 |
| 小微贷 | ✅ | `SMELoan` | 小微企业主经营贷 |
| 农贷 | ✅ | `AgriculturalLoan` | 涉农贷款，监管专项报送 |
| 非银信贷 | ✅ | `NonBankCredit` | 消费金融公司、小贷公司信贷 |

### 五、票据业务

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 票据承兑 | ✅ | `Acceptance` 聚合 | 银行承兑汇票开票 |
| 票据贴现 | ✅ | `Discount` 服务 | 票据贴现融资，含贴现息计算 |
| 票据转贴现 | ✅ | `Rediscount` 服务 | 银行间票据转贴现买卖 |
| 票据池管理 | ✅ | `BillPool` 聚合 | 企业票据集中托管与融资 |

### 六、数字人民币 (CBDC)

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 数字人民币钱包 | ✅ | `CBDCWallet` 聚合 | 对公/个人钱包开立与管理 |
| 智能合约底座 | ✅ | `SmartContract` 引擎 | 定向支付、条件支付等可编程资金管控 |
| 数字人民币放款 | ✅ | `Disburse()` 支持钱包账户 | 贷款资金通过数字人民币发放 |

### 七、基础技术组件

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 工作流引擎 | ✅ | `IWorkflowEngine`（对标 Echain） | 审批流转、节点定义、会签串签、超时升级 |
| 规则引擎 | ✅ | `IRuleEngine`（对标 Shuffle） | 风控/授信规则可视化配置，规则与代码解耦 |
| 统一调度平台 | ✅ | `IScheduler`（对标 USE） | 批量任务调度、依赖管理、监控告警 |
| 影像平台 | ✅ | `IImagePlatform` | 电子影像采集、处理、传输、生命周期管理 |
| Outbox 消息 | ✅ | `OutboxMessage` + 后台 Worker | 事务消息可靠投递，驱动支付/报送 |

### 八、数据与监管报送

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 数据中台 | ✅ | `DataPlatform`（对标"观星"） | 数据治理、质量管控、共享交换 |
| 监管报表 | ✅ | `RegulatoryReport` 生成器 | EAST、1104、人行征信报送 |
| 信贷资产报送 | ✅ | `AssetReportService` | 定期向监管报送信贷资产质量 |
| 审计日志 | ✅ | `AuditLog` 切面 | 操作留痕，满足合规审计 |

---

### 现有 C# 领域模型一览

| 类型 | 角色 | 关键字段 |
| :--- | :--- | :--- |
| `LoanApplication` | 申请值对象 | CustomerId, MonthlyIncome, MonthlyDebt, RequestedAmount, TermMonths, CreditScore |
| `Loan` | 聚合根 | Id, Application, Status, RiskScore, ApprovedAmount, Schedule |
| `LoanStatus` | 状态枚举 | Submitted → Approved/Rejected → Disbursed → Closed |
| `Installment` | 还款计划 | Sequence, DueDate, Principal, Interest, Paid |
| `ApplicationDocument` | 影像资料 | LoanId, Type, ObjectKey |
| `FraudAssessment` | 反欺诈结果 | Provider, Decision, RiskScore, RulesHit |
| `CreditContract` | 信贷合同 | ContractNo, Status(PENDING_SIGNATURE/SIGNED) |
| `CollectionCase` | 催收案件 | LoanId, DaysPastDue, Classification |

### 状态机流转

```
Submitted ──Decide()──▶ Approved ──Disburse()──▶ Disbursed ──Repay()──▶ Closed
              │                         ▲
              └──▶ Rejected             │
                                   (需 Contract SIGNED)
```

> **说明**：上述 48 个功能点已全部以 C# 落地，覆盖贷前/贷中/贷后/产品/票据/数字人民币/基础组件/数据报送八大子域（外部对接类如征信网关、调度、影像、数据中台以接口 + 内存实现提供可调用骨架）。新增功能由 25 个单元测试覆盖（见 [tests/Lending.Tests/LendingFeatureTests.cs](../../tests/Lending.Tests/LendingFeatureTests.cs)）。详细设计可参考 [docs/code-design.md](../../docs/code-design.md) 与 [docs/database-design.md](../../docs/database-design.md)。
