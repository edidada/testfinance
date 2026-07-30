using Lending.Api;
using Lending.Api.Bills;
using Lending.Api.Cbdc;
using Lending.Api.Components;
using Lending.Api.InLoan;
using Lending.Api.PostLoan;
using Lending.Api.PreLoan;
using Lending.Api.Products;
using Lending.Api.Reporting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 领域服务
builder.Services.AddSingleton<LendingService>();

// 基础组件
builder.Services.AddSingleton<IOutbox, InMemoryOutbox>();
builder.Services.AddSingleton<IAuditTrail, InMemoryAuditTrail>();
builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
builder.Services.AddSingleton<IRuleEngine, RuleEngine>();
builder.Services.AddSingleton<IScheduler, InMemoryScheduler>();
builder.Services.AddSingleton<IImagePlatform, InMemoryImagePlatform>();
builder.Services.AddHostedService<OutboxDispatcher>();

// 贷前
builder.Services.AddSingleton<SurveyService>();
builder.Services.AddSingleton<ICreditBureauGateway, ResilientCreditBureauGateway>(_ => new ResilientCreditBureauGateway(new FakeCreditBureauGateway()));

// 贷中
builder.Services.AddSingleton<CreditApprovalFlow>();
builder.Services.AddSingleton<CreditRuleSet>(_ => { var rs = new CreditRuleSet(_.GetRequiredService<IRuleEngine>()); rs.RegisterDefaults(); return rs; });

// 贷后
builder.Services.AddSingleton<EarlyRepaymentService>();
builder.Services.AddSingleton<CollectionService>();
builder.Services.AddSingleton<RiskWarningService>();

// 产品
builder.Services.AddSingleton<SupplyChainService>();

// 票据
builder.Services.AddSingleton<AcceptanceService>();
builder.Services.AddSingleton<DiscountService>();
builder.Services.AddSingleton<RediscountService>();
builder.Services.AddSingleton<BillPoolService>();

// 数字人民币
builder.Services.AddSingleton<CBDCWalletService>();
builder.Services.AddSingleton<SmartContractEngine>();

// 数据报送
builder.Services.AddSingleton<RegulatoryReportGenerator>();
builder.Services.AddSingleton<AssetReportService>();
builder.Services.AddSingleton<IDataPlatform, InMemoryDataPlatform>();

var app = builder.Build();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ===== 贷款主流程 =====
app.MapPost("/v1/loan-applications", (LoanApplication request, LendingService service) => { try { var loan = service.Submit(request); return Results.Created($"/v1/loans/{loan.Id}", loan); } catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); } });
app.MapGet("/v1/loans/{id:guid}", (Guid id, LendingService service) => Execute(() => service.Get(id)));
app.MapPost("/v1/loans/{id:guid}/decision", (Guid id, LendingService service) => Execute(() => service.Decide(id)));
app.MapPost("/v1/loans/{id:guid}/documents", (Guid id, DocumentRequest body, LendingService service) => Execute(() => service.AddDocument(id, body.Type, body.ObjectKey)));
app.MapPost("/v1/loans/{id:guid}/fraud-assessments", (Guid id, FraudRequest body, LendingService service) => Execute(() => service.AssessFraud(id, body.Provider, body.Decision, body.RiskScore, body.RulesHit)));
app.MapPost("/v1/loans/{id:guid}/contracts", (Guid id, ContractRequest body, LendingService service) => Execute(() => service.CreateContract(id, body.ContractNo)));
app.MapPost("/v1/loans/{id:guid}/contracts/sign", (Guid id, LendingService service) => Execute(() => service.SignContract(id)));
app.MapPost("/v1/loans/{id:guid}/disbursement", (Guid id, LendingService service) => Execute(() => service.Disburse(id)));
app.MapPost("/v1/loans/{id:guid}/installments/{sequence:int}/repayment", (Guid id, int sequence, LendingService service) => Execute(() => service.Repay(id, sequence)));
app.MapGet("/v1/post-loan/overdues/{asOf}", (DateOnly asOf, LendingService service) => Results.Ok(service.Overdue(asOf)));

