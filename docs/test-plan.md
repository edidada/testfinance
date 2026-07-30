# TestFinance 项目测试方案

本文档基于 TestFinance 代码结构，制定一套面向金融级应用的测试策略，覆盖从单元测试到端到端测试的完整链路。

---

## 1. 测试分层策略

本项目遵循经典的 **测试金字塔** 模型，底层单元测试最多，顶层端到端测试最少。

```
    /\
   /E2E\          <-- 少量，核心业务链路验证
  /------\
 / 集成测试 \      <-- 中等，API 与基础设施 (DB, MQ) 集成验证
/----------\
| 单元测试 |      <-- 大量，业务逻辑、边界条件、状态机验证
|----------|
```

### 1.1 各层测试职责

| 测试类型 | 目的 | 覆盖范围 | 执行速度 | 维护成本 |
| :--- | :--- | :--- | :--- | :--- |
| **单元测试** | 验证业务逻辑正确性 | 每个 Service 类的方法、规则校验 | 极快 (ms) | 低 |
| **集成测试** | 验证组件间协作 | API 层 + EF Core + Outbox + MQ | 中等 (s) | 中 |
| **端到端测试** | 验证完整业务流程 | 真实网关 → 真实服务 → 真实依赖 | 慢 (min) | 高 |
| **性能/压力测试** | 验证系统容量 | 核心接口在高并发下的吞吐量、P99 延迟 | 按需 | 高 |

---

## 2. 当前测试现状分析

项目 `tests/` 目录下包含三个测试项目，均使用了 `xUnit` 测试框架。

| 测试项目 | 对应服务 | 当前测试覆盖 |
| :--- | :--- | :--- |
| `Payments.Tests` | `Payments.Api` | 基础支付流程 |
| `Lending.Tests` | `Lending.Api` | 基础评分与贷款流程 |
| `Wealth.Tests` | `Wealth.Api` | 基础开户与交易流程 |

> **现状评估**：当前仅包含**单元测试**，且覆盖面较窄（主要验证 Happy Path），缺乏边界测试、并发测试、集成测试和性能测试。

---

## 3. 详细测试用例设计

### 3.1 单元测试 (Unit Tests)

针对每个服务的核心 `*Service` 类，编写详细的单元测试。

#### 3.1.1 支付服务 (PaymentService)

**测试文件**: `PaymentServiceTests.cs`

| 测试场景 | 测试用例 | 预期结果 |
| :--- | :--- | :--- |
| **创建支付** | `Create_ValidRequest_ShouldSucceed` | 返回 `Authorized` 状态的 Payment |
| | `Create_InvalidAmount_ShouldFail` | `ArgumentException` (Amount <= 0) |
| | `Create_InvalidCurrency_ShouldFail` | `ArgumentException` (Currency 长度 != 3) |
| | `Create_InvalidIdempotencyKey_ShouldFail` | `ArgumentException` (空/null) |
| **幂等性** | `Create_SameIdempotencyKey_ShouldReturnOriginal` | 返回同一个 Payment 实例 |
| | `Create_SameIdempotencyKey_DifferentBody_ShouldFail` | `InvalidOperationException` (409) |
| **状态机** | `Capture_AuthorizedPayment_ShouldSucceed` | 状态变为 `Captured` |
| | `Capture_CapturedPayment_ShouldFail` | `InvalidOperationException` (非法状态) |
| | `Capture_RefundedPayment_ShouldFail` | `InvalidOperationException` (非法状态) |
| **退款** | `Refund_CapturedPayment_ShouldSucceed` | 部分/全额退款成功 |
| | `Refund_ExceedAmount_ShouldFail` | `InvalidOperationException` (超额退款) |
| | `Refund_NonCapturedPayment_ShouldFail` | `InvalidOperationException` (非法状态) |
| **账本** | `Create_ShouldRecordLedgerEntry` | `Ledger` 包含 `authorization` 条目 |
| | `Capture_ShouldRecordLedgerEntry` | `Ledger` 包含 `capture` 条目 |

#### 3.1.2 信贷服务 (LendingService)

**测试文件**: `LendingServiceTests.cs`

