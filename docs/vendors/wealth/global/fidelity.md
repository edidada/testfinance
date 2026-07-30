# Fidelity Brokerage API 详细文档

> Fidelity 开发者网络提供 Brokerage API，支持账户管理、行情查询、下单交易等，采用 OAuth 2.0 认证。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://api.fidelity.com` |
| 沙箱 | `https://api.stage.fidelity.com` |
| API 版本 | `v1` |
| 认证方式 | OAuth 2.0（Authorization Code Flow） |
| 内容类型 | `application/json` |
| 限流 | 120 req/min（默认） |
| C 端组件 | Fidelity.com 交易平台、Learning Center |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| 获取 Token | POST | `/oauth/token` | OAuth 换 token | B |
| 刷新 Token | POST | `/oauth/token` | 刷新 access_token | B |
| 账户列表 | GET | `/v1/accounts` | 获取账户 | B |
| 账户余额 | GET | `/v1/accounts/{accountId}/balances` | 查询余额 | B |
| 账户持仓 | GET | `/v1/accounts/{accountId}/positions` | 查询持仓 | B |
| 下单 | POST | `/v1/accounts/{accountId}/orders` | 提交订单 | B |
| 撤单 | DELETE | `/v1/accounts/{accountId}/orders/{orderId}` | 撤销订单 | B |
| 查询订单 | GET | `/v1/accounts/{accountId}/orders` | 查询订单 | B |
| 实时报价 | GET | `/v1/marketdata/quotes` | 获取报价 | B |
| 历史行情 | GET | `/v1/marketdata/history` | 历史 K 线 | B |
| 证券搜索 | GET | `/v1/symbols/search` | 搜索证券 | B |

## 3. 接口详情

### 3.1 OAuth 认证 `/oauth/token`

#### 获取 Access Token（Authorization Code Flow）

1. 引导用户访问：
   ```
   https://api.fidelity.com/oauth/authorize?response_type=code&client_id=xxx&redirect_uri=https://shop.com/callback&scope=accounts:read,orders:place
   ```

2. 用户登录后回调带 `code`，换取 token：

```bash
curl -X POST https://api.fidelity.com/oauth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=authorization_code" \
  -d "code=xxx" \
  -d "client_id=xxx" \
  -d "client_secret=xxx" \
  -d "redirect_uri=https://shop.com/callback"
```

#### 成功响应示例

```json
{
  "access_token": "eyJxxx",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "rt-xxx",
  "scope": "accounts:read orders:place"
}
```

### 3.2 下单 `/v1/accounts/{accountId}/orders`

#### 请求头

| 头 | 说明 |
| :--- | :--- |
| `Authorization` | `Bearer eyJxxx` |
| `Content-Type` | `application/json` |
| `Idempotency-Key` | UUID（推荐） |

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `symbol` | string | 是 | 证券代码 | `AAPL` |
| `side` | string | 是 | 方向 | `BUY`/`SELL` |
| `quantity` | decimal | 是 | 数量 | `100` |
| `orderType` | string | 是 | 类型 | `MARKET`/`LIMIT`/`STOP`/`STOP_LIMIT` |
| `limitPrice` | decimal | 条件 | 限价 | `150.25` |
| `stopPrice` | decimal | 条件 | 止损价 | `145.00` |
| `timeInForce` | string | 是 | 有效期 | `DAY`/`GTC`/`IOC`/`GTD` |
| `expireDate` | string | 条件 | GTD 过期日 | `2026-12-31` |
| `clientOrderId` | string | 否 | 客户订单 ID（幂等） | `order-1001` |

#### 请求示例

```bash
curl -X POST https://api.fidelity.com/v1/accounts/ABC123/orders \
  -H "Authorization: Bearer eyJxxx" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: order-1001-uuid" \
  -d '{
    "symbol": "AAPL",
    "side": "BUY",
    "quantity": 100,
    "orderType": "LIMIT",
    "limitPrice": 150.25,
    "timeInForce": "DAY",
    "clientOrderId": "order-1001"
  }'
```

