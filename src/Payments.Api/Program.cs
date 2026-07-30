using Payments.Api;
using Serilog;
using Finance.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddDbContext<PaymentsDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Payments") ?? "Data Source=payments.db"));
var app = builder.Build();
using (var scope = app.Services.CreateScope()) { scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.EnsureCreated(); }
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
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
app.Run();

static IResult Try(Func<object> operation)
{
    try { return Results.Ok(operation()); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
    catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}
public sealed record RefundRequest(decimal Amount);
public sealed record RefundCommand(string MerchantRefundNo, decimal Amount, string Reason);
public partial class Program { }
