# 国内外金融科技主流供应商全景 (2026)

本文档整理支付、信贷、理财三大领域的国内外主流供应商，按「C 端网页/B 端 API 功能交付形态」横向对比，并提供代表性供应商的**接口出入参详细文档**入口，为技术选型与对接开发提供参考。

> 📌 **文档结构**
> - **本主文档**：供应商全景概览 + C/B 端功能交付形态对照表 + 性能/API 特色。
> - **子文档**：代表性供应商的详细 API 出入参文档，位于 [`docs/vendors/`](./vendors/) 目录下，按 `领域/地域` 组织。
>
> 📂 **关联文档**：本文聚焦**第三方可对接 API 服务**（如 Stripe、支付宝）。若需了解**面向银行/金融机构的 B2B IT 解决方案商**（如宇信科技、长亮科技、神州信息等），请见 [china-fintech-vendors.md](./china-fintech-vendors.md)。

---

## 1. 第三方支付 (对应 [Payments.Api](../src/Payments.Api/))

### 1.1 国际主流 (Global)

| 供应商 | 核心功能 | 性能指标 | API 特色 | 详细文档 |
| :--- | :--- | :--- | :--- | :--- |
| **Stripe** | 全球在线支付、订阅计费、反欺诈 | API P99 < 200ms; SLA 99.999% | 极简 RESTful；强大 .NET SDK；Webhooks；沙箱完善 | [📄 API 文档](./vendors/payments/global/stripe.md) |
| **Adyen** | 全球收单、本地支付方式、风险控制 | 年处理交易量 > 1 万亿欧元; P99 < 300ms | 统一 Payments API；200+ 本地支付方式；Cloud 风控 | [📄 API 文档](./vendors/payments/global/adyen.md) |
| **PayPal** | 跨境支付、P2P 转账、商业收款 | 全球 400M+ 用户 | RESTful API；Mass Payouts；Checkout 组件 | — |
| **Braintree** | 托管式支付 (Drop-in UI)、订阅管理 | P99 < 250ms | 客户端 SDK (JS/iOS/Android)；Vault 管理 | — |
| **Square** | 全渠道支付 (POS + 在线)、商户管理 | 北美市场领先 | 统一 API；Square API Explorer；Connect API | — |
| **Checkout.com** | 全球收单、本地路由、统一 API | 100+ 货币; P99 < 150ms | 单一 API 多支付方式；智能路由优化成功率 | — |

#### 1.1.1 国际供应商功能交付形态

| 供应商 | C 端功能（终端用户触达） | B 端 API 功能（商户/机构调用） | 交付形态 |
| :--- | :--- | :--- | :--- |
| **Stripe** | Checkout 托管收银台、Payment Element、Customer Portal、Apple/Google Pay 按钮 | PaymentIntents、Charges、Refunds、SetupIntents、Webhooks、Customers、Subscriptions | Drop-in SDK + RESTful |
| **Adyen** | Drop-in 组件、Web Components、Apple/Google Pay、本地支付方式组件 | `/payments`、`/payments/details`、`/refunds`、`/cancels`、Notification Webhook | Drop-in SDK + RESTful |
| **PayPal** | PayPal Checkout 按钮、Smart Payment Buttons、Pay Later | Orders API、Payments API、Payouts API、Webhooks | JS SDK + RESTful |
| **Braintree** | Drop-in UI、Hosted Fields、Apple/Google Pay 客户端 SDK | Transaction API、Customer API、Payment Method API、Webhooks | 客户端 SDK + 服务端 RESTful |
| **Square** | Square Online Store、POS App、Payment Form | Payments API、Orders API、Catalog API、Webhooks | SDK + RESTful |
| **Checkout.com** | Hosted Payments Page、Frames (Card Component)、Apple/Google Pay | `/payments`、`/payments/{id}/captures`、`/payments/{id}/refunds`、Webhooks | Drop-in + RESTful |

### 1.2 国内主流 (China)

