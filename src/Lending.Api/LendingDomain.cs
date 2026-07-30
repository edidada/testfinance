using System.Collections.Concurrent;
using Lending.Api.PostLoan;
using Lending.Api.Products;

namespace Lending.Api;

public enum LoanStatus { Submitted, Approved, Rejected, Disbursed, Closed }
public sealed record LoanApplication(string CustomerId, decimal MonthlyIncome, decimal MonthlyDebt, decimal RequestedAmount, int TermMonths, int CreditScore, ProductKind ProductKind = ProductKind.Consumer);
public sealed record Loan(Guid Id, LoanApplication Application, LoanStatus Status, int RiskScore, decimal ApprovedAmount, DateTimeOffset CreatedAt, IReadOnlyList<Installment> Schedule);
public sealed record Installment(int Sequence, DateOnly DueDate, decimal Principal, decimal Interest, bool Paid);
public sealed record ApplicationDocument(Guid Id, Guid LoanId, string Type, string ObjectKey, DateTimeOffset UploadedAt);
public sealed record FraudAssessment(Guid LoanId, string Provider, string Decision, int RiskScore, IReadOnlyList<string> RulesHit, DateTimeOffset AssessedAt);
public sealed record CreditContract(Guid Id, Guid LoanId, string ContractNo, string Status, DateTimeOffset CreatedAt, DateTimeOffset? SignedAt);
public sealed record CollectionCase(Guid LoanId, int DaysPastDue, string Classification, DateTimeOffset CreatedAt);

public sealed class LendingService
{
    private readonly ConcurrentDictionary<Guid, Loan> _loans = new();
    private readonly ConcurrentDictionary<Guid, List<ApplicationDocument>> _documents = new();
    private readonly ConcurrentDictionary<Guid, FraudAssessment> _fraud = new();
    private readonly ConcurrentDictionary<Guid, CreditContract> _contracts = new();
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
            var fraudBlocked = _fraud.TryGetValue(id, out var fraud) && (fraud.Decision == "REJECT" || fraud.RiskScore >= 80);
            var approved = !fraudBlocked && loan.RiskScore >= 60 && affordable >= estimatedPayment;
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
            if (loan.Status != LoanStatus.Approved || !_contracts.TryGetValue(id, out var contract) || contract.Status != "SIGNED") throw new InvalidOperationException("仅已批准且已签约贷款可放款。");
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
    public ApplicationDocument AddDocument(Guid loanId, string type, string objectKey)
    {
        _ = Get(loanId); if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(objectKey)) throw new ArgumentException("资料类型和对象存储键不能为空。");
        var document = new ApplicationDocument(Guid.NewGuid(), loanId, type, objectKey, DateTimeOffset.UtcNow); _documents.GetOrAdd(loanId, _ => []).Add(document); return document;
    }
    public FraudAssessment AssessFraud(Guid loanId, string provider, string decision, int riskScore, IReadOnlyList<string>? rulesHit)
    {
        _ = Get(loanId); if (riskScore is < 0 or > 100 || decision is not ("PASS" or "REVIEW" or "REJECT")) throw new ArgumentException("反欺诈结果无效。");
        var result = new FraudAssessment(loanId, provider, decision, riskScore, rulesHit ?? [], DateTimeOffset.UtcNow); _fraud[loanId] = result; return result;
    }
    public CreditContract CreateContract(Guid loanId, string contractNo)
    {
        var loan = Get(loanId); if (loan.Status != LoanStatus.Approved || string.IsNullOrWhiteSpace(contractNo)) throw new InvalidOperationException("仅批准贷款可生成合同。");
        return _contracts.GetOrAdd(loanId, _ => new CreditContract(Guid.NewGuid(), loanId, contractNo, "PENDING_SIGNATURE", DateTimeOffset.UtcNow, null));
    }
    public CreditContract SignContract(Guid loanId) { if (!_contracts.TryGetValue(loanId, out var contract)) throw new KeyNotFoundException("合同不存在。"); if (contract.Status != "PENDING_SIGNATURE") throw new InvalidOperationException("合同不可重复签署。"); return _contracts[loanId] = contract with { Status = "SIGNED", SignedAt = DateTimeOffset.UtcNow }; }
    public IReadOnlyCollection<CollectionCase> Overdue(DateOnly asOf) => _loans.Values.Where(x => x.Status == LoanStatus.Disbursed).SelectMany(x => x.Schedule.Where(i => !i.Paid && i.DueDate < asOf).Select(i => { var dpd = asOf.DayNumber - i.DueDate.DayNumber; return new CollectionCase(x.Id, dpd, FiveTierClassifier.Classify(dpd).ToString().ToUpperInvariant(), DateTimeOffset.UtcNow); })).ToArray();
    public static int Score(LoanApplication a) => Math.Clamp((a.CreditScore - 300) / 6 - (int)(a.MonthlyDebt / a.MonthlyIncome * 100m), 0, 100);
    private static IReadOnlyList<Installment> BuildSchedule(decimal amount, int months)
    {
        var principal = decimal.Round(amount / months, 2, MidpointRounding.ToEven);
        return Enumerable.Range(1, months).Select(i => new Installment(i, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(i), i == months ? amount - principal * (months - 1) : principal, decimal.Round(amount * 0.08m / 12m, 2, MidpointRounding.ToEven), false)).ToArray();
    }
}
