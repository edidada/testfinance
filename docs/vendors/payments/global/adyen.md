# Adyen 支付 API 详细文档

> Adyen 提供统一的全球收单 API，单一 `/payments` 接口支持 200+ 本地支付方式。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://checkout-live.adyen.com/checkout` |
| 沙箱 Base URL | `https://checkout-test.adyen.com/checkout` |
| API 版本 | `v71` |
| 认证方式 | HTTP Basic Auth（`x-api-key` 头） |
| 限流 | 每商户默认 50 并发 |
| 内容类型 | `application/json` |
| SLA | 99.99% |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| 发起支付 | POST | `/payments` | 统一下单接口 | B |
| 提交支付详情 | POST | `/payments/details` | 补充 3DS/重定向信息 | B |
| 创建支付链接 | POST | `/paymentLinks` | 生成 C 端托管支付链接 | B+C |
| 捕获 | POST | `/payments/{id}/captures` | 手动捕获授权 | B |
| 退款 | POST | `/payments/{id}/refunds` | 退款 | B |
| 取消 | POST | `/payments/{id}/cancels` | 取消未捕获支付 | B |
| 查询支付 | GET | `/payments/{id}` | 查询支付状态 | B |

## 3. 接口详情

### 3.1 发起支付 `/payments`

#### 请求头

| 头 | 必填 | 说明 |
| :--- | :--- | :--- |
| `x-API-key` | 是 | `AQEyhm...`（Adyen 后台获取） |
| `Content-Type` | 是 | `application/json` |
| `Idempotency-Key` | 推荐 | UUID，防重复 |

#### 请求体（JSON）

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `merchantAccount` | string | 是 | 商户账户标识 | `TestFinanceECOM` |
| `amount.value` | int | 是 | 金额（最小单位） | `8850` |
| `amount.currency` | string | 是 | ISO 4217 | `CNY` |
| `reference` | string | 是 | 商户业务单号 | `order-1001` |
| `paymentMethod.type` | string | 是 | 支付方式 | `scheme`（银行卡） |
| `paymentMethod.encryptedCardNumber` | string | 条件 | 卡号（前端加密） | `adyenjs_0_1_25$...` |
| `paymentMethod.encryptedExpiryMonth` | string | 条件 | 有效期月 | `adyenjs_0_1_25$...` |
| `paymentMethod.encryptedExpiryYear` | string | 条件 | 有效期年 | `adyenjs_0_1_25$...` |
| `paymentMethod.encryptedSecurityCode` | string | 条件 | CVV | `adyenjs_0_1_25$...` |
| `returnUrl` | string | 是 | 3DS 跳转回调 | `https://shop.com/return` |
| `shopperEmail` | string | 否 | 客户邮箱 | `test@test.com` |
| `shopperReference` | string | 否 | 客户标识 | `c-1` |
| `storePaymentMethod` | bool | 否 | 是否存储支付方式 | `true` |
| `additionalData.executeThreeD` | bool | 否 | 强制 3DS | `true` |

#### 请求示例

```bash
curl -X POST https://checkout-test.adyen.com/checkout/v71/payments \
  -H "x-API-key: AQEyhm..." \
  -H "Content-Type: application/json" \
  -d '{
    "merchantAccount": "TestFinanceECOM",
    "amount": { "value": 8850, "currency": "CNY" },
    "reference": "order-1001",
    "paymentMethod": {
      "type": "scheme",
      "encryptedCardNumber": "adyenjs_0_1_25$xxx",
      "encryptedExpiryMonth": "adyenjs_0_1_25$xxx",
      "encryptedExpiryYear": "adyenjs_0_1_25$xxx",
      "encryptedSecurityCode": "adyenjs_0_1_25$xxx"
    },
    "returnUrl": "https://shop.com/return"
  }'
```

#### 响应体字段

| 字段 | 类型 | 说明 |
| :--- | :--- | :--- |
| `pspReference` | string | Adyen 支付单号 `*-*-*` |
| `resultCode` | string | 结果码：`Authorised`/`Refused`/`RedirectShopper`/`IdentifyShopper`/`ChallengeShopper`/`Received`/`PresentToShopper`/`Cancelled` |
| `action.type` | string | 下一步动作类型（如 `threeDS2`） |
| `action.token` | string | 3DS token |
| `merchantReference` | string | 商户单号 |
| `amount` | object | 金额 |
| `additionalData` | object | 附加数据 |

#### 成功响应示例

```json
{
  "pspReference": "881600000000ABCD",
  "resultCode": "Authorised",
  "merchantReference": "order-1001",
  "amount": { "value": 8850, "currency": "CNY" }
}
```

#### 错误码

| HTTP 状态 | 业务码 | 含义 |
| :--- | :--- | :--- |
| 400 | `70000` | 通用校验失败 |
| 401 | `401` | API Key 无效 |
| 403 | `010` | 无权限操作该 merchantAccount |
| 422 | `100` | 支付被拒（余额/限额） |
| 422 | `125` | 3DS 验证失败 |
| 429 | `900` | 限流 |

### 3.2 退款 `/payments/{id}/refunds`

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `merchantAccount` | string | 是 | 商户账户 |
| `amount.value` | int | 是 | 退款金额 |
| `amount.currency` | string | 是 | 货币 |
| `reference` | string | 否 | 退款单号 |

### 3.3 通知（Webhook）

Adyen 通过 **Notification** 推送异步事件，需返回 `[accepted]`。

| 事件码 | 含义 |
| :--- | :--- |
| `AUTHORISATION` | 授权成功 |
| `CAPTURE` | 捕获完成 |
| `REFUND` | 退款完成 |
| `CANCELLATION` | 取消 |
| `CHARGEBACK` | 拒付 |

**验签**: 通过 HMAC-SHA256 计算通知 `additionalData.hmacSignature`。

## 4. .NET SDK 示例

```csharp
using Adyen;
using Adyen.Model.Checkout;
using Adyen.Service.Checkout;

var client = new Client("AQEyhm...", Environment.Test, "TestFinanceECOM");
var service = new PaymentsApi(client);

var response = await service.PaymentsAsync(new PaymentRequest
{
    MerchantAccount = "TestFinanceECOM",
    Amount = new Amount("CNY", 8850),
    Reference = "order-1001",
    PaymentMethod = new CheckoutCardDetails
    {
        Type = "scheme",
        EncryptedCardNumber = "adyenjs_0_1_25$xxx",
        EncryptedExpiryMonth = "adyenjs_0_1_25$xxx",
        EncryptedExpiryYear = "adyenjs_0_1_25$xxx",
        EncryptedSecurityCode = "adyenjs_0_1_25$xxx"
    },
    ReturnUrl = "https://shop.com/return"
}, idempotencyKey: "order-1001-uuid");

// resultCode == "Authorised" 表示成功
```

## 5. 最佳实践

- **卡信息加密**: 卡号/CVV 必须在前端用 Adyen Web Components 加密，**严禁明文上送服务端**。
- **统一接口**: 切换支付方式（如从卡切到支付宝）只需改 `paymentMethod.type`，无需换接口。
- **HMAC 验签**: Notification 必须验签，且要返回 `[accepted]`，否则 Adyen 会重试。
