# Stripe 支付 API 详细文档

> Stripe 是全球领先的在线支付基础设施，提供极简的 RESTful API 和完善的 .NET SDK。本文档覆盖核心支付流程的接口出入参。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://api.stripe.com` |
| 沙箱 Base URL | `https://api.stripe.com`（用测试密钥 `sk_test_*`） |
| API 版本 | `2025-01-27`（通过 `Stripe-Version` 头指定） |
| 认证方式 | Bearer Token（`Authorization: Bearer sk_live_xxx`） |
| 限流 | 100 读 ops/s；100 写 ops/s（可申请提升） |
| 内容类型 | `application/x-www-form-urlencoded`（REST）或 `application/json`（部分新接口） |
| SLA | 99.999% |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| 创建 PaymentIntent | POST | `/v1/payment_intents` | 创建支付意图（核心下单接口） | B |
| 查询 PaymentIntent | GET | `/v1/payment_intents/{id}` | 查询支付状态 | B |
| 确认 PaymentIntent | POST | `/v1/payment_intents/{id}/confirm` | 服务端确认支付 | B |
| 创建退款 | POST | `/v1/refunds` | 退款（部分/全额） | B |
| 创建 Customer | POST | `/v1/customers` | 创建客户（绑定支付方式） | B |
| 创建 Checkout Session | POST | `/v1/checkout/sessions` | 创建托管收银台（C 端跳转） | B+C |
| 列出 Webhook 端点 | GET | `/v1/webhook_endpoints` | 查询 Webhook 配置 | B |

## 3. 接口详情

### 3.1 创建 PaymentIntent

- **URL**: `POST /v1/payment_intents`
- **用途**: 创建一个支付意图，前端用返回的 `client_secret` 唤起 Payment Element 完成卡片输入。

#### 请求头

| 头 | 必填 | 说明 |
| :--- | :--- | :--- |
| `Authorization` | 是 | `Bearer sk_test_xxx` |
| `Content-Type` | 是 | `application/x-www-form-urlencoded` |
| `Stripe-Version` | 否 | 如 `2025-01-27`，不传用账号默认 |
| `Idempotency-Key` | 推荐 | 客户端生成 UUID，防止重复创建 |

#### 请求体（form-urlencoded 字段）

| 字段 | 类型 | 必填 | 约束 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `amount` | int | 是 | > 0，最小单位 | 金额（最小货币单位，如分） | `8850`（88.50 CNY） |
| `currency` | string | 是 | 3 位 ISO 4217 小写 | 货币代码 | `cny` |
| `payment_method_types[]` | string[] | 否 | - | 允许的支付方式 | `["card"]` |
| `customer` | string | 否 | `cus_*` | 关联客户 ID | `cus_xxx` |
| `metadata[order_id]` | string | 否 | <=500 字符 | 商户业务单号（对账用） | `order-1001` |
| `description` | string | 否 | - | 描述 | `TestFinance 订单` |
| `capture_method` | string | 否 | `automatic`/`manual` | 捕获方式 | `manual`（先授权后捕获） |
| `statement_descriptor` | string | 否 | <=22 字符 | 信用卡账单描述 | `TestFinance` |

#### 请求示例

```bash
curl -X POST https://api.stripe.com/v1/payment_intents \
  -H "Authorization: Bearer sk_test_xxx" \
  -H "Idempotency-Key: order-1001-uuid" \
  -d "amount=8850" \
  -d "currency=cny" \
  -d "payment_method_types[]=card" \
  -d "metadata[order_id]=order-1001" \
  -d "capture_method=manual" \
  -d "description=TestFinance 订单"
```

#### 响应体字段

| 字段 | 类型 | 说明 |
| :--- | :--- | :--- |
| `id` | string | PaymentIntent ID，`pi_*` |
| `object` | string | 固定 `payment_intent` |
| `amount` | int | 金额（最小单位） |
| `amount_capturable` | int | 可捕获金额 |
| `amount_received` | int | 已收金额 |
| `currency` | string | 货币代码 |
| `status` | string | 状态：`requires_payment_method`/`requires_confirmation`/`requires_action`/`succeeded`/`canceled`/`processing` |
| `client_secret` | string | 客户端密钥，前端用其唤起 Element |
| `capture_method` | string | 捕获方式 |
| `metadata` | object | 商户自定义元数据 |
| `next_action` | object | 下一步动作（如 3DS 验证） |

#### 成功响应示例（201 Created）

