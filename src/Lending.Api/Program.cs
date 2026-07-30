using Lending.Api;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<LendingService>();
var app = builder.Build();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/v1/loan-applications", (LoanApplication request, LendingService service) => { try { var loan = service.Submit(request); return Results.Created($"/v1/loans/{loan.Id}", loan); } catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); } });
app.MapGet("/v1/loans/{id:guid}", (Guid id, LendingService service) => Execute(() => service.Get(id)));
app.MapPost("/v1/loans/{id:guid}/decision", (Guid id, LendingService service) => Execute(() => service.Decide(id)));
app.MapPost("/v1/loans/{id:guid}/disbursement", (Guid id, LendingService service) => Execute(() => service.Disburse(id)));
app.MapPost("/v1/loans/{id:guid}/installments/{sequence:int}/repayment", (Guid id, int sequence, LendingService service) => Execute(() => service.Repay(id, sequence)));
app.Run();
static IResult Execute(Func<object> action) { try { return Results.Ok(action()); } catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); } catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); } }
public partial class Program { }
