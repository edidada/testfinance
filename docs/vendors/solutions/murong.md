# 沐融科技实时清算系统接口参考

> ⚠️ **私有部署参考设计**
> 沐融科技 RTGS 系统服务于央行与商业银行，接口规范以合同附件为准。本文基于 ISO 20022 / CNAPS 行业标准提供参考设计。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 部署模式 | 央行/清算机构/商业银行私有部署 |
| 协议 | ISO 20022 报文（XML）+ HTTPS 传输 |
| 认证 | 双向 TLS + 报文签名（PKI） |
| 数据格式 | XML（符合 ISO 20022 `pacs.008`/`pacs.009`） |
| 限流 | 按 SLA 约定 |
| 延迟要求 | RTGS 实时处理，单笔 < 100ms |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 |
| :--- | :--- | :--- | :--- |
| 提交大额支付 | POST | `/rtgs/payment/submit` | 提交 RTGS 大额支付指令 |
| 查询支付状态 | GET | `/rtgs/payment/{msgId}/status` | 查询指令状态 |
| 排队管理 | GET | `/rtgs/queue/{account}` | 查询排队中的指令 |
| 释放排队 | POST | `/rtgs/queue/{msgId}/release` | 手动释放排队指令 |
| 流动性查询 | GET | `/rtgs/liquidity/{account}` | 查询账户流动性 |
| 日终轧账 | POST | `/rtgs/eod/settle` | 日终批量结算 |

## 3. 接口详情

### 3.1 提交大额支付 `/rtgs/payment/submit`

> 报文遵循 ISO 20022 `pacs.008`（金融机构间客户信贷转账）。

#### 请求头

| 头 | 说明 |
| :--- | :--- |
| `Content-Type` | `application/xml` |
| `X-Signature` | PKI 报文签名（基于发送方私钥） |
| `X-Certificate` | 发送方证书序列号 |

#### 请求体（XML，简化示例）

| XML 元素 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- |
| `GrpHdr/MsgId` | 是 | 报文标识（唯一） | `MSG20260730001` |
| `GrpHdr/CreDtTm` | 是 | 创建时间 | `2026-07-30T12:00:00Z` |
| `GrpHdr/NbOfTxs` | 是 | 交易笔数 | `1` |
| `CdtTrfTxInf/IntrBkSttlmAmt` | 是 | 跨行结算金额 | `500000.00` |
| `CdtTrfTxInf/IntrBkSttlmDt` | 是 | 结算日期 | `2026-07-30` |
| `CdtTrfTxInf/DbtrAgtBICFI` | 是 | 付款行 BIC | `ICBCCNBJ` |
| `CdtTrfTxInf/CdtrAgtBICFI` | 是 | 收款行 BIC | `ABCCNBJ` |
| `CdtTrfTxInf/Dbtr/Nm` | 是 | 付款人名称 | `张三` |
| `CdtTrfTxInf/Cdtr/Nm` | 是 | 收款人名称 | `李四` |
| `CdtTrfTxInf/RmtInf/Ustrd` | 否 | 备注 | `货款` |

#### 请求示例

```bash
curl -X POST https://rtgs.internalclearing.com/rtgs/payment/submit \
  --cert client.crt --key client.key \
  -H "Content-Type: application/xml" \
  -H "X-Signature: xxx" \
  -H "X-Certificate: SN-001" \
  -d '<?xml version="1.0" encoding="UTF-8"?>
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08">
  <FIToFIPmtCdtTrf>
    <GrpHdr>
      <MsgId>MSG20260730001</MsgId>
      <CreDtTm>2026-07-30T12:00:00Z</CreDtTm>
      <NbOfTxs>1</NbOfTxs>
    </GrpHdr>
    <CdtTrfTxInf>
      <IntrBkSttlmAmt Ccy="CNY">500000.00</IntrBkSttlmAmt>
      <IntrBkSttlmDt>2026-07-30</IntrBkSttlmDt>
      <DbtrAgtBICFI>ICBCCNBJ</DbtrAgtBICFI>
      <CdtrAgtBICFI>ABCCNBJ</CdtrAgtBICFI>
      <Dbtr><Nm>张三</Nm></Dbtr>
      <Cdtr><Nm>李四</Nm></Cdtr>
    </CdtTrfTxInf>
  </FIToFIPmtCdtTrf>
</Document>'
```

#### 成功响应示例

