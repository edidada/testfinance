using Lending.Api.Components;

namespace Lending.Api.InLoan;

// ===== 受托支付（放款资金直接划付交易对手） =====

public enum PaymentChannel { Direct, Entrusted }
public sealed record EntrustedPayment(Guid LoanId, string CounterpartyAccount, string CounterpartyName, string CounterpartyBank, decimal Amount, string TradePurpose, DateTimeOffset CreatedAt);

public static class PaymentChannelPolicy
{
    // 监管要求：单笔超过一定金额（如 30 万）或特定用途须受托支付
    public const decimal EntrustedThreshold = 300_000m;

    public static PaymentChannel Decide(decimal amount, string purpose) =>
        amount >= EntrustedThreshold || purpose == "ENTRUSTED" ? PaymentChannel.Entrusted : PaymentChannel.Direct;

    public static EntrustedPayment Build(Guid loanId, string account, string name, string bank, decimal amount, string purpose)
    {
        if (string.IsNullOrWhiteSpace(account)) throw new ArgumentException("交易对手账户不能为空。");
        return new EntrustedPayment(loanId, account, name, bank, amount, purpose, DateTimeOffset.UtcNow);
    }
}

// ===== 审批工作流接入（封装 IWorkflowEngine，提供信贷专用流程） =====

public sealed class CreditApprovalFlow
{
    private readonly IWorkflowEngine _engine;
    public CreditApprovalFlow(IWorkflowEngine engine) => _engine = engine;

    // 信贷审批标准三节点：客户经理初审 → 风控复核 → 有权人终审
    public WorkflowInstance StartForLoan(Guid loanId, string[] riskOfficers, string[] approvers)
    {
        var nodes = new[]
        {
            new WorkflowNode("N1", "客户经理初审", WorkflowNodeKind.Serial, new[] { "officer-1" }, 60),
            new WorkflowNode("N2", "风控会签", WorkflowNodeKind.Countersign, riskOfficers, 120),
            new WorkflowNode("N3", "有权人终审", WorkflowNodeKind.Serial, approvers, 240)
        };
        return _engine.Start("CREDIT_APPROVAL", loanId.ToString(), nodes);
    }
}

// ===== 规则引擎接入（封装 IRuleEngine，注册信贷风控规则） =====

public sealed class CreditRuleSet
{
    private readonly IRuleEngine _engine;
    public CreditRuleSet(IRuleEngine engine) => _engine = engine;

    public void RegisterDefaults()
    {
        // 黑名单规则
        _engine.Register(new Rule("R_BLACKLIST", "黑名单拦截",
            f => f.TryGetValue("blacklisted", out var v) && (bool)v, "REJECT", "客户命中黑名单"));
        // 多头借贷规则
        _engine.Register(new Rule("R_MULTI_LEND", "多头借贷",
            f => f.TryGetValue("inquiry_90d", out var v) && (int)v >= 6, "REJECT", "近 90 天征信查询超 6 次"));
        // 年龄规则
        _engine.Register(new Rule("R_AGE", "年龄校验",
            f => f.TryGetValue("age", out var v) && ((int)v < 18 || (int)v > 65), "REJECT", "年龄不在 18-65 区间"));
        // 负债率规则
        _engine.Register(new Rule("R_DTI", "债务收入比",
            f => f.TryGetValue("dti", out var v) && (decimal)v > 0.75m, "REJECT", "债务收入比超过 75%"));
    }

    public RuleEvaluationResult Evaluate(IDictionary<string, object> facts) => _engine.Evaluate(facts);
}
