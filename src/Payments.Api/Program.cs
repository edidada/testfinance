using Payments.Api;
using Payments.Api.Channels;
using Payments.Api.Clearing;
using Payments.Api.Ledger;
using Payments.Api.Reconciliation;
using Payments.Api.RiskControl;
using Payments.Api.Rtgs;
using Payments.Api.Settlement;
using Payments.Api.Transport;
using Serilog;
using Finance.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 主服务
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddDbContext<PaymentsDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Payments") ?? "Data Source=payments.db"));

// Ledger 复式记账
builder.Services.AddSingleton<JournalService>();

// Clearing 清算
builder.Services.AddSingleton<NettingEngine>();
builder.Services.AddSingleton<ClearingBatchService>();

// Rtgs 大额实时支付
builder.Services.AddSingleton<RtgsQueue>();
builder.Services.AddSingleton<LiquidityManager>();
builder.Services.AddSingleton<EodSettlementService>();

// Reconciliation 对账
builder.Services.AddSingleton<DiffResolutionService>();

// RiskControl 风控
builder.Services.AddSingleton<LimitChecker>();
builder.Services.AddSingleton<BlacklistService>();
builder.Services.AddSingleton<AnomalyDetector>();
builder.Services.AddSingleton<PaymentRiskEngine>(_ => { var e = new PaymentRiskEngine(); e.RegisterDefaults(); return e; });

// Settlement 结算
builder.Services.AddSingleton<SettlementAccountService>();
builder.Services.AddSingleton<SettlementService>();
builder.Services.AddSingleton<SettlementBatchService>();

// Channels 渠道
builder.Services.AddSingleton<ChannelGatewayFactory>();
builder.Services.AddSingleton<ChannelLimitService>();

// Transport 传输
builder.Services.AddSingleton<IFinancialMessageQueue, InMemoryMessageQueue>();
builder.Services.AddSingleton<ITongLinkGateway, FakeTongLinkGateway>();
builder.Services.AddSingleton<DeadLetterQueue>();
builder.Services.AddSingleton<MessageRetryPolicy>();
builder.Services.AddSingleton<CnapsMessageBus>();