```xml
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.002.001.10">
  <FIToFIPmtStsRpt>
    <GrpHdr>
      <MsgId>RPT20260730001</MsgId>
      <CreDtTm>2026-07-30T12:00:01Z</CreDtTm>
    </GrpHdr>
    <TxInfAndSts>
      <OrgnlInstrId>MSG20260730001</OrgnlInstrId>
      <TxSts>ACSC</TxSts>
      <StsRsnInf><Rsn><Prtry>LIQUIDITY_CHECK_PASSED</Prtry></Rsn></StsRsnInf>
      <AccptncDtTm>2026-07-30T12:00:01Z</AccptncDtTm>
    </TxInfAndSts>
  </FIToFIPmtStsRpt>
</Document>
```

| `TxSts` 状态码 | 说明 |
| :--- | :--- |
| `ACSC` | 已结算（成功） |
| `ACSP` | 已受理 |
| `PDNG` | 排队中（流动性不足） |
| `RJCT` | 已拒绝 |
| `CANC` | 已撤销 |

#### 错误码

| 业务码 | 含义 |
| :--- | :--- |
| `FF01` | 报文格式错误 |
| `FF02` | 签名验证失败 |
| `AM04` | 余额不足（进入排队） |
| `AM06` | 金额超限 |
| `BE05` | 付款行 BIC 未知 |
| `BE06` | 收款行 BIC 未知 |
| `CURR` | 币种不支持 |
| `CUTO` | 超过业务截止时间 |

### 3.2 流动性查询 `/rtgs/liquidity/{account}`

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "account": "RTGS-ICBC-001",
    "available_balance": 500000000.00,
    "reserved_balance": 100000000.00,
    "queued_amount": 50000000.00,
    "credit_limit": 200000000.00,
    "as_of_time": "2026-07-30T12:00:00Z"
  }
}
```

### 3.3 日终轧账 `/rtgs/eod/settle`

#### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `settlement_date` | string | 是 | 结算日期 |
| `account` | string | 是 | 账户 |

#### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "settlement_date": "2026-07-30",
    "account": "RTGS-ICBC-001",
    "total_debit": 5000000000.00,
    "total_credit": 4980000000.00,
    "net_position": -20000000.00,
    "queued_count": 5,
    "settled_count": 1234,
    "eod_time": "2026-07-30T17:00:00Z"
  }
}
```

## 4. .NET 接入示例

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

public class MurongRtgsClient
{
    private readonly HttpClient _http;

    public MurongRtgsClient(string certPath, string certPassword)
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(new X509Certificate2(certPath, certPassword));
        _http = new HttpClient(handler) { BaseAddress = new Uri("https://rtgs.internalclearing.com") };
    }

    public async Task<string> SubmitPaymentAsync(string msgId, decimal amount, string debtorBic, string creditorBic)
    {
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"">
  <FIToFIPmtCdtTrf>
    <GrpHdr>
      <MsgId>{msgId}</MsgId>
      <CreDtTm>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</CreDtTm>
      <NbOfTxs>1</NbOfTxs>
    </GrpHdr>
    <CdtTrfTxInf>
      <IntrBkSttlmAmt Ccy=""CNY"">{amount}</IntrBkSttlmAmt>
      <IntrBkSttlmDt>{DateTime.UtcNow:yyyy-MM-dd}</IntrBkSttlmDt>
      <DbtrAgtBICFI>{debtorBic}</DbtrAgtBICFI>
      <CdtrAgtBICFI>{creditorBic}</CdtrAgtBICFI>
    </CdtTrfTxInf>
  </FIToFIPmtCdtTrf>
</Document>";

        var content = new StringContent(xml, Encoding.UTF8, "application/xml");
        var resp = await _http.PostAsync("/rtgs/payment/submit", content);
        return await resp.Content.ReadAsStringAsync();
    }
}
```

## 5. 最佳实践

- **ISO 20022 报文**: 严格遵循 `pacs.008`（贷记）/`pacs.009`（金融机构间转账）规范，字段不能缺漏。
- **PKI 签名**: 报文必须用发送方私钥签名，接收方用证书验签，确保不可抵赖。
- **流动性管理**: 大额支付前应查询 `/rtgs/liquidity` 确认余额，避免进入排队。
- **排队机制**: 余额不足时指令进入排队，可通过 `/rtgs/queue/release` 手动释放或等待日终处理。
- **业务截止**: 超过 `CUTO` 时间会被拒绝，需关注 RTGS 业务时间窗（通常 8:30-17:00）。
- **幂等**: `MsgId` 全局唯一，重复提交返回原状态。