| 供应商 | 核心功能 | 性能指标 | API 特色 | 详细文档 |
| :--- | :--- | :--- | :--- | :--- |
| **支付宝 (Alipay)** | C2B/B2B 支付、生活缴费、跨境结算 | 双十一峰值 > 10 万 TPS | 开放平台 RESTful；SDK (含 .NET)；场景化 API | [📄 API 文档](./vendors/payments/china/alipay.md) |
| **微信支付 (WeChat Pay)** | 社交支付、商户收款、小程序支付 | 日均交易量 > 10 亿 | API v3 (RESTful + JSON + RSA 签名) | [📄 API 文档](./vendors/payments/china/wechat-pay.md) |
| **银联 (UnionPay)** | 银行卡转接、闪付、云闪付 | 全球受理网络 | 银联开放平台；UPOP；QuickPass API | — |
| **云闪付** | 移动支付、跨境支付 | 全国覆盖率高 | 云闪付 App API；跨境汇款 API | — |
| **拉卡拉 (Lakala)** | 收单服务、普惠金融 | 国内收单前三 | 开放平台 API；商户进件/交易/结算全流程 | — |
| **汇付天下 (Huifu)** | 支付清算、跨境结算、供应链金融 | 持牌支付机构 | 汇付 API；分账、代付、跨境收单 | — |

#### 1.2.1 国内供应商功能交付形态

| 供应商 | C 端功能 | B 端 API 功能 | 交付形态 |
| :--- | :--- | :--- | :--- |
| **支付宝** | App 支付、手机网站支付、小程序支付、当面付（扫码/被扫）、刷脸支付、花呗分期 | `alipay.trade.pay`、`alipay.trade.query`、`alipay.trade.refund`、`alipay.trade.close`、`alipay.data.dataservice.bill.downloadurl.query`、异步通知 | H5/SDK + 开放平台 RESTful (网关模式) |
| **微信支付** | JSAPI 支付、小程序支付、Native 支付、APP 支付、付款码支付、刷脸支付 | `/v3/pay/transactions/jsapi`、`/v3/pay/transactions/native`、`/v3/refund/domestic/refunds`、`/v3/bill/tradebill`、回调通知 | H5/SDK + RESTful v3 (RSA 签名) |
| **银联** | 云闪付 App、控件支付、扫码支付、PC 网关支付 | 银联全渠道支付接口、对账文件接口、退货接口 | SDK + 网关报文 (Form/JSON) |
| **拉卡拉** | 收钱码、智能 POS、App 收款 | 商户进件、统一下单、订单查询、退款、清算 | 开放平台 RESTful |
| **汇付天下** | 收银台、聚合支付页面 | 收款接口、退款接口、分账接口、代付接口 | 收银台 + RESTful |

### 1.3 核心能力对比 (支付)

| 能力维度 | 国际供应商 | 国内供应商 |
| :--- | :--- | :--- |
| **本地支付方式** | 支持 200+ (Adyen, Checkout.com) | 微信、支付宝、银联为主 |
| **跨境结算** | 原生支持多币种本地收单 (Stripe, Adyen) | 依赖跨境业务专项 (支付宝跨境、银联国际) |
| **反欺诈** | 内置 AI 风控 (Stripe Radar, Adyen RevenueProtect) | 对接第三方风控 (蚂蚁风控、腾讯安全) |
| **对账/清算** | T+0 实时对账单 | T+1 日终对账为主，T+0 实时为辅 |
| **SDK 支持** | 完善的 .NET/Java/Node SDK | 主要 Java/PHP SDK，.NET 需自行封装 |
| **签名机制** | Bearer Token (Stripe) / HMAC (Adyen) | RSA2 (支付宝) / RSA-SHA256 (微信 v3) |

---

## 2. 信贷与风控 (对应 [Lending.Api](../src/Lending.Api/))

> 信贷风控领域 **B 端 API 占绝对主导**，C 端功能多为托管页面或 SDK（如 KYC 活体检测），故采用「C 端 / B 端分离表」呈现。

### 2.1 国际主流 (Global)

| 供应商 | 核心功能 | 性能指标 | API 特色 | 详细文档 |
| :--- | :--- | :--- | :--- | :--- |
| **FICO** | 信用评分、反欺诈、决策管理 | FICO Score 8/9 全球标准 | FICO Platform API；Blaze 决策引擎 | — |
| **Experian** | 征信报告、身份验证、反欺诈 | 全球 30+ 国家 bureau | Connect API；Identity Verification API | — |
| **TransUnion** | 征信、风险管理、营销洞察 | 北美、南非等市场领先 | TrueVision API；信用报告查询 | — |
| **Equifax** | 征信、劳动力验证、身份验证 | 全球四大征信之一 | Ignite API；微服务架构 | — |
| **Onfido** | 远程开户 (KYC)、身份核验、活体检测 | 全球合规; P99 < 2s | 托管 SDK (Web/iOS/Android)；RESTful | [📄 API 文档](./vendors/lending/global/onfido.md) |
| **Plaid** | 银行数据聚合、信用评估、财务健康度 | 美国 12,000+ 金融机构 | Products API (Auth/Income/Assets/Liabilities)；OAuth | [📄 API 文档](./vendors/lending/global/plaid.md) |

