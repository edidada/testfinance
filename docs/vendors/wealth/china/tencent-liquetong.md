# 腾讯理财通对接 API 文档

> 📄 **可对接 API 文档**
> 腾讯理财通是腾讯官方出品的合规第三方基金销售平台，面向 C 端用户。机构可通过腾讯开放平台对接，走商户签约流程。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://api.wealth.tencent.com`（占位，实际以签约为准） |
| 沙箱 | 提供测试环境 |
| API 版本 | `v1` |
| 认证方式 | 开放平台签名（RSA2） + OAuth 2.0（用户授权） |
| 内容类型 | `application/json` |
| 限流 | 按商户等级约定，通常 100-500 QPS |
| C 端入口 | 微信九宫格「理财通」、QQ 钱包 |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| 获取产品列表 | GET | `/v1/products` | 查询可代销基金 | B |
| 获取产品详情 | GET | `/v1/products/{code}` | 查询基金详情 | B |
| 获取实时估值 | GET | `/v1/products/{code}/realtime` | 实时估值 | B |
| 用户授权 | GET | `/oauth/authorize` | 引导用户授权 | B+C |
| 换取 Token | POST | `/oauth/token` | 用 code 换 token | B |
| 创建交易账户 | POST | `/v1/accounts` | 为用户开户 | B |
| 查询持仓 | GET | `/v1/accounts/{id}/holdings` | 查询用户持仓 | B |
| 申购 | POST | `/v1/accounts/{id}/subscriptions` | 申购基金 | B |
| 赎回 | POST | `/v1/accounts/{id}/redemptions` | 赎回基金 | B |
| 查询订单 | GET | `/v1/accounts/{id}/orders` | 查询订单 | B |
| 风险测评 | POST | `/v1/accounts/{id}/risk-assessment` | 提交风险测评 | B+C |

## 3. 接口详情

### 3.1 用户授权 `/oauth/authorize`

引导用户访问授权页（微信内 H5）：

```
https://open.wealth.tencent.com/oauth/authorize?response_type=code&client_id=xxx&redirect_uri=https://shop.com/callback&scope=trade&state=xxx
```

用户在理财通页面完成授权后回调：

```
https://shop.com/callback?code=AUTH_CODE&state=xxx
```

### 3.2 换取 Token `/oauth/token`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `grant_type` | string | 是 | `authorization_code` | `authorization_code` |
| `code` | string | 是 | 授权码 | `AUTH_CODE` |
| `client_id` | string | 是 | 应用 ID | `wx_xxx` |
| `client_secret` | string | 是 | 应用密钥 | `xxx` |
| `redirect_uri` | string | 是 | 回调地址 | `https://shop.com/callback` |

#### 成功响应示例

```json
{
  "access_token": "AT-xxx",
  "refresh_token": "RT-xxx",
  "token_type": "Bearer",
  "expires_in": 7200,
  "scope": "trade",
  "open_id": "o-xxx"
}
```

### 3.3 申购 `/v1/accounts/{id}/subscriptions`

#### 请求头

| 头 | 说明 |
| :--- | :--- |
| `Authorization` | `Bearer AT-xxx` |
| `X-Client-Id` | 应用 ID |
| `X-Signature` | RSA2 签名 |
| `X-Timestamp` | 时间戳 |
| `X-Nonce` | 随机串（防重放） |
| `Idempotency-Key` | 幂等键（推荐） |

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `product_code` | string | 是 | 基金代码 | `000001` |
| `amount` | decimal | 是 | 申购金额 | `5000.00` |
| `payment_method` | string | 是 | 支付方式 | `WECHAT_PAY`/`BANK_CARD` |
| `payment_account` | string | 是 | 支付账户 | `wx-xxx`/`622848...` |
| `trade_password_hash` | string | 是 | 交易密码哈希 | `sha256:xxx` |
| `client_order_id` | string | 是 | 客户订单 ID（幂等） | `order-1001` |

#### 请求示例

```bash
curl -X POST https://api.wealth.tencent.com/v1/accounts/o-xxx/subscriptions \
  -H "Authorization: Bearer AT-xxx" \
  -H "X-Client-Id: wx_xxx" \
  -H "X-Signature: xxx" \
  -H "X-Timestamp: 1785425000" \
  -H "X-Nonce: abc123" \
  -H "Idempotency-Key: order-1001-uuid" \
  -H "Content-Type: application/json" \
  -d '{
    "product_code": "000001",
    "amount": 5000.00,
    "payment_method": "WECHAT_PAY",
    "payment_account": "wx-xxx",
    "trade_password_hash": "sha256:abc123",
    "client_order_id": "order-1001"
  }'
```

#### 响应字段

| 字段 | 类型 | 说明 |
| :--- | :--- | :--- |
| `order_no` | string | 理财通订单号 |
| `client_order_id` | string | 客户订单 ID |
| `status` | string | 状态 |
| `product_code` | string | 基金代码 |
| `amount` | decimal | 金额 |
| `nav_date` | string | 净值日期 |
| `confirm_date` | string | 确认日期 |