| 测试场景 | 测试用例 | 预期结果 |
| :--- | :--- | :--- |
| **评分算法** | `Score_ValidInput_ShouldCalculateCorrectly` | 手动计算值与代码 `Score()` 一致 |
| | `Score_HighCreditLowDebt_ShouldBeHigh` | 评分高 (如 80+) |
| | `Score_LowCreditHighDebt_ShouldBeLow` | 评分低 (如 20-) |
| | `Score_EdgeCase_ShouldClampToRange` | 分数被限制在 `[0, 100]` |
| **申请** | `Submit_ValidApplication_ShouldSucceed` | 创建 `Submitted` 状态的 Loan |
| | `Submit_InvalidTerm_ShouldFail` | `ArgumentException` (Term < 1 或 > 120) |
| | `Submit_InvalidCreditScore_ShouldFail` | `ArgumentException` (Score < 300 或 > 950) |
| **决策** | `Decide_GoodScoreAffordable_ShouldApprove` | 状态变为 `Approved` |
| | `Decide_BadScoreOrUnaffordable_ShouldReject` | 状态变为 `Rejected` |
| | `Decide_AlreadyDecided_ShouldFail` | `InvalidOperationException` |
| **放款** | `Disburse_ApprovedLoan_ShouldGenerateSchedule` | 生成 24 条 `Installment` |
| | `Disburse_NonApproved_ShouldFail` | `InvalidOperationException` |
| **还款** | `Repay_ValidInstallment_ShouldUpdateStatus` | `Paid=true`，状态仍为 `Disbursed` |
| | `Repay_LastInstallment_ShouldCloseLoan` | 全部还清，状态变为 `Closed` |
| | `Repay_InvalidSequence_ShouldFail` | `InvalidOperationException` |

#### 3.1.3 理财服务 (WealthService)

**测试文件**: `WealthServiceTests.cs`

| 测试场景 | 测试用例 | 预期结果 |
| :--- | :--- | :--- |
| **开户** | `OpenAccount_ValidCustomer_ShouldSucceed` | 创建 `Pending` 状态账户 |
| | `OpenAccount_InvalidCustomerId_ShouldFail` | `ArgumentException` |
| **KYC** | `VerifyKyc_PendingAccount_ShouldSucceed` | 状态变为 `Verified` |
| | `VerifyKyc_AlreadyVerified_ShouldFail` | `InvalidOperationException` |
| | `VerifyKyc_Rejected_ShouldSucceed` | 状态变为 `Rejected` |
| **申购** | `Subscribe_KycVerified_ShouldUpdateHoldings` | 份额增加 |
| | `Subscribe_KycPending_ShouldFail` | `InvalidOperationException` |
| | `Subscribe_AmountBelowMin_ShouldFail` | `InvalidOperationException` |
| **赎回** | `Redeem_EnoughUnits_ShouldSucceed` | 份额减少 |
| | `Redeem_InsufficientUnits_ShouldFail` | `InvalidOperationException` |
| **估值** | `Valuation_WithHoldings_ShouldCalculateCorrectly` | Σ(Units * NAV) 等于预期值 |

### 3.2 集成测试 (Integration Tests)

在 `tests/` 目录下新增 `*IntegrationTests` 项目。

| 测试场景 | 测试用例 | 测试步骤 |
| :--- | :--- | :--- |
| **API + 数据库** | `PaymentsApi_CreatePayment_Integration` | 1. 启动 TestServer <br> 2. `POST /v1/payments` <br> 3. 查询数据库 `payments` 表 <br> 4. 断言记录存在 |
| | `PaymentsApi_CaptureAndLedger_Integration` | 1. 创建支付 <br> 2. 调用捕获接口 <br> 3. 查询 `payments` 状态 <br> 4. 查询 `ledger_entries` 数量 |
| **API + Outbox** | `LendingApi_Decision_Outbox_Integration` | 1. 提交申请、决策 <br> 2. 查询 `outbox_messages` 表 <br> 3. 验证 `event_type = 'LoanApproved'` |
| **并发幂等** | `WealthApi_ConcurrentRequests_Integration` | 1. 准备 `Idempotency-Key` <br> 2. 并发发送 10 个相同请求 <br> 3. 断言只有 1 次实际创建 |

