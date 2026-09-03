using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>
/// 报表数据聚合层(纯 Core、与 UI 无关,SelfTest 直测)。
/// 口径与账面一致:只计 status='normal'(作废/退款均撤出)、转账独立、校准(source=calibration)
/// 从「用途占比」剔除但在总览单列。范围语义:Range=日期区间(天然含未归属空档账);
/// Periods=按周期归属(周期报表不含空档账)。所有金额单位「分」。
/// </summary>
internal static class Reports
{
    // ============ 形状 ============

    /// <summary>报表范围:Range = 自定义日期区间(含未归属);Periods = 一个或多个周期。</summary>
    internal enum ScopeKind { Range, Periods }

    internal sealed class Scope
    {
        public required ScopeKind Kind { get; init; }
        public string? Start { get; init; }       // Range 用
        public string? End { get; init; }
        public IReadOnlyList<long>? PeriodIds { get; init; }   // Periods 用(≥1)
    }

    internal sealed record OverviewRow(long OutCents, long InCents, int Days,
        long AdjOutCents, long AdjInCents);

    /// <summary>支出分类占比行(已归并到大类;未归类单独一行置灰;不含校准)。</summary>
    internal sealed record ShareRow(string Name, string Color, long Cents, bool IsUnassigned);

    /// <summary>跨周期堆叠的一列(每周期一列;Bands 为该周期各支出分类额,已按 sort_order 升序+未归类居尾)。</summary>
    internal sealed record PeriodColumn(long PeriodId, string Name, string Start, string End,
        long InCents, long OutCents, long AdjOutCents, IReadOnlyList<ShareRow> Bands);

    internal sealed record AccountLine(long Id, string Name, string TypeKey, bool Enabled,
        long BookCents, long NetInWindowCents);

    internal sealed record TopRow(string Name, long AmountCents, string Date, string Category, bool IsIncome);

    internal sealed record DailyRow(string Date, long OutCents, long InCents, long CumNetCents);

    internal sealed record TransferLine(string Kind, int Count, long PrincipalCents, long DeltaNetCents);

    // ============ 总览 ============

    public static OverviewRow Overview(LedgerSession s, Scope scope)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = $@"
SELECT COALESCE(SUM(CASE WHEN t.direction='out' THEN t.amount_cents ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN t.direction='in'  THEN t.amount_cents ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN t.direction='out' AND t.source='calibration' THEN t.amount_cents ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN t.direction='in'  AND t.source='calibration' THEN t.amount_cents ELSE 0 END),0)
FROM transactions t
WHERE t.direction <> 'transfer' AND t.status='normal' AND {ScopeWhere(scope, "t")};";
        BindScope(cmd, scope);
        using var r = cmd.ExecuteReader();
        r.Read();
        var (start, end) = ScopeWindow(s, scope);
        return new OverviewRow(r.GetInt64(0), r.GetInt64(1), InclusiveDays(start, end),
            r.GetInt64(2), r.GetInt64(3));
    }

    // ============ 分类占比(支出,归并大类;校准剔除;未归类一行) ============

    public static IReadOnlyList<ShareRow> ExpenseShare(LedgerSession s, Scope scope)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = BandSql + $" AND {ScopeWhere(scope, "t")} {BandGroup} ORDER BY is_ua, so, COALESCE(cc.id, 0);";
        BindScope(cmd, scope);
        return ReadBands(cmd);
    }

    // ============ 跨周期堆叠列 ============

    public static IReadOnlyList<PeriodColumn> StackColumns(LedgerSession s, IReadOnlyList<long> periodIds)
    {
        var columns = new List<PeriodColumn>();
        foreach (var pid in periodIds)
        {
            // 该周期总收支 + 校准(out)
            long inC = 0, outC = 0, adjOut = 0;
            using (var cmd = s.Connection.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COALESCE(SUM(CASE WHEN t.direction='in'  THEN t.amount_cents ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN t.direction='out' THEN t.amount_cents ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN t.direction='out' AND t.source='calibration' THEN t.amount_cents ELSE 0 END),0)
FROM transactions t WHERE t.status='normal' AND t.direction<>'transfer' AND t.period_id=$pid;";
                cmd.Parameters.AddWithValue("$pid", pid);
                using var r = cmd.ExecuteReader();
                r.Read();
                inC = r.GetInt64(0);
                outC = r.GetInt64(1);
                adjOut = r.GetInt64(2);
            }

            IReadOnlyList<ShareRow> bands;
            using (var cmd = s.Connection.CreateCommand())
            {
                cmd.CommandText = BandSql + " AND t.period_id=$pid " + BandGroup + " ORDER BY is_ua, so, COALESCE(cc.id, 0);";
                cmd.Parameters.AddWithValue("$pid", pid);
                bands = ReadBands(cmd);
            }

            var period = Periods.Get(s, pid) ?? throw new InvalidOperationException("周期不存在。");
            columns.Add(new PeriodColumn(pid, period.Name, period.StartDate,
                period.EndDate ?? "", inC, outC, adjOut, bands));
        }
        return columns;
    }

    // ============ 账户与净资产 ============

    /// <summary>各账户当前账面 + 窗口内净变动(不设停用灰显判断,行带 Enabled 由渲染端灰显)。</summary>
    public static IReadOnlyList<AccountLine> AccountsBlock(LedgerSession s, string from, string to)
    {
        var result = new List<AccountLine>();
        foreach (var a in Accounts.ListAll(s))
        {
            var book = AccountCalibration.BookCents(s, a.Id);
            var move = Accounts.MovementBetween(s, a.Id, from, to);
            result.Add(new AccountLine(a.Id, a.Name, a.Type, a.Enabled, book, move.NetCents));
        }
        return result;
    }

    /// <summary>某日净资产(= 各启用账户账面截至该日之和;停用不计)。</summary>
    public static long NetAssetsAt(LedgerSession s, string endInclusive)
    {
        long sum = 0;
        foreach (var a in Accounts.ListEnabled(s))
            sum += AccountCalibration.BookThrough(s, a.Id, endInclusive);
        return sum;
    }

    // ============ 大额 TOP / 每日收支 / 转账小计 ============

    public static IReadOnlyList<TopRow> Top(LedgerSession s, Scope scope, bool income, int n)
    {
        var rows = new List<TopRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = $@"
SELECT t.name, t.amount_cents, t.date, COALESCE(cc.name,'未归类')
FROM transactions t
LEFT JOIN categories c  ON c.id  = t.category_id
LEFT JOIN categories cc ON cc.id = COALESCE(c.parent_id, c.id)
WHERE t.status='normal' AND t.direction='{(income ? "in" : "out")}'
  AND COALESCE(t.source,'')<>'calibration' AND {ScopeWhere(scope, "t")}
ORDER BY t.amount_cents DESC LIMIT $n;";
        cmd.Parameters.AddWithValue("$n", n);
        BindScope(cmd, scope);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new TopRow(r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3), income));
        return rows;
    }

    public static IReadOnlyList<DailyRow> Daily(LedgerSession s, Scope scope)
    {
        var rows = new List<DailyRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = $@"
SELECT t.date,
       COALESCE(SUM(CASE WHEN t.direction='out' THEN t.amount_cents ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN t.direction='in'  THEN t.amount_cents ELSE 0 END),0)
FROM transactions t
WHERE t.status='normal' AND t.direction<>'transfer' AND {ScopeWhere(scope, "t")}
GROUP BY t.date ORDER BY t.date;";
        BindScope(cmd, scope);
        using var r = cmd.ExecuteReader();
        long cum = 0;
        while (r.Read())
        {
            var o = r.GetInt64(1);
            var i = r.GetInt64(2);
            cum += i - o;
            rows.Add(new DailyRow(r.GetString(0), o, i, cum));
        }
        return rows;
    }

    public static IReadOnlyList<TransferLine> TransferSummary(LedgerSession s, Scope scope)
    {
        var rows = new List<TransferLine>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = $@"
SELECT COALESCE(t.transfer_kind,''),
       COUNT(*),
       COALESCE(SUM(t.principal_cents),0),
       COALESCE(SUM(t.delta_cents),0)
FROM transactions t
WHERE t.status='normal' AND t.direction='transfer' AND {ScopeWhere(scope, "t")}
GROUP BY COALESCE(t.transfer_kind,'') ORDER BY COALESCE(t.transfer_kind,'');";
        BindScope(cmd, scope);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new TransferLine(r.GetString(0), Convert.ToInt32(r.GetInt64(1)),
                r.GetInt64(2), r.GetInt64(3)));
        return rows;
    }

    // ============ 内部 SQL 片段 ============

    private const string BandSql = @"