#### 2.1.1 国际供应商 - C 端功能

| 供应商 | C 端功能（终端用户触达） | 交付形态 |
| :--- | :--- | :--- |
| **FICO** | （B2B 为主，C 端通过银行展示评分） | 嵌入式 UI |
| **Experian** | Experian Boost (用户授权上传账单)、身份验证托管页 | Web 托管页 + SDK |
| **Onfido** | 活体检测 SDK、证件拍摄引导、实时反馈页 | iOS/Android/Web SDK (Capture SDK) |
| **Plaid** | Plaid Link（银行账号连接弹窗，用户输入网银凭证） | Drop-in 模块 (Web/iOS/Android) |

#### 2.1.2 国际供应商 - B 端 API 功能

| 供应商 | B 端 API 接口 | 认证方式 |
| :--- | :--- | :--- |
| **FICO** | Score API、Decision Engine API、Blaze Rules API | OAuth2 / API Key |
| **Experian** | Credit Report API、Identity Verification API、Fraud Score API | OAuth2 |
| **Onfido** | `/v3/applicants`、`/v3/documents`、`/v3/live_videos`、`/v3/checks`、Webhook | Bearer Token |
| **Plaid** | `/link/token/create`、`/item/public_token/exchange`、`/accounts/get`、`/transactions/get`、`/identity/get`、`/income/get` | Client ID + Secret |

### 2.2 国内主流 (China)

| 供应商 | 核心功能 | 性能指标 | API 特色 | 详细文档 |
| :--- | :--- | :--- | :--- | :--- |
| **人行征信中心** | 企业/个人征信报告 | 国家级数据 | 征信查询接口（需持牌机构接入） | — |
| **百行征信** | 个人征信、反欺诈 | 国内首家持牌个人征信 | 信用评估 API；反欺诈 API | — |
| **朴道征信** | 个人征信、小微金融风控 | 第二家个人征信牌照 | 风控评分 API；反欺诈 API | [📄 API 文档](./vendors/lending/china/pudao.md) |
| **腾讯安全 (天御)** | 反欺诈、内容安全 | 毫秒级响应 | 天御风控 API (设备指纹/IP/行为) | — |
| **蚂蚁集团** | 智能风控、反洗钱 | 支付宝级风控能力 | 芝麻信用分 API；风控产品 API | — |
| **同盾科技** | 反欺诈、信用评估、设备指纹 | 国内头部第三方风控 | 反欺诈 API；信用评分 API；设备指纹 SDK | [📄 API 文档](./vendors/lending/china/tongdun.md) |

#### 2.2.1 国内供应商 - C 端功能

| 供应商 | C 端功能 | 交付形态 |
| :--- | :--- | :--- |
| **人行征信** | 个人征信报告查询页（柜台/网上查询） | 政府网站 |
| **朴道征信** | 用户授权页（征信查询授权） | Web 托管授权页 |
| **腾讯天御** | 验证码、设备指纹采集（无感） | 嵌入式 JS SDK |
| **蚂蚁集团** | 芝麻信用分授权页、刷脸认证 SDK | H5 授权页 + 阿里 SDK |
| **同盾科技** | 设备指纹采集 SDK、智能验证码 | 嵌入式 SDK (JS/iOS/Android) |

#### 2.2.2 国内供应商 - B 端 API 功能

| 供应商 | B 端 API 接口 | 认证/签名 |
| :--- | :--- | :--- |
| **人行征信** | 个人信用报告查询、企业信用报告查询、征信异议 | 专线 + 证书 (机构接入) |
| **朴道征信** | 信用评分、身份核验、反欺诈、多头借贷 | API Key + RSA 签名 |
| **腾讯天御** | 营销反欺诈 API、信贷反欺诈 API、设备指纹查询 API、IP 风险评估 | AppID + 签名 (HMAC-SHA256) |
| **蚂蚁集团** | 芝麻信用评分查询、身份认证、实名认证、刷脸结果验证 | 开放平台签名 (RSA2) |
| **同盾科技** | 设备指纹查询、IP 代理识别、地址反欺诈、信用评分、多头借贷 | API Key + 签名 |

### 2.3 核心能力对比 (信贷风控)

