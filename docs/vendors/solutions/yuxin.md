# 宇信科技信贷与支付系统接口参考

> ⚠️ **私有部署参考设计**
> 宇信科技系统为金融机构私有部署，接口规范以项目合同附件为准。本文基于行业惯例提供通用参考设计，实际接入请以合同为准。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 部署模式 | 金融机构私有云/机房 |
| 协议 | HTTPS POST（JSON） |
| 认证 | IP 白名单 + 商户证书 + RSA 签名 |
| 内容类型 | `application/json` |
| 限流 | 按合同约定，通常 200-500 QPS |
| 版本 | 按客户项目定制 |

## 2. 信贷系统接口

### 2.1 信贷进件 `/credit/application/submit`

#### 请求头

| 头 | 说明 |
| :--- | :--- |
| `X-Merchant-Id` | 商户号 |
| `X-Sign` | RSA-SHA256 签名 |
| `X-Timestamp` | 时间戳（毫秒） |
| `X-Request-No` | 请求流水号（幂等） |

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `product_code` | string | 是 | 贷款产品代码 | `CONSUMER_001` |
| `customer.name` | string | 是 | 姓名 | `张三` |
| `customer.id_type` | string | 是 | 证件类型 `0`=身份证 | `0` |
| `customer.id_number` | string | 是 | 证件号 | `110101199001011234` |
| `customer.mobile` | string | 是 | 手机号 | `13800000000` |
| `application.amount` | decimal | 是 | 申请金额 | `100000.00` |
| `application.term_months` | int | 是 | 期限（月） | `12` |
| `application.purpose` | string | 是 | 贷款用途 | `消费` |
| `application.monthly_income` | decimal | 否 | 月收入 | `20000.00` |
| `application.monthly_debt` | decimal | 否 | 月负债 | `5000.00` |
| `collateral.type` | string | 否 | 抵押类型 | `NONE`/`REAL_ESTATE` |
| `joint_loan.partner_id` | string | 否 | 联合贷合作方 | `partner-001` |

#### 请求示例

```bash
curl -X POST https://credit.internalbank.com/credit/application/submit \
  -H "X-Merchant-Id: M001" \
  -H "X-Sign: xxx" \
  -H "X-Timestamp: 1785425000000" \
  -H "X-Request-No: req-20260730-001" \
  -H "Content-Type: application/json" \
  -d '{
    "product_code": "CONSUMER_001",
    "customer": {
      "name": "张三",
      "id_type": "0",
      "id_number": "110101199001011234",
      "mobile": "13800000000"
    },
    "application": {
      "amount": 100000.00,
      "term_months": 12,
      "purpose": "消费",
      "monthly_income": 20000.00,
      "monthly_debt": 5000.00
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
    "customer_id": "C-2026-0001",
    "status": "SUBMITTED",
    "submit_time": "2026-07-30T12:00:00Z",
    "next_step": "AUTO_DECISION"
  }
}
```

#### 错误码

| `code` | 含义 |
| :--- | :--- |
| 0000 | 成功 |
| 1001 | 参数错误 |
| 2001 | 签名错误 |
| 3001 | 客户已存在未结清贷款 |
| 3002 | 证件号黑名单 |
| 4001 | 产品已下架 |
| 4002 | 金额超产品上限 |
| 5001 | 限流 |
| 9999 | 系统错误 |

### 2.2 审批结果查询 `/credit/application/{applicationNo}/decision`

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "application_no": "APP-20260730-0001",
    "status": "APPROVED",
    "approved_amount": 80000.00,
    "approved_term_months": 12,
    "interest_rate": 0.072,
    "repayment_method": "EQUAL_INSTALLMENT",
    "risk_score": 72,
    "decision_time": "2026-07-30T12:01:00Z",
    "reject_reason": null
  }
}
```

| `status` | 说明 |
| :--- | :--- |
| `SUBMITTED` | 已提交 |
| `UNDER_REVIEW` | 审核中 |
| `APPROVED` | 已批准 |
| `REJECTED` | 已拒绝 |
| `CANCELED` | 已撤销 |

### 2.3 放款 `/credit/loan/{applicationNo}/disburse`

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `disburse_amount` | decimal | 是 | 放款金额 |
| `account_number` | string | 是 | 收款账户 |
| `account_bank` | string | 是 | 收款银行代码 |
| `entrusted_payment` | bool | 否 | 是否受托支付 |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "loan_no": "L-2026-0001",
    "disburse_status": "PROCESSING",
    "schedule": [
      { "period": 1, "due_date": "2026-08-30", "principal": 6433.79, "interest": 480.00, "total": 6913.79 }
    ]
  }
}
```

## 3. 统一支付前置接口

### 3.1 清算指令下发 `/payment/clearing/instruction`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `channel` | string | 是 | 清算渠道 | `CNAPS_HVPS`（大额）/`CNAPS_BEPS`（小额）/`SUPER_NET` |
| `payer_account` | string | 是 | 付款账户 | `6222020200112345678` |
| `payer_bank` | string | 是 | 付款行号 | `ICBC` |
| `payee_account` | string | 是 | 收款账户 | `6228480402564890018` |
| `payee_bank` | string | 是 | 收款行号 | `ABC` |
| `amount` | decimal | 是 | 金额 | `50000.00` |
| `currency` | string | 是 | 货币 | `CNY` |
| `business_type` | string | 是 | 业务类型 | `REMITTANCE` |
| `urgent` | bool | 否 | 是否加急 | `true` |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "instruction_id": "INS-20260730-0001",
    "cnaps_seq": "202607301234567890",
    "status": "ACCEPTED",
    "accept_time": "2026-07-30T12:00:00Z"
  }
}
```

## 4. .NET 接入示例

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class YuxinCreditClient
{
    private readonly HttpClient _http;
    private readonly string _merchantId;
    private readonly string _privateKeyPem;

    public YuxinCreditClient(HttpClient http, string merchantId, string privateKeyPem)
    {
        _http = http;
        _merchantId = merchantId;
        _privateKeyPem = privateKeyPem;
    }

    public async Task<SubmitResponse> SubmitApplicationAsync(CreditApplication app)
    {
        var requestNo = $"req-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var json = JsonSerializer.Serialize(app);
        var sign = RsaSign(json, _privateKeyPem);

        var request = new HttpRequestMessage(HttpMethod.Post, "/credit/application/submit");
        request.Headers.Add("X-Merchant-Id", _merchantId);
        request.Headers.Add("X-Sign", sign);
        request.Headers.Add("X-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
        request.Headers.Add("X-Request-No", requestNo);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request);
        var respJson = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SubmitResponse>(respJson);
    }

    private static string RsaSign(string data, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var bytes = rsa.SignData(Encoding.UTF8.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(bytes);
    }
}
```

## 5. 最佳实践

- **幂等**: `X-Request-No` 必须全局唯一，重复请求返回原结果。
- **联合贷分单**: 联合贷场景需在 `joint_loan.partner_id` 中标注合作方，系统自动按比例分单。
- **受托支付**: `entrusted_payment=true` 时，资金直接划给交易对手而非借款人账户。
- **清算加急**: `urgent=true` 走大额实时通道，但成本更高，建议仅对时效敏感业务使用。
- **风控回调**: 信贷审批是异步流程，需通过 Webhook 接收决策结果，避免轮询。
