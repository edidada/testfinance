using Wealth.Api;
using Wealth.Api.Clearing;
using Wealth.Api.Compliance;
using Wealth.Api.Fee;
using Wealth.Api.Products;
using Wealth.Api.RiskControl;
using Wealth.Api.Ta;
using Wealth.Api.Trading;
using Wealth.Api.Valuation;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 主服务
builder.Services.AddSingleton<WealthService>();

// Valuation 估值
builder.Services.AddSingleton<NavHistoryService>();

// Fee 费用（纯静态方法，无需注册）

// Trading 交易
builder.Services.AddSingleton<OrderBook>();
builder.Services.AddSingleton<MatchingEngine>();
builder.Services.AddSingleton<TradeLogService>();

// TA 登记过户
builder.Services.AddSingleton<TaAccountService>();
builder.Services.AddSingleton<TaConfirmationService>();
builder.Services.AddSingleton<NonTradeTransferService>();

// RiskControl 风控
builder.Services.AddSingleton<RiskAlertService>();

// Compliance 合规
builder.Services.AddSingleton<AmlRuleEngine>();
builder.Services.AddSingleton<IDualRecordingService, FakeDualRecordingService>();

// Products 产品
builder.Services.AddSingleton<ProductLifecycleService>();
builder.Services.AddSingleton<ProductFeeService>();

// Clearing 清算
builder.Services.AddSingleton<FundClearingService>();
builder.Services.AddSingleton<ShareClearingService>();
builder.Services.AddSingleton<ReconciliationFileGenerator>();
builder.Services.AddSingleton<ICustodianGateway, FakeCustodianGateway>();
builder.Services.AddSingleton<ClearingDiffService>();

var app = builder.Build();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ===== 理财主流程 =====
app.MapGet("/v1/products", (WealthService service) => Results.Ok(service.Products()));
app.MapGet("/v1/products/{code}", (string code, WealthService service) => Execute(() => service.Product(code)));
app.MapPost("/v1/products/{code}/nav", (string code, NavRequest body, WealthService service) => Execute(() => service.PublishNav(code, body.NavDate, body.Nav)));
app.MapPost("/v1/accounts", (OpenAccountRequest body, WealthService service) => { try { var account = service.OpenAccount(body.CustomerId); return Results.Created($"/v1/accounts/{account.Id}", account); } catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); } });
app.MapPost("/v1/accounts/{id:guid}/kyc", (Guid id, KycRequest body, WealthService service) => Execute(() => service.VerifyKyc(id, body.Accepted)));
app.MapPost("/v1/accounts/{id:guid}/risk-assessment", (Guid id, RiskRequest body, WealthService service) => Execute(() => service.AssessRisk(id, body.RiskLevel)));
app.MapPost("/v1/accounts/{id:guid}/subscriptions", (Guid id, TradeRequest body, WealthService service) => Execute(() => service.Subscribe(id, body.ProductCode, body.Amount)));
app.MapPost("/v1/accounts/{id:guid}/redemptions", (Guid id, RedeemRequest body, WealthService service) => Execute(() => service.Redeem(id, body.ProductCode, body.Units)));
app.MapGet("/v1/accounts/{id:guid}/valuation", (Guid id, WealthService service) => Execute(() => new { accountId = id, value = service.Valuation(id), currency = "CNY" }));
app.MapGet("/v1/accounts/{id:guid}/holdings", (Guid id, WealthService service) => Execute(() => service.Holdings(id)));
app.MapGet("/v1/accounts/{id:guid}/orders", (Guid id, WealthService service) => Execute(() => service.Orders(id)));
app.MapPost("/v1/orders/{id:guid}/confirmation", (Guid id, ConfirmRequest body, WealthService service) => Execute(() => service.ConfirmOrder(id, body.ConfirmDate)));

// ===== Valuation 估值 =====
app.MapPost("/v1/valuation/nav", (NavCalcRequest body) => Execute(() => NavCalculator.Calculate(body.ProductCode, body.NavDate, body.TotalAssets, body.TotalLiabilities, body.TotalUnits, body.PreviousNav, body.AccumulatedDividendPerUnit)));
app.MapPost("/v1/valuation/sheet", (ValuationSheetRequest body) => Execute(() => ValuationSheetGenerator.Generate(body.ProductCode, body.ValuationDate, body.Lines)));
app.MapPost("/v1/valuation/nav-history", (NavHistoryRequest body, NavHistoryService svc) => Execute(() => svc.Record(body.ProductCode, body.NavDate, body.UnitNav, body.AccumulatedNav)));
app.MapGet("/v1/valuation/{productCode}/max-drawdown", (string productCode, NavHistoryService svc) => Execute(() => new { productCode, maxDrawdown = svc.MaxDrawdown(productCode) }));
app.MapPost("/v1/valuation/fx-revalue", (FxRevalueRequest body) => Execute(() => FxRevaluationEngine.Revalue(body.Positions)));

