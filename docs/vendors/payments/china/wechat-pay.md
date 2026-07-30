# 微信支付 API 详细文档

> 微信支付 v3 API 采用标准 RESTful 设计，JSON 格式，RSA-SHA256 签名。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://api.mch.weixin.qq.com` |
| 沙箱 | 通过测试商户号 + 沙箱密钥 |
| API 版本 | v3 |
| 认证/签名 | RSA-SHA256（请求头 `Authorization` 携带签名） |
| 证书 | 商户 API 证书（用于敏感接口） + 平台证书（验签响应） |
| 内容类型 | `application/json` |
| 限流 | 默认 6000 QPS/商户号 |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| JSAPI 下单 | POST | `/v3/pay/transactions/jsapi` | 小程序/公众号支付 | B |
| Native 下单 | POST | `/v3/pay/transactions/native` | 扫码支付 | B |
| APP 下单 | POST | `/v3/pay/transactions/app` | APP 支付 | B |
| 查询订单 | GET | `/v3/pay/transactions/out-trade-no/{out_trade_no}` | 按商户单号查询 | B |
| 关闭订单 | POST | `/v3/pay/transactions/out-trade-no/{out_trade_no}/close` | 关闭订单 | B |
| 申请退款 | POST | `/v3/refund/domestic/refunds` | 退款 | B |
| 查询退款 | GET | `/v3/refund/domestic/refunds/{out_refund_no}` | 查询退款 | B |
| 申请交易账单 | GET | `/v3/bill/tradebill` | 下载对账单 | B |
| 申请资金账单 | GET | `/v3/bill/fundflowbill` | 下载资金账单 | B |

## 3. 接口详情

### 3.1 JSAPI 下单 `/v3/pay/transactions/jsapi`

#### 请求头

| 头 | 必填 | 说明 |
| :--- | :--- | :--- |
| `Authorization` | 是 | `WECHATPAY2-SHA256-RSA2048 mchid="xxx",nonce_str="xxx",timestamp="xxx",serial_no="xxx",signature="xxx"` |
| `Accept` | 是 | `application/json` |
| `Content-Type` | 是 | `application/json` |
| `User-Agent` | 是 | 自定义 |

#### 请求体（JSON）

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `appid` | string | 是 | 应用 ID（公众号/小程序） | `wx8888888888888888` |
| `mchid` | string | 是 | 商户号 | `1900000109` |
| `description` | string | 是 | 商品描述 | `TestFinance 订单` |
| `out_trade_no` | string | 是 | 商户订单号（<=32） | `order-1001` |
| `time_expire` | string | 否 | 过期时间 RFC3339 | `2026-07-30T13:00:00+08:00` |
| `attach` | string | 否 | 附加数据（<=128） | `custom-data` |
| `notify_url` | string | 是 | 异步通知地址 | `https://shop.com/notify` |
| `amount.total` | int | 是 | 金额（分） | `8850` |
| `amount.currency` | string | 否 | 货币，默认 CNY | `CNY` |
| `payer.openid` | string | 是 | 用户 openid | `oUpF8xxxx` |
| `goods_tag` | string | 否 | 订单优惠标记 | - |
| `support_fapiao` | bool | 否 | 是否支持电子发票 | `false` |

#### 请求示例

```bash
curl -X POST https://api.mch.weixin.qq.com/v3/pay/transactions/jsapi \
  -H "Authorization: WECHATPAY2-SHA256-RSA2048 mchid=\"1900000109\",nonce_str=\"abc\",timestamp=\"1785425000\",serial_no=\"xxx\",signature=\"xxx\"" \
  -H "Accept: application/json" \
  -H "Content-Type: application/json" \
  -d '{
    "appid": "wx8888888888888888",
    "mchid": "1900000109",
    "description": "TestFinance 订单",
    "out_trade_no": "order-1001",
    "notify_url": "https://shop.com/notify",
    "amount": { "total": 8850, "currency": "CNY" },
    "payer": { "openid": "oUpF8xxxx" }
  }'
```

#### 响应体字段

| 字段 | 类型 | 说明 |
| :--- | :--- | :--- |
| `prepay_id` | string | 预支付交易会话标识 |

#### 成功响应示例（200 OK）

```json
{ "prepay_id": "wx26112221580621e9b071c00d9e093b0000" }
```

