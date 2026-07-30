# 同盾科技风控 API 详细文档

> 同盾科技是国内头部第三方风控服务商，提供设备指纹、IP 风险、地址反欺诈、信用评分、多头借贷等能力。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://api.tongdun.net` |
| 沙箱 | 提供测试账号和环境 |
| 协议 | HTTPS POST（form-urlencoded） |
| 认证/签名 | `partner_key` + 签名（HMAC-SHA256 或 MD5） |
| 编码 | UTF-8 |
| 内容类型 | `application/x-www-form-urlencoded` |
| C 端组件 | 设备指纹采集 SDK（JS/iOS/Android） |

## 2. 接口列表

| 接口 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- |
| 设备指纹查询 | `/v1/risk/profile` | 查询设备指纹信息 | B |
| IP 风险评估 | `/v1/risk/ip` | 评估 IP 风险 | B |
| 地址反欺诈 | `/v1/risk/address` | 地址真实性校验 | B |
| 营销反欺诈 | `/v1/risk/anti_fraud` | 营销活动反欺诈 | B |
| 信贷反欺诈 | `/v1/risk/credit_anti_fraud` | 信贷反欺诈评分 | B |
| 信用评分 | `/v1/risk/credit_score` | 综合信用评分 | B |
| 多头借贷 | `/v1/risk/multi_loan` | 多头借贷检测 | B |
| 设备指纹上报 | `/v1/fingerprint/report` | SDK 上报设备信息 | C |

## 3. 接口详情

### 3.1 信贷反欺诈 `/v1/risk/credit_anti_fraud`

#### 请求参数（公共参数 + 业务参数）

**公共参数**：

| 参数 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- |
| `partner_key` | 是 | 合作伙伴密钥 | `xxxx` |
| `app_name` | 是 | 应用名称 | `testfinance` |
| `event_id` | 是 | 事件 ID（同盾分配） | `abc123` |
| `timestamp` | 是 | 时间戳（秒） | `1785425000` |
| `token_id` | 是 | 设备指纹 token | `xxx` |
| `sign` | 是 | 签名 | `xxx` |

**业务参数**（`event_id` 决定具体字段）：

| 参数 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `ip_address` | string | 是 | 用户 IP | `1.2.3.4` |
| `name` | string | 是 | 姓名 | `张三` |
| `id_number` | string | 是 | 身份证号 | `110101199001011234` |
| `mobile` | string | 是 | 手机号 | `13800000000` |
| `bank_card` | string | 否 | 银行卡号 | `6222020200112345678` |
| `email` | string | 否 | 邮箱 | `test@test.com` |
| `address` | string | 否 | 地址 | `北京市朝阳区` |
| `apply_amount` | string | 否 | 申请金额 | `100000` |
| `apply_period` | string | 否 | 申请期限 | `12` |

#### 签名规则

1. 将所有参数（除 `sign`）按 key 字典序排序
2. 拼接为 `key1=value1&key2=value2&...`
3. 在末尾拼接 `&partner_key=xxx`
4. 对拼接串做 MD5（或 HMAC-SHA256），得到 `sign`

#### 请求示例

```bash
curl -X POST https://api.tongdun.net/v1/risk/credit_anti_fraud \
  -d "partner_key=xxxx" \
  -d "app_name=testfinance" \
  -d "event_id=abc123" \
  -d "timestamp=1785425000" \
  -d "token_id=xxx" \
  -d "sign=xxxx" \
  -d "ip_address=1.2.3.4" \
  -d "name=%E5%BC%A0%E4%B8%89" \
  -d "id_number=110101199001011234" \
  -d "mobile=13800000000" \
  -d "apply_amount=100000" \
  -d "apply_period=12"
```

#### 成功响应示例

```json
{
  "success": true,
  "reason_id": 0,
  "reason_desc": "成功",
  "seq_id": "xxx123",
  "risk_level": "LOW",
  "risk_score": 23,
  "final_decision": "ACCEPT",
  "final_description": "建议通过",
  "policy_set_name": "默认策略集",
  "rules_hit": [
    { "rule_id": "rule-001", "rule_name": "IP 高风险", "decision": "REVIEW" }
  ]
}
```

#### 响应字段

