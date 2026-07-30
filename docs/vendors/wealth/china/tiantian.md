# 天天基金数据 API 详细文档

> 天天基金网是国内最大的独立基金销售平台，提供基金行情、估值、详情等公开数据 API。交易类 API 仅面向合作机构开放。

## 1. 概览

| 项目 | 值 |
| :--- | :--- |
| 数据 API Base URL | `https://fundgz.1234567.com.cn`（估值）/ `https://api.fund.eastmoney.com`（详情） |
| 交易 API | 需机构合作接入（私有协议） |
| 协议 | HTTPS GET（公开数据） |
| 认证 | 公开数据无需认证；交易 API 需商户证书 |
| 内容类型 | `application/json`（部分接口 JSONP） |
| 限流 | 无明确限制，建议 < 10 QPS |
| C 端组件 | 天天基金 App、Web 交易页 |

## 2. 接口列表（公开数据）

| 接口 | 方法 | 路径 | 用途 | C/B 端 |
| :--- | :--- | :--- | :--- | :--- |
| 实时估值 | GET | `/js/{fundCode}.js` | 基金实时估值 | B |
| 基金详情 | GET | `/f10/ jjxq` | 基金详情页数据 | B |
| 历史净值 | GET | `/f10/lsjz` | 历史净值列表 | B |
| 基金排名 | GET | `/Data/ FundRank` | 基金排行榜 | B |
| 基金公司 | GET | `/Data/ FundCompanyRank` | 基金公司排名 | B |
| 基金持仓 | GET | `/f10/ jjcc` | 基金持仓明细 | B |

> ⚠️ 公开 API 接口路径可能随时变更，建议以官方页面为准。

## 3. 接口详情

### 3.1 实时估值 `/js/{fundCode}.js`

返回 JSONP 格式的实时估值数据。

#### 请求参数

| 参数 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `rt` | long | 否 | 时间戳防缓存 | `1785425000000` |

#### 请求示例

```bash
curl "https://fundgz.1234567.com.cn/js/000001.js?rt=1785425000000"
```

#### 响应示例（JSONP）

```javascript
jsonpgz({
  "fundcode": "000001",
  "name": "华夏成长",
  "jzrq": "2026-07-29",
  "dwjz": "1.2345",
  "gsz": "1.2400",
  "gszzl": "0.45",
  "gztime": "2026-07-30 15:00"
});
```

#### 响应字段

| 字段 | 说明 |
| :--- | :--- |
| `fundcode` | 基金代码 |
| `name` | 基金名称 |
| `jzrq` | 净值日期 |
| `dwjz` | 单位净值 |
| `gsz` | 估算净值 |
| `gszzl` | 估算涨跌幅（%） |
| `gztime` | 估算时间 |

### 3.2 历史净值 `/f10/lsjz`

#### 请求参数

| 参数 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `fundCode` | string | 是 | 基金代码 | `000001` |
| `pageIndex` | int | 是 | 页码 | `1` |
| `pageSize` | int | 是 | 每页数量 | `20` |
| `startDate` | string | 否 | 起始日期 | `2026-01-01` |
| `endDate` | string | 否 | 结束日期 | `2026-07-30` |

#### 请求示例

```bash
curl "https://api.fund.eastmoney.com/f10/lsjz?fundCode=000001&pageIndex=1&pageSize=20" \
  -H "Referer: https://fundf10.eastmoney.com/"
```

> ⚠️ 该接口需要 `Referer` 头，否则返回 403。

#### 成功响应示例

```json
{
  "Data": {
    "LSJZList": [
      {
        "FSRQ": "2026-07-29",
        "DWJZ": "1.2345",
        "JZZZL": "0.45",
        "LJJZ": "3.5678"
      }
    ],
    "total": 100,
    "pageIndex": 1,
    "pageSize": 20
  },
  "ErrCode": 0
}
```

#### 响应字段

| 字段 | 说明 |
| :--- | :--- |
| `FSRQ` | 净值日期 |
| `DWJZ` | 单位净值 |
| `JZZZL` | 日增长率（%） |
| `LJJZ` | 累计净值 |

### 3.3 基金详情 `/f10/jjxq`

返回基金的基本信息（成立日期、规模、经理、费率等）。

#### 请求示例

