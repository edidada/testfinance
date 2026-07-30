# 长亮科技银行核心系统接口参考

> ⚠️ **私有部署参考设计**
> 长亮科技核心系统为银行私有部署，接口规范以项目合同附件为准。本文基于行业惯例提供通用参考设计。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 部署模式 | 银行私有云/机房，分布式架构 |
| 协议 | HTTPS POST（JSON） |
| 认证 | 双向 TLS + 数字证书 |
| 内容类型 | `application/json` |
| 限流 | 按合同约定 |
| 版本 | 按客户项目定制 |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 |
| :--- | :--- | :--- | :--- |
| 客户开户 | POST | `/core/customer/open` | 创建客户信息 (CIF) |
| 存款账户开立 | POST | `/core/deposit/account/open` | 开立活期/定期账户 |
| 存款 | POST | `/core/deposit/deposit` | 现金/转账存入 |
| 取款 | POST | `/core/deposit/withdraw` | 现金/转账支取 |
| 转账 | POST | `/core/transfer` | 内部账户间转账 |
| 总账记账 | POST | `/core/gl/post` | 总账分录记账 |
| 日终轧账 | POST | `/core/eod/balance` | 日终批量轧账 |
| 账户查询 | GET | `/core/account/{accountNo}` | 查询账户信息 |
| 交易流水查询 | GET | `/core/account/{accountNo}/transactions` | 查询交易明细 |

## 3. 接口详情

### 3.1 存款账户开立 `/core/deposit/account/open`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `customer_id` | string | 是 | 客户 ID（CIF） | `C-2026-0001` |
| `product_code` | string | 是 | 存款产品代码 | `CURRENT_CNY`/`FIXED_1Y` |
| `currency` | string | 是 | 货币 | `CNY` |
| `initial_deposit` | decimal | 否 | 初始存入金额 | `100.00` |
| `password_hash` | string | 是 | 密码哈希（不可逆） | `sha256:xxx` |
| `branch_code` | string | 是 | 网点号 | `0001` |
| `joint_customers[]` | array | 否 | 联名账户客户 | - |

#### 请求示例

```bash
curl -X POST https://core.internalbank.com/core/deposit/account/open \
  --cert client.crt --key client.key \
  -H "Content-Type: application/json" \
  -H "X-Request-No: req-20260730-001" \
  -d '{
    "customer_id": "C-2026-0001",
    "product_code": "CURRENT_CNY",
    "currency": "CNY",
    "initial_deposit": 100.00,
    "password_hash": "sha256:abc123",
    "branch_code": "0001"
  }'
```

#### 成功响应示例

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "account_no": "6228480402564890018",
    "customer_id": "C-2026-0001",
    "product_code": "CURRENT_CNY",
    "currency": "CNY",
    "balance": 100.00,
    "status": "ACTIVE",
    "open_time": "2026-07-30T12:00:00Z"
  }
}
```

#### 错误码

| `code` | 含义 |
| :--- | :--- |
| 0000 | 成功 |
| 1001 | 参数错误 |
| 2001 | 客户不存在 |
| 3001 | 产品已下架 |
| 3002 | 客户状态异常（如冻结） |
| 4001 | 密码哈希格式错误 |
| 9999 | 系统错误 |

### 3.2 转账 `/core/transfer`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `payer_account` | string | 是 | 付款账户 | `6228480402564890018` |
| `payee_account` | string | 是 | 收款账户 | `6222020200112345678` |
| `amount` | decimal | 是 | 金额 | `5000.00` |
| `currency` | string | 是 | 货币 | `CNY` |
| `password_hash` | string | 是 | 取款密码哈希 | `sha256:xxx` |
| `remark` | string | 否 | 摘要（<=32） | `还款` |
| `client_seq` | string | 是 | 客户端流水号（幂等） | `seq-001` |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "transaction_no": "T-20260730-0001",
    "payer_account": "6228480402564890018",
    "payee_account": "6222020200112345678",
    "amount": 5000.00,
    "fee": 0.00,
    "status": "SUCCESS",
    "book_time": "2026-07-30T12:00:00Z",
    "ledger_entries": [
      { "account": "622848...", "direction": "DEBIT", "amount": 5000.00 },
      { "account": "622202...", "direction": "CREDIT", "amount": 5000.00 }
    ]
  }
}
```

> 核心系统采用**复式记账**，每笔交易都对应借贷平衡的总账分录，与本项目 [PaymentDomain.cs](../../../src/Payments.Api/PaymentDomain.cs) 的 `LedgerEntry` 设计一致。

### 3.3 总账记账 `/core/gl/post`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `entries[]` | array | 是 | 分录数组（借贷必须平衡） | - |
| `entries[].subject` | string | 是 | 科目号 | `1001`（库存现金） |
| `entries[].direction` | string | 是 | 方向 | `DEBIT`/`CREDIT` |
| `entries[].amount` | decimal | 是 | 金额 | `5000.00` |
| `entries[].account_no` | string | 否 | 分户账 | `622848...` |
| `abstract` | string | 是 | 摘要 | `客户转账` |
| `operator` | string | 是 | 柜员号 | `T001` |

#### 校验规则

- `entries` 中 `DEBIT` 金额总和必须等于 `CREDIT` 金额总和（借贷平衡）
- 单笔分录金额 > 0
- 科目号必须存在于科目表

## 4. .NET 接入示例

```csharp
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

public class ChangliangCoreClient
{
    private readonly HttpClient _http;

    public ChangliangCoreClient(HttpClient http, string certPath, string certPassword)
    {
        _http = http;
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(new X509Certificate2(certPath, certPassword));
        _http = new HttpClient(handler) { BaseAddress = new Uri("https://core.internalbank.com") };
    }

    public async Task<OpenAccountResponse> OpenDepositAccountAsync(OpenAccountRequest req)
    {
        req.RequestNo = $"req-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var json = JsonSerializer.Serialize(req);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/core/deposit/account/open");
        request.Headers.Add("X-Request-No", req.RequestNo);
        request.Content = content;
        var resp = await _http.SendAsync(request);
        var respJson = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OpenAccountResponse>(respJson);
    }
}
```

## 5. 最佳实践

- **双向 TLS**: 银行核心系统必须用 mTLS，避免明文传输。
- **幂等**: `client_seq` / `X-Request-No` 必须唯一，防止重复记账。
- **复式记账**: 所有资金变动必须借贷平衡，系统会拒绝不平衡的分录。
- **日终批处理**: 日终轧账是批量任务，非交易时段调用 `/core/eod/balance`。
- **密码安全**: 密码哈希不可逆，前端用 SM3/SHA-256 哈希后上送，服务端不存明文。