#### 响应字段

| 字段 | 说明 |
| :--- | :--- |
| `orderId` | 订单 ID |
| `status` | 状态 |
| `symbol` | 证券代码 |
| `side` | 方向 |
| `quantity` | 数量 |
| `orderType` | 类型 |
| `limitPrice` | 限价 |
| `createdTime` | 创建时间 |

#### 成功响应示例（201 Created）

```json
{
  "orderId": "ord-123456",
  "status": "PENDING",
  "symbol": "AAPL",
  "side": "BUY",
  "quantity": 100,
  "orderType": "LIMIT",
  "limitPrice": 150.25,
  "timeInForce": "DAY",
  "createdTime": "2026-07-30T12:00:00Z"
}
```

#### 错误码

| HTTP 状态 | 业务码 | 含义 |
| :--- | :--- | :--- |
| 400 | `INVALID_SYMBOL` | 证券代码无效 |
| 400 | `INVALID_QUANTITY` | 数量无效（如小数） |
| 401 | `INVALID_TOKEN` | Token 失效 |
| 403 | `INSUFFICIENT_PERMISSIONS` | 权限不足 |
| 403 | `INSUFFICIENT_FUNDS` | 资金不足 |
| 409 | `DUPLICATE_ORDER` | 重复订单（Idempotency-Key 命中） |
| 429 | `RATE_LIMIT_EXCEEDED` | 限流 |

### 3.3 账户持仓 `/v1/accounts/{accountId}/positions`

#### 成功响应示例

```json
{
  "positions": [
    {
      "symbol": "AAPL",
      "quantity": 100,
      "marketPrice": 150.25,
      "marketValue": 15025.00,
      "averageCost": 148.00,
      "unrealizedGainLoss": 225.00,
      "assetType": "EQUITY"
    }
  ]
}
```

### 3.4 实时报价 `/v1/marketdata/quotes`

#### 查询参数

| 参数 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- |
| `symbols` | 是 | 证券代码（逗号分隔） | `AAPL,MSFT` |

#### 成功响应示例

```json
{
  "quotes": [
    {
      "symbol": "AAPL",
      "lastPrice": 150.25,
      "bidPrice": 150.24,
      "askPrice": 150.26,
      "volume": 1000000,
      "change": 1.25,
      "changePercent": 0.0084,
      "timestamp": "2026-07-30T12:00:00Z"
    }
  ]
}
```

## 4. .NET 接入示例

```csharp
using System.Net.Http.Headers;
using System.Text.Json;

public class FidelityClient
{
    private readonly HttpClient _http;
    private const string BaseUrl = "https://api.fidelity.com";

    public FidelityClient(HttpClient http, string accessToken)
    {
        _http = http;
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<OrderResponse> PlaceOrderAsync(string accountId, OrderRequest order, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/accounts/{accountId}/orders");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = new StringContent(JsonSerializer.Serialize(order), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(request);
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new Exception($"Fidelity API error: {json}");
        return JsonSerializer.Deserialize<OrderResponse>(json);
    }
}

// 使用
var client = new FidelityClient(httpClient, "eyJxxx");
var order = new OrderRequest
{
    Symbol = "AAPL",
    Side = "BUY",
    Quantity = 100,
    OrderType = "LIMIT",
    LimitPrice = 150.25m,
    TimeInForce = "DAY",
    ClientOrderId = "order-1001"
};
var result = await client.PlaceOrderAsync("ABC123", order, "order-1001-uuid");
```

## 5. 最佳实践

- **Token 刷新**: `access_token` 有效期 1 小时，需用 `refresh_token` 自动刷新。
- **幂等**: `Idempotency-Key` 或 `clientOrderId` 二选一，防止重复下单。
- **Scope**: 申请 OAuth 时按需申请 scope，最小权限原则。
- **市场数据订阅**: 实时报价需单独订阅 Market Data 权限。
- **沙箱**: 用 `api.stage.fidelity.com` 测试，避免真实资金风险。
