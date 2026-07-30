# 恒生电子金融 IT 系统 API 详细文档

> 恒生电子是国内最大的金融软件供应商，提供 ARES 投资交易系统、TA 登记过户系统、估值系统、清算系统等。本文档以 TA 系统（基金登记过户）和投资交易系统为例，描述典型接口设计。

> ⚠️ 恒生电子系统多为**私有部署**，接口规范以项目合同附件为准，本文为通用参考设计。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 部署模式 | 私有化部署（金融机构机房或私有云） |
| 协议 | HTTP/HTTPS（Web Service）或 TCP（私有协议） |
| 数据格式 | JSON / XML / 定长报文 |
| 认证 | IP 白名单 + 商户证书 + 签名 |
| 限流 | 按合同约定，通常 100-500 QPS |
| 版本 | 按客户项目定制 |

## 2. 系统 modules

| 系统 | 用途 | 典型接口 |
| :--- | :--- | :--- |
| **TA 系统** | 基金登记过户（开户、申购、赎回、转换、分红） | TA 开户、TA 申购、TA 赎回、TA 确认查询 |
| **投资交易系统 (ARES)** | 资管机构投资股票/债券/基金 | 下单、撤单、持仓查询、资金查询 |
| **估值系统** | 基金/资管产品估值 | 净值计算、估值表导出 |
| **清算系统** | 资金清算 | 清算流水、对账文件 |
| **风控系统** | 投资合规风控 | 风控指标查询、超限告警 |

## 3. 接口详情（TA 系统示例）

### 3.1 TA 开户 `/ta/account/open`

#### 请求头

| 头 | 说明 |
| :--- | :--- |
| `X-Merchant-Id` | 商户号 |
| `X-Sign` | 签名（RSA 或 HMAC-SHA256） |
| `X-Timestamp` | 时间戳 |
| `Content-Type` | `application/json` |

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `merchant_no` | string | 是 | 商户号 | `M001` |
| `request_no` | string | 是 | 请求流水号（幂等） | `req-20260730-001` |
| `fund_code` | string | 是 | 基金代码 | `000001` |
| `customer.name` | string | 是 | 客户姓名 | `张三` |
| `customer.id_type` | string | 是 | 证件类型 | `0`（身份证） |
| `customer.id_number` | string | 是 | 证件号 | `110101199001011234` |
| `customer.mobile` | string | 是 | 手机号 | `13800000000` |
| `customer.risk_level` | string | 是 | 风险等级 | `C3` |
| `customer.bank_account` | string | 是 | 银行账户 | `6222020200112345678` |
| `customer.bank_code` | string | 是 | 银行代码 | `ICBC` |
| `customer.address` | string | 否 | 地址 | `北京市朝阳区` |

#### 请求示例

```bash
curl -X POST https://ta.internalbank.com/ta/account/open \
  -H "X-Merchant-Id: M001" \
  -H "X-Sign: xxx" \
  -H "X-Timestamp: 1785425000000" \
  -H "Content-Type: application/json" \
  -d '{
    "merchant_no": "M001",
    "request_no": "req-20260730-001",
    "fund_code": "000001",
    "customer": {
      "name": "张三",
      "id_type": "0",
      "id_number": "110101199001011234",
      "mobile": "13800000000",
      "risk_level": "C3",
      "bank_account": "6222020200112345678",
      "bank_code": "ICBC"
    }
  }'
```

#### 成功响应示例

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "ta_account": "TA-2026-000000001",
    "fund_code": "000001",
    "customer_name": "张三",
    "open_date": "2026-07-30",
    "status": "SUCCESS"
  }
}
```

#### 错误码

| `code` | 含义 |
| :--- | :--- |
| 0000 | 成功 |
| 1001 | 参数错误 |
| 2001 | 签名错误 |
| 3001 | 客户已开户 |
| 3002 | 证件号格式错误 |
| 4001 | 风险等级不匹配 |
| 5001 | 基金代码不存在 |
| 9999 | 系统错误 |

### 3.2 TA 申购 `/ta/trade/subscribe`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `merchant_no` | string | 是 | 商户号 | `M001` |
| `request_no` | string | 是 | 请求流水号 | `req-20260730-002` |
| `ta_account` | string | 是 | TA 账户 | `TA-2026-000000001` |
| `fund_code` | string | 是 | 基金代码 | `000001` |
| `amount` | decimal | 是 | 申购金额（元） | `5000.00` |
| `trade_date` | string | 是 | 交易日期 | `2026-07-30` |
| `payment_method` | string | 是 | 支付方式 | `BANK` |
| `bank_account` | string | 是 | 银行账户 | `6222020200112345678` |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "apply_serial": "APP-20260730-0001",
    "ta_account": "TA-2026-000000001",
    "fund_code": "000001",
    "amount": 5000.00,
    "trade_date": "2026-07-30",
    "status": "ACCEPTED",
    "confirm_date": "2026-07-31",
    "nav_date": "2026-07-30"
  }
}
```

