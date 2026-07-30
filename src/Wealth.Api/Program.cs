using Wealth.Api;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<WealthService>();
var app = builder.Build();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
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
app.Run();
static IResult Execute(Func<object> action) { try { return Results.Ok(action()); } catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); } catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); } catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); } }
public sealed record OpenAccountRequest(string CustomerId);
public sealed record KycRequest(bool Accepted);
public sealed record TradeRequest(string ProductCode, decimal Amount);
public sealed record RedeemRequest(string ProductCode, decimal Units);
public sealed record RiskRequest(int RiskLevel);
public sealed record NavRequest(DateOnly NavDate, decimal Nav);
public sealed record ConfirmRequest(DateOnly ConfirmDate);
public partial class Program { }