```bash
curl "https://fundf10.eastmoney.com/jjjz_000001.html"
```

返回 HTML 页面，需解析。也提供结构化 JSON 接口（需机构合作）。

### 3.4 基金排行榜 `/Data/FundRank`

#### 请求参数

| 参数 | 类型 | 必填 | 说明 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| `fundType` | int | 否 | 类型：0 全部, 1 股票, 2 混合, 3 债券, 4 指数 | `1` |
| `sort` | string | 否 | 排序字段 | `SYL_Y`（近 1 年） |
| `order` | string | 否 | 排序方向 | `desc` |
| `pi` | int | 是 | 页码 | `1` |
| `pz` | int | 是 | 每页数量 | `20` |

#### 成功响应示例（简化）

```json
{
  "datas": [
    {
      "code": "000001",
      "name": "华夏成长",
      "net": "1.2345",
      "netTotal": "3.5678",
      "rate": "0.45",
      "rateY": "12.34"
    }
  ],
  "allNum": 5000,
  "pageIndex": 1
}
```

## 4. 机构交易 API（合作接入）

天天基金面向合作机构提供交易 API（申购、赎回、查询持仓等），采用：

- **协议**: HTTPS POST（XML 或 JSON）
- **认证**: 商户证书 + 签名
- **流程**: 商户进件 → 签约 → 获取 API 文档（私有） → 联调 → 上线

核心交易接口（机构接入后获取详细文档）：

| 接口 | 用途 |
| :--- | :--- |
| 基金申购 | 提交申购订单 |
| 基金赎回 | 提交赎回订单 |
| 撤单 | 撤销未确认订单 |
| 订单查询 | 查询订单状态 |
| 持仓查询 | 查询客户持仓 |
| 交易密码校验 | 校验交易密码 |
| 适当性管理 | 风险等级校验 |

## 5. .NET 接入示例（公开数据）

```csharp
using System.Text.RegularExpressions;
using System.Text.Json;

public class TiantianFundClient
{
    private readonly HttpClient _http;

    public TiantianFundClient(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.Add("Referer", "https://fundf10.eastmoney.com/");
    }

    public async Task<FundGz> GetRealTimeGzAsync(string fundCode)
    {
        var url = $"https://fundgz.1234567.com.cn/js/{fundCode}.js?rt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var jsonp = await _http.GetStringAsync(url);

        // 解析 JSONP: jsonpgz({...});
        var match = Regex.Match(jsonp, @"jsonpgz\((.*)\);?");
        if (!match.Success) throw new Exception("Invalid JSONP response");
        var json = match.Groups[1].Value;
        return JsonSerializer.Deserialize<FundGz>(json);
    }

    public async Task<List<HistoryNav>> GetHistoryNavAsync(string fundCode, int pageIndex = 1, int pageSize = 20)
    {
        var url = $"https://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex={pageIndex}&pageSize={pageSize}";
        var json = await _http.GetStringAsync(url);
        var resp = JsonSerializer.Deserialize<LsjzResponse>(json);
        return resp.Data.LSJZList;
    }
}

public record FundGz(string Fundcode, string Name, string Dwjz, string Gsz, string Gszzl, string Gztime);
public record HistoryNav(string FSRQ, string DWJZ, string JZZZL, string LJJZ);

// 使用
var client = new TiantianFundClient(httpClient);
var gz = await client.GetRealTimeGzAsync("000001");
Console.WriteLine($"基金 {gz.Name} 估算净值 {gz.Gsz}, 涨跌幅 {gz.Gszzl}%");
```

## 6. 最佳实践

- **缓存**: 公开数据接口有访问频率限制，建议本地缓存（如 Redis），TTL 5 分钟。
- **Referer 头**: 部分接口（如 `lsjz`）必须带 `Referer: https://fundf10.eastmoney.com/`，否则 403。
- **JSONP 解析**: 估值接口返回 JSONP，需正则提取 JSON 部分。
- **交易日**: 估值仅在交易日 9:30-15:00 更新，非交易时段返回最近一次估值。
- **数据准确性**: 估值仅供参考，最终以基金公司披露的净值（`dwjz`）为准。
- **机构交易**: 交易 API 需机构签约，且涉及客户资金，必须走合规流程。
