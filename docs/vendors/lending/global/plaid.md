# Plaid 银行数据聚合 API 详细文档

> Plaid 通过统一 API 聚合美国 12,000+ 金融机构数据，提供 Auth（账户验证）、Income（收入）、Liabilities（负债）等能力，用于信贷评估。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://production.plaid.com` |
| 沙箱 Base URL | `https://sandbox.plaid.com` |
| API 版本 | `2020-09-14`（通过 `Plaid-Version` 头指定） |
| 认证方式 | `client_id` + `secret`（请求体内） |
| 内容类型 | `application/json` |
| 限流 | 100 req/min（默认，可申请提升） |
| C 端组件 | Plaid Link（Drop-in，用户连接银行账号） |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| 创建 Link Token | POST | `/link/token/create` | 为前端 Plaid Link 生成 token | B |
| 交换 Public Token | POST | `/item/public_token/exchange` | 用 public_token 换 access_token | B |
| 获取账户 | POST | `/accounts/get` | 查询用户银行账户列表 | B |
| 获取交易 | POST | `/transactions/get` | 拉取历史交易 | B |
| 获取收入 | POST | `/income/get` | 收入评估 | B |
| 获取负债 | POST | `/liabilities/get` | 负债评估 | B |
| 获取身份 | POST | `/identity/get` | 身份核验 | B |
| 获取余额 | POST | `/accounts/balance/get` | 实时余额 | B |

## 3. 接口详情

### 3.1 创建 Link Token `/link/token/create`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `client_id` | string | 是 | 应用 ID | `63f4c1xxxx` |
| `secret` | string | 是 | 密钥 | `sandbox-xxxx` |
| `client_name` | string | 是 | 展示给用户的名称 | `TestFinance` |
| `language` | string | 是 | 语言 | `en` |
| `country_codes[]` | string[] | 是 | 国家码 | `["US"]` |
| `user.client_user_id` | string | 是 | 商户用户 ID | `c-1` |
| `user.legal_name` | string | 否 | 用户姓名 | `John Doe` |
| `user.email_address` | string | 否 | 邮箱 | `test@test.com` |
| `products[]` | string[] | 是 | 申请的产品 | `["auth","transactions","income"]` |
| `webhook` | string | 否 | Webhook URL | `https://shop.com/plaid-webhook` |

#### 请求示例

```bash
curl -X POST https://sandbox.plaid.com/link/token/create \
  -H "Content-Type: application/json" \
  -d '{
    "client_id": "63f4c1xxxx",
    "secret": "sandbox-xxxx",
    "client_name": "TestFinance",
    "language": "en",
    "country_codes": ["US"],
    "user": { "client_user_id": "c-1" },
    "products": ["auth", "transactions", "income"]
  }'
```

#### 响应体

| 字段 | 类型 | 说明 |
| :--- | :--- | :--- |
| `link_token` | string | 用于前端 Plaid Link |
| `expiration` | string | 过期时间 RFC3339 |
| `request_id` | string | 请求 ID |

#### 成功响应示例

```json
{
  "link_token": "link-sandbox-xxxx-yyyy",
  "expiration": "2026-07-30T13:00:00Z",
  "request_id": "abcd1234"
}
```

#### 错误码

| HTTP 状态 | 业务码 | 含义 |
| :--- | :--- | :--- |
| 400 | `INVALID_API_KEYS` | client_id/secret 错误 |
| 400 | `INVALID_PRODUCT` | 产品不支持 |
| 400 | `INVALID_COUNTRY_CODE` | 国家码错误 |
| 429 | `RATE_LIMIT_EXCEEDED` | 限流 |

### 3.2 交换 Public Token `/item/public_token/exchange`

用户在 Plaid Link 完成银行连接后，前端拿到 `public_token`，服务端用本接口换 `access_token`。

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `client_id` | string | 是 | - |
| `secret` | string | 是 | - |
| `public_token` | string | 是 | 前端返回 |

#### 成功响应示例

```json
{
  "access_token": "access-sandbox-xxxx",
  "item_id": "dMvBxxxx",
  "request_id": "abcd"
}
```

### 3.3 获取收入 `/income/get`

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `client_id` | string | 是 | - |
| `secret` | string | 是 | - |
| `access_token` | string | 是 | 用户 access_token |
| `options.count` | int | 否 | 返回交易数 |
| `options.start_date` | string | 否 | 起始日期 |

#### 成功响应示例

```json
{
  "income": {
    "income_streams": [
      { "confidence": 0.95, "monthly_income": 5000, "name": "EMPLOYER" }
    ],
    "last_year_income": 60000,
    "last_year_income_before_tax": 75000,
    "projected_yearly_income": 62000,
    "max_historical_overdraft_amount": 0
  },
  "request_id": "abcd"
}
```

## 4. Webhook 事件

| Webhook Code | 触发时机 |
| :--- | :--- |
| `HISTORICAL_UPDATE` | 历史交易同步完成 |
| `DEFAULT_UPDATE` | 新交易更新 |
| `ITEM_LOGIN_REQUIRED` | 需重新登录（凭证失效） |
| `ERROR` | 错误 |

## 5. .NET SDK 示例

```csharp
using Plaid;

var client = new PlaidClient("sandbox", "63f4c1xxxx", "sandbox-xxxx");

// 1. 创建 Link Token
var linkTokenResponse = await client.LinkTokenCreateAsync(new LinkTokenCreateRequest
{
    ClientName = "TestFinance",
    Language = "en",
    CountryCodes = new[] { "US" },
    User = new LinkTokenCreateRequestUser { ClientUserId = "c-1" },
    Products = new[] { "auth", "transactions", "income" }
});

// 2. 前端拿 link_token 唤起 Plaid Link，拿到 public_token
// 3. 交换 access_token
var exchangeResponse = await client.ItemPublicTokenExchangeAsync(new ItemPublicTokenExchangeRequest
{
    PublicToken = "public-sandbox-xxxx"
});
string accessToken = exchangeResponse.AccessToken;

// 4. 查收入
var incomeResponse = await client.IncomeGetAsync(new IncomeGetRequest
{
    AccessToken = accessToken
});
decimal yearlyIncome = incomeResponse.Income.LastYearIncome;
```

## 6. 最佳实践

- **access_token 安全**: 一个用户一个 `access_token`，需加密存储（KMS）。
- **轮询 Webhook**: 交易同步是异步的，应通过 Webhook 触发后续业务，而非轮询 `/transactions/get`。
- **重登录**: 收到 `ITEM_LOGIN_REQUIRED` 时，重新生成 Link Token 让用户重新授权。
- **沙箱**: 用 `sandbox` 环境测试，提供测试银行 `First Platypus Bank`（用户名 `user_good`，密码 `pass_good`）。
