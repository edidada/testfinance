# 投资理财服务 (Wealth.Api)

**工业级资产管理与交易服务**。本服务旨在为用户提供账户管理、产品交易与资产估值功能，确保交易原子性、估值准确性及监管合规性，严格遵循证券投资基金相关法律法规。

## 核心业务能力

1.  **全生命周期账户管理**：覆盖开户、适当性评估、KYC 验证、账户冻结/解冻全流程。
2.  **安全交易执行**：支持申购、赎回、转换等指令，严格遵守交易日、开放期等业务规则，确保交易原子性。
3.  **实时资产估值**：整合最新净值或行情，实时计算账户总资产，并提供历史估值追溯与最大回撤分析。
4.  **TA 登记过户**：对接中央 TA 系统，处理份额登记、申购赎回确认、分红派息与基金转换。
5.  **风控与合规**：集中度限额、VaR 风险价值、投资限制校验、适当性匹配与 AML 反洗钱。

## 架构设计与模式

基于 **领域驱动设计 (DDD)** 与 **CQRS (命令查询职责分离)** 原则构建。

- **聚合根**：`InvestmentAccount` 作为核心聚合根，封装客户状态、持仓（Units）与交易记录。
- **一致性保障**：
    -   **强一致性**：交易指令（如申购/赎回）必须与份额更新、订单生成在**同一数据库事务**中完成。
    -   **最终一致性**：交易结果通过 **Outbox 模式** 发布，驱动下游托管、TA 系统进行资金清算与份额确认。
- **净值快照**：交易时记录 `nav_snapshot`，保证成交价格锁定，避免后续净值波动导致的交易纠纷。
- **状态机**：产品生命周期、TA 确认单、风控告警均采用显式状态机控制流转。

## 关键技术实现

### 1. 估值引擎 (Valuation Engine)
- **净值计算**：单位净值 = (总资产 - 总负债) / 总份额；累计净值含历史分红。
- **估值表**：按摊余成本法 / 公允价值法分类汇总，输出市值、成本与浮动盈亏。
- **FX 重估**：多币种持仓按市场汇率与账面汇率差额计提汇兑损益。
- **最大回撤**：基于历史净值序列计算峰谷最大跌幅。

### 2. 交易核心 (Trading Engine)
- **交易日引擎**：内置复杂的交易日历（跳过周末、节假日），精确控制申购赎回的确认日、清算日。
- **指令簿与撮合**：对于流动性差的产品，处理指令队列与**价格优先 + 时间优先**撮合。
- **比例配售**：认购超额时按申购金额比例分配份额。
- **费率计算**：集成管理费、托管费、销售服务费按日计提，申购费阶梯、赎回费按持有期递减。

### 3. TA 登记过户
- **份额管理**：TA 账户开立、冻结/解冻、可用份额计算。
- **申购赎回确认**：确认单状态机，份额 = 金额 / 净值。
- **分红派息**：现金分红与红利再投两种方式。
- **基金转换**：转出金额扣转换费后折算为转入基金份额。
- **非交易过户**：继承、赠与等非交易场景的份额转移。

### 4. 外部系统集成
- **TA (过户登记)**：对接中央 TA 系统，同步客户资料、持仓、交易明细。
- **托管银行 (Custodian)**：接收交易指令，执行资金清算与证券交割。
- **行情/估值源**：实时获取产品净值、底层资产行情，用于估值计算。

### 5. 合规与风控
- **适当性管理**：交易前必须校验客户风险承受能力与产品风险等级是否匹配（双录/电子签署）。
- **反洗钱 (AML)**：大额现金、结构性拆分、快速资金转移三类可疑交易识别。
- **投资限制**：单一发行人 ≤10%、单一券种 ≤20%、股票类 ≤40%、流动性资产 ≥5%。
- **VaR 风险价值**：历史模拟法计算 95%/99% 置信区间下最大可能损失。

## 数据模型概览 (参考)

详细设计请参见 [database-design.md](../../docs/database-design.md)。
- **`investment_accounts`**：投资账户主表（含 KYC 状态）。
- **`holdings`**：客户持仓明细（份额）。
- **`orders`**：交易订单表（申购/赎回记录）。
- **`products`**：金融产品信息表（净值、费率、状态）。

## API 契约