| 能力维度 | 国际供应商 | 国内供应商 |
| :--- | :--- | :--- |
| **数据源** | 传统征信 (Experian/TransUnion) + alternative data (Plaid) | 人行征信 + 行为数据 + 运营商数据 |
| **评分模型** | FICO Score (通用) + 定制化模型 | 自研评分卡 + 机器学习模型 |
| **决策引擎** | 可视化规则引擎 (FICO Blaze, SAS) | 规则引擎 + 模型服务 (多为自研) |
| **AI 风控** | 强调可解释性 (XAI)，满足监管 | 深度学习为主，部分场景可解释性不足 |
| **监管合规** | GDPR, CCPA, Fair Credit Reporting Act | 《个人信息保护法》, 《征信业管理条例》 |
| **典型延迟** | FICO 实时评分 < 100ms; Plaid < 500ms | 腾讯天御 < 50ms; 同盾 < 200ms |

---

## 3. 投资理财与资产管理 (对应 [Wealth.Api](../src/Wealth.Api/))

### 3.1 国际主流 (Global)

| 供应商 | 核心功能 | 性能指标 | API 特色 | 详细文档 |
| :--- | :--- | :--- | :--- | :--- |
| **BlackRock (iShares)** | ETF 管理、风险分析、投资组合 | 全球最大资管 (10T+ AUM) | Aladdin 平台；iShares Core API | — |
| **Vanguard** | 指数基金、养老金、经纪业务 | 全球第二大共同基金 | 主要经销商渠道，API 较少开放 | — |
| **Fidelity** | 全品类投资、养老金、财富管理 | 美国头部综合金融 | Fidelity Developer Network；Brokerage API | [📄 API 文档](./vendors/wealth/global/fidelity.md) |
| **Interactive Brokers** | 全球多资产交易、API 驱动 | 低延迟，多市场接入 | TWS API (Java/C++/Python)；IBKR REST | [📄 API 文档](./vendors/wealth/global/ibkr.md) |
| **Robinhood** | 零佣金交易、散户友好 | C 端用户庞大 | Web API；Crypto API | — |
| **Calastone** | 全球基金交易网络 | 连接 40+ 国家 | Calastone Connect API；基金订单路由 | — |
| **Refinitiv (LSEG)** | 金融数据、定价、交易 | 全球金融数据巨头 | Refinitiv Data API；Elektron API | — |
| **Bloomberg** | 金融终端、数据、交易 | 行业标准 | B-PIPE API；Market Data API | — |

#### 3.1.1 国际供应商功能交付形态

| 供应商 | C 端功能 | B 端 API 功能 | 交付形态 |
| :--- | :--- | :--- | :--- |
| **BlackRock** | iShares 投资者门户、组合可视化 | Aladdin Risk API、Portfolio Analytics API、iShares Product API | 机构平台 + RESTful |
| **Fidelity** | Fidelity.com 交易平台、退休规划器、Learning Center | Brokerage API、Quotes API、Historical Data API、OAuth2 授权 | Web + RESTful (OAuth2) |
| **Interactive Brokers** | TWS 桌面客户端、Client Portal Web、IBKR Mobile | REST API (历史/实时行情、下单、账户)、TWS API (Socket)、Web API | 桌面 + RESTful + Socket |
| **Robinhood** | Robinhood App (iOS/Android)、Web 交易页 | Orders API、Quotes API、Crypto API（受限） | App + RESTful |
| **Calastone** | （纯 B2B，无 C 端） | Order Routing API、Settlement API、Reconciliation API | RESTful + MQ (AS2) |
| **Refinitiv** | Refinitiv Workspace 终端、Eikon | Data API、Pricing API、Time Series API、News API | 终端 + RESTful + Streaming |

### 3.2 国内主流 (China)