// ===== Fee 费用 =====
app.MapPost("/v1/fees/management", (FeeAccrualRequest body) => Execute(() => FeeAccrualCalculator.ManagementFee(body.ProductCode, body.AccrualDate, body.Nav, body.TotalUnits, body.AnnualRate)));
app.MapPost("/v1/fees/subscription", (SubFeeRequest body) => Execute(() => SubscriptionFeeCalculator.Calculate(body.Amount)));
app.MapPost("/v1/fees/redemption", (RedFeeRequest body) => Execute(() => RedemptionFeeCalculator.Calculate(body.Units, body.Nav, body.HoldingDays)));

// ===== Trading 交易 =====
app.MapPost("/v1/trading/orders", (PlaceOrderRequest body, OrderBook svc) => Execute(() => svc.Place(body.ProductCode, body.Side, body.Price, body.Quantity)));
app.MapPost("/v1/trading/orders/{id:guid}/cancel", (Guid id, OrderBook svc) => Execute(() => svc.Cancel(id)));
app.MapPost("/v1/trading/match", (MatchRequest body, MatchingEngine svc) => Execute(() => svc.Match(body.BuyOrders, body.SellOrders)));
app.MapPost("/v1/trading/allocate", (AllocationRequestDto body) => Execute(() => ProRataAllocator.Allocate(new AllocationRequest(body.ProductCode, body.TotalOffering, body.Requests.Select(r => (r.AccountId, r.RequestedAmount)).ToArray()))));

// ===== TA 登记过户 =====
app.MapPost("/v1/ta/accounts", (OpenTaAccountRequest body, TaAccountService svc) => Execute(() => svc.Open(body.CustomerId, body.FundCode, body.ShareClass)));
app.MapPost("/v1/ta/accounts/{id:guid}/freeze", (Guid id, FreezeRequest body, TaAccountService svc) => Execute(() => svc.Freeze(id, body.Shares)));
app.MapPost("/v1/ta/confirmations", (TaSubscribeRequest body, TaConfirmationService svc) => Execute(() => svc.Subscribe(body.AccountId, body.Amount, body.Nav)));
app.MapPost("/v1/ta/confirmations/{id:guid}/confirm", (Guid id, TaConfirmRequest body, TaConfirmationService svc) => Execute(() => svc.Confirm(id, body.ConfirmDate)));
app.MapPost("/v1/ta/dividends", (DividendRequest body) => Execute(() => DividendProcessor.Process(body.AccountId, body.Shares, new Wealth.Api.Ta.DividendDeclaration(body.FundCode, body.RecordDate, body.ExDividendDate, body.PerUnitDividend, body.Method), body.Nav)));
app.MapPost("/v1/ta/conversions", (ConversionRequest body) => Execute(() => FundConversionService.Convert(body.AccountId, body.FromFund, body.ToFund, body.FromShares, body.FromNav, body.ToNav)));

// ===== RiskControl 风控 =====
app.MapPost("/v1/risk/concentration", (ConcentrationRequest body) => Execute(() => ConcentrationChecker.Check(body.Dimension, body.CurrentValue, body.TotalValue, body.MaxRatio)));
app.MapPost("/v1/risk/var", (VarRequest body) => Execute(() => VarCalculator.Historical(body.Returns, body.PortfolioValue, body.HoldingDays)));
app.MapPost("/v1/risk/restrictions", (RestrictionsRequest body) => Execute(() => InvestmentRestrictionChecker.Check((body.SingleIssuer, body.TotalValue), (body.SingleBond, body.TotalValue), (body.Equity, body.TotalValue), (body.LiquidAsset, body.TotalValue))));
app.MapPost("/v1/risk/alerts", (TriggerAlertRequest body, RiskAlertService svc) => Execute(() => svc.Trigger(body.RuleId, body.Description, body.Excess)));

// ===== Compliance 合规 =====
app.MapPost("/v1/compliance/suitability", (SuitabilityRequest body) => Execute(() => SuitabilityMatcher.Match(body.CustomerRiskLevel, body.ProductRiskLevel)));
app.MapPost("/v1/compliance/aml/large-cash", (AmlLargeCashRequest body, AmlRuleEngine svc) => Execute(() => new { alert = svc.CheckLargeCash(body.CustomerId, body.Amount, body.TransactionType) }));
app.MapPost("/v1/compliance/aml/structuring", (AmlStructuringRequest body, AmlRuleEngine svc) => Execute(() => new { alert = svc.CheckStructuring(body.CustomerId, body.Amount, DateTimeOffset.UtcNow) }));

// ===== Products 产品 =====
app.MapPost("/v1/fund-products", (CreateProductRequest body, ProductLifecycleService svc) => Execute(() => svc.Create(new FundProduct(body.Code, body.Name, body.ShareClass, body.RiskLevel, ProductPhase.Raising, body.MinSubscription, body.ManagementFeeRate, body.CustodyFeeRate, body.InceptionDate, body.MaturityDate))));
app.MapPost("/v1/fund-products/{code}/transition", (string code, TransitionRequest body, ProductLifecycleService svc) => Execute(() => svc.Transition(code, body.NewPhase)));
app.MapGet("/v1/share-classes", () => Results.Ok(ShareClassCatalog.All()));

