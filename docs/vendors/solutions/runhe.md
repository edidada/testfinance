# 润和软件信贷与财富系统接口参考

> ⚠️ **私有部署参考设计**
> 润和软件数字信贷与互联网金融服务系统为金融机构私有部署，接口规范以合同附件为准。本文为通用参考设计。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 部署模式 | 金融机构私有云 |
| 协议 | HTTPS POST（JSON） |
| 认证 | JWT + IP 白名单 |
| 内容类型 | `application/json` |
| 限流 | 按合同约定 |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 |
| :--- | :--- | :--- | :--- |
| 用户认证 | POST | `/auth/login` | 获取 JWT |
| 互联网信贷进件 | POST | `/v1/online-loan/apply` | 互联网贷款申请 |
| 自动审批 | POST | `/v1/online-loan/{id}/auto-decision` | 规则引擎自动决策 |
| 放款 | POST | `/v1/online-loan/{id}/disburse` | 放款 |
| 还款 | POST | `/v1/online-loan/{id}/repay` | 还款 |
| 财富产品列表 | GET | `/v1/wealth/products` | 查询理财产品 |
| 财富申购 | POST | `/v1/wealth/subscribe` | 申购 |

## 3. 接口详情

### 3.1 互联网信贷进件 `/v1/online-loan/apply`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `channel` | string | 是 | 渠道 | `APP`/`WEB`/`MINI_PROGRAM` |
| `customer.mobile` | string | 是 | 手机号 | `13800000000` |
| `customer.id_number` | string | 是 | 身份证号 | `110101199001011234` |
| `customer.name` | string | 是 | 姓名 | `张三` |
| `product_code` | string | 是 | 产品代码 | `CONSUME_001` |
| `amount` | decimal | 是 | 申请金额 | `50000.00` |
| `term_days` | int | 是 | 期限（天，互联网贷款常用天） | `90` |
| `device.fingerprint` | string | 是 | 设备指纹 | `fp-xxx` |
| `device.ip` | string | 是 | 客户端 IP | `1.2.3.4` |
| `device.user_agent` | string | 否 | UA | `Mozilla/...` |
| `location` | object | 否 | 定位 | `{"lat": 39.9, "lng": 116.4}` |

#### 请求示例

```bash
curl -X POST https://loan.internalbank.com/v1/online-loan/apply \
  -H "Authorization: Bearer eyJxxx" \
  -H "Content-Type: application/json" \
  -d '{
    "channel": "APP",
    "customer": {
      "mobile": "13800000000",
      "id_number": "110101199001011234",
      "name": "张三"
    },
    "product_code": "CONSUME_001",
    "amount": 50000.00,
    "term_days": 90,
    "device": {
      "fingerprint": "fp-xxx",
      "ip": "1.2.3.4"
    }
  }'
```

#### 成功响应示例

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "application_no": "APP-20260730-0001",
    "status": "AUTO_DECISION_PENDING",
    "risk_indicators": {
      "device_risk_score": 15,
      "ip_risk_level": "LOW",
      "multi_loan_count_30d": 2
    },
    "submit_time": "2026-07-30T12:00:00Z"
  }
}
```

#### 错误码

| `code` | 含义 |
| :--- | :--- |
| 0000 | 成功 |
| 1001 | 参数错误 |
| 2001 | 设备指纹异常 |
| 3001 | 客户黑名单 |
| 3002 | 多头借贷超限 |
| 4001 | 产品已下架 |
| 5001 | 限流 |
| 9999 | 系统错误 |

### 3.2 自动审批 `/v1/online-loan/{id}/auto-decision`

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "application_no": "APP-20260730-0001",
    "decision": "APPROVE",
    "approved_amount": 45000.00,
    "approved_term_days": 90,
    "daily_interest_rate": 0.0003,
    "repayment_method": "ONE_TIME",
    "rules_hit": [
      { "rule_id": "R001", "rule_name": "年龄 25-55", "result": "PASS" },
      { "rule_id": "R002", "rule_name": "多头借贷 < 5", "result": "PASS" }
    ],
    "model_score": 0.82,
    "model_version": "v2.3.1",
    "decision_time": "2026-07-30T12:00:05Z"
  }
}
```

### 3.3 财富产品申购 `/v1/wealth/subscribe`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `customer_id` | string | 是 | 客户 ID | `C-2026-0001` |
| `product_code` | string | 是 | 产品代码 | `WEALTH_001` |
| `amount` | decimal | 是 | 申购金额 | `10000.00` |
| `risk_level` | string | 是 | 客户风险等级 | `C3` |
| `payment_account` | string | 是 | 扣款账户 | `622848...` |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "order_no": "ORD-20260730-0001",
    "status": "ACCEPTED",
    "nav_date": "2026-07-30",
    "confirm_date": "2026-07-31",
    "amount": 10000.00
  }
}
```

## 4. .NET 接入示例

```csharp
using System.Net.Http.Headers;
using System.Text.Json;

public class RunheClient
{
    private readonly HttpClient _http;
    private string _jwt;

    public RunheClient(HttpClient http) => _http = http;

    public async Task LoginAsync(string username, string password)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password)
        });
        var resp = await _http.PostAsync("/auth/login", content);
        var json = await resp.Content.ReadAsStringAsync();
        _jwt = JsonSerializer.Deserialize<LoginResponse>(json).Token;
    }

    public async Task<ApplyResponse> ApplyAsync(LoanApplication app)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/online-loan/apply");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
        request.Content = new StringContent(JsonSerializer.Serialize(app), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(request);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApplyResponse>(json);
    }
}
```

## 5. 最佳实践

- **设备风控**: 互联网贷款必须采集设备指纹 + IP，作为反欺诈输入。
- **自动决策**: 规则引擎 + 模型双轨，命中规则直接拒绝，未命中走模型评分。
- **期限单位**: 互联网贷款常用"天"而非"月"，与传统信贷不同。
- **适当性**: 财富申购必须校验客户风险等级 ≥ 产品风险等级。
- **净值锁定**: 申购按当日收盘净值成交，非交易时间顺延。
