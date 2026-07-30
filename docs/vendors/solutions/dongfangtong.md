# 东方通中间件集成参考

> ⚠️ **私有部署参考设计**
> 东方通中间件（TongLINK/Q、TongWeb）为金融基础设施私有部署组件，集成方式以产品文档与项目合同为准。本文为通用集成参考。

## 1. 概览

| 产品 | 类型 | 用途 |
| :--- | :--- | :--- |
| **TongLINK/Q** | 消息中间件 | 跨节点可靠消息传输（CNAPS 数据传输组件） |
| **TongWeb** | 应用服务器 | Java EE 应用承载（国产化替代 WebLogic/WAS） |
| **TongSEC** | 安全中间件 | 加密、签名、PKI 集成 |
| **TongRDS** | 分布式缓存 | Redis 协议兼容的国产缓存 |

> 东方通中间件以 **Java API** 为主，C# 集成需通过 HTTP 网关或 Web Service 适配。本文聚焦 TongLINK/Q 的集成。

## 2. TongLINK/Q 集成

### 2.1 部署模式

```
[应用 A] --send--> [TongLINK/Q 队列] --transfer--> [TongLINK/Q 队列] --recv--> [应用 B]
```

TongLINK/Q 提供点对点（PTP）和发布订阅（Pub/Sub）两种模型，保证消息**不丢失、不重复、有序**。

### 2.2 集成方式

| 方式 | 适用场景 | 说明 |
| :--- | :--- | :--- |
| Java API | Java 应用 | 原生集成，性能最高 |
| JMS | Java EE 应用 | 标准 JMS 接口 |
| C API | C/C++ 应用 | 原生 C 接口 |
| HTTP 网关 | 非 Java 应用（含 .NET） | 通过 HTTP 网关转换 |

### 2.3 HTTP 网关接口（适用 .NET 集成）

东方通提供 HTTP 网关，将消息收发能力暴露为 RESTful API，便于 .NET 等非 Java 应用集成。

#### 2.3.1 发送消息 `/tonglink/q/send`

##### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `queue_name` | string | 是 | 队列名 | `CNAPS_HVPS_IN` |
| `message_body` | string | 是 | 消息内容（Base64 编码） | `base64:xxx` |
| `priority` | int | 否 | 优先级 0-9（默认 5） | `5` |
| `ttl` | int | 否 | 存活时间（秒，0=永久） | `86400` |
| `delivery_mode` | string | 否 | 投递模式 | `PERSISTENT`/`NON_PERSISTENT` |
| `message_id` | string | 否 | 自定义消息 ID（幂等） | `msg-001` |

##### 请求示例

```bash
curl -X POST https://tonglink.internalbank.com/tonglink/q/send \
  -H "Content-Type: application/json" \
  -H "Authorization: Basic xxx" \
  -d '{
    "queue_name": "CNAPS_HVPS_IN",
    "message_body": "base64:PD94bWw...",
    "priority": 5,
    "ttl": 86400,
    "delivery_mode": "PERSISTENT",
    "message_id": "msg-001"
  }'
```

##### 成功响应示例

```json
{
  "code": "0000",
  "message": "成功",
  "data": {
    "message_id": "msg-001",
    "queue_name": "CNAPS_HVPS_IN",
    "send_time": "2026-07-30T12:00:00Z",
    "status": "SENT"
  }
}
```

##### 错误码

| `code` | 含义 |
| :--- | :--- |
| 0000 | 成功 |
| 1001 | 队列不存在 |
| 1002 | 消息体过大（>10MB） |
| 2001 | 认证失败 |
| 3001 | 目标节点不可达 |
| 4001 | 队列已满 |
| 9999 | 系统错误 |

#### 2.3.2 接收消息 `/tonglink/q/receive`

##### 请求体

| 字段 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `queue_name` | string | 是 | 队列名 | `CNAPS_HVPS_OUT` |
| `timeout` | int | 否 | 等待超时（秒） | `30` |
| `max_messages` | int | 否 | 最大拉取数 | `10` |
| `auto_ack` | bool | 否 | 是否自动确认 | `false` |