| 字段 | 说明 |
| :--- | :--- |
| `success` | 调用是否成功 |
| `risk_level` | `LOW`/`MEDIUM`/`HIGH` |
| `risk_score` | 风险分（0-100，越高越危险） |
| `final_decision` | `ACCEPT`/`REVIEW`/`REJECT` |
| `final_description` | 决策描述 |
| `rules_hit[]` | 命中的规则列表 |
| `seq_id` | 调用流水号 |

#### 错误码

| `reason_id` | 含义 | 排查建议 |
| :--- | :--- | :--- |
| 0 | 成功 | - |
| 1 | 参数错误 | 检查请求参数 |
| 2 | 签名错误 | 检查 `sign` 计算 |
| 3 | 权限不足 | 检查 `app_name` 授权 |
| 4 | 事件 ID 错误 | 检查 `event_id` |
| 5 | 限流 | 降低频率 |
| 99 | 系统错误 | 重试 |

### 3.2 设备指纹采集（C 端 SDK）

前端引入同盾 SDK：

```html
<script src="https://cdn.tongdun.net/fm.js"></script>
<script>
  _fmOpt = {
    partner: "testfinance",
    appName: "testfinance_web",
    token: "xxx"
  };
  // SDK 自动采集设备信息，并通过 token_id 关联
</script>
```

服务端调用 `/v1/risk/profile` 传入 `token_id` 即可获取设备指纹信息（设备型号、运营商、是否模拟器、是否 root 等）。

### 3.3 多头借贷 `/v1/risk/multi_loan`

#### 业务参数

| 参数 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `mobile` | string | 是 | 手机号 |
| `id_number` | string | 是 | 身份证号 |

#### 响应示例

```json
{
  "success": true,
  "multi_loan_score": 75,
  "loan_count_30d": 3,
  "loan_count_90d": 8,
  "loan_platforms_90d": ["platformA", "platformB"],
  "risk_level": "HIGH"
}
```

## 4. .NET 接入示例（无官方 SDK，自行封装）

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Web;

public class TongdunClient
{
    private readonly HttpClient _http;
    private readonly string _partnerKey;
    private readonly string _appName;

    public TongdunClient(HttpClient http, string partnerKey, string appName)
    {
        _http = http;
        _partnerKey = partnerKey;
        _appName = appName;
    }

    public async Task<TongdunResponse> CreditAntiFraudAsync(TongdunRequest req)
    {
        var dict = new SortedDictionary<string, string>
        {
            ["partner_key"] = _partnerKey,
            ["app_name"] = _appName,
            ["event_id"] = req.EventId,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["token_id"] = req.TokenId,
            ["ip_address"] = req.IpAddress,
            ["name"] = req.Name,
            ["id_number"] = req.IdNumber,
            ["mobile"] = req.Mobile,
            ["apply_amount"] = req.ApplyAmount,
            ["apply_period"] = req.ApplyPeriod
        };

        // 计算签名
        string signSource = string.Join("&", dict.Select(kv => $"{kv.Key}={kv.Value}")) + "&partner_key=" + _partnerKey;
        dict["sign"] = Md5(signSource);

        var content = new FormUrlEncodedContent(dict);
        var resp = await _http.PostAsync("/v1/risk/credit_anti_fraud", content);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TongdunResponse>(json);
    }

    private static string Md5(string input)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}

// 使用
var client = new TongdunClient(httpClient, "xxxx", "testfinance");
var result = await client.CreditAntiFraudAsync(new TongdunRequest
{
    EventId = "abc123",
    TokenId = "xxx",
    IpAddress = "1.2.3.4",
    Name = "张三",
    IdNumber = "110101199001011234",
    Mobile = "13800000000",
    ApplyAmount = "100000",
    ApplyPeriod = "12"
});
if (result.FinalDecision == "REJECT") { /* 拒绝贷款 */ }
```

## 5. 最佳实践

- **签名缓存**: `partner_key` 严禁泄露，建议放 KMS 或配置中心。
- **设备指纹**: 前端 SDK 必须在用户操作前加载，确保 `token_id` 已生成。
- **决策联动**: `final_decision=REVIEW` 时可结合自有规则二次决策，与本项目 [LendingService](../../../src/Lending.Api/) 的 `Decide` 方法结合。
- **沙箱**: 同盾提供测试 `event_id`，可模拟各种风险等级。