> 前端调起支付时，需用 `prepay_id` 生成 `paySign` 等 5 个参数，通过 JSAPI `wx.requestPayment` 唤起。

#### 错误码

| HTTP 状态 | 业务码 | 含义 | 排查建议 |
| :--- | :--- | :--- | :--- |
| 400 | `PARAM_ERROR` | 参数错误 | 检查 JSON 字段 |
| 401 | `SIGN_ERROR` | 签名错误 | 检查 `Authorization` 头 |
| 403 | `NO_AUTH` | 无权限 | 检查商户号与 appid 绑定 |
| 404 | `ORDER_NOT_EXIST` | 订单不存在 | 检查 `out_trade_no` |
| 429 | `FREQUENCY_LIMITED` | 限流 | 降低频率 |
| 500 | `SYSTEM_ERROR` | 系统错误 | 重试 |

### 3.2 申请退款 `/v3/refund/domestic/refunds`

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `out_trade_no` | string | 二选一 | 商户订单号 |
| `transaction_id` | string | 二选一 | 微信支付订单号 |
| `out_refund_no` | string | 是 | 商户退款单号（唯一） |
| `reason` | string | 否 | 退款原因 |
| `amount.refund` | int | 是 | 退款金额（分） |
| `amount.total` | int | 是 | 原订单金额（分） |
| `amount.currency` | string | 是 | 货币 |
| `notify_url` | string | 否 | 退款通知地址 |

#### 成功响应示例

```json
{
  "refund_id": "50000000382023073012345678901",
  "out_refund_no": "refund-1001",
  "transaction_id": "4200000000202607301234567890",
  "out_trade_no": "order-1001",
  "refund_status": "SUCCESS",
  "success_time": "2026-07-30T12:01:00+08:00",
  "amount": { "refund": 2000, "total": 8850, "currency": "CNY" }
}
```

### 3.3 异步通知

微信支付通过 POST 推送通知到 `notify_url`，**请求体为 AEAD_AES_256_GCM 加密**，需用 APIv3 密钥解密。

| 解密后字段 | 说明 |
| :--- | :--- |
| `trade_state` | `SUCCESS`/`REFUND`/`NOTPAY`/`CLOSED`/`REVOKED`/`USERPAYING`/`PAYERROR` |
| `out_trade_no` | 商户订单号 |
| `transaction_id` | 微信支付订单号 |
| `amount.total` | 金额 |
| `success_time` | 支付时间 |

**响应**: 必须返回 `{ "code": "SUCCESS", "message": "成功" }`，否则重试。

**验签**: 用 `Wechatpay-Signature` 头 + 微信支付平台证书验签。

## 4. .NET SDK 示例

```csharp
using WechatPay.NET;

var client = new WechatPayClient(new WechatPayOptions
{
    MerchantId = "1900000109",
    AppId = "wx8888888888888888",
    ApiKeyV3 = "32位APIv3密钥",
    MerchantSerialNumber = "证书序列号",
    MerchantPrivateKey = "商户私钥PEM",
    PlatformCertificateManager = new InMemoryCertificateManager()
});

var response = await client.V3.Pay.Transactions.JsApiAsync(new JsApiRequest
{
    AppId = "wx8888888888888888",
    MchId = "1900000109",
    Description = "TestFinance 订单",
    OutTradeNo = "order-1001",
    NotifyUrl = "https://shop.com/notify",
    Amount = new Amount { Total = 8850, Currency = "CNY" },
    Payer = new Payer { OpenId = "oUpF8xxxx" }
});

// 退款
await client.V3.Refund.Domestic.CreateAsync(new RefundRequest
{
    OutTradeNo = "order-1001",
    OutRefundNo = "refund-1001",
    Amount = new RefundAmount { Refund = 2000, Total = 8850, Currency = "CNY" }
});
```

## 5. 最佳实践

- **金额**: 单位为分（int），与本项目 `PaymentDomain.cs` 的 `decimal` 之间需 `* 100` 转换。
- **幂等**: `out_trade_no` / `out_refund_no` 必须唯一。
- **证书管理**: 平台证书需定期轮换，SDK 提供 `CertificateManager` 自动更新。
- **通知解密**: 必须用 APIv3 密钥解密 `resource.ciphertext`，不能直接用密文。
- **敏感信息**: 商户私钥严禁入库，建议用 KMS。
