using System.Security.Cryptography;
using System.Text;

namespace Payments.Api.Channels;

// ===== 渠道路由策略 =====

public enum ChannelType { WechatJsApi, WechatNative, Alipay, UnionPay, BankTransfer, Internal }
public sealed record ChannelOption(ChannelType Type, decimal FeeRate, decimal SuccessRate, int Priority);

public static class ChannelRouter
{
    private static readonly ChannelOption[] _channels =
    {
        new(ChannelType.WechatJsApi, 0.006m, 0.995m, 1),
        new(ChannelType.Alipay, 0.006m, 0.996m, 2),
        new(ChannelType.WechatNative, 0.006m, 0.993m, 3),
        new(ChannelType.UnionPay, 0.008m, 0.998m, 4),
        new(ChannelType.BankTransfer, 0.003m, 0.99m, 5),
        new(ChannelType.Internal, 0m, 1m, 0)
    };

    // 智能路由：按成本（费率）+ 成功率 + 优先级综合打分
    public static ChannelOption Select(decimal amount, string? preferredChannel = null)
    {
        if (preferredChannel is not null && Enum.TryParse<ChannelType>(preferredChannel, true, out var preferred))
            return _channels.First(c => c.Type == preferred);

        // 自动路由仅在真实外部渠道中选择（排除 Internal 内部通道，仅显式指定时可用）
        // 成本 = 金额 × 费率；得分 = 成本 - 成功率 × 100 + 优先级（得分越低越优）
        return _channels
            .Where(c => c.Type != ChannelType.Internal)
            .OrderBy(c => amount * c.FeeRate - c.SuccessRate * 100 + c.Priority)
            .First();
    }

    public static IReadOnlyList<ChannelOption> All() => _channels;
}

// ===== 回调验签（HMAC-SHA256 / RSA-SHA256） =====

public static class SignatureVerifier
{
    // HMAC-SHA256（微信支付 v3 风格）
    public static bool VerifyHmac(string payload, string signature, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        return string.Equals(computed, signature, StringComparison.OrdinalIgnoreCase);
    }

    // 生成 HMAC-SHA256 签名
    public static string SignHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    // RSA-SHA256 验签（支付宝风式）
    public static bool VerifyRsa(string payload, string signature, RSA publicKey)
    {
        var bytes = publicKey.VerifyData(Encoding.UTF8.GetBytes(payload),
            Convert.FromHexString(signature),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return bytes;
    }
}

// ===== 渠道网关接口骨架 =====

public interface IChannelGateway
{
    ChannelType Type { get; }
    Task<ChannelPaymentResult> CreatePaymentAsync(decimal amount, string currency, string subject, string outTradeNo, CancellationToken ct = default);
    Task<ChannelPaymentResult> QueryAsync(string outTradeNo, CancellationToken ct = default);
    Task<ChannelRefundResult> RefundAsync(string outTradeNo, string refundNo, decimal amount, CancellationToken ct = default);
}

public sealed record ChannelPaymentResult(bool Success, string ChannelTransactionId, string TradeState, string? FailReason);
public sealed record ChannelRefundResult(bool Success, string RefundId, string Status, string? FailReason);

// 微信支付适配（Fake 实现）
public sealed class WechatPayGateway : IChannelGateway
{
    public ChannelType Type => ChannelType.WechatJsApi;
    public Task<ChannelPaymentResult> CreatePaymentAsync(decimal amount, string currency, string subject, string outTradeNo, CancellationToken ct = default)
        => Task.FromResult(new ChannelPaymentResult(true, $"wx_{outTradeNo}", "NOTPAY", null));
    public Task<ChannelPaymentResult> QueryAsync(string outTradeNo, CancellationToken ct = default)
        => Task.FromResult(new ChannelPaymentResult(true, $"wx_{outTradeNo}", "SUCCESS", null));
    public Task<ChannelRefundResult> RefundAsync(string outTradeNo, string refundNo, decimal amount, CancellationToken ct = default)
        => Task.FromResult(new ChannelRefundResult(true, refundNo, "SUCCESS", null));
}

// 支付宝适配（Fake 实现）
public sealed class AlipayGateway : IChannelGateway
{
    public ChannelType Type => ChannelType.Alipay;
    public Task<ChannelPaymentResult> CreatePaymentAsync(decimal amount, string currency, string subject, string outTradeNo, CancellationToken ct = default)
        => Task.FromResult(new ChannelPaymentResult(true, $"ali_{outTradeNo}", "WAIT_BUYER_PAY", null));
    public Task<ChannelPaymentResult> QueryAsync(string outTradeNo, CancellationToken ct = default)
        => Task.FromResult(new ChannelPaymentResult(true, $"ali_{outTradeNo}", "TRADE_SUCCESS", null));
    public Task<ChannelRefundResult> RefundAsync(string outTradeNo, string refundNo, decimal amount, CancellationToken ct = default)
        => Task.FromResult(new ChannelRefundResult(true, refundNo, "REFUND_SUCCESS", null));
}

// 渠道网关工厂
public sealed class ChannelGatewayFactory
{
    private readonly Dictionary<ChannelType, IChannelGateway> _gateways;

    public ChannelGatewayFactory()
    {
        _gateways = new()
        {
            [ChannelType.WechatJsApi] = new WechatPayGateway(),
            [ChannelType.Alipay] = new AlipayGateway()
        };
    }

    public IChannelGateway Get(ChannelType type) =>
        _gateways.TryGetValue(type, out var gw) ? gw : throw new KeyNotFoundException($"渠道 {type} 未接入。");
}

// ===== 渠道限额管理 =====

public sealed record ChannelLimit(ChannelType Type, decimal DailyLimit, decimal SingleMaxLimit);
public sealed class ChannelLimitService
{
    private readonly Dictionary<ChannelType, ChannelLimit> _limits = new()
    {
        [ChannelType.WechatJsApi] = new(ChannelType.WechatJsApi, 500_000m, 50_000m),
        [ChannelType.Alipay] = new(ChannelType.Alipay, 500_000m, 50_000m),
        [ChannelType.UnionPay] = new(ChannelType.UnionPay, 5_000_000m, 500_000m),
        [ChannelType.BankTransfer] = new(ChannelType.BankTransfer, 50_000_000m, 5_000_000m)
    };

    public bool Validate(ChannelType type, decimal amount) =>
        _limits.TryGetValue(type, out var limit) && amount <= limit.SingleMaxLimit;

    public ChannelLimit Get(ChannelType type) => _limits.TryGetValue(type, out var l) ? l : throw new KeyNotFoundException($"渠道 {type} 限额未配置。");
}
