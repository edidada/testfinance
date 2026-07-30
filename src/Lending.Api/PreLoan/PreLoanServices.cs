using System.Collections.Concurrent;

namespace Lending.Api.PreLoan;

// ===== 尽调报告生成 =====

public sealed record SurveyReport(Guid Id, Guid LoanId, string CustomerSummary, string LoanPurpose, string RepaymentSource, decimal EstimatedAnnualRevenue, string Conclusion, DateTimeOffset GeneratedAt);

public sealed class SurveyService
{
    public SurveyReport Generate(Guid loanId, string customerSummary, string loanPurpose, string repaymentSource, decimal estimatedAnnualRevenue, bool recommendedApprove)
    {
        if (string.IsNullOrWhiteSpace(customerSummary)) throw new ArgumentException("客户概况不能为空。");
        var conclusion = recommendedApprove ? "尽调通过，建议授信" : "尽调发现风险，建议拒绝";
        return new SurveyReport(Guid.NewGuid(), loanId, customerSummary, loanPurpose, repaymentSource, estimatedAnnualRevenue, conclusion, DateTimeOffset.UtcNow);
    }
}

// ===== 财务指标核算（资产负债率/流动比率/息税前利润） =====

public sealed record FinancialStatement(decimal TotalAssets, decimal TotalLiabilities, decimal CurrentAssets, decimal CurrentLiabilities, decimal Revenue, decimal OperatingCosts, decimal InterestExpense, decimal TaxExpense);
public sealed record FinancialMetrics(decimal DebtToAssetRatio, decimal CurrentRatio, decimal EBIT, decimal EBITMargin, decimal InterestCoverageRatio)
{
    // 资产负债率 = 负债 / 资产
    // 流动比率 = 流动资产 / 流动负债
    // EBIT = 收入 - 经营成本（不含利息与税）
    // 利息保障倍数 = EBIT / 利息支出
};

public static class FinancialMetricsCalculator
{
    public static FinancialMetrics Compute(FinancialStatement s)
    {
        if (s.TotalAssets <= 0) throw new ArgumentException("总资产必须大于 0。");
        if (s.CurrentLiabilities <= 0) throw new ArgumentException("流动负债必须大于 0。");
        var debtRatio = decimal.Round(s.TotalLiabilities / s.TotalAssets, 4, MidpointRounding.ToEven);
        var currentRatio = decimal.Round(s.CurrentAssets / s.CurrentLiabilities, 4, MidpointRounding.ToEven);
        var ebit = s.Revenue - s.OperatingCosts;
        var ebitMargin = s.Revenue > 0 ? decimal.Round(ebit / s.Revenue, 4, MidpointRounding.ToEven) : 0m;
        var interestCoverage = s.InterestExpense > 0 ? decimal.Round(ebit / s.InterestExpense, 4, MidpointRounding.ToEven) : decimal.MaxValue;
        return new FinancialMetrics(debtRatio, currentRatio, ebit, ebitMargin, interestCoverage);
    }
}

// ===== 内部评级法（IRB：PD/LGD/EAD） =====

// 巴塞尔协议 III 内部评级法核心参数
public sealed record InternalRating(Guid Id, Guid LoanId, string RatingGrade, decimal PD, decimal LGD, decimal EAD, decimal ExpectedLoss, DateTimeOffset RatedAt)
{
    // 预期损失 EL = PD × LGD × EAD
};

public static class InternalRatingEngine
{
    // 评级等级与违约概率(PD)映射表（Demo，单位：百分比）
    private static readonly Dictionary<string, decimal> GradeToPD = new()
    {
        ["AAA"] = 0.03m, ["AA"] = 0.05m, ["A"] = 0.08m, ["BBB"] = 0.15m,
        ["BB"] = 0.50m, ["B"] = 1.50m, ["CCC"] = 5.00m, ["CC"] = 15.00m
    };

    public static InternalRating Rate(Guid loanId, string ratingGrade, decimal exposure, decimal collateralValue)
    {
        if (!GradeToPD.TryGetValue(ratingGrade, out var pd))
            throw new ArgumentException($"未知评级等级：{ratingGrade}");
        if (exposure <= 0) throw new ArgumentException("风险敞口必须大于 0。");
        // LGD = (敞口 - 抵押物回收值) / 敞口，最低 0（无抵押则 LGD=45% 标准）
        var recovery = Math.Min(collateralValue, exposure);
        var lgd = collateralValue > 0 ? decimal.Round((exposure - recovery) / exposure, 4, MidpointRounding.ToEven) : 0.45m;
        lgd = Math.Clamp(lgd, 0m, 1m);
        // EAD = 敞口（Demo：未考虑提款率与 CCF）
        var ead = exposure;
        var el = decimal.Round(pd / 100m * lgd * ead, 2, MidpointRounding.ToEven);
        return new InternalRating(Guid.NewGuid(), loanId, ratingGrade, pd, lgd, ead, el, DateTimeOffset.UtcNow);
    }

    public static IReadOnlyDictionary<string, decimal> Grades => GradeToPD;
}

// ===== 征信查询接入（接口 + Fake 实现，处理高延迟与降级） =====

public sealed record CreditBureauQuery(string CustomerId, string IdNumber);
public sealed record CreditBureauReport(string CustomerId, int Score, int OverdueCount30d, int OverdueCount90d, int InquiryCount90d, decimal TotalOutstanding, DateTimeOffset QueriedAt);

public interface ICreditBureauGateway
{
    Task<CreditBureauReport> QueryAsync(CreditBureauQuery query, CancellationToken ct = default);
}

// Fake 实现：模拟人行征信返回（生产环境替换为真实对接）
public sealed class FakeCreditBureauGateway : ICreditBureauGateway
{
    public Task<CreditBureauReport> QueryAsync(CreditBureauQuery query, CancellationToken ct = default)
    {
        var report = new CreditBureauReport(query.CustomerId, Score: 720, OverdueCount30d: 0, OverdueCount90d: 0, InquiryCount90d: 2, TotalOutstanding: 50000m, DateTimeOffset.UtcNow);
        return Task.FromResult(report);
    }
}

// 带熔断降级的包装：征信不可用时降级为"拒绝授信"策略
public sealed class ResilientCreditBureauGateway : ICreditBureauGateway
{
    private readonly ICreditBureauGateway _inner;
    private int _failures;
    private DateTimeOffset _circuitOpenUntil;
    private readonly object _gate = new();

    public ResilientCreditBureauGateway(ICreditBureauGateway inner) => _inner = inner;

    public async Task<CreditBureauReport> QueryAsync(CreditBureauQuery query, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_circuitOpenUntil > DateTimeOffset.UtcNow)
                throw new InvalidOperationException("征信服务熔断中，降级为拒绝策略。");
        }
        try
        {
            var report = await _inner.QueryAsync(query, ct);
            lock (_gate) { _failures = 0; }
            return report;
        }
        catch
        {
            lock (_gate)
            {
                _failures++;
                if (_failures >= 3) _circuitOpenUntil = DateTimeOffset.UtcNow.AddSeconds(30);
            }
            throw;
        }
    }
}