> **注意**: 申购是异步流程——`ACCEPTED` 表示受理，份额需在 `confirm_date` 后查询。

### 3.3 TA 赎回 `/ta/trade/redeem`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `merchant_no` | string | 是 | - | `M001` |
| `request_no` | string | 是 | - | `req-20260730-003` |
| `ta_account` | string | 是 | - | `TA-2026-000000001` |
| `fund_code` | string | 是 | - | `000001` |
| `redeem_shares` | decimal | 是 | 赎回份额 | `1000.000000` |
| `trade_date` | string | 是 | - | `2026-07-30` |
| `large_redeem` | bool | 否 | 是否巨额赎回 | `false` |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "apply_serial": "APP-20260730-0002",
    "status": "ACCEPTED",
    "confirm_date": "2026-07-31",
    "redeem_amount": 1234.50,
    "fee": 6.17
  }
}
```

### 3.4 交易确认查询 `/ta/trade/confirm/query`

#### 查询参数

| 参数 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `ta_account` | string | 是 | TA 账户 |
| `fund_code` | string | 否 | 基金代码 |
| `start_date` | string | 是 | 起始日期 |
| `end_date` | string | 是 | 结束日期 |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "confirms": [
      {
        "apply_serial": "APP-20260730-0001",
        "trade_type": "SUBSCRIBE",
        "fund_code": "000001",
        "amount": 5000.00,
        "confirm_shares": 4050.123456,
        "nav": 1.2345,
        "confirm_date": "2026-07-31",
        "status": "CONFIRMED"
      }
    ]
  }
}
```

## 4. 投资交易系统 (ARES) 示例接口

| 接口 | 路径 | 用途 |
| :--- | :--- | :--- |
| 下单 | `/ares/order/place` | 提交股票/债券交易订单 |
| 撤单 | `/ares/order/cancel` | 撤销订单 |
| 持仓查询 | `/ares/portfolio/positions` | 查询持仓 |
| 资金查询 | `/ares/account/balance` | 查询资金余额 |
| 风控校验 | `/ares/risk/check` | 交易前风控校验 |

## 5. 对账文件

TA 系统每日日终生成对账文件，通过 SFTP 推送：

| 文件 | 内容 |
| :--- | :--- |
| `YYYYMMDD_trade.txt` | 交易明细（开户/申购/赎回/确认） |
| `YYYYMMDD_position.txt` | 持仓明细 |
| `YYYYMMDD_balance.txt` | 资金余额 |

文件格式通常为定长报文或 CSV，需按合同约定解析。

## 6. .NET 接入示例

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class HundsunTaClient
{
    private readonly HttpClient _http;
    private readonly string _merchantNo;
    private readonly string _signKey;

    public HundsunTaClient(HttpClient http, string merchantNo, string signKey)
    {
        _http = http;
        _merchantNo = merchantNo;
        _signKey = signKey;
        _http.BaseAddress = new Uri("https://ta.internalbank.com");
    }

    public async Task<OpenAccountResponse> OpenAccountAsync(OpenAccountRequest req)
    {
        req.MerchantNo = _merchantNo;
        req.RequestNo = $"req-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        var json = JsonSerializer.Serialize(req);
        var sign = HmacSha256(json, _signKey);

        var request = new HttpRequestMessage(HttpMethod.Post, "/ta/account/open");
        request.Headers.Add("X-Merchant-Id", _merchantNo);
        request.Headers.Add("X-Sign", sign);
        request.Headers.Add("X-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request);
        var respJson = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OpenAccountResponse>(respJson);
    }

    private static string HmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(bytes);
    }
}

// 使用
var client = new HundsunTaClient(httpClient, "M001", "sign-key");
var resp = await client.OpenAccountAsync(new OpenAccountRequest
{
    FundCode = "000001",
    Customer = new Customer
    {
        Name = "张三",
        IdType = "0",
        IdNumber = "110101199001011234",
        Mobile = "13800000000",
        RiskLevel = "C3",
        BankAccount = "6222020200112345678",
        BankCode = "ICBC"
    }
});
```

## 7. 最佳实践

- **幂等**: `request_no` 必须全局唯一，重复请求返回原结果。
- **异步确认**: 申购/赎回 `ACCEPTED` 后需查询 `/ta/trade/confirm/query` 获取最终份额。
- **对账**: 每日日终必须拉取对账文件，与本地系统核对，差异需人工介入。
- **适当性管理**: 申购前必须校验客户风险等级（`customer.risk_level`）与基金风险等级匹配。
- **净值锁定**: 申购按当日收盘净值（`nav_date`）成交，非交易时间下单顺延到下一交易日。
- **签名密钥**: 私有部署的 `sign_key` 严禁泄露，建议用 HSM 或 KMS 托管。
- **专线接入**: 生产环境建议专线接入 TA 系统，避免公网传输敏感数据。
