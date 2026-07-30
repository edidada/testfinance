# 国内外金融科技主流供应商全景 (2026)

本文档整理了支付、信贷、理财三大领域的国内外主流软件/服务供应商，分析其核心功能、性能指标与 API 能力，为技术选型提供参考。

---

## 1. 第三方支付 (对应 Payments.Api)

### 1.1 国际主流 (Global)

| 供应商 | 核心功能 | 性能指标 | API 特色 |
| :--- | :--- | :--- | :--- |
| **Stripe** | 全球在线支付、订阅计费、反欺诈 | API P99 < 200ms; SLA 99.999% | 极简 RESTful API；强大的 SDK (含 .NET)；Webhooks；测试沙箱完善 |
| **Adyen** | 全球收单、本地支付方式、风险控制 | 年处理交易量 > 1 万亿欧元；API P99 < 300ms | 统一的 Payments API；支持 200+ 本地支付方式；Cloud 风控引擎 |
| **PayPal** | 跨境支付、P2P 转账、商业收款 | 全球 400M+ 用户；API 稳定 | RESTful API；支持 Mass Payouts；PayPal Checkout 组件 |
| **Braintree (PayPal 旗下)** | 托管式支付 (Drop-in UI)、订阅管理 | P99 < 250ms | 客户端 SDK (JS, iOS, Android)；客户库 (Vault) 管理 |
| **Square** | 全渠道支付 (POS + 在线)、商户端管理 | 北美市场领先 | 统一 API；Square API Explorer 便于调试；Connect API 支持多边结算 |
| **Checkout.com** | 全球收单、本地路由、统一 API | 处理 100+ 货币；API P99 < 150ms | 单一 API 接入多支付方式；智能路由优化成功率 |

### 1.2 国内主流 (China)

| 供应商 | 核心功能 | 性能指标 | API 特色 |
| :--- | :--- | :--- | :--- |
| **支付宝 (Alipay)** | C2B/B2B 支付、生活缴费、跨境结算 | 双十一峰值 > 10 万 TPS | 开放平台 API；支付宝 SDK (含 .NET)；丰富的场景化 API (当面付、手机网站支付等) |
| **微信支付 (WeChat Pay)** | 社交支付、商户收款、小程序支付 | 日均交易量 > 10 亿 | API v3 (RESTful + JSON)；API v2 (XML，兼容)；商户平台自动生成签名 |
| **银联 (UnionPay)** | 银行卡转接、闪付、云闪付 | 全球受理网络 | 银联开放平台；UPOP (在线支付)；QuickPass API |
| **云闪付 (UnionPay 旗下)** | 移动支付、跨境支付 | 全国覆盖率高 | 云闪付 App API；跨境汇款 API |
| **拉卡拉 (Lakala)** | 收单服务、普惠金融 | 国内收单前三 | 开放平台 API；商户进件、交易、结算全流程接口 |
| **汇付天下 (Huifu)** | 支付清算、跨境结算、供应链金融 | 持牌支付机构 | 汇付 API；支持分账、代付、跨境收单 |

### 1.3 核心能力对比 (支付)

| 能力维度 | 国际供应商 | 国内供应商 |
| :--- | :--- | :--- |
| **本地支付方式** | 支持 200+ (Adyen, Checkout.com) | 微信、支付宝、银联为主 |
| **跨境结算** | 原生支持多币种本地收单 (Stripe, Adyen) | 依赖跨境业务专项 (支付宝跨境、银联国际) |
| **反欺诈** | 内置 AI 风控 (Stripe Radar, Adyen RevenueProtect) | 对接第三方风控 (如蚂蚁风控、腾讯安全) |
| **对账/清算** | T+0 实时对账单 (大多数供应商) | T+1 日终对账为主，T+0 实时为辅 |
| **SDK 支持** | 完善的 .NET/Java/Node SDK | 主要提供 Java/PHP SDK，.NET 需自行封装 |

---

## 2. 信贷与风控 (对应 Lending.Api)

### 2.1 国际主流 (Global)

| 供应商 | 核心功能 | 性能指标 | API 特色 |
| :--- | :--- | :--- | :--- |
| **FICO** | 信用评分、反欺诈、决策管理 | FICO Score 8/9 全球标准 | FICO Platform API；决策引擎 (FICO Blaze)；实时评分 API |
| **Experian** | 征信报告、身份验证、反欺诈 | 全球 30+ 国家有 bureau | Connect API；Identity Verification API；Credit Check API |
| **TransUnion** | 征信、风险管理、营销洞察 | 北美、南非等市场领先 | TrueVision API；信用报告查询 API |
| **Equifax** | 征信、劳动力验证、身份验证 | 全球四大征信之一 | Ignite API；微服务架构 |
| **KYC-Chain (Onfido)** | 远程开户 (KYC)、身份核验、活体检测 | 全球合规；P99 < 2s | 托管式 SDK (Web, iOS, Android)；RESTful API |
| **Plaid** | 银行数据聚合、信用评估、财务健康度 | 美国覆盖 12,000+ 金融机构 | Products API (Auth, Income, Assets, Liabilities)；稳定的 OAuth 流程 |
| **Credit Karma** | 信用监控、评分模拟器 | C 端免费服务 | 主要 C 端产品，B 端 API 较少 |

### 2.2 国内主流 (China)

