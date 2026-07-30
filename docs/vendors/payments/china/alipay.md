# 支付宝支付 API 详细文档

> 支付宝开放平台采用**网关模式**，所有请求都发到统一网关 `https://openapi.alipay.com/gateway.do`，通过 `method` 参数区分接口。签名算法为 RSA2。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产网关 | `https://openapi.alipay.com/gateway.do` |
| 沙箱网关 | `https://openapi-sandbox.dl.alipaydev.com/gateway.do` |
| 协议 | HTTPS POST（form-urlencoded） |
| 签名算法 | RSA2（SHA256WithRSA） |
| 编码 | UTF-8 |
| 数据格式 | JSON（`biz_content` 字段内为 JSON 字符串） |
| 版本控制 | 通过 `method` 名称内置版本（如 `alipay.trade.pay` 默认 1.0） |

## 2. 公共请求参数（所有接口通用）

| 参数 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- |
| `app_id` | 是 | 应用 ID | `2021000xxxxxxxxx` |
| `method` | 是 | 接口名 | `alipay.trade.pay` |
| `format` | 是 | 数据格式 | `JSON` |
| `charset` | 是 | 编码 | `utf-8` |
| `sign_type` | 是 | 签名类型 | `RSA2` |
| `sign` | 是 | 签名值 | `abcd...` |
| `timestamp` | 是 | 时间戳 | `2026-07-30 12:00:00` |
| `version` | 是 | API 版本 | `1.0` |
| `notify_url` | 否 | 异步回调地址 | `https://shop.com/notify` |
| `biz_content` | 是 | 业务参数（JSON 字符串） | 见各接口 |
| `app_auth_token` | 否 | ISV 代调用 token | - |

## 3. 接口列表

| `method` | 用途 | C/B 端 |
| :--- | :--- | :--- |
| `alipay.trade.pay` | 统一收单下单并支付（当面付） | B+C |
| `alipay.trade.precreate` | 生成扫码支付二维码 | B+C |
| `alipay.trade.create` | 创建交易（小程序/网站支付） | B |
| `alipay.trade.query` | 查询交易状态 | B |
| `alipay.trade.refund` | 退款 | B |
| `alipay.trade.fastpay.refund.query` | 查询退款 | B |
| `alipay.trade.close` | 关闭交易 | B |
| `alipay.data.dataservice.bill.downloadurl.query` | 申请对账文件下载 | B |
| `alipay.fund.trans.uni.transfer` | 单笔转账到账户 | B |

## 4. 接口详情

### 4.1 统一收单下单并支付 `alipay.trade.pay`

#### biz_content 字段

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `out_trade_no` | string | 是 | 商户订单号（<=64） | `order-1001` |
| `total_amount` | string | 是 | 金额（元，2 位小数） | `88.50` |
| `subject` | string | 是 | 订单标题 | `TestFinance 订单` |
| `product_code` | string | 是 | 产品码 | `FACE_TO_FACE_PAYMENT`（当面付） |
| `auth_no` | string | 否 | 预授权号 | - |
| `body` | string | 否 | 描述 | - |
| `time_expire` | string | 否 | 过期时间 | `2026-07-30 13:00:00` |
| `goods_detail[]` | array | 否 | 商品明细 | - |
| `extend_params` | object | 否 | 扩展参数（花呗分期等） | - |
| `business_params` | object | 否 | 业务参数 | - |

#### 请求示例

```bash
curl -X POST https://openapi.alipay.com/gateway.do \
  -d "app_id=2021000xxxxxxxxx" \
  -d "method=alipay.trade.pay" \
  -d "format=JSON" \
  -d "charset=utf-8" \
  -d "sign_type=RSA2" \
  -d "sign=xxxx" \
  -d "timestamp=2026-07-30 12:00:00" \
  -d "version=1.0" \
  -d "notify_url=https://shop.com/notify" \
  --data-urlencode 'biz_content={
    "out_trade_no": "order-1001",
    "total_amount": "88.50",
    "subject": "TestFinance 订单",
    "product_code": "FACE_TO_FACE_PAYMENT"
  }'
```

#### 响应字段

