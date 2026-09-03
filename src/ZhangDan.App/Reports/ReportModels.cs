using System;
using System.Collections.Generic;
using System.Text.Json;
using ZhangDan;

namespace ZhangDan.App.Reporting;

/// <summary>报表范围模式:Period=单周期;Custom=自定义日期(可跨周期、含空档);Compare=多周期对比。</summary>
internal enum ReportRangeKind
{
    Period,
    Custom,
    Compare
}

/// <summary>
/// 一次生成请求(历史记录里持久化,支持「重新生成」)。
/// </summary>
internal sealed class ReportRequest
{
    public ReportRangeKind Kind { get; set; }
    public string? Start { get; set; }               // Custom 用 yyyy-MM-dd
    public string? End { get; set; }
    public long[]? PeriodIds { get; set; }           // Period(1 个)/ Compare(≥2,按开始日升序)
    public bool BlockOverview { get; set; } = true;
    public bool BlockShare { get; set; } = true;
    public bool BlockTrend { get; set; } = true;
    public bool BlockAccounts { get; set; } = true;
    public bool BlockTop { get; set; } = true;
    public bool BlockDaily { get; set; } = true;
    public bool BlockPool { get; set; } = true;
    public bool BlockTransfer { get; set; } = true;
    public bool PercentMode { get; set; }            // 趋势图金额 / 100% 占比
    public bool ToPdf { get; set; } = true;
    public bool ToXlsx { get; set; } = true;
    public string? SaveDir { get; set; }
    public string? BaseName { get; set; }            // 文件名(无扩展名,已清洗)
    public string ScopeLabel { get; set; } = "";     // 人类可读范围(历史列/文件名后缀用)
}

/// <summary>历史一条(存账本 meta 键 report.history,随账本走)。</summary>
internal sealed class ReportHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GeneratedAt { get; set; } = "";
    public ReportRequest Request { get; set; } = new();
    public string? PdfPath { get; set; }
    public string? XlsxPath { get; set; }
}

/// <summary>
/// 报表历史存储:JSON 数组存于账本 meta(键 report.history)。随账本文件移动、无外置边车。
/// </summary>
internal static class ReportStore
{
    private const string Key = "report.history";

    public static List<ReportHistoryEntry> Load(LedgerSession s)
    {
        var raw = s.GetMeta(Key);
        if (string.IsNullOrWhiteSpace(raw))
            return new List<ReportHistoryEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<ReportHistoryEntry>>(raw) ?? new List<ReportHistoryEntry>();
        }
        catch
        {
            return new List<ReportHistoryEntry>();   // 损坏当空,不影响使用
        }
    }

    public static void Append(LedgerSession s, ReportHistoryEntry e)
    {
        var list = Load(s);
        list.Insert(0, e);
        s.SetMeta(Key, JsonSerializer.Serialize(list));
    }

    public static void Remove(LedgerSession s, Guid id)
    {
        var list = Load(s);
        list.RemoveAll(x => x.Id == id);
        Save(s, list);
    }

    /// <summary>整表覆写(「重新生成」就地改一条时用)。</summary>
    public static void Save(LedgerSession s, List<ReportHistoryEntry> list)
        => s.SetMeta(Key, JsonSerializer.Serialize(list));
}