```json
{
  "id": "pi_3OkL2X2eZvKYlo2C0xJYabcd",
  "object": "payment_intent",
  "amount": 8850,
  "amount_capturable": 0,
  "amount_received": 0,
  "currency": "cny",
  "status": "requires_payment_method",
  "client_secret": "pi_3OkL2X2eZvKYlo2C0xJYabcd_secret_abcDEF",
  "capture_method": "manual",
  "metadata": { "order_id": "order-1001" },
  "next_action": null,
  "created": 1785425000
}
```

#### 错误码

| HTTP 状态 | 业务码 | 含义 | 排查建议 |
| :--- | :--- | :--- | :--- |
| 400 | `parameter_missing` | 缺少必填参数 | 检查 `amount`/`currency` |
| 400 | `parameter_invalid_integer` | 金额非整数 | `amount` 必须是整数分 |
| 401 | `invalid_api_key` | API Key 无效 | 检查 `Authorization` 头 |
| 402 | `card_declined` | 卡被拒 | 余额不足或风控拦截 |
| 402 | `insufficient_funds` | 余额不足 | 换卡或降低金额 |
| 429 | `rate_limit` | 触发限流 | 降低请求频率 |
| 500 | `internal_server_error` | Stripe 内部错误 | 重试（带同 `Idempotency-Key`） |

### 3.2 捕获 PaymentIntent

- **URL**: `POST /v1/payment_intents/{id}/capture`
- **用途**: 对 `manual` 捕获方式的 PaymentIntent 执行资金划拨。

#### 请求体

| 字段 | 类型 | 必填 | 约束 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `amount_to_capture` | int | 否 | <= `amount_capturable` | 部分捕获金额（不传则全额） |

#### 成功响应示例

```json
{
  "id": "pi_3OkL2X2eZvKYlo2C0xJYabcd",
  "object": "payment_intent",
  "amount": 8850,
  "amount_received": 8850,
  "status": "succeeded",
  "capture_method": "manual"
}
```

### 3.3 创建退款

- **URL**: `POST /v1/refunds`

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `payment_intent` | string | 是 | 原支付意图 ID `pi_*` |
| `amount` | int | 否 | 退款金额（不传则全额） |
| `reason` | string | 否 | `duplicate`/`fraudulent`/`requested_by_customer` |
| `metadata[order_id]` | string | 否 | 商户单号 |

#### 成功响应示例

```json
{
  "id": "re_3OkL2X2eZvKYlo2C0xyz",
  "object": "refund",
  "amount": 2000,
  "currency": "cny",
  "payment_intent": "pi_3OkL2X2eZvKYlo2C0xJYabcd",
  "reason": "requested_by_customer",
  "status": "succeeded"
}
```

### 3.4 Webhook 事件

Stripe 通过 Webhook 推送异步事件。

| 事件名 | 触发时机 | Payload 关键字段 |
| :--- | :--- | :--- |
| `payment_intent.succeeded` | 支付成功 | `data.object.id`、`data.object.amount_received` |
| `payment_intent.payment_failed` | 支付失败 | `data.object.last_payment_error.code` |
| `charge.refunded` | 退款完成 | `data.object.amount_refunded` |

**Webhook 验签**: 使用 `Stripe-Signature` 头（`t=timestamp,v1=signature`），通过 HMAC-SHA256 计算。

## 4. .NET SDK 示例

```csharp
using Stripe;

// 配置密钥
StripeConfiguration.ApiKey = "sk_test_xxx";

// 创建 PaymentIntent
var service = new PaymentIntentService();
var intent = await service.CreateAsync(new PaymentIntentCreateOptions
{
    Amount = 8850,
    Currency = "cny",
    PaymentMethodTypes = new List<string> { "card" },
    CaptureMethod = "manual",
    Metadata = new Dictionary<string, string> { ["order_id"] = "order-1001" }
}, new RequestOptions { IdempotencyKey = "order-1001-uuid" });

// 捕获
await service.CaptureAsync(intent.Id, new PaymentIntentCaptureOptions
{
    AmountToCapture = 8850
});

// 退款
var refundService = new RefundService();
await refundService.CreateAsync(new RefundCreateOptions
{
    PaymentIntent = intent.Id,
    Amount = 2000,
    Reason = "requested_by_customer"
});
```

## 5. 最佳实践

- **幂等性**: 所有写操作必须传 `Idempotency-Key`，与本项目 [Payments.Api](../../../src/Payments.Api/) 设计一致。
- **金额**: Stripe 用最小货币单位（如 CNY 用分），需在接入层做 `decimal * 100` 转换。
- **Webhook**: 必须验签，且要做幂等处理（同事件可能推送多次）。
- **测试**: 测试密钥 `sk_test_*` 配合测试卡 `4242 4242 4242 4242` 使用。