| 供应商 | 核心功能 | 性能指标 | API 特色 | 详细文档 |
| :--- | :--- | :--- | :--- | :--- |
| **易方达基金** | 公募基金管理 | 国内头部公募 | 直销/代销，API 面向机构 | — |
| **华夏基金** | 公募、ETF、养老金 | 国内头部公募 | 机构 API；TA 系统对接 | — |
| **天弘基金 (余额宝)** | 货币基金、互联网理财 | 余额宝全球最大货币基金 | 阿里生态 API；直销 API | — |
| **蚂蚁财富 (支付宝)** | 基金代销、智能投顾 | C 端最大理财入口 | 支付宝 App；机构开放平台 | — |
| **腾讯理财通** | 基金代销、智能投顾 | 微信九宫格入口，腾讯官方出品 | 开放平台 (OAuth2 + RSA2) | [📄 API 文档](./vendors/wealth/china/tencent-liquetong.md) |
| **天天基金网 (东方财富)** | 基金代销、数据 | 国内最大独立基金销售 | 数据 API (行情/估值)；基金筛选 API | [📄 API 文档](./vendors/wealth/china/tiantian.md) |
| **中国结算 (中证登)** | TA 过户、登记结算 | 国家级基础设施 | TA 接口（需报备）；DVP 结算接口 | — |
| **恒生电子** | 金融 IT 解决方案 | 国内最大金融软件商 | 投资交易系统 API；TA/估值系统 | [📄 API 文档](./vendors/wealth/china/hundsun.md) |
| **赢时胜** | 资管 IT、TA、估值 | 公募/私募核心系统供应商 | 赢时胜 API；TA 系统 | — |

#### 3.2.1 国内供应商功能交付形态

| 供应商 | C 端功能 | B 端 API 功能 | 交付形态 |
| :--- | :--- | :--- | :--- |
| **天弘基金** | 余额宝（支付宝内）、天弘 App | 余额宝申购/赎回接口、收益查询、对账文件 | 阿里生态内嵌 + RESTful |
| **蚂蚁财富** | 支付宝内基金频道、智能投顾、定投 | 基金产品查询、下单、撤单、持仓查询、对账 | 支付宝开放平台 (网关模式) |
| **腾讯理财通** | 微信九宫格「理财通」、QQ 钱包、理财通 App | OAuth 授权、产品查询、申购/赎回、持仓查询、风险测评、Webhook | 微信 H5 + RESTful (OAuth2 + RSA2) |
| **天天基金** | 天天基金 App、Web 交易页、定投计划 | 行情数据 API、估值数据 API、基金详情 API、（交易 API 仅面向合作机构） | App + RESTful |
| **恒生电子** | （B2B 软件，无 C 端） | ARES 交易系统、TA 系统、估值系统、清算系统接口 | 私有协议 + Web Service |
| **中证登** | （基础设施，无 C 端） | TA 过户接口、登记接口、结算接口、对账接口 | 专线 + 报文 (FIX/自定义) |

### 3.3 核心能力对比 (理财资管)

| 能力维度 | 国际供应商 | 国内供应商 |
| :--- | :--- | :--- |
| **资管规模** | BlackRock (10T+ USD) | 易方达、华夏 (万亿 RMB) |
| **产品丰富度** | 股票、债券、另类、私募、对冲 | 公募为主，另类/私募门槛高 |
| **交易通道** | 全球多市场 (IBKR, Calastone) | 沪深北 + 港股通 (有限) |
| **IT 系统** | Aladdin, Murex, Sophis | 恒生电子、赢时胜 (市场份额高) |
| **智能投顾** | Wealthfront, Betterment | 蚂蚁财富、天弘基金等 (规模大) |
| **监管环境** | SEC, MiFID II, FCA | 证监会、基金业协会 |
| **TA 系统** | Euroclear, Clearstream | 中证登、恒生电子 (自研) |

---

## 4. 技术选型建议

### 4.1 境外业务

- **支付**：Stripe (北美) / Adyen (欧洲/全球)
- **信贷风控**：FICO (评分) + Plaid (银行数据) + Onfido (KYC)
- **理财**：Interactive Brokers (交易) + Calastone (基金) + Refinitiv (数据)

### 4.2 境内业务

- **支付**：支付宝 / 微信支付 (C 端) + 银联 (B 端/跨境)
- **信贷风控**：人行征信 + 腾讯安全/同盾科技 (反欺诈) + 朴道征信 (评分)
- **理财**：蚂蚁财富/天天基金 (代销) + 中证登 (TA) + 恒生电子 (IT)

### 4.3 混合/跨境业务

- **跨境支付**：支付宝跨境 / 银联国际 / Adyen (本地收单)
- **全球理财**：Interactive Brokers / Fidelity / BlackRock iShares
- **技术栈适配**：优先选择提供完善 .NET SDK 的供应商（如 Stripe, Adyen, Fidelity），降低集成成本。

### 4.4 系统集成商选型

若需求是**建设银行/金融机构内部 IT 系统**（核心系统、信贷系统、支付前置、TA 系统等），而非对接第三方 API，请见 [china-fintech-vendors.md](./china-fintech-vendors.md)。该文档覆盖宇信科技、长亮科技、神州信息、润和软件、沐融科技、东方通、高伟达等 B2B 解决方案商的能力矩阵、功能点与接口参考设计。