| 供应商 | 核心功能 | 性能指标 | API 特色 |
| :--- | :--- | :--- | :--- |
| **中国人民银行征信中心** | 企业/个人征信报告 | 国家级数据 | 征信查询接口 (需持牌机构接入)；企业征信分（中诚信等评级） |
| **百行征信** | 个人征信、反欺诈 | 国内首家持牌个人征信 | 信用评估 API；反欺诈 API |
| **朴道征信** | 个人征信、小微金融风控 | 第二家个人征信牌照 | 风控评分 API；反欺诈 API |
| **腾讯安全** | 天御风控、反欺诈、内容安全 | 毫秒级响应 | 天御风控 API (设备指纹、IP 风险、行为模式)；反欺诈 API |
| **蚂蚁集团 (蚂蚁智信)** | 智能风控、反洗钱 | 支付宝级风控能力 | 芝麻信用分 API (授权场景)；风控产品 API |
| **同盾科技** | 反欺诈、信用评估、设备指纹 | 国内头部第三方风控 | 反欺诈 API；信用评分 API；设备指纹 SDK |
| **中科富通 (FTC)** | 反欺诈、身份核验、活体检测 | 基于 AI | 人脸识别 API；活体检测 SDK |
| **银联智策** | 信用评估、风险监控 | 银联数据支持 | 信用评分 API；风控 API |

### 2.3 核心能力对比 (信贷风控)

| 能力维度 | 国际供应商 | 国内供应商 |
| :--- | :--- | :--- |
| **数据源** | 传统征信 (Experian, TransUnion) + alternative data (Plaid) | 人行征信 + 行为数据 + 运营商数据 |
| **评分模型** | FICO Score (通用) + 定制化模型 | 自研评分卡 + 机器学习模型 |
| **决策引擎** | 可视化规则引擎 (FICO Blaze, SAS) | 规则引擎 + 模型服务 (多为自研) |
| **AI 风控** | 强调可解释性 (XAI)，满足监管 | 深度学习为主，部分场景可解释性不足 |
| **监管合规** | GDPR, CCPA, Fair Credit Reporting Act | 《个人信息保护法》, 《征信业管理条例》 |

---

## 3. 投资理财与资产管理 (对应 Wealth.Api)

### 3.1 国际主流 (Global)

| 供应商 | 核心功能 | 性能指标 | API 特色 |
| :--- | :--- | :--- | :--- |
| **BlackRock (iShares)** | ETF 管理、风险分析、投资组合 | 全球最大资管 (10T+ AUM) | Aladdin 平台 (机构)；iShares Core API |
| **Vanguard** | 指数基金、养老金、经纪业务 | 全球第二大共同基金 | 主要通过经销商渠道，API 较少直接开放 |
| **Fidelity** | 全品类投资、养老金、财富管理 | 美国头部综合金融 | 开发者 API (Fidelity Developer Network)；Brokerage API |
| **Interactive Brokers** | 全球多资产交易、API 驱动 | 低延迟，多市场接入 | TWS API (Java, C++, Python)；IBKR API (REST) |
| **Robinhood** | 零佣金交易、散户友好 | C 端用户庞大 | Web API (非官方)；Crypto API |
| **Calastone (Java Motion)** | 全球基金交易网络 | 连接 40+ 国家 | Calastone Connect API；标准化基金订单路由 |
| **Refinitiv (LSEG)** | 金融数据、定价、交易 | 全球金融数据巨头 | Refinitiv Data API；Elektron API |
| **Bloomberg** | 金融终端、数据、交易 | 行业标准 | B-PIPE API；Market Data API |

### 3.2 国内主流 (China)

| 供应商 | 核心功能 | 性能指标 | API 特色 |
| :--- | :--- | :--- | :--- |
| **易方达基金** | 公募基金管理 | 国内头部公募 | 主要直销/代销，API 面向机构 |
| **华夏基金** | 公募、ETF、养老金 | 国内头部公募 | 机构 API；TA 系统对接 |
| **天弘基金 (余额宝)** | 货币基金、互联网理财 | 余额宝全球最大货币基金 | 阿里生态 API；直销 API |
| **蚂蚁财富 (支付宝)** | 基金代销、智能投顾 | C 端最大理财入口 | 主要通过支付宝 App；机构可对接开放平台 |
| **天天基金网 (东方财富)** | 基金代销、数据 | 国内最大独立基金销售 | 数据 API (行情、估值)；基金筛选 API |
| **中国结算 (中证登)** | TA 过户、登记结算 | 国家级基础设施 | TA 接口 (需报备)；DVP 结算接口 |
| **恒生电子** | 金融 IT 解决方案 | 国内最大金融软件商 | 投资交易系统 API；TA/估值系统 |
| **赢时胜** | 资管 IT、TA、估值 | 公募/私募核心系统供应商 | 赢时胜 API；TA 系统 |

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

*   **支付**：Stripe (北美) / Adyen (欧洲/全球)
*   **信贷风控**：FICO (评分) + Plaid (银行数据) + Onfido (KYC)
*   **理财**：Interactive Brokers (交易) + Calastone (基金) + Refinitiv (数据)

### 4.2 境内业务

*   **支付**：支付宝 / 微信支付 (C 端) + 银联 (B 端/跨境)
*   **信贷风控**：人行征信 + 腾讯安全/同盾科技 (反欺诈) + 朴道征信 (评分)
*   **理财**：蚂蚁财富/天天基金 (代销) + 中证登 (TA) + 恒生电子 (IT)

### 4.3 混合/跨境业务

*   **跨境支付**：支付宝跨境 / 银联国际 / Adyen (本地收单)
*   **全球理财**：Interactive Brokers / Fidelity / BlackRock iShares
*   **技术栈适配**：优先选择提供完善 .NET SDK 的供应商（如 Stripe, Adyen, Fidelity），降低集成成本。
