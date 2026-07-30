# 数据库表与视图设计

> 本文档配套 [architecture.md](./architecture.md)，将三个服务的内存领域模型落地为生产级 SQL 数据库。
> SQL 方言以 **PostgreSQL 15+** 为基准（`uuid`、`timestamptz`、`jsonb`、`citext`、生成列），核心结构可移植到 SQL Server / MySQL。

## 1. 设计原则

| 关注点 | 约定 |
| --- | --- |
| 服务隔离 | 三个服务各自独立数据库 `payments_db` / `lending_db` / `wealth_db`，**禁止跨服务共享表或外键**；跨服务仅通过事件总线异步通信。 |
| 命名 | 表名 `snake_case` 复数；主键 `id`；外键 `<单数>_id`；时间列 `created_at` / `updated_at`（`timestamptz`）。 |
| 金额 | 统一 `numeric(19,4)`；净值/份额用 `numeric(19,6)`；**严禁** `float` / `double` / `real`。 |
| 主键 | 聚合根用应用层生成的 `uuid`（v7/v4），明细/日志用 `bigserial`。 |
| 乐观并发 | 每个聚合根表含 `version int not null default 0`，更新时 `where id = ? and version = ?` 并 `version = version + 1`。 |
| 多租户 | 可选 `tenant_id uuid not null`，所有业务索引前缀包含 `tenant_id`；单租户部署可去掉该列。 |
| 时间审计 | `created_at` / `updated_at` / `created_by` / `updated_by`；状态机表另记 `status_changed_at`。 |
| 只追加 | 账本 `ledger_*`、Outbox `outbox_messages`、审计 `audit_log` 只 INSERT，禁止 UPDATE/DELETE（用触发器或权限强制）。 |
| PII | `customer_id` 等敏感列建议存哈希化/脱敏标识，明细下沉到加密保管库；列注释标注 `[PII]`。 |
| 状态枚举 | 用 `varchar` + `CHECK` 而非原生 enum，便于跨工具可读；取值与 C# `enum` 一一对应（小写蛇形）。 |

### 1.1 公共横切表（每个数据库各一份，结构一致、物理隔离）

每个数据库都包含下面三张表，分别承载幂等、事件投递与审计。

```sql
-- idempotency_keys：幂等去重，对应架构中 (tenant_id, operation, idempotency_key) 唯一约束
CREATE TABLE idempotency_keys (
    id              bigserial PRIMARY KEY,
    tenant_id       uuid        NOT NULL,
    operation       varchar(32) NOT NULL,                 -- 如 'create_payment' / 'subscribe'
    idempotency_key varchar(128) NOT NULL,                -- 来自 Idempotency-Key 头，<=128
    request_hash    char(64)    NOT NULL,                 -- 请求体 SHA-256 十六进制
    response_code   smallint    NOT NULL,                 -- HTTP 状态码
    response_body   jsonb,                               -- 回放用的响应快照
    resource_id     uuid,                                -- 关联聚合根 ID
    created_at      timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL,                 -- 过期后可清理
    CONSTRAINT uq_idem UNIQUE (tenant_id, operation, idempotency_key)
);
CREATE INDEX ix_idem_expires ON idempotency_keys (expires_at);

-- outbox_messages：事务内写领域数据 + Outbox，后台投递器至少一次投递
CREATE TABLE outbox_messages (
    id             bigserial PRIMARY KEY,
    tenant_id      uuid        NOT NULL,
    aggregate_type varchar(32) NOT NULL,                  -- 'payment' / 'loan' / 'order'
    aggregate_id   uuid        NOT NULL,
    event_type     varchar(48) NOT NULL,                  -- 'PaymentCaptured' 等
    payload        jsonb       NOT NULL,
    headers        jsonb       NOT NULL DEFAULT '{}'::jsonb, -- correlation_id / traceparent
    occurred_at    timestamptz NOT NULL DEFAULT now(),
    processed_at   timestamptz,                           -- NULL 表示待投递
    attempts       int         NOT NULL DEFAULT 0,
    last_error     text,
    locked_until   timestamptz                            -- 投递器抢占锁
);
CREATE INDEX ix_outbox_pending ON outbox_messages (occurred_at)
    WHERE processed_at IS NULL;

-- audit_log：每项决策与资金状态变迁的不可变审计
CREATE TABLE audit_log (
    id             bigserial PRIMARY KEY,
    tenant_id      uuid        NOT NULL,
    entity_type    varchar(32) NOT NULL,
    entity_id      uuid        NOT NULL,
    action         varchar(32) NOT NULL,                  -- 'create' / 'capture' / 'refund' ...
    before_state   jsonb,
    after_state    jsonb,
    operator       varchar(64),                           -- 操作者主体
    reason_code    varchar(32),                           -- 业务原因码
    correlation_id uuid,                                  -- 链路追踪
    request_id     varchar(64),
    occurred_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_audit_entity ON audit_log (entity_type, entity_id, occurred_at DESC);
```