// ===== Clearing 清算 =====
app.MapPost("/v1/clearing/fund", (FundClearingRequest body, FundClearingService svc) => Execute(() => svc.Clear(body.ProductCode, body.BusinessDate, body.SubscriptionAmount, body.RedemptionAmount)));
app.MapPost("/v1/clearing/share", (ShareClearingRequest body, ShareClearingService svc) => Execute(() => svc.Clear(body.AccountId, body.ProductCode, body.BusinessDate, body.Subscribed, body.Redeemed)));
app.MapPost("/v1/clearing/diffs", (ClearingDiffRequest body, ClearingDiffService svc) => Execute(() => svc.Detect(body.ProductCode, body.Type, body.TaAmount, body.CustodianAmount)));

app.Run();

static IResult Execute(Func<object> action) { try { return Results.Ok(action()); } catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); } catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); } catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); } }

// 请求 DTO
public sealed record OpenAccountRequest(string CustomerId);
public sealed record KycRequest(bool Accepted);
public sealed record TradeRequest(string ProductCode, decimal Amount);
public sealed record RedeemRequest(string ProductCode, decimal Units);
public sealed record RiskRequest(int RiskLevel);
public sealed record NavRequest(DateOnly NavDate, decimal Nav);
public sealed record ConfirmRequest(DateOnly ConfirmDate);
public sealed record NavCalcRequest(string ProductCode, DateOnly NavDate, decimal TotalAssets, decimal TotalLiabilities, decimal TotalUnits, decimal PreviousNav, decimal AccumulatedDividendPerUnit);
public sealed record ValuationSheetRequest(string ProductCode, DateOnly ValuationDate, IReadOnlyList<ValuationLine> Lines);
public sealed record NavHistoryRequest(string ProductCode, DateOnly NavDate, decimal UnitNav, decimal AccumulatedNav);
public sealed record FxRevalueRequest(IReadOnlyList<FxPosition> Positions);
public sealed record FeeAccrualRequest(string ProductCode, DateOnly AccrualDate, decimal Nav, decimal TotalUnits, decimal AnnualRate);
public sealed record SubFeeRequest(decimal Amount);
public sealed record RedFeeRequest(decimal Units, decimal Nav, int HoldingDays);
public sealed record PlaceOrderRequest(string ProductCode, OrderSide Side, decimal Price, decimal Quantity);
public sealed record MatchRequest(IReadOnlyList<TradeOrder> BuyOrders, IReadOnlyList<TradeOrder> SellOrders);
public sealed record AllocationRequestDto(string ProductCode, decimal TotalOffering, IReadOnlyList<AllocationRequestItem> Requests);
public sealed record AllocationRequestItem(Guid AccountId, decimal RequestedAmount);
public sealed record OpenTaAccountRequest(string CustomerId, string FundCode, string ShareClass);
public sealed record FreezeRequest(decimal Shares);
public sealed record TaSubscribeRequest(Guid AccountId, decimal Amount, decimal Nav);
public sealed record TaConfirmRequest(DateOnly ConfirmDate);
public sealed record DividendRequest(Guid AccountId, decimal Shares, string FundCode, DateOnly RecordDate, DateOnly ExDividendDate, decimal PerUnitDividend, DividendMethod Method, decimal Nav);
public sealed record ConversionRequest(Guid AccountId, string FromFund, string ToFund, decimal FromShares, decimal FromNav, decimal ToNav);
public sealed record ConcentrationRequest(string Dimension, decimal CurrentValue, decimal TotalValue, decimal MaxRatio);
public sealed record VarRequest(IReadOnlyList<decimal> Returns, decimal PortfolioValue, int HoldingDays);
public sealed record RestrictionsRequest(decimal SingleIssuer, decimal SingleBond, decimal Equity, decimal LiquidAsset, decimal TotalValue);
public sealed record TriggerAlertRequest(string RuleId, string Description, decimal Excess);
public sealed record SuitabilityRequest(int CustomerRiskLevel, int ProductRiskLevel);
public sealed record AmlLargeCashRequest(string CustomerId, decimal Amount, string TransactionType);
public sealed record AmlStructuringRequest(string CustomerId, decimal Amount);
public sealed record CreateProductRequest(string Code, string Name, string ShareClass, int RiskLevel, decimal MinSubscription, decimal ManagementFeeRate, decimal CustodyFeeRate, DateOnly InceptionDate, DateOnly? MaturityDate);
public sealed record TransitionRequest(ProductPhase NewPhase);
public sealed record FundClearingRequest(string ProductCode, DateOnly BusinessDate, decimal SubscriptionAmount, decimal RedemptionAmount);
public sealed record ShareClearingRequest(Guid AccountId, string ProductCode, DateOnly BusinessDate, decimal Subscribed, decimal Redeemed);
public sealed record ClearingDiffRequest(string ProductCode, ClearingDiffType Type, decimal TaAmount, decimal CustodianAmount);
public partial class Program { }