| 字段 | 说明 |
| :--- | :--- |
| `code` | 网关返回码，`10000` 表示成功 |
| `msg` | 网关返回信息，`Success` |
| `sub_code` | 业务错误码（失败时） |
| `sub_msg` | 业务错误信息 |
| `trade_no` | 支付宝交易号 |
| `out_trade_no` | 商户订单号 |
| `total_amount` | 金额 |
| `buyer_logon_id` | 买家账号（脱敏） |
| `buyer_pay_amount` | 买家实付金额 |
| `point_amount` | 积分金额 |
| `receipt_amount` | 实收金额 |

#### 成功响应示例

```json
{
  "alipay_trade_pay_response": {
    "code": "10000",
    "msg": "Success",
    "trade_no": "2026073022001xxxxxxxxx",
    "out_trade_no": "order-1001",
    "total_amount": "88.50",
    "buyer_logon_id": "138****0000",
    "buyer_pay_amount": "88.50",
    "receipt_amount": "88.50",
    "point_amount": "0.00"
  },
  "sign": "xxxx"
}
```

#### 错误码

| `code` | `sub_code` | 含义 | 排查建议 |
| :--- | :--- | :--- | :--- |
| 20000 | `ACQ.SYSTEM_ERROR` | 系统错误 | 重试（同 `out_trade_no` 幂等） |
| 20000 | `ACQ.INVALID_PARAMETER` | 参数错误 | 检查 `biz_content` |
| 40004 | `ACQ.TRADE_HAS_SUCCESS` | 交易已成功 | 幂等返回原结果 |
| 40004 | `ACQ.TRADE_HAS_CLOSE` | 交易已关闭 | 重新下单 |
| 40004 | `ACQ.REFUND_AMT_NOT_EQUAL_TOTAL` | 退款金额异常 | 检查金额 |
| 40004 | `ACQ.PAYMENT_AUTH_CODE_INVALID` | 付款码无效 | 提示用户刷新付款码 |
| 40004 | `ACQ.BUYER_BALANCE_NOT_ENOUGH` | 余额不足 | 换支付方式 |

### 4.2 退款 `alipay.trade.refund`

#### biz_content

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `out_trade_no` | string | 二选一 | 商户订单号 |
| `trade_no` | string | 二选一 | 支付宝交易号 |
| `refund_amount` | string | 是 | 退款金额（元） |
| `out_request_no` | string | 是 | 退款请求号（多次部分退款必填，唯一） |
| `refund_reason` | string | 否 | 退款原因 |

### 4.3 异步通知（Notify）

支付宝通过 GET/POST 推送通知到 `notify_url`，需返回字符串 `success`。

| 参数 | 说明 |
| :--- | :--- |
| `trade_status` | `TRADE_SUCCESS`/`WAIT_BUYER_PAY`/`TRADE_CLOSED` |
| `out_trade_no` | 商户订单号 |
| `trade_no` | 支付宝交易号 |
| `total_amount` | 金额 |
| `gmt_payment` | 支付时间 |

**验签**: 通知参数含 `sign`，用支付宝公钥验签。

## 5. .NET SDK 示例

```csharp
using AlipaySDKNet;

// 配置
IAopClient client = new DefaultAopClient(
    "https://openapi.alipay.com/gateway.do",
    appId,
    privateKeyPem,
    "json", "1.0", "RSA2",
    alipayPublicKeyPem,
    "UTF-8", false);

// 当面付下单
var request = new AlipayTradePayRequest();
var bizContent = new
{
    out_trade_no = "order-1001",
    total_amount = "88.50",
    subject = "TestFinance 订单",
    product_code = "FACE_TO_FACE_PAYMENT"
};
request.SetBizContent(JsonConvert.SerializeObject(bizContent));
request.SetNotifyUrl("https://shop.com/notify");

var response = await client.ExecuteAsync(request);
if (response.Code == "10000") { /* 成功 */ }
```

## 6. 最佳实践

- **幂等**: `out_trade_no` 必须全局唯一，重复请求会返回原结果。
- **金额格式**: 字符串，2 位小数（与本项目 [PaymentDomain.cs](../../../src/Payments.Api/PaymentDomain.cs) 的 `decimal` 一致）。
- **异步通知**: 必须验签 + 返回 `success`，否则支付宝会重试 8 次。
- **沙箱**: 使用沙箱 App ID + 沙箱版钱包测试。
