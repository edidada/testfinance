# 神州信息核心与信贷系统接口参考

> ⚠️ **私有部署参考设计**
> 神州信息 ModelBank⁵ / Sm@RT 系列为金融机构私有部署，接口规范以合同附件为准。本文为通用参考设计。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 部署模式 | 金融机构私有云 |
| 协议 | HTTPS POST（JSON） |
| 认证 | OAuth 2.0 客户端凭证 + IP 白名单 |
| 内容类型 | `application/json` |
| 限流 | 按合同约定 |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 |
| :--- | :--- | :--- | :--- |
| 获取 Token | POST | `/oauth/token` | OAuth 客户端凭证 |
| 核心账务记账 | POST | `/v1/core/gl/post` | 总账分录 |
| 客户信息查询 | GET | `/v1/cif/customer/{id}` | 查询客户 360 |
| 信贷审批 | POST | `/v1/credit/approve` | 人工/自动审批 |
| 贷后预警 | GET | `/v1/credit/postloan/warning` | 贷后风险预警 |
| 风险加权资产 | GET | `/v1/risk/rwa` | RWA 计算 |
| 财富产品查询 | GET | `/v1/wealth/products` | 查询产品列表 |

## 3. 接口详情

### 3.1 OAuth 认证 `/oauth/token`

```bash
curl -X POST https://core.internalbank.com/oauth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=xxx" \
  -d "client_secret=xxx" \
  -d "scope=core:write credit:write"
```

#### 成功响应

```json
{
  "access_token": "eyJxxx",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "core:write credit:write"
}
```

### 3.2 信贷审批 `/v1/credit/approve`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `application_no` | string | 是 | 申请单号 | `APP-20260730-0001` |
| `decision` | string | 是 | 决策 | `APPROVE`/`REJECT`/`MANUAL_REVIEW` |
| `approved_amount` | decimal | 条件 | 批准金额（APPROVE 必填） | `80000.00` |
| `approved_term_months` | int | 条件 | 批准期限 | `12` |
| `interest_rate` | decimal | 条件 | 利率 | `0.072` |
| `reviewer_id` | string | 是 | 审批人 ID | `R001` |
| `reject_reason_code` | string | 条件 | 拒绝原因码 | `POOR_CREDIT` |
| `remark` | string | 否 | 备注 | `客户信用良好` |

#### 请求示例

```bash
curl -X POST https://core.internalbank.com/v1/credit/approve \
  -H "Authorization: Bearer eyJxxx" \
  -H "Content-Type: application/json" \
  -d '{
    "application_no": "APP-20260730-0001",
    "decision": "APPROVE",
    "approved_amount": 80000.00,
    "approved_term_months": 12,
    "interest_rate": 0.072,
    "reviewer_id": "R001",
    "remark": "客户信用良好"
  }'
```

#### 成功响应示例

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "application_no": "APP-20260730-0001",
    "final_decision": "APPROVED",
    "approved_amount": 80000.00,
    "approval_time": "2026-07-30T12:00:00Z",
    "approver": "R001",
    "audit_trail_id": "AT-001"
  }
}
```

#### 错误码

| `code` | 含义 |
| :--- | :--- |
| 0000 | 成功 |
| 1001 | 参数错误 |
| 2001 | 申请单不存在 |
| 3001 | 申请单状态不允许审批 |
| 3002 | 审批人无权限 |
| 4001 | 超审批人额度 |
| 9999 | 系统错误 |

### 3.3 贷后预警 `/v1/credit/postloan/warning`

#### 查询参数

| 参数 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `loan_no` | string | 否 | 贷款号 | `L-2026-0001` |
| `warning_level` | string | 否 | 预警等级 | `HIGH`/`MEDIUM`/`LOW` |
| `start_date` | string | 否 | 起始日期 | `2026-07-01` |
| `end_date` | string | 否 | 结束日期 | `2026-07-30` |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "warnings": [
      {
        "loan_no": "L-2026-0001",
        "customer_name": "张三",
        "warning_type": "OVERDUE_30D",
        "warning_level": "HIGH",
        "overdue_days": 35,
        "overdue_amount": 6913.79,
        "warning_time": "2026-07-30T09:00:00Z"
      }
    ],
    "total": 1
  }
}
```

### 3.4 风险加权资产 `/v1/risk/rwa`

#### 查询参数

| 参数 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `report_date` | string | 是 | 报告日期 |
| `subject` | string | 否 | 科目（如信用风险/市场风险） |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "report_date": "2026-06-30",
    "total_rwa": 12500000000.00,
    "tier1_capital": 1500000000.00,
    "capital_adequacy_ratio": 0.12,
    "breakdown": [
      { "risk_type": "CREDIT", "rwa": 10000000000.00 },
      { "risk_type": "MARKET", "rwa": 1500000000.00 },
      { "risk_type": "OPERATIONAL", "rwa": 1000000000.00 }
    ]
  }
}
```

## 4. .NET 接入示例

```csharp
using System.Net.Http.Headers;
using System.Text.Json;

public class DciClient
{
    private readonly HttpClient _http;
    private string _accessToken;
    private DateTime _tokenExpiry;

    public DciClient(HttpClient http) => _http = http;

    public async Task EnsureTokenAsync(string clientId, string clientSecret)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry) return;
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("scope", "core:write credit:write")
        });
        var resp = await _http.PostAsync("/oauth/token", content);
        var json = await resp.Content.ReadAsStringAsync();
        var token = JsonSerializer.Deserialize<TokenResponse>(json);
        _accessToken = token.AccessToken;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    }

    public async Task<ApproveResponse> ApproveAsync(ApproveRequest req)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/credit/approve");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(request);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApproveResponse>(json);
    }
}
```

## 5. 最佳实践

- **Token 缓存**: `access_token` 有效期 1 小时，需自动刷新。
- **审批权限**: 审批人额度超限时返回 `4001`，需走上一级审批。
- **审计追踪**: `audit_trail_id` 必须落库，满足监管合规审计。
- **RWA 计算**: 资本充足率是监管核心指标，必须按月报送。