// ===== 贷前扩展 =====
app.MapPost("/v1/loans/{id:guid}/survey", (Guid id, SurveyRequest body, SurveyService svc) => Execute(() => svc.Generate(id, body.CustomerSummary, body.LoanPurpose, body.RepaymentSource, body.EstimatedAnnualRevenue, body.RecommendedApprove)));
app.MapPost("/v1/financial-metrics", (FinancialStatement stmt) => Execute(() => FinancialMetricsCalculator.Compute(stmt)));
app.MapPost("/v1/loans/{id:guid}/internal-rating", (Guid id, InternalRatingRequest body) => Execute(() => InternalRatingEngine.Rate(id, body.RatingGrade, body.Exposure, body.CollateralValue)));
app.MapPost("/v1/credit-bureau/query", async (CreditBureauQuery query, ICreditBureauGateway gw) => { try { return Results.Ok(await gw.QueryAsync(query)); } catch (Exception) { return Results.StatusCode(503); } });

// ===== 贷中扩展 =====
app.MapPost("/v1/loans/{id:guid}/approval-flow", (Guid id, ApprovalFlowRequest body, CreditApprovalFlow flow) => Execute(() => flow.StartForLoan(id, body.RiskOfficers, body.Approvers)));
app.MapPost("/v1/rules/evaluate", (Dictionary<string, object> facts, CreditRuleSet rs) => Execute(() => rs.Evaluate(facts)));
app.MapPost("/v1/loans/{id:guid}/entrusted-payment", (Guid id, EntrustedPaymentRequest body) => Execute(() => PaymentChannelPolicy.Build(id, body.Account, body.Name, body.Bank, body.Amount, body.Purpose)));

// ===== 贷后扩展 =====
app.MapPost("/v1/loans/{id:guid}/early-repayment", (Guid id, EarlyRepaymentRequestDto body, LendingService loans, EarlyRepaymentService svc) => Execute(() => { var loan = loans.Get(id); var remaining = loan.Schedule.Where(x => !x.Paid).Sum(x => x.Principal); var count = loan.Schedule.Count(x => !x.Paid); return svc.Settle(new EarlyRepaymentRequest(id, body.Kind, body.Amount, DateTimeOffset.UtcNow), remaining, count, 0.08m); }));
app.MapPost("/v1/post-loan/collections", (OpenCollectionRequest body, CollectionService svc) => Execute(() => svc.Open(body.LoanId, body.DaysPastDue)));
app.MapPost("/v1/post-loan/collections/{caseId:guid}/actions", (Guid caseId, CollectionActionDto body, CollectionService svc) => Execute(() => svc.RecordAction(caseId, new CollectionAction(body.Type, body.Operator, body.Note, DateTimeOffset.UtcNow))));
app.MapPost("/v1/post-loan/risk-warnings", (RiskWarningRequest body, RiskWarningService svc) => Execute(() => svc.Evaluate(body.LoanId, body.Signals.Select(s => new RiskSignal(body.LoanId, s.SignalType, s.Description, s.Level, DateTimeOffset.UtcNow)))));

// ===== 产品 =====
app.MapGet("/v1/products", () => Results.Ok(ProductCatalog.All()));
app.MapPost("/v1/products/validate", (ProductValidateRequest body) => Execute(() => ProductCatalog.Validate(body.Kind, body.Amount, body.TermMonths, body.AnnualRate)));
app.MapPost("/v1/supply-chain/receivables", (ReceivableRequest body, SupplyChainService svc) => Execute(() => svc.UploadReceivable(new Receivable(Guid.NewGuid(), body.CoreEnterpriseId, body.Amount, body.DueDate, false))));

// ===== 票据 =====
app.MapPost("/v1/bills/acceptances", (AcceptanceRequest body, AcceptanceService svc) => Execute(() => svc.Issue(body.BillNo, body.Drawer, body.Payee, body.AcceptorBank, body.FaceAmount, body.TermDays)));
app.MapPost("/v1/bills/acceptances/{id:guid}/accept", (Guid id, AcceptanceService svc) => Execute(() => svc.Accept(id)));
app.MapPost("/v1/bills/discounts", (DiscountRequest body, DiscountService svc) => Execute(() => svc.Discount(body.BillId, body.Holder, body.FaceAmount, body.DiscountRate, body.DaysToMaturity)));
app.MapPost("/v1/bills/rediscounts", (RediscountRequest body, RediscountService svc) => Execute(() => svc.Trade(body.BillId, body.Direction, body.CounterpartyBank, body.FaceAmount, body.Rate, body.RemainingDays)));
app.MapPost("/v1/bills/pools", (BillPoolRequest body, BillPoolService svc) => Execute(() => svc.Open(body.Owner)));

