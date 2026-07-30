using System.Collections.Concurrent;

namespace Lending.Api.Reporting;

// ===== 监管报表（EAST/1104/征信报送） =====

public enum ReportType { EAST, R1104, CreditBureau, AssetQuality }
public sealed record RegulatoryReport(Guid Id, ReportType Type, string Period, string Format, int RecordCount, string Status, DateTimeOffset GeneratedAt, string Content);

public sealed class RegulatoryReportGenerator
{
    private readonly ConcurrentDictionary<Guid, RegulatoryReport> _reports = new();

    public RegulatoryReport Generate(ReportType type, string period, IEnumerable<object> records)
    {
        var list = records.ToArray();
        var content = System.Text.Json.JsonSerializer.Serialize(list);
        var report = new RegulatoryReport(Guid.NewGuid(), type, period, type == ReportType.EAST ? "CSV" : "XLSX", list.Length, "READY", DateTimeOffset.UtcNow, content);
        _reports[report.Id] = report;
        return report;
    }

    public RegulatoryReport Get(Guid id) => _reports.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException("报表不存在。");
    public IReadOnlyList<RegulatoryReport> ByPeriod(string period) => _reports.Values.Where(x => x.Period == period).ToArray();
}

// ===== 信贷资产报送 =====

public sealed record AssetQualitySnapshot(Guid Id, string Period, decimal TotalLoanBalance, decimal NonPerformingBalance, decimal NPLRatio, decimal ProvisionCoverage, DateTimeOffset SnapshotAt)
{
    // 不良率 = 不良余额 / 贷款余额
    // 拨备覆盖率 = 拨备余额 / 不良余额
};

public sealed class AssetReportService
{
    public AssetQualitySnapshot Snapshot(string period, decimal totalLoanBalance, decimal nonPerformingBalance, decimal provisionBalance)
    {
        if (totalLoanBalance <= 0) throw new ArgumentException("贷款总余额必须大于 0。");
        var nplRatio = decimal.Round(nonPerformingBalance / totalLoanBalance, 4, MidpointRounding.ToEven);
        var coverage = nonPerformingBalance > 0 ? decimal.Round(provisionBalance / nonPerformingBalance, 4, MidpointRounding.ToEven) : decimal.MaxValue;
        return new AssetQualitySnapshot(Guid.NewGuid(), period, totalLoanBalance, nonPerformingBalance, nplRatio, coverage, DateTimeOffset.UtcNow);
    }
}

// ===== 数据中台（接口骨架 + 内存实现） =====

public interface IDataPlatform
{
    void Register(string dataset, object record);
    IEnumerable<T> Query<T>(string dataset, Func<T, bool>? predicate = null);
    int Count(string dataset);
}

public sealed class InMemoryDataPlatform : IDataPlatform
{
    private readonly ConcurrentDictionary<string, List<object>> _store = new();

    public void Register(string dataset, object record)
    {
        var list = _store.GetOrAdd(dataset, _ => new List<object>());
        lock (list) { list.Add(record); }
    }

    public IEnumerable<T> Query<T>(string dataset, Func<T, bool>? predicate = null)
    {
        if (!_store.TryGetValue(dataset, out var list)) return Array.Empty<T>();
        var typed = list.OfType<T>();
        return predicate is null ? typed : typed.Where(predicate);
    }

    public int Count(string dataset) => _store.TryGetValue(dataset, out var list) ? list.Count : 0;
}