---

## 2. payments_db — 支付服务

对应 [PaymentDomain.cs](../src/Payments.Api/PaymentDomain.cs)。聚合根 `Payment`、只追加账本 `LedgerEntry`、幂等键。

### 2.1 表

```sql
CREATE TABLE payments (
    id              uuid        PRIMARY KEY,
    tenant_id       uuid        NOT NULL,
    merchant_id     varchar(64) NOT NULL,
    amount          numeric(19,4) NOT NULL,
    currency        char(3)     NOT NULL,                 -- ISO 4217
    reference       varchar(128) NOT NULL,                -- 商户业务单号
    status          varchar(16) NOT NULL DEFAULT 'authorized',
                    CHECK (status IN ('authorized','captured','refunded','failed')),
    refunded_amount numeric(19,4) NOT NULL DEFAULT 0,
    version         int         NOT NULL DEFAULT 0,       -- 乐观并发
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    created_by      varchar(64),
    updated_by      varchar(64),
    CHECK (amount > 0),
    CHECK (refunded_amount >= 0 AND refunded_amount <= amount)
);
CREATE INDEX ix_payments_merchant_time ON payments (tenant_id, merchant_id, created_at DESC);
CREATE INDEX ix_payments_reference     ON payments (tenant_id, reference);
CREATE INDEX ix_payments_status        ON payments (tenant_id, status);
```

```sql
-- 复式账本：架构要求"只追加的双分录模型"，业务状态不能替代会计分录
CREATE TABLE ledger_accounts (
    account_code    varchar(32) PRIMARY KEY,              -- 如 'merchant_clearing' / 'revenue' / 'refunds'
    name            varchar(64) NOT NULL,
    normal_balance  varchar(6)  NOT NULL CHECK (normal_balance IN ('debit','credit')),
    category        varchar(16) NOT NULL                  -- asset/liability/revenue/expense
);

CREATE TABLE ledger_entries (
    id              bigserial PRIMARY KEY,
    journal_id      bigint      NOT NULL,                 -- 同一journal_id 借贷必相等
    account_code    varchar(32) NOT NULL REFERENCES ledger_accounts(account_code),
    direction       varchar(6)  NOT NULL CHECK (direction IN ('debit','credit')),
    amount          numeric(19,4) NOT NULL,
    payment_id      uuid,                                 -- 关联回 payments.id
    memo            varchar(128),
    occurred_at     timestamptz NOT NULL DEFAULT now(),
    CHECK (amount > 0)
);
CREATE INDEX ix_ledger_journal ON ledger_entries (journal_id);
CREATE INDEX ix_ledger_payment ON ledger_entries (payment_id, occurred_at);
-- 强制借贷平衡：每个 journal_id 下 sum(debit)=sum(credit)
CREATE INDEX ix_ledger_balance_check ON ledger_entries (journal_id);
```

