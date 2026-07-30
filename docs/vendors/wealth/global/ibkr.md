# Interactive Brokers (IBKR) API 详细文档

> IBKR 是全球领先的 API 驱动券商，支持多市场、多资产交易。提供 REST API、TWS Socket API、Client Portal Web API。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://api.ibkr.com`（Client Portal API） |
| 沙箱 | `https://api.ibkr.com`（用 Paper Trading 账户） |
| 认证方式 | Session（通过 `POST /v1/api/iserver/authenticate` 建立）或 OAuth |
| 内容类型 | `application/json` |
| 限流 | 每秒 50 请求（默认） |
| C 端组件 | TWS 桌面、Client Portal Web、IBKR Mobile |

## 2. 接口列表（Client Portal API）

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| 认证 | POST | `/v1/api/iserver/authenticate` | 建立会话 | B |
| 重新认证 | POST | `/v1/api/iserver/reauthenticate` | 刷新会话 | B |
| 账户列表 | GET | `/v1/api/portfolio/accounts` | 获取账户 | B |
| 账户持仓 | GET | `/v1/api/portfolio/{accountId}/positions` | 查询持仓 | B |
| 账户余额 | GET | `/v1/api/portfolio/{accountId}/summary` | 查询资产汇总 | B |
| 下单 | POST | `/v1/api/iserver/account/{accountId}/orders` | 提交订单 | B |
| 撤单 | DELETE | `/v1/api/iserver/account/{accountId}/order/{orderId}` | 撤销订单 | B |
| 查询订单 | GET | `/v1/api/iserver/account/{accountId}/orders` | 查询订单状态 | B |
| 历史行情 | GET | `/v1/api/hmds/history` | 拉取历史 K 线 | B |
| 实时报价 | GET | `/v1/api/iserver/marketdata/quote` | 获取实时报价 | B |
| 证券搜索 | GET | `/v1/api/iserver/secdef/search` | 搜索证券 | B |

## 3. 接口详情

### 3.1 下单 `/v1/api/iserver/account/{accountId}/orders`

#### 请求体（JSON 数组）

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `orders[].conid` | int | 是 | 合约 ID | `265598`（AAPL） |
| `orders[].side` | string | 是 | 方向 | `BUY`/`SELL` |
| `orders[].quantity` | decimal | 是 | 数量 | `100` |
| `orders[].orderType` | string | 是 | 类型 | `MKT`/`LMT`/`STP`/`STOP_LIMIT` |
| `orders[].price` | decimal | 条件 | 限价单必填 | `150.25` |
| `orders[].auxPrice` | decimal | 条件 | 止损价 | `145.00` |
| `orders[].tif` | string | 是 | 有效期 | `DAY`/`GTC`/`IOC`/`GTD` |
| `orders[].outsideRTH` | bool | 否 | 是否盘外交易 | `false` |
| `orders[].coid` | string | 否 | 客户订单 ID（幂等） | `order-1001` |

#### 请求示例

```bash
curl -X POST https://api.ibkr.com/v1/api/iserver/account/DU1234567/orders \
  -H "Content-Type: application/json" \
  -H "Cookie: api=xxx" \
  -d '{
    "orders": [{
      "conid": 265598,
      "side": "BUY",
      "quantity": 100,
      "orderType": "LMT",
      "price": 150.25,
      "tif": "DAY",
      "outsideRTH": false,
      "coid": "order-1001"
    }]
  }'
```

#### 响应字段

| 字段 | 说明 |
| :--- | :--- |
| `order_id` | 订单 ID |
| `order_status` | 状态 |
| `local_order_id` | 本地订单 ID |
| `order_request` | 回显请求 |
| `message` | 消息列表 |

#### 成功响应示例

```json
{
  "order_id": 1234567890,
  "local_order_id": "order-1001",
  "order_status": "PreSubmitted",
  "message": ["order created"]
}
```