详细出入参定义请参见 [network-api.md](../../docs/network-api.md)。
- `POST /v1/accounts` / `GET /v1/accounts/{id}`
- `POST /v1/accounts/{id}/kyc`
- `POST /v1/accounts/{id}/subscriptions`
- `POST /v1/accounts/{id}/redemptions`
- `GET /v1/accounts/{id}/valuation`

完整端点列表见 [Program.cs](./Program.cs)，按子域扩展：
- **理财主流程**：`/v1/products`、`/v1/products/{code}/nav`、`/v1/accounts`、`/v1/accounts/{id}/kyc`、`/v1/accounts/{id}/risk-assessment`、`/v1/accounts/{id}/subscriptions`、`/v1/accounts/{id}/redemptions`、`/v1/accounts/{id}/valuation`、`/v1/accounts/{id}/holdings`、`/v1/accounts/{id}/orders`、`/v1/orders/{id}/confirmation`
- **估值**：`/v1/valuation/nav`、`/v1/valuation/sheet`、`/v1/valuation/nav-history`、`/v1/valuation/{productCode}/max-drawdown`、`/v1/valuation/fx-revalue`
- **费用**：`/v1/fees/management`、`/v1/fees/subscription`、`/v1/fees/redemption`
- **交易**：`/v1/trading/orders`、`/v1/trading/orders/{id}/cancel`、`/v1/trading/match`、`/v1/trading/allocate`
- **TA 登记过户**：`/v1/ta/accounts`、`/v1/ta/accounts/{id}/freeze`、`/v1/ta/confirmations`、`/v1/ta/dividends`、`/v1/ta/conversions`
- **风控**：`/v1/risk/concentration`、`/v1/risk/var`、`/v1/risk/restrictions`、`/v1/risk/alerts`
- **合规**：`/v1/compliance/suitability`、`/v1/compliance/aml/large-cash`、`/v1/compliance/aml/structuring`
- **产品**：`/v1/fund-products`、`/v1/fund-products/{code}/transition`、`/v1/share-classes`
- **清算**：`/v1/clearing/fund`、`/v1/clearing/share`、`/v1/clearing/diffs`

---

## 理财系统功能点清单（C# 实现映射）

> 以下功能点清单参考国内主流理财/基金服务平台（如恒生电子 O3 资管平台、TA 系统）的能力体系，**剔除 AI 类功能**，聚焦可由 C# 代码落地的业务与技术功能。每个功能点均已以 C# 落地，实现位于 [WealthDomain.cs](./WealthDomain.cs) 与各子域目录（`Valuation/`、`Fee/`、`Trading/`、`Ta/`、`RiskControl/`、`Compliance/`、`Products/`、`Clearing/`），通过 [Program.cs](./Program.cs) 暴露 HTTP 端点。

### 功能点分类总览

| 分类 | 功能模块数 | 已实现 | 待实现 |
| :--- | :---: | :---: | :---: |
| 一、理财主流程 | 10 | 10 | 0 |
| 二、估值 | 6 | 6 | 0 |
| 三、费用计提 | 3 | 3 | 0 |
| 四、交易撮合 | 4 | 4 | 0 |
| 五、TA 登记过户 | 6 | 6 | 0 |
| 六、风险控制 | 4 | 4 | 0 |
| 七、合规 | 5 | 5 | 0 |
| 八、产品管理 | 4 | 4 | 0 |
| 九、清算与托管 | 5 | 5 | 0 |
| **合计** | **47** | **47** | **0** |

---

### 一、理财主流程

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 产品查询 | ✅ | `WealthService.Products()` / `GET /v1/products` | 产品列表与详情，按编码排序 |
| 净值发布 | ✅ | `PublishNav()` / `POST /v1/products/{code}/nav` | 发布新净值，校验正值，写入历史 |
| 投资账户开户 | ✅ | `OpenAccount()` / `POST /v1/accounts` | 创建待 KYC 账户，关联客户标识 |
| KYC 验证 | ✅ | `VerifyKyc()` / `POST /v1/accounts/{id}/kyc` | 状态流转 Pending → Verified/Rejected，不可重复 |
| 风险评估 | ✅ | `AssessRisk()` / `POST /v1/accounts/{id}/risk-assessment` | 1-5 级风险承受能力评定 |
| 申购 | ✅ | `Subscribe()` / `POST /v1/accounts/{id}/subscriptions` | 校验开放期/起投/适当性，份额 = 金额/净值 |
| 赎回 | ✅ | `Redeem()` / `POST /v1/accounts/{id}/redemptions` | 校验持仓充足，扣减份额 |
| 持仓查询 | ✅ | `Holdings()` / `GET /v1/accounts/{id}/holdings` | 输出份额、净值、市值 |
| 账户估值 | ✅ | `Valuation()` / `GET /v1/accounts/{id}/valuation` | 汇总各持仓市值合计 |
| 订单确认 | ✅ | `ConfirmOrder()` / `POST /v1/orders/{id}/confirmation` | ACCEPTED → CONFIRMED，锁定确认日 |