#### 成功响应示例（201 Created）

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "order_no": "LT-20260730-0001",
    "client_order_id": "order-1001",
    "status": "ACCEPTED",
    "product_code": "000001",
    "amount": 5000.00,
    "nav_date": "2026-07-30",
    "confirm_date": "2026-07-31",
    "accept_time": "2026-07-30T12:00:00Z"
  }
}
```

| `status` | 说明 |
| :--- | :--- |
| `ACCEPTED` | 已受理 |
| `CONFIRMED` | 已确认（份额到账） |
| `FAILED` | 失败 |
| `CANCELED` | 已撤销 |

#### 错误码

| `code` | 含义 | 排查建议 |
| :--- | :--- | :--- |
| 0000 | 成功 | - |
| 1001 | 参数错误 | 检查请求体 |
| 2001 | 签名错误 | 检查 RSA2 签名 |
| 3001 | Token 失效 | 刷新 token |
| 3002 | 用户未开户 | 先调用 `/v1/accounts` |
| 4001 | 风险等级不匹配 | 客户风险等级 < 产品等级 |
| 4002 | 金额低于起购 | 检查起购金额 |
| 4003 | 交易密码错误 | 重新输入 |
| 5001 | 限流 | 降低频率 |
| 9999 | 系统错误 | 重试 |

### 3.4 赎回 `/v1/accounts/{id}/redemptions`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `product_code` | string | 是 | 基金代码 | `000001` |
| `redeem_shares` | decimal | 是 | 赎回份额 | `1000.000000` |
| `large_redeem` | bool | 否 | 巨额赎回 | `false` |
| `trade_password_hash` | string | 是 | 交易密码哈希 | `sha256:xxx` |
| `client_order_id` | string | 是 | 客户订单 ID | `order-1002` |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "order_no": "LT-20260730-0002",
    "status": "ACCEPTED",
    "redeem_shares": 1000.000000,
    "nav_date": "2026-07-30",
    "confirm_date": "2026-07-31",
    "estimated_amount": 1234.50,
    "fee": 6.17
  }
}
```

### 3.5 查询持仓 `/v1/accounts/{id}/holdings`

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "account_id": "o-xxx",
    "total_market_value": 12345.67,
    "total_cost": 11000.00,
    "total_profit": 1345.67,
    "holdings": [
      {
        "product_code": "000001",
        "product_name": "华夏成长",
        "shares": 10000.000000,
        "available_shares": 10000.000000,
        "cost_price": 1.1000,
        "latest_nav": 1.2345,
        "market_value": 12345.00,
        "profit": 1345.00,
        "profit_rate": 0.122
      }
    ]
  }
}
```

### 3.6 Webhook 事件

| 事件 | 触发时机 | Payload 关键字段 |
| :--- | :--- | :--- |
| `order.confirmed` | 订单确认 | `order_no`、`shares`、`nav` |
| `order.failed` | 订单失败 | `order_no`、`reason` |
| `nav.updated` | 净值更新 | `product_code`、`nav` |
| `dividend.distributed` | 分红 | `product_code`、`dividend_per_share` |

**验签**: 用 `X-Signature` 头 + 腾讯公钥验签。

## 4. RSA2 签名规则

1. 将请求参数（除 `sign`）按 key 字典序排序
2. 拼接为 `key1=value1&key2=value2&...`
3. 用商户私钥做 RSA-SHA256 签名
4. Base64 编码

```csharp
using System.Security.Cryptography;
using System.Text;

public static string SignRsa2(Dictionary<string, string> param, string privateKeyPem)
{
    var sorted = param.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}");
    var source = string.Join("&", sorted);
    using var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);
    var bytes = rsa.SignData(Encoding.UTF8.GetBytes(source), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return Convert.ToBase64String(bytes);
}
```

## 5. .NET 接入示例

```csharp
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class TencentLiquetongClient
{
    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _privateKeyPem;

    public TencentLiquetongClient(HttpClient http, string clientId, string privateKeyPem)
    {
        _http = http;
        _clientId = clientId;
        _privateKeyPem = privateKeyPem;
    }

    public async Task<SubscribeResponse> SubscribeAsync(string accessToken, string accountId, SubscribeRequest req)
    {
        var body = JsonSerializer.Serialize(req);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");
        var sign = SignRsa2(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["timestamp"] = ts,
            ["nonce"] = nonce,
            ["body"] = body
        }, _privateKeyPem);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/accounts/{accountId}/subscriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Client-Id", _clientId);
        request.Headers.Add("X-Signature", sign);
        request.Headers.Add("X-Timestamp", ts);
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("Idempotency-Key", req.ClientOrderId + "-uuid");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SubscribeResponse>(json);
    }
}

// 使用
var client = new TencentLiquetongClient(httpClient, "wx_xxx", privateKeyPem);
var result = await client.SubscribeAsync("AT-xxx", "o-xxx", new SubscribeRequest
{
    ProductCode = "000001",
    Amount = 5000.00m,
    PaymentMethod = "WECHAT_PAY",
    PaymentAccount = "wx-xxx",
    TradePasswordHash = "sha256:abc123",
    ClientOrderId = "order-1001"
});
```

## 6. 最佳实践

- **OAuth 流程**: 用户授权必须在微信内 H5 完成，获取 `open_id` 后绑定本地用户。
- **Token 刷新**: `access_token` 有效期 2 小时，用 `refresh_token` 自动刷新。
- **适当性管理**: 申购前必须校验客户风险等级 ≥ 产品风险等级，返回 `4001` 时提示用户重新测评。
- **净值锁定**: 申购按当日收盘净值成交（`nav_date`），15:00 后下单顺延到下一交易日。
- **份额精度**: 份额保留 6 位小数，与本项目 [WealthService](../../../src/Wealth.Api/) 一致。
- **交易密码**: 必须前端哈希后上送，不能明文传输。
- **幂等**: `client_order_id` 全局唯一，重复请求返回原订单。
- **巨额赎回**: 单日赎回份额超基金规模 10% 触发巨额赎回，需延迟确认。