SELECT COALESCE(cc.name,'未归类'), COALESCE(cc.color,'#B0BEC5'),
       SUM(t.amount_cents),
       CASE WHEN t.category_id IS NULL THEN 1 ELSE 0 END AS is_ua,
       COALESCE(cc.sort_order, 2147483647) AS so
FROM transactions t
LEFT JOIN categories c  ON c.id  = t.category_id
LEFT JOIN categories cc ON cc.id = COALESCE(c.parent_id, c.id)
WHERE t.status='normal' AND t.direction='out' AND COALESCE(t.source,'')<>'calibration'
  AND (t.category_id IS NULL OR cc.kind='expense')";

    /// <summary>BandSql 的分组:未归类(分类 null)单独一组;其余按归并后的顶层分类 id 分组。</summary>
    private const string BandGroup =
        "GROUP BY CASE WHEN t.category_id IS NULL THEN -1 ELSE COALESCE(cc.id, 0) END";

    private static List<ShareRow> ReadBands(SqliteCommand cmd)
    {
        var rows = new List<ShareRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new ShareRow(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3) == 1));
        return rows;
    }

    private static string ScopeWhere(Scope scope, string t) => scope.Kind switch
    {
        ScopeKind.Range => $"{t}.date BETWEEN $from AND $to",
        _ => $"{t}.period_id IN ({string.Join(",", scope.PeriodIds!)})"
    };

    private static void BindScope(SqliteCommand cmd, Scope scope)
    {
        if (scope.Kind == ScopeKind.Range)
        {
            cmd.Parameters.AddWithValue("$from", scope.Start);
            cmd.Parameters.AddWithValue("$to", scope.End);
        }
    }

    /// <summary>窗口起止日期:Range 直接取;Periods 取所选周期最小开始 ~ 最大结束(未设结束日按今天)。</summary>
    private static (string From, string To) ScopeWindow(LedgerSession s, Scope scope)
    {
        if (scope.Kind == ScopeKind.Range)
            return (scope.Start!, scope.End!);
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT MIN(start_date),
       MAX(CASE WHEN end_date IS NULL THEN $today ELSE end_date END)
FROM periods WHERE id IN (" + string.Join(",", scope.PeriodIds!) + ");";
        cmd.Parameters.AddWithValue("$today", DateTime.Today.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetString(0), r.GetString(1));
    }

    private static int InclusiveDays(string from, string to)
    {
        if (DateTime.TryParse(from, out var f) && DateTime.TryParse(to, out var t2) && t2 >= f)
            return (int)(t2 - f).TotalDays + 1;
        return 0;
    }
}