> **注意**: IBKR 下单分两步——首次请求返回"预览"，需再次发同样请求确认。可通过 `orders[].coid` 实现幂等。

#### 错误码

| HTTP 状态 | 业务码 | 含义 |
| :--- | :--- | :--- |
| 400 | `INVALID_CONID` | 合约 ID 无效 |
| 400 | `INVALID_QUANTITY` | 数量无效 |
| 401 | `NOT_AUTHENTICATED` | 未认证 |
| 403 | `NO_PERMISSION` | 无账户权限 |
| 429 | `RATE_LIMIT` | 限流 |
| 500 | `SYSTEM_ERROR` | 系统错误 |

### 3.2 账户持仓 `/v1/api/portfolio/{accountId}/positions`

#### 查询参数

| 参数 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `page` | int | 否 | 页码，默认 0 |
| `size` | int | 否 | 每页数量 |

#### 成功响应示例

```json
[
  {
    "conid": 265598,
    "ticker": "AAPL",
    "position": 100,
    "mktPrice": 150.25,
    "mktValue": 15025.00,
    "avgPrice": 148.00,
    "unrealizedPnl": 225.00,
    "assetClass": "STK"
  }
]
```

### 3.3 历史行情 `/v1/api/hmds/history`

#### 查询参数

| 参数 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `conid` | int | 是 | 合约 ID | `265598` |
| `period` | string | 是 | 周期 | `1d`/`1w`/`1m`/`1y` |
| `bar` | string | 是 | K 线周期 | `1min`/`5min`/`1h`/`1day` |
| `outsideRth` | bool | 否 | 是否含盘外 | `false` |

#### 成功响应示例

```json
{
  "data": [
    { "t": 1785424800000, "o": 150.0, "c": 150.25, "h": 150.5, "l": 149.8, "v": 1000000 }
  ],
  "points": 1
}
```

## 4. 认证流程

IBKR Client Portal API 需要先建立 Session：

```bash
# 1. 启动 Client Portal Gateway（本地 Java 进程）
# 2. 用户在浏览器登录 IBKR 账户
# 3. 调用认证接口建立 API Session
curl -X POST https://api.ibkr.com/v1/api/iserver/authenticate \
  -d '{
    "partner": true,
    "ibsso": true
  }'
```

或使用 OAuth 2.0（需申请）。

## 5. .NET 接入示例

```csharp
using System.Net.Http;
using System.Text.Json;

public class IbkrClient
{
    private readonly HttpClient _http;
    private const string BaseUrl = "https://api.ibkr.com/v1/api";

    public IbkrClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri(BaseUrl);
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string accountId, OrderRequest order)
    {
        var payload = JsonSerializer.Serialize(new { orders = new[] { order } });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"/iserver/account/{accountId}/orders", content);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<PlaceOrderResponse>(json);
    }

    public async Task<List<Position>> GetPositionsAsync(string accountId)
    {
        var resp = await _http.GetAsync($"/portfolio/{accountId}/positions");
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<Position>>(json);
    }
}

// 使用
var client = new IbkrClient(httpClient);
var order = new OrderRequest
{
    Conid = 265598,
    Side = "BUY",
    Quantity = 100,
    OrderType = "LMT",
    Price = 150.25m,
    Tif = "DAY",
    Coid = "order-1001"
};
var result = await client.PlaceOrderAsync("DU1234567", order);
```

## 6. 最佳实践

- **会话管理**: Session 有效期约 24 小时，需定期调用 `/iserver/reauthenticate`。
- **两步下单**: 首次请求是预览，需确认 `order_status` 后再提交确认（或直接传确认参数）。
- **幂等**: `coid` 客户订单 ID 必须唯一，可用于防止重复下单。
- **实时行情**: 高频场景建议用 TWS Socket API（WebSocket），而非 REST 轮询。
- **Paper Trading**: 用 `DU` 开头的测试账户验证流程，避免真实资金风险。
