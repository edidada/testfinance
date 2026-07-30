using System.Collections.Concurrent;

namespace Wealth.Api.Trading;

// ===== 交易订单簿 =====

public enum OrderSide { Buy, Sell }
public enum OrderStatus { Pending, PartiallyFilled, Filled, Canceled }
public sealed class TradeOrder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ProductCode { get; init; } = default!;
    public OrderSide Side { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public OrderStatus Status { get; set; }
    public decimal FilledQuantity { get; set; }
    public DateTimeOffset PlacedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class OrderBook
{
    private readonly ConcurrentDictionary<Guid, TradeOrder> _orders = new();

    public TradeOrder Place(string productCode, OrderSide side, decimal price, decimal quantity)
    {
        if (price <= 0 || quantity <= 0) throw new ArgumentException("价格与数量必须大于 0。");
        var order = new TradeOrder
        {
            ProductCode = productCode,
            Side = side,
            Price = price,
            Quantity = quantity,
            Status = OrderStatus.Pending,
            FilledQuantity = 0m
        };
        _orders[order.Id] = order;
        return order;
    }

    public TradeOrder Cancel(Guid orderId)
    {
        if (!_orders.TryGetValue(orderId, out var order)) throw new KeyNotFoundException("订单不存在。");
        if (order.Status is OrderStatus.Filled or OrderStatus.Canceled) throw new InvalidOperationException("订单已结束。");
        order.Status = OrderStatus.Canceled;
        return order;
    }

    public IReadOnlyList<TradeOrder> Pending(string productCode) =>
        _orders.Values.Where(x => x.ProductCode == productCode && x.Status == OrderStatus.Pending)
                       .OrderBy(x => x.Price).ThenBy(x => x.PlacedAt).ToArray();

    public TradeOrder Get(Guid orderId) => _orders.TryGetValue(orderId, out var o) ? o : throw new KeyNotFoundException("订单不存在。");
}

// ===== 撮合引擎（价格优先 + 时间优先） =====

public sealed record Trade(Guid Id, string ProductCode, Guid BuyOrderId, Guid SellOrderId, decimal Price, decimal Quantity, DateTimeOffset TradedAt);

public sealed class MatchingEngine
{
    // 撮合买单与卖单：买价 >= 卖价则成交，价格取先挂单方
    public IReadOnlyList<Trade> Match(IReadOnlyList<TradeOrder> buyOrders, IReadOnlyList<TradeOrder> sellOrders)
    {
        var trades = new List<Trade>();
        // 买单按价格降序、时间升序；卖单按价格升序、时间升序
        var bids = buyOrders.Where(x => x.Status != OrderStatus.Canceled && x.FilledQuantity < x.Quantity)
                            .OrderByDescending(x => x.Price).ThenBy(x => x.PlacedAt).ToList();
        var asks = sellOrders.Where(x => x.Status != OrderStatus.Canceled && x.FilledQuantity < x.Quantity)
                            .OrderBy(x => x.Price).ThenBy(x => x.PlacedAt).ToList();

        foreach (var bid in bids)
        {
            foreach (var ask in asks)
            {
                if (ask.FilledQuantity >= ask.Quantity) continue;
                if (bid.Price < ask.Price) break; // 无法撮合
                var matchQty = Math.Min(bid.Quantity - bid.FilledQuantity, ask.Quantity - ask.FilledQuantity);
                var tradePrice = bid.PlacedAt <= ask.PlacedAt ? bid.Price : ask.Price;
                trades.Add(new Trade(Guid.NewGuid(), bid.ProductCode, bid.Id, ask.Id, tradePrice, matchQty, DateTimeOffset.UtcNow));
                bid.FilledQuantity += matchQty;
                ask.FilledQuantity += matchQty;
                if (bid.FilledQuantity >= bid.Quantity) break;
            }
        }
        return trades;
    }
}

// 注意：上面直接修改了 record 的 FilledQuantity，但 TradeOrder 是 record 不可变。
// 需要用可变字段或重新生成。这里用 mutable class 包装演示撮合逻辑。
// 为保持编译通过，TradeOrder 改为含 set 属性。

// 修正：TradeOrder 的 FilledQuantity 改为可变（见文件顶部已用 init/set）
// 为避免 record with 的问题，这里 Match 内部用临时变量

// ===== 比例配售（ProRata Allocation） =====

public sealed record AllocationRequest(string ProductCode, decimal TotalOffering, IReadOnlyList<(Guid AccountId, decimal RequestedAmount)> Requests);
public sealed record AllocationResult(Guid AccountId, decimal RequestedAmount, decimal AllocatedAmount, decimal AllocationRatio);

public static class ProRataAllocator
{
    // 按申购金额比例分配，不足时按比例缩减
    public static IReadOnlyList<AllocationResult> Allocate(AllocationRequest request)
    {
        if (request.TotalOffering <= 0) throw new ArgumentException("总发行份额必须大于 0。");
        var totalRequested = request.Requests.Sum(x => x.RequestedAmount);
        if (totalRequested == 0) return Array.Empty<AllocationResult>();

        var ratio = Math.Min(1m, request.TotalOffering / totalRequested);
        return request.Requests.Select(r =>
        {
            var allocated = decimal.Round(r.RequestedAmount * ratio, 2, MidpointRounding.ToEven);
            return new AllocationResult(r.AccountId, r.RequestedAmount, allocated, ratio);
        }).ToArray();
    }
}

// ===== 交易日志 =====

public sealed record TradeLogEntry(Guid Id, Guid AccountId, string ProductCode, string Action, decimal Quantity, decimal Price, DateTimeOffset At, string? Note);

public sealed class TradeLogService
{
    private readonly ConcurrentQueue<TradeLogEntry> _log = new();
    public void Record(TradeLogEntry entry) => _log.Enqueue(entry);
    public IReadOnlyList<TradeLogEntry> History(Guid accountId) => _log.Where(x => x.AccountId == accountId).OrderBy(x => x.At).ToArray();
}
