# 高伟达核心与数字管理系统接口参考

> ⚠️ **私有部署参考设计**
> 高伟达核心业务系统与数字管理平台为金融机构私有部署，接口规范以合同附件为准。本文为通用参考设计。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 部署模式 | 金融机构私有云 |
| 协议 | HTTPS POST（JSON） |
| 认证 | AppKey + AppSecret + HMAC-SHA256 签名 |
| 内容类型 | `application/json` |
| 限流 | 按合同约定 |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 |
| :--- | :--- | :--- | :--- |
| 核心账务记账 | POST | `/api/v1/gl/post` | 总账分录记账 |
| 账户余额查询 | GET | `/api/v1/account/{accountNo}/balance` | 查询余额 |
| 监管报表生成 | POST | `/api/v1/regulatory/report` | 生成 EAST/1104 报表 |
| 监管报表下载 | GET | `/api/v1/regulatory/report/{reportId}/download` | 下载报表文件 |
| 管理驾驶舱 | GET | `/api/v1/dashboard/kpi` | 关键指标查询 |
| 数据中台查询 | POST | `/api/v1/data/query` | 通用数据查询 |

## 3. 接口详情

### 3.1 监管报表生成 `/api/v1/regulatory/report`

#### 请求头

| 头 | 说明 |
| :--- | :--- |
| `X-App-Key` | 应用 Key |
| `X-Timestamp` | 时间戳 |
| `X-Signature` | HMAC-SHA256 签名 |
| `X-Request-Id` | 请求 ID（幂等） |

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `report_type` | string | 是 | 报表类型 | `EAST`/`1104`/`PBC` |
| `report_code` | string | 是 | 报表编码 | `EAST_001` |
| `period` | string | 是 | 报送期次 | `2026-06` |
| `format` | string | 否 | 输出格式 | `CSV`/`XML`/`XLSX`（默认 CSV） |
| `async` | bool | 否 | 是否异步生成 | `true`（大数据量必须异步） |

#### 请求示例

```bash
curl -X POST https://core.internalbank.com/api/v1/regulatory/report \
  -H "X-App-Key: AK-001" \
  -H "X-Timestamp: 1785425000000" \
  -H "X-Signature: xxx" \
  -H "X-Request-Id: req-001" \
  -H "Content-Type: application/json" \
  -d '{
    "report_type": "EAST",
    "report_code": "EAST_001",
    "period": "2026-06",
    "format": "CSV",
    "async": true
  }'
```

#### 成功响应示例

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "report_id": "RPT-20260730-0001",
    "status": "PROCESSING",
    "estimated_time": 300,
    "callback_url": "https://core.internalbank.com/api/v1/regulatory/report/RPT-20260730-0001/status"
  }
}
```

| `status` | 说明 |
| :--- | :--- |
| `PROCESSING` | 生成中 |
| `SUCCESS` | 生成成功 |
| `FAILED` | 生成失败 |

#### 错误码

| `code` | 含义 |
| :--- | :--- |
| 0000 | 成功 |
| 1001 | 参数错误 |
| 2001 | 报表类型不支持 |
| 3001 | 报送期次数据未就绪 |
| 4001 | 报表已生成（幂等返回原 ID） |
| 9999 | 系统错误 |

### 3.2 管理驾驶舱 `/api/v1/dashboard/kpi`

#### 查询参数

| 参数 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `kpi_codes` | string | 是 | 指标代码（逗号分隔） | `DEPOSIT_BALANCE,LOAN_BALANCE,NIM` |
| `period` | string | 是 | 期次 | `2026-06` |
| `branch_code` | string | 否 | 网点号 | `0001` |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "period": "2026-06",
    "kpis": [
      {
        "code": "DEPOSIT_BALANCE",
        "name": "存款余额",
        "value": 125000000000.00,
        "unit": "CNY",
        "yoy_growth": 0.085,
        "mom_growth": 0.012
      },
      {
        "code": "LOAN_BALANCE",
        "name": "贷款余额",
        "value": 98000000000.00,
        "unit": "CNY",
        "yoy_growth": 0.102,
        "mom_growth": 0.015
      },
      {
        "code": "NIM",
        "name": "净息差",
        "value": 0.0235,
        "unit": "RATIO",
        "yoy_growth": -0.001,
        "mom_growth": 0
      }
    ]
  }
}
```

### 3.3 核心账务记账 `/api/v1/gl/post`

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `entries[]` | array | 是 | 分录（借贷平衡） |
| `entries[].subject` | string | 是 | 科目号 |
| `entries[].direction` | string | 是 | `DEBIT`/`CREDIT` |
| `entries[].amount` | decimal | 是 | 金额 |
| `abstract` | string | 是 | 摘要 |
| `operator` | string | 是 | 操作员 |

#### 签名规则

1. 拼接 `app_key + timestamp + request_body + app_secret`
2. HMAC-SHA256 计算签名
3. Base64 编码

```csharp
using System.Security.Cryptography;
using System.Text;

private static string Sign(string appKey, long timestamp, string body, string appSecret)
{
    var source = $"{appKey}{timestamp}{body}{appSecret}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
    var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(source));
    return Convert.ToBase64String(bytes);
}
```

## 4. .NET 接入示例

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class GaoweidaClient
{
    private readonly HttpClient _http;
    private readonly string _appKey;
    private readonly string _appSecret;

    public GaoweidaClient(HttpClient http, string appKey, string appSecret)
    {
        _http = http;
        _appKey = appKey;
        _appSecret = appSecret;
    }

    public async Task<ReportResponse> GenerateReportAsync(ReportRequest req)
    {
        var body = JsonSerializer.Serialize(req);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sign = Sign(_appKey, ts, body, _appSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/regulatory/report");
        request.Headers.Add("X-App-Key", _appKey);
        request.Headers.Add("X-Timestamp", ts.ToString());
        request.Headers.Add("X-Signature", sign);
        request.Headers.Add("X-Request-Id", $"req-{Guid.NewGuid():N}");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ReportResponse>(json);
    }

    private static string Sign(string appKey, long ts, string body, string secret)
    {
        var source = $"{appKey}{ts}{body}{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(source)));
    }
}
```

## 5. 最佳实践

- **异步报表**: EAST/1104 报表数据量大，必须用 `async=true`，通过回调或轮询获取结果。
- **幂等**: `X-Request-Id` 全局唯一，重复请求返回原 `report_id`。
- **数据时效**: 监管报表数据需 T+1 日生成，不能在当日报送期次。
- **签名密钥**: `app_secret` 严禁泄露，建议放 KMS。
- **报表格式**: EAST 报送为 CSV，1104 报送为 XLSX，需按监管规范生成。
