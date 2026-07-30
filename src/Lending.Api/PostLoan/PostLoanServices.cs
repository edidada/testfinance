using System.Collections.Concurrent;

namespace Lending.Api.PostLoan;

// ===== 五级分类（正常/关注/次级/可疑/损失，按逾期天数阈值） =====

public enum AssetClassification { Normal, SpecialMention, Substandard, Doubtful, Loss }

public static class FiveTierClassifier
{
    // 银行贷款风险五级分类：0 正常 / 1-30 关注 / 31-60 次级 / 61-90 可疑 / 91+ 损失（Demo 阈值）
    public static AssetClassification Classify(int daysPastDue) => daysPastDue switch
    {
        0 => AssetClassification.Normal,
        <= 30 => AssetClassification.SpecialMention,
        <= 60 => AssetClassification.Substandard,
        <= 90 => AssetClassification.Doubtful,
        _ => AssetClassification.Loss
    };

    // 次级及以下为不良贷款
    public static bool IsNonPerforming(AssetClassification c) => c >= AssetClassification.Substandard;
}

// ===== 罚息计算（逾期本金 × 罚息利率 × 逾期天数） =====

public sealed record PenaltyCalculation(Guid LoanId, int InstallmentSeq, decimal OverduePrincipal, decimal PenaltyRate, int DaysPastDue, decimal PenaltyAmount);

public static class PenaltyCalculator
{
    // 罚息利率通常在合同利率基础上加收 30%-50%，Demo 取 1.5 倍合同利率（年化）
    public static PenaltyCalculation Calculate(Guid loanId, int installmentSeq, decimal overduePrincipal, decimal annualContractRate, int daysPastDue, decimal penaltyMultiplier = 1.5m)
    {
        if (overduePrincipal < 0) throw new ArgumentException("逾期本金不能为负。");
        if (daysPastDue <= 0) throw new ArgumentException("逾期天数必须大于 0。");
        var dailyRate = annualContractRate * penaltyMultiplier / 360m;
        var amount = decimal.Round(overduePrincipal * dailyRate * daysPastDue, 2, MidpointRounding.ToEven);
        return new PenaltyCalculation(loanId, installmentSeq, overduePrincipal, annualContractRate * penaltyMultiplier, daysPastDue, amount);
    }
}

// ===== 提前还款（部分/全额，含违约金） =====

public enum EarlyRepaymentKind { Partial, Full }
public sealed record EarlyRepaymentRequest(Guid LoanId, EarlyRepaymentKind Kind, decimal Amount, DateTimeOffset RequestedAt);
public sealed record EarlyRepaymentResult(Guid LoanId, decimal PrincipalPaid, decimal InterestSaved, decimal PenaltyFee, decimal TotalDue, int RemainingInstallments);

public sealed class EarlyRepaymentService
{
    // 违约金 = 提前本金 × 违约金率（Demo：剩余期数<6 取 2%，否则 1%）
    public EarlyRepaymentResult Settle(EarlyRepaymentRequest req, decimal remainingPrincipal, int remainingInstallments, decimal annualRate)
    {
        if (req.Amount <= 0 || req.Amount > remainingPrincipal) throw new ArgumentException("提前还款金额无效。");
        var isFull = req.Kind == EarlyRepaymentKind.Full;
        var principalPaid = isFull ? remainingPrincipal : req.Amount;
        // 节省利息 = 提前本金 × 年利率 × 剩余期数 / 12（简化估算）
        var interestSaved = decimal.Round(principalPaid * annualRate * remainingInstallments / 12m, 2, MidpointRounding.ToEven);
        var penaltyRate = remainingInstallments < 6 ? 0.02m : 0.01m;
        var penaltyFee = decimal.Round(principalPaid * penaltyRate, 2, MidpointRounding.ToEven);
        var totalDue = principalPaid + penaltyFee;
        var remaining = isFull ? 0 : remainingInstallments; // Demo：部分提前还款简化为不缩期，仅扣本金
        return new EarlyRepaymentResult(req.LoanId, principalPaid, interestSaved, penaltyFee, totalDue, remaining);
    }
}

// ===== 催收案件状态机 =====

public enum CollectionStatus { Assigned, Contacted, Promised, Kept, Broken, Litigation, Closed }
public sealed record CollectionCaseV2(Guid Id, Guid LoanId, int DaysPastDue, AssetClassification Classification, CollectionStatus Status, string? Assignee, DateTimeOffset CreatedAt, IReadOnlyList<CollectionAction> Actions);
public sealed record CollectionAction(string Type, string Operator, string? Note, DateTimeOffset At);

public sealed class CollectionService
{
    private readonly ConcurrentDictionary<Guid, CollectionCaseV2> _cases = new();

    public CollectionCaseV2 Open(Guid loanId, int daysPastDue)
    {
        var c = new CollectionCaseV2(Guid.NewGuid(), loanId, daysPastDue, FiveTierClassifier.Classify(daysPastDue), CollectionStatus.Assigned, null, DateTimeOffset.UtcNow, []);
        _cases[c.Id] = c;
        return c;
    }

    public CollectionCaseV2 Assign(Guid caseId, string assignee)
    {
        var c = Get(caseId);
        if (c.Status != CollectionStatus.Assigned) throw new InvalidOperationException("案件已分派。");
        return Update(c with { Status = CollectionStatus.Contacted, Assignee = assignee });
    }

    public CollectionCaseV2 RecordAction(Guid caseId, CollectionAction action)
    {
        var c = Get(caseId);
        var status = action.Type switch
        {
            "PROMISE" => CollectionStatus.Promised,
            "KEPT" => CollectionStatus.Kept,
            "BROKEN" => CollectionStatus.Broken,
            "LITIGATE" => CollectionStatus.Litigation,
            "CLOSE" => CollectionStatus.Closed,
            _ => c.Status
        };
        return Update(c with { Status = status, Actions = c.Actions.Append(action).ToArray() });
    }

    public CollectionCaseV2 Get(Guid caseId) => _cases.TryGetValue(caseId, out var c) ? c : throw new KeyNotFoundException("催收案件不存在。");
    private CollectionCaseV2 Update(CollectionCaseV2 c) { _cases[c.Id] = c; return c; }
}

// ===== 风险预警（贷后风险信号采集与分级） =====

public enum WarningLevel { Low, Medium, High }
public sealed record RiskSignal(Guid LoanId, string SignalType, string Description, WarningLevel Level, DateTimeOffset DetectedAt);
public sealed record RiskWarning(Guid Id, Guid LoanId, WarningLevel Level, IReadOnlyList<RiskSignal> Signals, DateTimeOffset CreatedAt);

public sealed class RiskWarningService
{
    private readonly ConcurrentDictionary<Guid, RiskWarning> _warnings = new();

    public RiskWarning Evaluate(Guid loanId, IEnumerable<RiskSignal> signals)
    {
        var list = signals.ToArray();
        var level = list.Any(s => s.Level == WarningLevel.High) ? WarningLevel.High
                  : list.Any(s => s.Level == WarningLevel.Medium) ? WarningLevel.Medium
                  : WarningLevel.Low;
        var w = new RiskWarning(Guid.NewGuid(), loanId, level, list, DateTimeOffset.UtcNow);
        _warnings[w.Id] = w;
        return w;
    }

    public IReadOnlyList<RiskWarning> HighPriority() => _warnings.Values.Where(x => x.Level == WarningLevel.High).OrderByDescending(x => x.CreatedAt).ToArray();
    public RiskWarning? Latest(Guid loanId) => _warnings.Values.Where(x => x.LoanId == loanId).MaxBy(x => x.CreatedAt);
}
