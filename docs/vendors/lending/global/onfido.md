# Onfido KYC API 详细文档

> Onfido 提供远程身份核验 (KYC)，通过托管 SDK 引导用户拍摄证件和活体视频，服务端调用 API 完成验证。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 生产 Base URL | `https://api.onfido.com` |
| 沙箱 Base URL | `https://api.sandbox.onfido.com` |
| API 版本 | `v3` |
| 认证方式 | Bearer Token（`Authorization: Token token=xxx`） |
| 内容类型 | `application/json`（文档上传为 `multipart/form-data`） |
| 限流 | 600 req/min |
| C 端组件 | Onfido Capture SDK (iOS/Android/Web) |

## 2. 接口列表

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| 创建 Applicant | POST | `/v3/applicants` | 创建待验证用户 | B |
| 上传证件 | POST | `/v3/documents` | 上传身份证/护照 | B |
| 上传活体视频 | POST | `/v3/live_videos` | 上传活体检测视频 | B |
| 创建 Check | POST | `/v3/checks` | 触发一次完整的身份核验 | B |
| 查询 Check | GET | `/v3/checks/{id}` | 查询核验结果 | B |
| 查询 Report | GET | `/v3/reports/{id}` | 查询单项报告 | B |
| 生成 SDK Token | POST | `/v3/sdk_token` | 为前端 SDK 生成 token | B |

## 3. 接口详情

### 3.1 创建 Applicant `/v3/applicants`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `first_name` | string | 是 | 名 | `John` |
| `last_name` | string | 是 | 姓 | `Doe` |
| `email` | string | 否 | 邮箱 | `test@test.com` |
| `dob` | string | 否 | 出生日期 YYYY-MM-DD | `1990-01-01` |
| `address.building_number` | string | 否 | 门牌号 | `100` |
| `address.street` | string | 否 | 街道 | `Main St` |
| `address.country` | string | 是（有地址时） | 国家码 GBR | `GBR` |
| `address.postcode` | string | 否 | 邮编 | `SW1A1AA` |

#### 请求示例

```bash
curl -X POST https://api.sandbox.onfido.com/v3/applicants \
  -H "Authorization: Token token=xxx" \
  -H "Content-Type: application/json" \
  -d '{
    "first_name": "John",
    "last_name": "Doe",
    "email": "test@test.com",
    "dob": "1990-01-01"
  }'
```

#### 成功响应示例

```json
{
  "id": "abc123-456-789",
  "created_at": "2026-07-30T12:00:00Z",
  "first_name": "John",
  "last_name": "Doe"
}
```

### 3.2 上传证件 `/v3/documents`

`multipart/form-data` 上传。

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `applicant_id` | string | 是 | 申请人 ID |
| `file` | binary | 是 | 证件图片（JPG/PNG，<=10MB） |
| `type` | string | 是 | 类型：`passport`/`driving_licence`/`national_identity_card` |
| `side` | string | 条件 | `front`/`back`（驾驶执照需双面） |
| `issuing_country` | string | 是 | 3 字母国家码 |

#### 成功响应示例

```json
{
  "id": "doc-xxx",
  "applicant_id": "abc123",
  "type": "passport",
  "side": "front",
  "issuing_country": "GBR"
}
```

### 3.3 创建 Check `/v3/checks`

#### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `applicant_id` | string | 是 | 申请人 ID | `abc123` |
| `report_names[]` | string[] | 是 | 报告类型 | `["document","facial_similarity_video","watchlist"]` |
| `tags[]` | string[] | 否 | 标签 | `["vip"]` |
| `redirect_uri` | string | 否 | 完成后跳转 | `https://shop.com/return` |
| `asynchronous` | bool | 否 | 是否异步（默认 true） | `true` |

#### 请求示例

```bash
curl -X POST https://api.sandbox.onfido.com/v3/checks \
  -H "Authorization: Token token=xxx" \
  -H "Content-Type: application/json" \
  -d '{
    "applicant_id": "abc123",
    "report_names": ["document", "facial_similarity_video", "watchlist"],
    "asynchronous": true
  }'
```

#### 成功响应示例

```json
{
  "id": "check-xxx",
  "applicant_id": "abc123",
  "status": "in_progress",
  "result": null,
  "report_ids": ["rep-1", "rep-2", "rep-3"],
  "created_at": "2026-07-30T12:00:00Z"
}
```

| `status` | 说明 |
| :--- | :--- |
| `in_progress` | 进行中 |
| `complete` | 完成 |
| `withdrawn` | 撤销 |

| `result` | 说明 |
| :--- | :--- |
| `clear` | 通过 |
| `consider` | 需人工复核 |
| `unidentified` | 无法识别 |

### 3.4 查询 Check `/v3/checks/{id}`

```json
{
  "id": "check-xxx",
  "status": "complete",
  "result": "clear",
  "report_ids": ["rep-1", "rep-2", "rep-3"],
  "completed_at": "2026-07-30T12:02:00Z"
}
```

### 3.5 Webhook 事件

| 事件 | 触发时机 |
| :--- | :--- |
| `check.completed` | Check 完成 |
| `check.started` | Check 开始 |
| `check.withdrawn` | 撤销 |
| `report.withdrawn` | 报告撤销 |

**验签**: 用 Webhook Secret 通过 HMAC-SHA256 验签 `X-Signature` 头。

## 4. .NET SDK 示例

```csharp
using Onfido;

var client = new OnfidoClient("sandbox", "token=xxx");

// 1. 创建申请人
var applicant = await client.Applicant.CreateAsync(new ApplicantRequest
{
    FirstName = "John",
    LastName = "Doe",
    Email = "test@test.com",
    Dob = "1990-01-01"
});

// 2. 上传证件（前端 SDK 上传）
await client.Document.UploadAsync(applicant.Id, new DocumentUploadRequest
{
    File = fileStream,
    Type = "passport",
    IssuingCountry = "GBR"
});

// 3. 创建 Check
var check = await client.Check.CreateAsync(new CheckRequest
{
    ApplicantId = applicant.Id,
    ReportNames = new[] { "document", "facial_similarity_video", "watchlist" },
    Asynchronous = true
});

// 4. 轮询或通过 Webhook 获取结果
var result = await client.Check.GetAsync(check.Id);
if (result.Status == "complete" && result.Result == "clear") { /* KYC 通过 */ }
```

## 5. 最佳实践

- **异步优先**: Check 设 `asynchronous=true`，用 Webhook 接收结果，避免长轮询。
- **SDK 上传**: 证件和活体视频应由 Onfido Capture SDK 直接上传到 Onfido，不经过商户服务器，降低合规风险。
- **人工复核**: `result=consider` 时需在 Onfido Dashboard 人工复核。
- **沙箱**: 用 `sandbox` 环境测试，提供测试图片（如 `passport.jpg`）。