var app = builder.Build();
using (var scope = app.Services.CreateScope()) { scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.EnsureCreated(); }
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ===== 支付主流程 =====
app.MapPost("/v1/payments", (CreatePayment request, HttpRequest http, PaymentService service) =>
{
    try { var payment = service.Create(request, http.Headers["Idempotency-Key"].ToString()); return Results.Created($"/v1/payments/{payment.Id}", payment); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
});
app.MapGet("/v1/payments/{id:guid}", (Guid id, PaymentService service) => Try(() => service.Get(id)));
app.MapPost("/v1/payments/{id:guid}/capture", (Guid id, PaymentService service) => Try(() => service.Capture(id)));
app.MapPost("/v1/payments/{id:guid}/refunds", (Guid id, RefundRequest body, PaymentService service) => Try(() => service.Refund(id, body.Amount)));
app.MapPost("/v1/payments/{id:guid}/refund-requests", (Guid id, RefundCommand body, PaymentService service) => Try(() => service.RequestRefund(id, body.MerchantRefundNo, body.Amount, body.Reason)));
app.MapPost("/v1/payments/{id:guid}/close", (Guid id, PaymentService service) => Try(() => service.Close(id)));
app.MapPost("/v1/payments/channel-notifications", (ChannelNotification body, PaymentService service) => Try(() => service.ApplyChannelNotification(body)));
app.MapGet("/v1/payments/reconciliation/{date}", (DateOnly date, PaymentService service) => Results.Ok(service.Reconcile(date)));
app.MapGet("/v1/payments/{id:guid}/refunds", (Guid id, PaymentService service) => Results.Ok(service.Refunds(id)));
app.MapGet("/v1/payments/{id:guid}/ledger", (Guid id, PaymentService service) => Results.Ok(service.Ledger(id)));

// ===== Ledger 复式记账 =====
app.MapGet("/v1/ledger/accounts", () => Results.Ok(ChartOfAccounts.All()));
app.MapPost("/v1/ledger/journal-entries", (JournalEntryRequest body, JournalService svc) => Try(() => svc.Post(body.BatchNo, body.Lines)));
app.MapGet("/v1/ledger/accounts/{code}/balance", (string code, JournalService svc) => Try(() => new { account = code, balance = svc.Balance(code) }));
app.MapPost("/v1/ledger/fx-convert", (FxConvertRequest body) => Try(() => FxConverter.Convert(body.From, body.To, body.Amount, body.AsOf)));

// ===== Clearing 清算 =====
app.MapPost("/v1/clearing/batches", (OpenBatchRequest body, ClearingBatchService svc) => Try(() => svc.Open(body.Currency, body.BusinessDate)));
app.MapPost("/v1/clearing/batches/{id:guid}/instructions", (Guid id, AddInstructionRequest body, ClearingBatchService svc) => Try(() => svc.Add(id, new ClearingInstruction(Guid.NewGuid(), body.DebtorBic, body.CreditorBic, body.Amount, body.Currency))));
app.MapPost("/v1/clearing/batches/{id:guid}/close", (Guid id, ClearingBatchService svc) => Try(() => svc.Close(id)));
app.MapPost("/v1/clearing/batches/{id:guid}/settle", (Guid id, ClearingBatchService svc) => Try(() => svc.Settle(id)));
app.MapPost("/v1/clearing/netting", (NettingRequest body, NettingEngine svc) => Try(() => svc.Net(body.Instructions, body.Currency)));
app.MapPost("/v1/clearing/pacs008", (Pacs008Request body) => Try(() => Pacs008Builder.Build(new Pacs008Message(body.MessageId, body.InstrId, body.EndToEndId, body.Amount, body.Currency, body.DebtorBic, body.DebtorAccount, body.DebtorName, body.CreditorBic, body.CreditorAccount, body.CreditorName, DateTimeOffset.UtcNow, body.RemittanceInfo))));
app.MapPost("/v1/clearing/cnaps", (CnapsRequest body) => Try(() => { var ch = CnapsBuilder.SelectChannel(body.Amount, body.Urgent); return CnapsBuilder.Build(ch, body.DebtorAccount, body.CreditorAccount, body.Amount, body.Currency); }));
app.MapGet("/v1/clearing/bic/{bic}/validate", (string bic) => Results.Ok(new { bic, valid = BicValidator.IsValid(bic) }));

// ===== RTGS 大额实时支付 =====
app.MapPost("/v1/rtgs/queue", (EnqueueRtgsRequest body, RtgsQueue svc) => Try(() => svc.Enqueue(new RtgsInstruction(Guid.NewGuid(), body.InstrId, body.DebtorBic, body.CreditorBic, body.Amount, body.Currency, body.Priority, RtgsStatus.Queued, DateTimeOffset.UtcNow, null))));
app.MapPost("/v1/rtgs/queue/dequeue", (DequeueRequest body, RtgsQueue svc) => Try(() => svc.DequeueBatch(body.Limit)));
app.MapPost("/v1/rtgs/{id:guid}/settle", (Guid id, RtgsQueue svc) => Try(() => svc.Settle(id)));
app.MapPost("/v1/rtgs/liquidity/{bic}/reserve", (string bic, ReserveRequest body, LiquidityManager svc) => Try(() => svc.Reserve(bic, body.Amount)));
app.MapPost("/v1/rtgs/eod-settlement", (EodRequest body, EodSettlementService svc) => Try(() => svc.Settle(body.Bic, body.TotalDebit, body.TotalCredit)));

// ===== Reconciliation 对账 =====
app.MapPost("/v1/reconciliation/parse-csv", (ParseCsvRequest body) => Try(() => ReconciliationFileParser.ParseCsv(body.Content)));
app.MapPost("/v1/reconciliation/diff", (DiffRequest body) => Try(() => DiffDetector.Detect(body.Local, body.Channel)));
app.MapPost("/v1/reconciliation/diffs", (OpenDiffRequest body, DiffResolutionService svc) => Try(() => svc.Open(body.TransactionId, body.Type)));
app.MapPost("/v1/reconciliation/diffs/{id:guid}/resolve", (Guid id, ResolveDiffRequest body, DiffResolutionService svc) => Try(() => svc.Resolve(id, body.Resolution)));

// ===== RiskControl 风控 =====
app.MapPost("/v1/risk/limits", (LimitRule body, LimitChecker svc) => { svc.Configure(body); return Results.Ok(body); });
app.MapPost("/v1/risk/check", (RiskCheckRequest body, LimitChecker svc) => Try(() => svc.Check(body.MerchantId, body.Amount)));
app.MapPost("/v1/risk/blacklist/{value}", (string value, BlacklistService svc) => { svc.Add(value, ListType.Blacklist, "manual"); return Results.Ok(); });
app.MapGet("/v1/risk/blacklist/{value}", (string value, BlacklistService svc) => Results.Ok(new { value, blacklisted = svc.IsBlacklisted(value) }));
app.MapPost("/v1/risk/evaluate", (RiskContext body, PaymentRiskEngine svc) => Try(() => svc.Evaluate(body)));

// ===== Settlement 结算 =====
app.MapPost("/v1/settlement/accounts", (OpenAccountRequest body, SettlementAccountService svc) => Try(() => svc.Open(body.Bic, body.Name, body.Currency)));
app.MapPost("/v1/settlement/accounts/{bic}/topup", (string bic, TopUpRequest body, SettlementAccountService svc) => Try(() => svc.TopUp(bic, body.Amount)));
app.MapPost("/v1/settlement/transfers", (TransferRequest body, SettlementService svc) => Try(() => svc.Initiate(body.FromBic, body.ToBic, body.Amount, body.Currency, body.Purpose)));
app.MapPost("/v1/settlement/transfers/{id:guid}/settle", (Guid id, SettlementService svc) => Try(() => svc.Settle(id)));

// ===== Channels 渠道 =====
app.MapGet("/v1/channels", () => Results.Ok(ChannelRouter.All()));
app.MapPost("/v1/channels/select", (SelectChannelRequest body) => Try(() => ChannelRouter.Select(body.Amount, body.Preferred)));
app.MapPost("/v1/channels/verify-signature", (VerifySigRequest body) => Try(() => SignatureVerifier.VerifyHmac(body.Payload, body.Signature, body.Secret)));

// ===== Transport 传输 =====
app.MapPost("/v1/transport/publish", (PublishRequest body, IFinancialMessageQueue mq) => { mq.Publish(body.Topic, body.MessageId, body.Payload, body.Headers); return Results.Ok(); });
app.MapPost("/v1/transport/consume", (ConsumeRequest body, IFinancialMessageQueue mq) => Try(() => new { payload = mq.Consume(body.Topic) }));
app.MapGet("/v1/transport/dlq", (DeadLetterQueue dlq) => Results.Ok(dlq.All()));

app.Run();

static IResult Try(Func<object> operation)
{
    try { return Results.Ok(operation()); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
    catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}

// 请求 DTO
public sealed record RefundRequest(decimal Amount);
public sealed record RefundCommand(string MerchantRefundNo, decimal Amount, string Reason);
public sealed record JournalEntryRequest(string BatchNo, IReadOnlyList<JournalLine> Lines);
public sealed record FxConvertRequest(string From, string To, decimal Amount, DateOnly AsOf);
public sealed record OpenBatchRequest(string Currency, DateOnly BusinessDate);
public sealed record AddInstructionRequest(string DebtorBic, string CreditorBic, decimal Amount, string Currency);
public sealed record NettingRequest(IReadOnlyList<ClearingInstruction> Instructions, string Currency);
public sealed record Pacs008Request(string MessageId, string InstrId, string EndToEndId, decimal Amount, string Currency, string DebtorBic, string DebtorAccount, string DebtorName, string CreditorBic, string CreditorAccount, string CreditorName, string RemittanceInfo);
public sealed record CnapsRequest(decimal Amount, string Currency, string DebtorAccount, string CreditorAccount, bool Urgent);
public sealed record EnqueueRtgsRequest(string InstrId, string DebtorBic, string CreditorBic, decimal Amount, string Currency, Priority Priority);
public sealed record DequeueRequest(int Limit);
public sealed record ReserveRequest(decimal Amount);
public sealed record EodRequest(string Bic, decimal TotalDebit, decimal TotalCredit);
public sealed record ParseCsvRequest(string Content);
public sealed record DiffRequest(IReadOnlyList<ReconciliationRecord> Local, IReadOnlyList<ReconciliationRecord> Channel);
public sealed record OpenDiffRequest(string TransactionId, DiffType Type);
public sealed record ResolveDiffRequest(string Resolution);
public sealed record RiskCheckRequest(string MerchantId, decimal Amount);
public sealed record OpenAccountRequest(string Bic, string Name, string Currency);
public sealed record TopUpRequest(decimal Amount);
public sealed record TransferRequest(string FromBic, string ToBic, decimal Amount, string Currency, string Purpose);
public sealed record SelectChannelRequest(decimal Amount, string? Preferred);
public sealed record VerifySigRequest(string Payload, string Signature, string Secret);
public sealed record PublishRequest(string Topic, string MessageId, string Payload, Dictionary<string, string>? Headers);
public sealed record ConsumeRequest(string Topic);
public partial class Program { }