> 注：当前 [PaymentDomain.cs](../src/Payments.Api/PaymentDomain.cs#L10) 的 `LedgerEntry` 为单边（Type, Amount）。生产环境按架构要求升级为上面的复式账本；过渡期可用 §2.2 的 `v_payment_ledger` 视图模拟单边视图。

### 2.2 视图

```sql
-- 商户维度支付汇总
CREATE VIEW v_payment_summary AS
SELECT tenant_id, merchant_id,
       count(*)                                   AS payment_count,
       sum(amount)                                AS authorized_amount,
       sum(amount) FILTER (WHERE status='captured') AS captured_amount,
       sum(refunded_amount)                       AS refunded_amount
FROM payments
GROUP BY tenant_id, merchant_id;

-- 支付单 + 账本明细（复式展开，便于对账）
CREATE VIEW v_payment_ledger AS
SELECT p.id AS payment_id, p.merchant_id, p.status,
       le.account_code, le.direction, le.amount, le.occurred_at, le.memo
FROM payments p
JOIN ledger_entries le ON le.payment_id = p.id;

-- 部分退款敞口：已捕获但仍有可退额度
CREATE VIEW v_refund_exposure AS
SELECT id, merchant_id, amount, refunded_amount,
       (amount - refunded_amount) AS refundable_remaining
FROM payments
WHERE status IN ('captured','refunded') AND refunded_amount < amount;

-- 待投递 Outbox
CREATE VIEW v_outbox_pending AS
SELECT id, aggregate_id, event_type, occurred_at, attempts, last_error
FROM outbox_messages
WHERE processed_at IS NULL
ORDER BY occurred_at;
```

事件类型：`PaymentAuthorized` / `PaymentCaptured` / `PaymentRefunded` / `PaymentFailed`。

---

## 3. lending_db — 信贷服务

对应 [LendingDomain.cs](../src/Lending.Api/LendingDomain.cs)。聚合根 `Loan`（含申请信息）、还款计划 `Installment`。

### 3.1 表

```sql
CREATE TABLE loans (
    id               uuid        PRIMARY KEY,
    tenant_id        uuid        NOT NULL,
    customer_id      varchar(64) NOT NULL,                -- [PII] 建议脱敏/哈希
    monthly_income   numeric(19,4) NOT NULL,
    monthly_debt     numeric(19,4) NOT NULL,
    requested_amount numeric(19,4) NOT NULL,
    term_months      int         NOT NULL,
    credit_score     int         NOT NULL,
    risk_score       int         NOT NULL,                -- 由 Score() 计算，0..100
    approved_amount  numeric(19,4) NOT NULL DEFAULT 0,
    status           varchar(16) NOT NULL DEFAULT 'submitted',
                     CHECK (status IN ('submitted','approved','rejected','disbursed','closed')),
    version          int         NOT NULL DEFAULT 0,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    created_by       varchar(64),
    updated_by       varchar(64),
    CHECK (monthly_income > 0),
    CHECK (monthly_debt >= 0),
    CHECK (requested_amount > 0),
    CHECK (term_months BETWEEN 1 AND 120),
    CHECK (credit_score BETWEEN 300 AND 950),
    CHECK (risk_score BETWEEN 0 AND 100)
);
CREATE INDEX ix_loans_customer_time ON loans (tenant_id, customer_id, created_at DESC);
CREATE INDEX ix_loans_status         ON loans (tenant_id, status);

-- 评分公式与代码一致：Score(a)=Clamp((credit_score-300)/6 - (int)(monthly_debt/monthly_income*100),0,100)
-- 注意 (credit_score-300)/6 与 monthly_debt/monthly_income*100 均为整数截断，
-- 建议在应用层计算后写入 risk_score，以保证与 LendingService.Score 完全一致。
```

```sql
CREATE TABLE loan_installments (
    id          bigserial PRIMARY KEY,
    loan_id     uuid        NOT NULL REFERENCES loans(id),
    sequence    int         NOT NULL,                     -- 对应 Installment.Sequence
    due_date    date        NOT NULL,
    principal   numeric(19,4) NOT NULL,
    interest    numeric(19,4) NOT NULL,
    paid        boolean     NOT NULL DEFAULT false,
    paid_at     timestamptz,
    version     int         NOT NULL DEFAULT 0,
    UNIQUE (loan_id, sequence)
);
CREATE INDEX ix_installments_due ON loan_installments (due_date) WHERE paid = false;
```

### 3.2 视图

```sql
-- 贷款组合概览
CREATE VIEW v_loan_portfolio AS
SELECT tenant_id, status,
       count(*)                              AS loan_count,
       sum(approved_amount)                  AS approved_total,
       avg(risk_score)::numeric(5,2)         AS avg_risk
FROM loans
GROUP BY tenant_id, status;

-- 逾期分期：到期日已过且未还
CREATE VIEW v_overdue_installments AS
SELECT l.id AS loan_id, l.customer_id, li.sequence, li.due_date,
       li.principal, li.interest, (current_date - li.due_date) AS overdue_days
FROM loans l
JOIN loan_installments li ON li.loan_id = l.id
WHERE l.status = 'disbursed' AND li.paid = false AND li.due_date < current_date;

-- 风险评分分布
CREATE VIEW v_risk_distribution AS
SELECT CASE
         WHEN risk_score < 40 THEN 'high'
         WHEN risk_score < 70 THEN 'medium'
         ELSE 'low'
       END AS risk_band,
       count(*) AS loan_count
FROM loans
GROUP BY 1;

-- 还款进度
CREATE VIEW v_repayment_progress AS
SELECT l.id AS loan_id,
       count(*) AS total_installments,
       count(*) FILTER (WHERE li.paid) AS paid_installments,
       round(100.0 * count(*) FILTER (WHERE li.paid) / NULLIF(count(*),0), 2) AS paid_pct
FROM loans l
JOIN loan_installments li ON li.loan_id = l.id
WHERE l.status IN ('disbursed','closed')
GROUP BY l.id;
```

事件类型：`LoanSubmitted` / `LoanApproved` / `LoanRejected` / `LoanDisbursed` / `InstallmentPaid`。

---

## 4. wealth_db — 投资理财服务

对应 [WealthDomain.cs](../src/Wealth.Api/WealthDomain.cs)。产品 `Product`、账户 `InvestmentAccount`、持仓 `Units`、订单 `Order`。

### 4.1 表

```sql
CREATE TABLE products (
    code                varchar(32) PRIMARY KEY,          -- 对应 Product.Code，大小写不敏感
    name                varchar(128) NOT NULL,
    currency            char(3)      NOT NULL,
    nav                 numeric(19,6) NOT NULL,           -- 当前净值
    minimum_subscription numeric(19,4) NOT NULL,
    is_open             boolean      NOT NULL DEFAULT true,
    version             int          NOT NULL DEFAULT 0,
    created_at          timestamptz  NOT NULL DEFAULT now(),
    updated_at          timestamptz  NOT NULL DEFAULT now(),
    CHECK (nav > 0),
    CHECK (minimum_subscription > 0)
);

-- 净值历史：当前代码仅有瞬时 NAV，生产需保留历史以支撑估值与回溯
CREATE TABLE nav_history (
    product_code varchar(32) NOT NULL REFERENCES products(code),
    nav_date     date        NOT NULL,
    nav          numeric(19,6) NOT NULL,
    PRIMARY KEY (product_code, nav_date)
);

CREATE TABLE investment_accounts (
    id             uuid        PRIMARY KEY,
    tenant_id      uuid        NOT NULL,
    customer_id    varchar(64) NOT NULL,                  -- [PII]
    kyc_status     varchar(16) NOT NULL DEFAULT 'pending',
                   CHECK (kyc_status IN ('pending','verified','rejected')),
    kyc_verified_at timestamptz,
    version        int         NOT NULL DEFAULT 0,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_accounts_customer ON investment_accounts (tenant_id, customer_id);
CREATE INDEX ix_accounts_kyc      ON investment_accounts (tenant_id, kyc_status);

-- 持仓：把 InvestmentAccount.Units 字典拆为行
CREATE TABLE holdings (
    id           bigserial PRIMARY KEY,
    account_id   uuid        NOT NULL REFERENCES investment_accounts(id),
    product_code varchar(32) NOT NULL REFERENCES products(code),
    units        numeric(19,6) NOT NULL DEFAULT 0,
    version      int         NOT NULL DEFAULT 0,
    UNIQUE (account_id, product_code),
    CHECK (units >= 0)
);
CREATE INDEX ix_holdings_product ON holdings (product_code);

CREATE TABLE orders (
    id           uuid        PRIMARY KEY,
    account_id   uuid        NOT NULL REFERENCES investment_accounts(id),
    product_code varchar(32) NOT NULL REFERENCES products(code),
    order_type   varchar(16) NOT NULL,
                 CHECK (order_type IN ('subscribe','redeem')),
    units        numeric(19,6) NOT NULL,
    nav          numeric(19,6) NOT NULL,                  -- 成交净值快照
    amount       numeric(19,4) NOT NULL,                  -- = units * nav，存档便于对账
    status       varchar(16) NOT NULL DEFAULT 'executed',
    created_at   timestamptz NOT NULL DEFAULT now(),
    created_by   varchar(64),
    CHECK (units > 0)
);
CREATE INDEX ix_orders_account_time ON orders (account_id, created_at DESC);
CREATE INDEX ix_orders_product_time ON orders (product_code, created_at DESC);
```

### 4.2 视图

```sql
-- 账户估值：持仓 × 当前 NAV
CREATE VIEW v_account_valuation AS
SELECT h.account_id, h.product_code, h.units, p.nav,
       (h.units * p.nav)::numeric(19,4) AS market_value
FROM holdings h
JOIN products p ON p.code = h.product_code
WHERE h.units > 0;

-- 账户汇总估值（对应 WealthService.Valuation）
CREATE VIEW v_account_total_valuation AS
SELECT a.id AS account_id, a.customer_id, a.kyc_status,
       coalesce(sum(h.units * p.nav), 0)::numeric(19,4) AS total_value
FROM investment_accounts a
LEFT JOIN holdings h ON h.account_id = a.id
LEFT JOIN products  p ON p.code = h.product_code
GROUP BY a.id, a.customer_id, a.kyc_status;

-- 产品规模 (AUM)
CREATE VIEW v_product_aum AS
SELECT p.code, p.name, p.nav,
       coalesce(sum(h.units), 0)::numeric(19,6) AS total_units,
       (coalesce(sum(h.units), 0) * p.nav)::numeric(19,4) AS aum
FROM products p
LEFT JOIN holdings h ON h.product_code = p.code
GROUP BY p.code, p.name, p.nav;

-- 待 KYC 账户
CREATE VIEW v_kyc_pending AS
SELECT id, customer_id, created_at
FROM investment_accounts
WHERE kyc_status = 'pending'
ORDER BY created_at;
```

事件类型：`KycVerified` / `KycRejected` / `OrderExecuted`（订阅/赎回统一一个事件，`order_type` 区分）。

---

## 5. 实体/字段与代码映射

| 服务 | 表 | 对应 C# 类型 | 关键字段映射 |
| --- | --- | --- | --- |
| Payments | `payments` | `Payment` | `id→Id` `merchant_id→MerchantId` `amount→Amount` `currency→Currency` `reference→Reference` `status→Status` `refunded_amount→RefundedAmount` |
| Payments | `ledger_entries` | `LedgerEntry` | `payment_id→PaymentId` `memo→Type` `amount→Amount` `occurred_at→OccurredAt` |
| Lending | `loans` | `Loan` + `LoanApplication` | 申请字段平铺；`risk_score→RiskScore` `approved_amount→ApprovedAmount` `status→Status` |
| Lending | `loan_installments` | `Installment` | `sequence→Sequence` `due_date→DueDate` `principal→Principal` `interest→Interest` `paid→Paid` |
| Wealth | `products` | `Product` | `code→Code` `nav→Nav` `minimum_subscription→MinimumSubscription` `is_open→IsOpen` |
| Wealth | `investment_accounts` | `InvestmentAccount` | `kyc_status→Kyc` |
| Wealth | `holdings` | `InvestmentAccount.Units` | 字典展开为行 |
| Wealth | `orders` | `Order` | `order_type→Type` `units→Units` `nav→Nav` |

## 6. 迁移与上线门禁

- 迁移脚本置于 `db/migrations/<service>/V<序号>__<说明>.sql`，前向 + 回滚各一份；CI 执行回滚演练。
- 每张聚合根表必须先建索引再灌数据；金额列严禁变更精度（破坏性变更需双写迁移）。
- 上线门禁参见 [architecture.md#发布门禁](./architecture.md#发布门禁)：并发幂等集成测试、契约测试、迁移回滚演练、灾备恢复演练、渗透测试、SBOM 扫描及合规审批。