##### 成功响应示例

```json
{
  "code": "0000",
  "data": {
    "messages": [
      {
        "message_id": "msg-002",
        "queue_name": "CNAPS_HVPS_OUT",
        "message_body": "base64:PD94bWw...",
        "priority": 5,
        "send_time": "2026-07-30T12:00:00Z",
        "delivery_count": 1
      }
    ],
    "count": 1
  }
}
```

#### 2.3.3 确认消息 `/tonglink/q/ack`

##### 请求体

| 字段 | 类型 | 必填 | 说明 |
| :--- | :--- | :--- | :--- |
| `queue_name` | string | 是 | 队列名 |
| `message_id` | string | 是 | 消息 ID |

##### 成功响应示例

```json
{ "code": "0000", "message": "成功" }
```

> **注意**: `auto_ack=false` 时必须显式 ACK，否则消息会在超时后重投。

## 3. TongWeb 应用服务器集成

TongWeb 兼容 Java EE / Jakarta EE 规范，部署方式与 Tomcat/WebLogic 类似。C# 应用不直接部署在 TongWeb，而是通过 HTTP 调用部署在其上的 Java 服务。

### 3.1 典型架构

```
[C# .NET 服务] --HTTP--> [TongWeb 上的 Java 网关] --API--> [核心系统]
```

## 4. .NET 接入示例

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class TongLinkClient
{
    private readonly HttpClient _http;

    public TongLinkClient(HttpClient http, string username, string password)
    {
        _http = http;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public async Task<SendResponse> SendAsync(string queueName, byte[] body, string messageId = null)
    {
        var req = new
        {
            queue_name = queueName,
            message_body = "base64:" + Convert.ToBase64String(body),
            priority = 5,
            ttl = 86400,
            delivery_mode = "PERSISTENT",
            message_id = messageId ?? $"msg-{Guid.NewGuid():N}"
        };
        var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/tonglink/q/send", content);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SendResponse>(json);
    }

    public async Task<ReceiveResponse> ReceiveAsync(string queueName, int timeoutSeconds = 30)
    {
        var req = new { queue_name = queueName, timeout = timeoutSeconds, max_messages = 10, auto_ack = false };
        var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/tonglink/q/receive", content);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ReceiveResponse>(json);
    }

    public async Task AckAsync(string queueName, string messageId)
    {
        var req = new { queue_name = queueName, message_id = messageId };
        var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        await _http.PostAsync("/tonglink/q/ack", content);
    }
}

// 使用：发送 CNAPS 报文
var client = new TongLinkClient(httpClient, "tonglink-user", "password");
var reportBytes = Encoding.UTF8.GetBytes("<xml>CNAPS 报文</xml>");
await client.SendAsync("CNAPS_HVPS_IN", reportBytes, "msg-001");

// 接收响应
var received = await client.ReceiveAsync("CNAPS_HVPS_OUT");
foreach (var msg in received.Data.Messages)
{
    var body = Convert.FromBase64String(msg.MessageBody.Replace("base64:", ""));
    // 处理报文
    await client.AckAsync("CNAPS_HVPS_OUT", msg.MessageId);
}
```

## 5. 最佳实践

- **持久化**: 金融场景必须用 `delivery_mode=PERSISTENT`，消息落盘后再返回成功。
- **ACK 机制**: 用 `auto_ack=false` + 显式 ACK，确保业务处理成功后才删除消息。
- **幂等**: `message_id` 全局唯一，消费方需做幂等处理（同一消息可能重投）。
- **死信队列**: 多次消费失败的消息进入死信队列，需监控告警。
- **消息大小**: 单条消息建议 < 1MB，大文件用文件传输（TongLINK/Q File Transfer）。
- **集群高可用**: TongLINK/Q 部署多节点，故障自动切换，应用层无感知。
- **国产化**: 信创场景下，TongLINK/Q 替代 IBM MQ，TongWeb 替代 WebLogic，需评估功能差异。
