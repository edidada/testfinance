# 朴道征信 API 详细文档

> 朴道征信是国内第二家持牌个人征信机构，提供信用评分、身份核验、反欺诈、多头借贷等合规征信服务。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | 通过专线接入（持牌机构）或 `https://api.pudaosc.com` |
| 沙箱 | 提供测试环境 |
| 协议 | HTTPS POST（JSON） |
| 认证/签名 | API Key + RSA 签名 |
| 内容类型 | `application/json` |
| 接入门槛 | 需持牌金融机构或经授权的放贷机构 |
| C 端组件 | 用户授权页（H5 托管） |

## 2. 接口列表

| 接口 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- |
| 用户授权 | `/v1/auth/authorize` | 生成用户授权链接 | B+C |
| 信用评分 | `/v1/score/credit` | 综合信用评分 | B |
| 身份核验 | `/v1/verify/identity` | 三要素/四要素核验 | B |
| 反欺诈查询 | `/v1/risk/fraud` | 反欺诈评分 | B |
| 多头借贷 | `/v1/risk/multi_loan` | 多头借贷检测 | B |
| 信用报告 | `/v1/report/credit` | 详细信用报告 | B |
| 授权状态查询 | `/v1/auth/status` | 查询授权状态 | B |

## 3. 接口详情

### 3.1 用户授权 `/v1/auth/authorize`

> 征信查询必须先获得用户授权（合规要求）。

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `app_id` | string | 是 | 应用 ID | `pd_xxx` |
| `timestamp` | long | 是 | 时间戳（毫秒） | `1785425000000` |
| `sign` | string | 是 | RSA 签名 | `xxx` |
| `user_id` | string | 是 | 商户用户 ID | `c-1` |
| `name` | string | 是 | 姓名 | `张三` |
| `id_number` | string | 是 | 身份证号 | `110101199001011234` |
| `mobile` | string | 是 | 手机号 | `13800000000` |
| `callback_url` | string | 是 | 授权完成回调 | `https://shop.com/auth/callback` |
| `query_types[]` | string[] | 是 | 授权查询类型 | `["credit_score", "multi_loan"]` |

#### 成功响应示例

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "auth_url": "https://api.pudaosc.com/auth/page?token=xxx",
    "auth_token": "auth-xxx",
    "expire_time": "2026-07-30T13:00:00Z"
  }
}
```

用户在 `auth_url` 页面完成人脸识别 + 协议签署后，会回调 `callback_url`。

### 3.2 信用评分 `/v1/score/credit`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `app_id` | string | 是 | - | `pd_xxx` |
| `timestamp` | long | 是 | - | `1785425000000` |
| `sign` | string | 是 | - | `xxx` |
| `auth_token` | string | 是 | 用户授权 token | `auth-xxx` |
| `name` | string | 是 | - | `张三` |
| `id_number` | string | 是 | - | `110101199001011234` |
| `mobile` | string | 是 | - | `13800000000` |

#### 请求示例

```bash
curl -X POST https://api.pudaosc.com/v1/score/credit \
  -H "Content-Type: application/json" \
  -d '{
    "app_id": "pd_xxx",
    "timestamp": 1785425000000,
    "sign": "xxx",
    "auth_token": "auth-xxx",
    "name": "张三",
    "id_number": "110101199001011234",
    "mobile": "13800000000"
  }'
```

#### 成功响应示例

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "score": 720,
    "grade": "B",
    "score_range": "300-950",
    "default_probability": 0.0123,
    "query_time": "2026-07-30T12:00:00Z",
    "score_factors": [
      { "factor": "还款历史", "impact": "positive", "description": "近 12 个月无逾期" },
      { "factor": "负债水平", "impact": "negative", "description": "当前多头借贷数量较多" }
    ]
  }
}
```

#### 响应字段

| 字段 | 说明 |
| :--- | :--- |
| `score` | 信用分（300-950） |
| `grade` | 等级：A+/A/B/C/D |
| `default_probability` | 违约概率（0-1） |
| `score_factors[]` | 评分因子（可解释性） |

#### 错误码

| `code` | 含义 | 排查建议 |
| :--- | :--- | :--- |
| 0000 | 成功 | - |
| 1001 | 参数错误 | 检查请求体 |
| 2001 | 签名错误 | 检查 RSA 签名 |
| 3001 | 授权 token 无效 | 重新走授权流程 |
| 3002 | 授权已过期 | 重新授权 |
| 4001 | 用户不存在 | 征信库无此用户 |
| 5001 | 限流 | 降低频率 |
| 9999 | 系统错误 | 重试 |

### 3.3 多头借贷 `/v1/risk/multi_loan`

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `auth_token` | string | 是 | - |
| `name` | string | 是 | - |
| `id_number` | string | 是 | - |
| `mobile` | string | 是 | - |
| `loan_type` | string | 否 | `consumer`/`mortgage`/`auto` |

#### 响应示例

```json
{
  "code": "0000",
  "data": {
    "multi_loan_count_30d": 2,
    "multi_loan_count_90d": 5,
    "multi_loan_count_180d": 9,
    "loan_platforms_90d": 5,
    "overdue_count_180d": 1,
    "risk_level": "MEDIUM"
  }
}
```

## 4. RSA 签名规则

1. 将请求参数（除 `sign`）按 key 字典序排序
2. 拼接为 `key1=value1&key2=value2&...`
3. 用商户私钥对拼接串做 RSA-SHA256 签名
4. Base64 编码得到 `sign`

```csharp
using System.Security.Cryptography;
using System.Text;

public string Sign(Dictionary<string, string> param, string privateKeyPem)
{
    var sorted = param.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}");
    string source = string.Join("&", sorted);
    using var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);
    var bytes = rsa.SignData(Encoding.UTF8.GetBytes(source), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return Convert.ToBase64String(bytes);
}
```

## 5. .NET 接入示例

```csharp
using System.Security.Cryptography;
using System.Text.Json;

public class PudaoClient
{
    private readonly HttpClient _http;
    private readonly string _appId;
    private readonly string _privateKeyPem;

    public PudaoClient(HttpClient http, string appId, string privateKeyPem)
    {
        _http = http;
        _appId = appId;
        _privateKeyPem = privateKeyPem;
    }

    public async Task<CreditScoreResponse> GetCreditScoreAsync(string authToken, string name, string idNumber, string mobile)
    {
        var param = new Dictionary<string, string>
        {
            ["app_id"] = _appId,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            ["auth_token"] = authToken,
            ["name"] = name,
            ["id_number"] = idNumber,
            ["mobile"] = mobile
        };
        param["sign"] = Sign(param, _privateKeyPem);

        var content = new StringContent(JsonSerializer.Serialize(param), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/v1/score/credit", content);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<CreditScoreResponse>(json);
    }
}
```

## 6. 最佳实践

- **授权合规**: 必须先调用 `/v1/auth/authorize` 获取用户授权，不能跳过。
- **授权 token 缓存**: `auth_token` 有有效期（通常 30 天），可缓存复用，避免重复授权。
- **签名密钥**: 私钥严禁泄露，建议用 KMS 托管。
- **可解释性**: `score_factors` 字段提供了评分因子，可用于向用户解释拒贷原因（满足监管公平性要求）。
- **专线接入**: 生产环境建议用专线，避免公网传输敏感数据。
- **与本项目集成**: 将 `score` 与 [LendingService.Score](../../../src/Lending.Api/) 结合，作为决策引擎的输入特征之一。