---

## 5. 详细 API 出入参文档索引

下表汇总所有已编写详细接口文档的供应商。文档统一包含：**接口列表、URL、请求头、请求体字段表、请求示例、响应体字段表、成功响应示例、错误码、.NET SDK 示例**。

### 5.1 支付领域 (Payments)

| 地域 | 供应商 | 文档路径 | 核心接口示例 |
| :--- | :--- | :--- | :--- |
| 国际 | **Stripe** | [vendors/payments/global/stripe.md](./vendors/payments/global/stripe.md) | `POST /v1/payment_intents` |
| 国际 | **Adyen** | [vendors/payments/global/adyen.md](./vendors/payments/global/adyen.md) | `POST /payments` |
| 国内 | **支付宝** | [vendors/payments/china/alipay.md](./vendors/payments/china/alipay.md) | `alipay.trade.pay` |
| 国内 | **微信支付** | [vendors/payments/china/wechat-pay.md](./vendors/payments/china/wechat-pay.md) | `POST /v3/pay/transactions/jsapi` |

### 5.2 信贷风控领域 (Lending)

| 地域 | 供应商 | 文档路径 | 核心接口示例 |
| :--- | :--- | :--- | :--- |
| 国际 | **Plaid** | [vendors/lending/global/plaid.md](./vendors/lending/global/plaid.md) | `POST /link/token/create` |
| 国际 | **Onfido** | [vendors/lending/global/onfido.md](./vendors/lending/global/onfido.md) | `POST /v3/checks` |
| 国内 | **同盾科技** | [vendors/lending/china/tongdun.md](./vendors/lending/china/tongdun.md) | 反欺诈查询 API |
| 国内 | **朴道征信** | [vendors/lending/china/pudao.md](./vendors/lending/china/pudao.md) | 信用评分 API |

### 5.3 投资理财领域 (Wealth)

| 地域 | 供应商 | 文档路径 | 核心接口示例 |
| :--- | :--- | :--- | :--- |
| 国际 | **Interactive Brokers** | [vendors/wealth/global/ibkr.md](./vendors/wealth/global/ibkr.md) | `POST /v1/api/orders` |
| 国际 | **Fidelity** | [vendors/wealth/global/fidelity.md](./vendors/wealth/global/fidelity.md) | `POST /orders` |
| 国内 | **腾讯理财通** | [vendors/wealth/china/tencent-liquetong.md](./vendors/wealth/china/tencent-liquetong.md) | `POST /v1/accounts/{id}/subscriptions` |
| 国内 | **天天基金** | [vendors/wealth/china/tiantian.md](./vendors/wealth/china/tiantian.md) | 基金详情 API |
| 国内 | **恒生电子** | [vendors/wealth/china/hundsun.md](./vendors/wealth/china/hundsun.md) | TA 申购接口 |

### 5.4 金融 IT 解决方案商（⚠️ 私有部署参考设计）

下列供应商为**面向银行/金融机构的 B2B 项目交付型**解决方案商，系统多为私有部署，接口规范以合同附件为准。详见 [china-fintech-vendors.md](./china-fintech-vendors.md)。

| 供应商 | 主能力域 | 文档路径 | 代表性接口 |
| :--- | :--- | :--- | :--- |
| **宇信科技** | 信贷 + 支付前置 | [vendors/solutions/yuxin.md](./vendors/solutions/yuxin.md) | 信贷进件、支付清算 |
| **长亮科技** | 银行核心 | [vendors/solutions/changliang.md](./vendors/solutions/changliang.md) | 存款开户、总账记账 |
| **神州信息** | 核心 + 信贷 + 财富 | [vendors/solutions/dci.md](./vendors/solutions/dci.md) | 核心账务、信贷审批 |
| **高伟达** | 核心 + 数字管理 | [vendors/solutions/gaoweida.md](./vendors/solutions/gaoweida.md) | 核心账务、监管报表 |
| **润和软件** | 信贷 + 财富 | [vendors/solutions/runhe.md](./vendors/solutions/runhe.md) | 互联网信贷放款 |
| **沐融科技** | 实时清算 | [vendors/solutions/murong.md](./vendors/solutions/murong.md) | RTGS 大额支付 |
| **东方通** | 中间件 | [vendors/solutions/dongfangtong.md](./vendors/solutions/dongfangtong.md) | TongLINK/Q 消息收发 |
