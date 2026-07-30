using System.Collections.Concurrent;

namespace Lending.Api;

public enum LoanStatus { Submitted, Approved, Rejected, Disbursed, Closed }
public sealed record LoanApplication(string CustomerId, decimal MonthlyIncome, decimal MonthlyDebt, decimal RequestedAmount, int TermMonths, int CreditScore);
public sealed record Loan(Guid Id, LoanApplication Application, LoanStatus Status, int RiskScore, decimal ApprovedAmount, DateTimeOffset CreatedAt, IReadOnlyList<Installment> Schedule);
public sealed record Installment(int Sequence, DateOnly DueDate, decimal Principal, decimal Interest, bool Paid);

public sealed class LendingService
{
    private readonly ConcurrentDictionary<Guid, Loan> _loans = new();
    private readonly object _gate = new();

    public Loan Submit(LoanApplication application)
    {
        if (string.IsNullOrWhiteSpace(application.CustomerId) || application.MonthlyIncome <= 0 || application.MonthlyDebt < 0 || application.RequestedAmount <= 0 || application.TermMonths is < 1 or > 120 || application.CreditScore is < 300 or > 950) throw new ArgumentException("申请参数无效。");
        var risk = Score(application);
        var loan = new Loan(Guid.NewGuid(), application, LoanStatus.Submitted, risk, 0m, DateTimeOffset.UtcNow, []);
        _loans[loan.Id] = loan;
        return loan;
    }

    public Loan Decide(Guid id)
    {
        lock (_gate)
        {
            var loan = Get(id);
            if (loan.Status != LoanStatus.Submitted) throw new InvalidOperationException("申请已处理。");
            var affordable = loan.Application.MonthlyIncome * 0.4m - loan.Application.MonthlyDebt;
            var estimatedPayment = loan.Application.RequestedAmount / loan.Application.TermMonths * 1.08m;
            var approved = loan.RiskScore >= 60 && affordable >= estimatedPayment;
            var changed = loan with { Status = approved ? LoanStatus.Approved : LoanStatus.Rejected, ApprovedAmount = approved ? loan.Application.RequestedAmount : 0m };
            _loans[id] = changed;
            return changed;
        }
    }

    public Loan Disburse(Guid id)
    {
        lock (_gate)
        {
            var loan = Get(id);
            if (loan.Status != LoanStatus.Approved) throw new InvalidOperationException("仅已批准贷款可放款。");
            var schedule = BuildSchedule(loan.ApprovedAmount, loan.Application.TermMonths);
            var changed = loan with { Status = LoanStatus.Disbursed, Schedule = schedule };
            _loans[id] = changed;
            return changed;
        }
    }

    public Loan Repay(Guid id, int installment)
    {
        lock (_gate)
        {
            var loan = Get(id);
            if (loan.Status != LoanStatus.Disbursed || installment < 1 || installment > loan.Schedule.Count) throw new InvalidOperationException("还款操作无效。");
            var schedule = loan.Schedule.Select(x => x.Sequence == installment ? x with { Paid = true } : x).ToArray();
            var changed = loan with { Schedule = schedule, Status = schedule.All(x => x.Paid) ? LoanStatus.Closed : LoanStatus.Disbursed };
            _loans[id] = changed;
            return changed;
        }
    }

    public Loan Get(Guid id) => _loans.TryGetValue(id, out var loan) ? loan : throw new KeyNotFoundException("贷款不存在。");
    public static int Score(LoanApplication a) => Math.Clamp((a.CreditScore - 300) / 6 - (int)(a.MonthlyDebt / a.MonthlyIncome * 100m), 0, 100);
    private static IReadOnlyList<Installment> BuildSchedule(decimal amount, int months)
    {
        var principal = decimal.Round(amount / months, 2, MidpointRounding.ToEven);
        return Enumerable.Range(1, months).Select(i => new Installment(i, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(i), i == months ? amount - principal * (months - 1) : principal, decimal.Round(amount * 0.08m / 12m, 2, MidpointRounding.ToEven), false)).ToArray();
    }
}
