using System.Collections.Concurrent;
using System.Globalization;

namespace Payments.Api.Reconciliation;

// ===== 对账文件解析（CSV / 定长） =====

public enum ReconciliationFormat { Csv, FixedLength }
public sealed record ReconciliationRecord(string TransactionId, decimal Amount, string Currency, string Status, DateTimeOffset TransactionTime);

public static class ReconciliationFileParser
{
    // 解析渠道对账文件（CSV 格式：txId,amount,currency,status,time）
    public static IReadOnlyList<ReconciliationRecord> ParseCsv(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var records = new List<ReconciliationRecord>();
        foreach (var line in lines.Skip(1)) // 跳过表头
        {
            var fields = line.Trim().Split(',');
            if (fields.Length < 5) continue;
            records.Add(new ReconciliationRecord(
                fields[0],
                decimal.Parse(fields[1], CultureInfo.InvariantCulture),
                fields[2],
                fields[3],
                DateTimeOffset.Parse(fields[4], CultureInfo.InvariantCulture)));
        }
        return records;
    }

    // 解析定长对账文件（Demo：每行 50 字符，txId[0-16] amount[17-30] status[31-40]）
    public static IReadOnlyList<ReconciliationRecord> ParseFixedLength(string content, int recordLength = 50)
    {
        var records = new List<ReconciliationRecord>();
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < recordLength) continue;
            var txId = line[..16].Trim();
            var amount = decimal.Parse(line.Substring(16, 14).Trim(), CultureInfo.InvariantCulture);
            var status = line.Substring(30, 10).Trim();
            records.Add(new ReconciliationRecord(txId, amount, "CNY", status, DateTimeOffset.UtcNow));
        }
        return records;
    }
}

// ===== 差异识别（Diff Detector） =====

public enum DiffType { Match, MissingInLocal, MissingInChannel, AmountMismatch, StatusMismatch }
public sealed record ReconciliationDiff(string TransactionId, DiffType Type, decimal? LocalAmount, decimal? ChannelAmount, string? LocalStatus, string? ChannelStatus);

public static class DiffDetector
{
    // 比对本地与渠道流水，识别四类差异
    public static IReadOnlyList<ReconciliationDiff> Detect(
        IReadOnlyList<ReconciliationRecord> local,
        IReadOnlyList<ReconciliationRecord> channel)
    {
        var diffs = new List<ReconciliationDiff>();
        var localDict = local.ToDictionary(x => x.TransactionId);
        var channelDict = channel.ToDictionary(x => x.TransactionId);

        // 渠道有本地无：MissingInLocal
        foreach (var ch in channelDict)
        {
            if (!localDict.ContainsKey(ch.Key))
                diffs.Add(new ReconciliationDiff(ch.Key, DiffType.MissingInLocal, null, ch.Value.Amount, null, ch.Value.Status));
        }

        // 本地有渠道无：MissingInChannel
        foreach (var loc in localDict)
        {
            if (!channelDict.TryGetValue(loc.Key, out var ch))
            {
                diffs.Add(new ReconciliationDiff(loc.Key, DiffType.MissingInChannel, loc.Value.Amount, null, loc.Value.Status, null));
                continue;
            }
            // 金额不符
            if (loc.Value.Amount != ch.Amount)
                diffs.Add(new ReconciliationDiff(loc.Key, DiffType.AmountMismatch, loc.Value.Amount, ch.Amount, loc.Value.Status, ch.Status));
            // 状态不符
            else if (loc.Value.Status != ch.Status)
                diffs.Add(new ReconciliationDiff(loc.Key, DiffType.StatusMismatch, loc.Value.Amount, ch.Amount, loc.Value.Status, ch.Status));
        }
        return diffs;
    }
}

// ===== 差错处理状态机 =====

public enum DiffStatus { Identified, Investigating, Resolved, WrittenOff }
public sealed record DiffCase(Guid Id, string TransactionId, DiffType Type, DiffStatus Status, string? Resolution, DateTimeOffset CreatedAt);

public sealed class DiffResolutionService
{
    private readonly ConcurrentDictionary<Guid, DiffCase> _cases = new();

    public DiffCase Open(string transactionId, DiffType type)
    {
        var diff = new DiffCase(Guid.NewGuid(), transactionId, type, DiffStatus.Identified, null, DateTimeOffset.UtcNow);
        _cases[diff.Id] = diff;
        return diff;
    }

    public DiffCase Investigate(Guid caseId)
    {
        var diff = Get(caseId);
        if (diff.Status != DiffStatus.Identified) throw new InvalidOperationException("差错已处理。");
        return Update(diff with { Status = DiffStatus.Investigating });
    }

    public DiffCase Resolve(Guid caseId, string resolution)
    {
        var diff = Get(caseId);
        return Update(diff with { Status = DiffStatus.Resolved, Resolution = resolution });
    }

    public DiffCase WriteOff(Guid caseId)
    {
        var diff = Get(caseId);
        return Update(diff with { Status = DiffStatus.WrittenOff, Resolution = "挂账处理" });
    }

    public DiffCase Get(Guid caseId) => _cases.TryGetValue(caseId, out var d) ? d : throw new KeyNotFoundException("差错单不存在。");
    private DiffCase Update(DiffCase d) { _cases[d.Id] = d; return d; }
}
