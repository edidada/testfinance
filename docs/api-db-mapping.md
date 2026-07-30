# HTTP 接口与数据库表/视图映射关系

> 本文档基于 [network-api.md](./network-api.md) 的接口定义与 [database-design.md](./database-design.md) 的表视图设计，提供一份详细的 **API → DB** 映射清单，便于后端开发、DBA 与测试人员对齐数据流向。

## 通用说明

### 操作类型
- **读 (Read)**：接口执行前需要查询的表/视图
- **写 (Write)**：接口执行过程中会 INSERT/UPDATE 的表
- **视图 (View)**：可直接用于接口响应查询或业务逻辑判断的数据库视图

### 跨切表（横切关注点）
> **注意**：当前 Demo 代码仅在 `POST /v1/payments` 中实现了 `Idempotency-Key`。生产环境应按照 [network-api.md§1.2 网络协议总览](./network-api.md#12-网络协议总览) 的要求，在**所有 `POST` 写操作**中强制幂等。以下映射按生产标准给出。

- **idempotency_keys**：所有 `POST` 写操作都会**读**（校验幂等键）和**写**（存储请求哈希/响应快照）此表
- **audit_log**：所有状态变更操作都会写此表（不可变审计）
- **outbox_messages**：所有状态变更操作都会写此表（事务内写，后台异步投递）

---

## 一、Payments.Api (端口 5101)

> 对应 [network-api.md§2 Payments.Api](./network-api.md#2-paymentsapi端口-5101) 接口定义与 [database-design.md§2 payments_db](./database-design.md#2-paymentsdb--支付服务) 表设计。

| HTTP 方法 | URL | 操作说明 | 操作类型 | 涉及表/视图 |
| :--- | :--- | :--- | :---: | :--- |
| **POST** | `/v1/payments` | 创建授权支付 (幂等) | **读** | `idempotency_keys` |
| | | | **写** | `payments` <br> `ledger_entries` (Type: `authorization`) <br> `outbox_messages` (Event: `PaymentAuthorized`) <br> `audit_log` |
| **GET** | `/v1/payments/{id}` | 查询支付单 | **读** | `payments` |
| **POST** | `/v1/payments/{id}/capture` | 捕获支付 | **读** | `payments` <br> `idempotency_keys` |
| | | | **写** | `payments` (UPDATE status=`captured`) <br> `ledger_entries` (Type: `capture`) <br> `outbox_messages` (Event: `PaymentCaptured`) <br> `audit_log` |
| **POST** | `/v1/payments/{id}/refunds` | 退款 (部分/全额) | **读** | `payments` <br> `idempotency_keys` |
| | | | **写** | `payments` (UPDATE refunded_amount, status) <br> `ledger_entries` (Type: `refund`) <br> `outbox_messages` (Event: `PaymentRefunded`) <br> `audit_log` |
| **GET** | `/v1/payments/{id}/ledger` | 查询账本明细 | **读** | **`v_payment_ledger`** (视图，直接返回账本明细) <br> `ledger_entries` <br> `payments` |

### 辅助视图（供后台管理/报表使用）
| 视图名 | 用途 | 对应源表 |
| :--- | :--- | :--- |
| `v_payment_summary` | 商户维度支付汇总 (商户对账) | `payments` |
| `v_refund_exposure` | 查询退款敞口 (风控监控) | `payments` |
| `v_outbox_pending` | 待投递的 Outbox 消息 (运维告警) | `outbox_messages` |

---

## 二、Lending.Api (端口 5102)

> 对应 [network-api.md§3 Lending.Api](./network-api.md#3-lendingapi端口-5102) 接口定义与 [database-design.md§3 lending_db](./database-design.md#3-lending_db--信贷服务) 表设计。

| HTTP 方法 | URL | 操作说明 | 操作类型 | 涉及表/视图 |
| :--- | :--- | :--- | :---: | :--- |
| **POST** | `/v1/loan-applications` | 提交贷款申请 | **读** | `idempotency_keys` |
| | | | **写** | `loans` (INSERT, status=`submitted`) <br> `outbox_messages` (Event: `LoanSubmitted`) <br> `audit_log` |
| **GET** | `/v1/loans/{id}` | 查询贷款详情 | **读** | `loans` <br> `loan_installments` (还款计划) |
| | | | **视图** | `v_repayment_progress` (可选，用于快速计算还款进度) |
| **POST** | `/v1/loans/{id}/decision` | 自动评分与审批决策 | **读** | `loans` <br> `idempotency_keys` |
| | | | **写** | `loans` (UPDATE status, approved_amount) <br> `outbox_messages` (Event: `LoanApproved`/`Rejected`) <br> `audit_log` |
| | | | **视图** | `v_risk_distribution` (可选，用于评分分布分析) |
| **POST** | `/v1/loans/{id}/disbursement` | 放款 (生成还款计划) | **读** | `loans` <br> `idempotency_keys` |
| | | | **写** | `loans` (UPDATE status=`disbursed`) <br> `loan_installments` (INSERT 多条分期记录) <br> `outbox_messages` (Event: `LoanDisbursed`) <br> `audit_log` |
| **POST** | `/v1/loans/{id}/installments/{sequence}/repayment` | 偿还指定分期 | **读** | `loans` <br> `loan_installments` <br> `idempotency_keys` |
| | | | **写** | `loan_installments` (UPDATE paid=`true`) <br> `loans` (UPDATE status if 全额还清) <br> `outbox_messages` (Event: `InstallmentPaid`) <br> `audit_log` |
| | | | **视图** | `v_overdue_installments` (可选，用于逾期计算) |

### 辅助视图（供后台管理/报表使用）
| 视图名 | 用途 | 对应源表 |
| :--- | :--- | :--- |
| `v_loan_portfolio` | 贷款组合概览 (风控报表) | `loans` |
| `v_overdue_installments` | 逾期分期清单 (催收) | `loans`, `loan_installments` |
| `v_risk_distribution` | 风险评分分布 | `loans` |
| `v_repayment_progress` | 还款进度详情 | `loans`, `loan_installments` |

---

## 三、Wealth.Api (端口 5103)

> 对应 [network-api.md§4 Wealth.Api](./network-api.md#4-wealthapi端口-5103) 接口定义与 [database-design.md§4 wealth_db](./database-design.md#4-wealth_db--投资理财服务) 表设计。

| HTTP 方法 | URL | 操作说明 | 操作类型 | 涉及表/视图 |
| :--- | :--- | :--- | :---: | :--- |
| **GET** | `/v1/products` | 查询产品列表 | **读** | `products` |
| | | | **视图** | `v_product_aum` (可选，用于展示产品规模) |
| **POST** | `/v1/accounts` | 开户 | **读** | `idempotency_keys` |
| | | | **写** | `investment_accounts` (INSERT, kyc_status=`pending`) <br> `outbox_messages` <br> `audit_log` |
| **POST** | `/v1/accounts/{id}/kyc` | KYC 审核 (通过/拒绝) | **读** | `investment_accounts` <br> `idempotency_keys` |
| | | | **写** | `investment_accounts` (UPDATE kyc_status) <br> `outbox_messages` (Event: `KycVerified`/`Rejected`) <br> `audit_log` |
| | | | **视图** | `v_kyc_pending` (可选，用于获取待审核列表) |
| **POST** | `/v1/accounts/{id}/subscriptions` | 申购 | **读** | `investment_accounts` (校验 KYC 状态) <br> `products` (校验产品开放状态及获取 NAV) <br> `idempotency_keys` |
| | | | **写** | `holdings` (UPDATE/INSERT 份额) <br> `orders` (INSERT 订单记录) <br> `outbox_messages` (Event: `OrderExecuted`) <br> `audit_log` |
| **POST** | `/v1/accounts/{id}/redemptions` | 赎回 | **读** | `investment_accounts` (校验 KYC) <br> `products` (校验产品开放状态及获取 NAV) <br> `holdings` (校验持仓份额) <br> `idempotency_keys` |
| | | | **写** | `holdings` (UPDATE 扣减份额) <br> `orders` (INSERT 订单记录) <br> `outbox_messages` (Event: `OrderExecuted`) <br> `audit_log` |
| **GET** | `/v1/accounts/{id}/valuation` | 账户估值 | **读** | `holdings` <br> `products` |
| | | | **视图** | **`v_account_total_valuation`** (视图，直接返回汇总估值) <br> **`v_account_valuation`** (视图，返回明细估值) |

### 辅助视图（供后台管理/报表使用）
| 视图名 | 用途 | 对应源表 |
| :--- | :--- | :--- |
| `v_account_valuation` | 每个账户的持仓明细估值 | `holdings`, `products` |
| `v_account_total_valuation` | 每个账户的汇总估值 (对应 `Valuation` 接口) | `investment_accounts`, `holdings`, `products` |
| `v_product_aum` | 产品资产管理规模 (AUM) 统计 | `products`, `holdings` |
| `v_kyc_pending` | 待 KYC 审核的账户清单 | `investment_accounts` |

---

## 映射关系汇总图

```mermaid
graph TD
    subgraph "Payments.Api"
        A1[POST /v1/payments] --> W1[(payments)]
        A1 --> W2[(ledger_entries)]
        A2[GET /v1/payments/{id}] --> R1[(payments)]
        A3[POST /v1/payments/{id}/capture] --> W1
        A4[POST /v1/payments/{id}/refunds] --> W1
        A5[GET /v1/payments/{id}/ledger] --> V1[v_payment_ledger]
        V1 --> R2[(ledger_entries)]
    end

    subgraph "Lending.Api"
        B1[POST /v1/loan-applications] --> W3[(loans)]
        B2[GET /v1/loans/{id}] --> R3[(loans)]
        B2 --> R4[(loan_installments)]
        B3[POST /v1/loans/{id}/decision] --> W3
        B4[POST /v1/loans/{id}/disbursement] --> W3
        B4 --> W4[(loan_installments)]
        B5[POST /v1/.../repayment] --> W4
        B5 --> W3
    end

    subgraph "Wealth.Api"
        C1[GET /v1/products] --> R5[(products)]
        C2[POST /v1/accounts] --> W5[(investment_accounts)]
        C3[POST /v1/.../kyc] --> W5
        C4[POST /v1/.../subscriptions] --> W6[(holdings)]
        C4 --> W7[(orders)]
        C5[POST /v1/.../redemptions] --> W6
        C5 --> W7
        C6[GET /v1/.../valuation] --> V2[v_account_total_valuation]
    end

    style A1 fill:#e6f3ff,stroke:#1890ff,stroke-width:2px
    style B1 fill:#e6f3ff,stroke:#1890ff,stroke-width:2px
    style C2 fill:#e6f3ff,stroke:#1890ff,stroke-width:2px
    style V1 fill:#fff7e6,stroke:#fa8c16,stroke-width:2px
    style V2 fill:#fff7e6,stroke:#fa8c16,stroke-width:2px
```