### 二、估值

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 净值计算 | ✅ | `NavCalculator` / `POST /v1/valuation/nav` | 单位净值 + 累计净值 + 日收益率 |
| 估值表生成 | ✅ | `ValuationSheetGenerator` / `POST /v1/valuation/sheet` | 汇总市值/成本/浮动盈亏及占比 |
| 历史净值序列 | ✅ | `NavHistoryService` / `POST /v1/valuation/nav-history` | 按日记录，支持区间查询 |
| 最大回撤 | ✅ | `NavHistoryService.MaxDrawdown()` / `GET /v1/valuation/{code}/max-drawdown` | 峰谷最大跌幅计算 |
| FX 重估 | ✅ | `FxRevaluationEngine` / `POST /v1/valuation/fx-revalue` | 持仓 × (市场汇率 - 账面汇率) |
| 估值口径策略 | ✅ | `ValuationMethodPolicy` | 货币/固收用摊余成本，权益用公允价值 |

### 三、费用计提

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 管理/托管/销售费计提 | ✅ | `FeeAccrualCalculator` / `POST /v1/fees/management` | 日计提 = 净值 × 份额 × 年费率 / 365 |
| 申购费阶梯 | ✅ | `SubscriptionFeeCalculator` / `POST /v1/fees/subscription` | 按金额分档（1.5%/1.2%/0.8%/0.1%），封顶 |
| 赎回费阶梯 | ✅ | `RedemptionFeeCalculator` / `POST /v1/fees/redemption` | 按持有期递减（1.5%→0.75%→0.5%→0.25%→0%） |

### 四、交易撮合

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 订单簿 | ✅ | `OrderBook` / `POST /v1/trading/orders` | 限价单挂单、撤单，按价格时间排序 |
| 撮合引擎 | ✅ | `MatchingEngine` / `POST /v1/trading/match` | 价格优先 + 时间优先，买价 ≥ 卖价即成交 |
| 比例配售 | ✅ | `ProRataAllocator` / `POST /v1/trading/allocate` | 认购超额按申购金额比例分配 |
| 交易日志 | ✅ | `TradeLogService` | 交易动作留痕，按账户查询历史 |

### 五、TA 登记过户

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| TA 账户 | ✅ | `TaAccountService` / `POST /v1/ta/accounts` | 按基金+份额类别开户，维护总份额/冻结份额 |
| 份额冻结/解冻 | ✅ | `TaAccountService.Freeze/Unfreeze` / `POST /v1/ta/accounts/{id}/freeze` | 冻结可用份额，解冻归还 |
| 申购赎回确认 | ✅ | `TaConfirmationService` / `POST /v1/ta/confirmations` | 确认单 Pending → Confirmed/Failed |
| 分红处理 | ✅ | `DividendProcessor` / `POST /v1/ta/dividends` | 现金分红与红利再投（份额 = 分红/净值） |
| 基金转换 | ✅ | `FundConversionService` / `POST /v1/ta/conversions` | 转出金额扣转换费折算转入份额 |
| 非交易过户 | ✅ | `NonTradeTransferService` | 继承/赠与等场景份额转移，校验可过户份额 |

### 六、风险控制

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 集中度限额校验 | ✅ | `ConcentrationChecker` / `POST /v1/risk/concentration` | 单一维度占比超限即拒，输出占比与原因 |
| VaR 风险价值 | ✅ | `VarCalculator` / `POST /v1/risk/var` | 历史模拟法 95%/99% 分位，含持有期调整 |
| 投资限制校验 | ✅ | `InvestmentRestrictionChecker` / `POST /v1/risk/restrictions` | 单一发行人/券种/股票/流动性四规则 |
| 超限告警状态机 | ✅ | `RiskAlertService` / `POST /v1/risk/alerts` | Triggered → Acknowledged → Resolved |