// ===== 数字人民币 =====
app.MapPost("/v1/cbdc/wallets", (OpenWalletRequest body, CBDCWalletService svc) => Execute(() => svc.Open(body.OwnerId, body.Type, body.Level)));
app.MapPost("/v1/cbdc/wallets/topup", (TopUpRequest body, CBDCWalletService svc) => Execute(() => svc.TopUp(body.WalletId, body.Amount)));
app.MapPost("/v1/cbdc/wallets/transfer", (TransferRequest body, CBDCWalletService svc) => Execute(() => svc.Transfer(body.FromWalletId, body.ToWalletId, body.Amount)));
app.MapPost("/v1/cbdc/smart-contracts", (DeployContractRequest body, SmartContractEngine svc) => Execute(() => svc.Deploy(body.FromWalletId, body.ToWalletId, body.Amount, body.ConditionExpression, TimeSpan.FromMinutes(body.TtlMinutes))));
app.MapPost("/v1/cbdc/smart-contracts/{id:guid}/fulfill", (Guid id, Dictionary<string, string> facts, SmartContractEngine svc) => Execute(() => svc.Fulfill(id, facts)));

// ===== 数据报送 =====
app.MapPost("/v1/regulatory/reports", (ReportRequest body, RegulatoryReportGenerator svc) => Execute(() => svc.Generate(body.Type, body.Period, body.Records)));
app.MapPost("/v1/regulatory/asset-quality", (AssetQualityRequest body, AssetReportService svc) => Execute(() => svc.Snapshot(body.Period, body.TotalLoanBalance, body.NonPerformingBalance, body.ProvisionBalance)));
app.MapGet("/v1/audit/{entityId:guid}", (Guid entityId, IAuditTrail trail) => Results.Ok(trail.Query(entityId)));
app.MapPost("/v1/images", (UploadImageRequest body, IImagePlatform platform) => Execute(() => platform.Upload(body.ObjectKey, body.MediaType, body.SizeBytes)));

app.Run();

static IResult Execute(Func<object> action) { try { return Results.Ok(action()); } catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); } catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); } catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); } }

public partial class Program { }

// 请求 DTO
public sealed record DocumentRequest(string Type, string ObjectKey);
public sealed record FraudRequest(string Provider, string Decision, int RiskScore, IReadOnlyList<string>? RulesHit);
public sealed record ContractRequest(string ContractNo);
public sealed record SurveyRequest(string CustomerSummary, string LoanPurpose, string RepaymentSource, decimal EstimatedAnnualRevenue, bool RecommendedApprove);
public sealed record InternalRatingRequest(string RatingGrade, decimal Exposure, decimal CollateralValue);
public sealed record ApprovalFlowRequest(string[] RiskOfficers, string[] Approvers);
public sealed record EntrustedPaymentRequest(string Account, string Name, string Bank, decimal Amount, string Purpose);
public sealed record EarlyRepaymentRequestDto(EarlyRepaymentKind Kind, decimal Amount);
public sealed record OpenCollectionRequest(Guid LoanId, int DaysPastDue);
public sealed record CollectionActionDto(string Type, string Operator, string? Note);
public sealed record RiskWarningRequest(Guid LoanId, IReadOnlyList<RiskSignalDto> Signals);
public sealed record RiskSignalDto(string SignalType, string Description, WarningLevel Level);
public sealed record ProductValidateRequest(ProductKind Kind, decimal Amount, int TermMonths, decimal AnnualRate);
public sealed record ReceivableRequest(string CoreEnterpriseId, decimal Amount, DateOnly DueDate);
public sealed record AcceptanceRequest(string BillNo, string Drawer, string Payee, string AcceptorBank, decimal FaceAmount, int TermDays);
public sealed record DiscountRequest(Guid BillId, string Holder, decimal FaceAmount, decimal DiscountRate, int DaysToMaturity);
public sealed record RediscountRequest(Guid BillId, RediscountDirection Direction, string CounterpartyBank, decimal FaceAmount, decimal Rate, int RemainingDays);
public sealed record BillPoolRequest(string Owner);
public sealed record OpenWalletRequest(string OwnerId, WalletType Type, WalletLevel Level);
public sealed record TopUpRequest(Guid WalletId, decimal Amount);
public sealed record TransferRequest(Guid FromWalletId, Guid ToWalletId, decimal Amount);
public sealed record DeployContractRequest(Guid FromWalletId, Guid ToWalletId, decimal Amount, string ConditionExpression, int TtlMinutes);
public sealed record ReportRequest(ReportType Type, string Period, IReadOnlyList<object> Records);
public sealed record AssetQualityRequest(string Period, decimal TotalLoanBalance, decimal NonPerformingBalance, decimal ProvisionBalance);
public sealed record UploadImageRequest(string ObjectKey, string MediaType, long SizeBytes);
