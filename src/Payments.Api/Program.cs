using Payments.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PaymentService>();
var app = builder.Build();
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
app.MapGet("/v1/payments/{id:guid}/ledger", (Guid id, PaymentService service) => Results.Ok(service.Ledger(id)));
app.Run();

static IResult Try(Func<object> operation)
{
    try { return Results.Ok(operation()); }
    catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}
public sealed record RefundRequest(decimal Amount);
public partial class Program { }