### 七、合规

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 适当性匹配 | ✅ | `SuitabilityMatcher` / `POST /v1/compliance/suitability` | 客户风险等级 ≥ 产品风险等级才匹配 |
| AML 大额现金 | ✅ | `AmlRuleEngine.CheckLargeCash` | 单笔现金 ≥ 5 万触发告警 |
| AML 结构性拆分 | ✅ | `AmlRuleEngine.CheckStructuring` | 24h 内 ≥3 笔接近 5 万识别为拆分 |
| AML 快速资金转移 | ✅ | `AmlRuleEngine.CheckRapidMovement` | 到账 1h 内转出 ≥10 万识别 |
| 双录管理 | ✅ | `IDualRecordingService` + `FakeDualRecordingService` | 录音/录像启停与回放接口骨架 |

### 八、产品管理

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 产品生命周期 | ✅ | `ProductLifecycleService` / `POST /v1/fund-products` | Raising → ClosedForRaising → Open → Suspended → Liquidating → Liquidated |
| 份额类别目录 | ✅ | `ShareClassCatalog` / `GET /v1/share-classes` | A 前端/B 后端/C 销售服务费三类配置 |
| 产品费率配置 | ✅ | `ProductFeeService` | 按产品配置管理/托管/申购/赎回费率 |
| 产品风险等级映射 | ✅ | `ProductRiskMapper` | 资产类别 → 默认风险等级（货基 1 → 另类 5） |

### 九、清算与托管

| 功能点 | 状态 | C# 实现 / 设计 | 说明 |
| :--- | :---: | :--- | :--- |
| 资金清算 | ✅ | `FundClearingService` / `POST /v1/clearing/fund` | 申购 - 赎回 = 净流，按日清算 |
| 份额清算 | ✅ | `ShareClearingService` / `POST /v1/clearing/share` | 申购份额 - 赎回份额 = 净份额 |
| 对账文件生成 | ✅ | `ReconciliationFileGenerator` | 生成 TA 与托管对账 CSV 文件 |
| 托管银行接口 | ✅ | `ICustodianGateway` + `FakeCustodianGateway` | 持仓查询、资金划付、对账文件下载骨架 |
| 对账差异处理 | ✅ | `ClearingDiffService` / `POST /v1/clearing/diffs` | TA 与托管差额识别、登记、解决 |

---

### 现有 C# 领域模型一览

| 类型 | 角色 | 关键字段 |
| :--- | :--- | :--- |
| `InvestmentAccount` | 聚合根 | Id, CustomerId, Kyc, Units, RiskLevel |
| `Product` | 产品值对象 | Code, Name, Currency, Nav, MinimumSubscription, IsOpen, RiskLevel |
| `Order` | 交易订单 | Id, AccountId, ProductCode, Type, Units, Nav, Status |
| `KycStatus` | 状态枚举 | Pending → Verified / Rejected |
| `FundProduct` | 基金产品 | Code, ShareClass, RiskLevel, Phase, MinSubscription, FeeRate |
| `TaAccount` | TA 账户 | CustomerId, FundCode, ShareClass, TotalShares, FrozenShares |
| `TaConfirmation` | TA 确认单 | Type, AmountOrShares, Nav, Status, ConfirmDate |
| `NavCalculation` | 净值计算 | TotalAssets, TotalLiabilities, TotalUnits, UnitNav, AccumulatedNav |
| `ValuationSheet` | 估值表 | Lines, TotalMarketValue, TotalCost, TotalUnrealizedPnL |

### 状态机流转

```
账户 KYC：Pending ──VerifyKyc()──▶ Verified / Rejected
订单：    ACCEPTED ──ConfirmOrder()──▶ CONFIRMED
产品生命周期：Raising ──▶ ClosedForRaising ──▶ Open ⇄ Suspended ──▶ Liquidating ──▶ Liquidated
TA 确认单：Pending ──Confirm()──▶ Confirmed / Fail()──▶ Failed
风控告警：Triggered ──Acknowledge()──▶ Acknowledged ──Resolve()──▶ Resolved
```

> **说明**：上述 47 个功能点已全部以 C# 落地，覆盖理财主流程/估值/费用/交易/TA/风控/合规/产品/清算九大子域（外部对接类如 TA、托管银行、双录以接口 + 内存实现提供可调用骨架）。新增功能由 25 个单元测试覆盖（见 [tests/Wealth.Tests/WealthFeatureTests.cs](../../tests/Wealth.Tests/WealthFeatureTests.cs)）。详细设计可参考 [docs/code-design.md](../../docs/code-design.md) 与 [docs/database-design.md](../../docs/database-design.md)。