### 3.3 端到端测试 (E2E Tests)

| 业务场景 | 测试步骤 | 验证点 |
| :--- | :--- | :--- |
| **完整信贷流程** | 1. 创建客户 <br> 2. 提交贷款申请 <br> 3. 审批通过 <br> 4. 接收 `LoanDisbursed` 事件 (Outbox → Payments) <br> 5. Payments 执行资金划拨 <br> 6. 查询还款账户 | 1. 所有状态正确流转 <br> 2. Payments `payments` 表有记录 <br> 3. `ledger_entries` 借贷平衡 |
| **完整理财流程** | 1. 开户 <br> 2. KYC 通过 <br> 3. 申购债券 <br> 4. 接收 `OrderExecuted` 事件 <br> 5. 查询托管系统账户 | 1. 持仓份额正确 <br> 2. 订单记录存在 <br> 3. 估值准确 |
| **退款流程** | 1. 创建并捕获支付 <br> 2. 部分退款 <br> 3. 查询退款金额 <br> 4. 再次全额退款 <br> 5. 验证状态为 `Refunded` | 1. 退款金额累计正确 <br> 2. 账本流水完整 |

### 3.4 性能测试 (Load Tests)

| 接口 | 目标吞吐量 (RPS) | 目标 P99 延迟 | 测试工具 |
| :--- | :--- | :--- | :--- |
| `POST /v1/payments` (创建) | 5000 | < 100ms | `k6`, `JMeter`, `Artillery` |
| `POST /v1/payments/{id}/capture` | 3000 | < 50ms | `k6`, `JMeter` |
| `POST /v1/loan-applications` | 2000 | < 150ms | `k6`, `JMeter` |
| `POST /v1/.../subscriptions` | 2000 | < 100ms | `k6`, `JMeter` |

**示例 k6 脚本逻辑**：
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 100, // 100 并发虚拟用户
  duration: '30s',
  thresholds: {
    http_req_duration: ['p(99)<100'], // 99% 请求 < 100ms
  },
};

export default function () {
  const payload = JSON.stringify({
    merchantId: 'perf-test',
    amount: Math.random() * 1000,
    currency: 'CNY',
    reference: 'perf-' + Date.now()
  });
  const res = http.post('http://localhost:5101/v1/payments', payload, {
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': 'key-' + __VU + '-' + __ITER }
  });
  check(res, { 'status is 201': (r) => r.status === 201 });
  sleep(1);
}
```

---

## 4. 测试工具栈

| 类别 | 推荐工具 | 用途 |
| :--- | :--- | :--- |
| **单元测试框架** | xUnit (当前已使用) | 单元测试执行 |
| **测试 Mock** | Moq, NSubstitute | 模拟依赖行为 |
| **断言库** | FluentAssertions | 更优雅的断言语法 |
| **内存数据库** | SQLite In-Memory, EF Core InMemory | 集成测试用的假数据库 |
| **消息队列测试** | Testcontainers (Docker) | 真实 RabbitMQ/Kafka 实例 |
| **E2E 测试** | Playwright, Selenium (Web), 或直接用 `HttpClient` | 端到端流程验证 |
| **性能测试** | k6, Apache JMeter, Artillery | 压力/负载测试 |
| **覆盖率** | coverlet.msbuild (内置) | 代码覆盖率统计 |
| **变异测试** | Stryker.NET | 检验测试用例质量 |

---

## 5. CI/CD 集成策略

在 `CI` 流水线中，按顺序执行以下阶段：

```yaml
# .github/workflows/test.yml (伪代码)
stages:
  - build          # 1. dotnet build
  - unit-test      # 2. dotnet test (单元测试) + 覆盖率报告
  - integration-test # 3. dotnet test (集成测试，需 Docker Compose 启动依赖)
  - e2e-test       # 4. E2E 测试
  - coverage-report # 5. 汇总报告，检查覆盖率门槛 (如 > 80%)
```

**质量门禁**：
*   所有单元测试必须通过。
*   代码行覆盖率不得低于 **80%**。
*   关键业务路径（状态机转换、幂等性）覆盖率必须达到 **100%**。
*   代码复杂度超过阈值的函数必须有单元测试。
