using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>记账周期行(periods 表)。周期决定某笔流水归属哪期生活费。</summary>
internal sealed record PeriodRow(
    long Id,
    string Name,
    string StartDate,
    string? EndDate,
    string Status);

/// <summary>周期写读:新建(active)、按日期找覆盖周期。</summary>
internal static class Periods
{
    /// <summary>新建进行中周期(status='active');end 为空 = 不设结束日期。
    /// 周期不允许日期重叠(与任何既有周期重叠都拒绝),避免封存/归属冲突。</summary>
    public static long Insert(LedgerSession s, string name, string startDate, string? endDate)
    {
        if (HasOverlap(s, startDate, endDate))
            throw new InvalidOperationException(
                "新周期与已有周期日期重叠。请先结束/封存既有周期,或调整起止日期。");
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO periods (name, start_date, end_date, status)
VALUES ($name, $start, $end, 'active');";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$start", startDate);
        cmd.Parameters.AddWithValue("$end", (object?)endDate ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid();";
        var id = Convert.ToInt64(cmd.ExecuteScalar());

        // 补归属:此前已记、落在本期范围内且仍属「未归属」的流水,一并归入本期。
        // 但某日若已被封存周期覆盖,视为冻结历史:已归属的归其原期(非未归属,自然不动),
        // 仍游离未归属的也不改挂(不能塞进新周期变相改写封存期)。
        const string notSealed = @"
  AND NOT EXISTS (SELECT 1 FROM periods x WHERE x.status = 'sealed'
      AND x.start_date <= transactions.date
      AND (x.end_date IS NULL OR x.end_date >= transactions.date))";
        cmd.CommandText = endDate is null
            ? "UPDATE transactions SET period_id = $pid WHERE period_id IS NULL AND date >= $start" + notSealed + ";"
            : "UPDATE transactions SET period_id = $pid WHERE period_id IS NULL AND date BETWEEN $start AND $end" + notSealed + ";";
        cmd.Parameters.AddWithValue("$pid", id);
        cmd.ExecuteNonQuery();

        return id;
    }

    /// <summary>新区间(任一 end 为 null = 长期)是否与任何既有周期重叠。</summary>
    private static bool HasOverlap(LedgerSession s, string startDate, string? endDate)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*) FROM periods
WHERE start_date <= $newEnd AND (end_date IS NULL OR end_date >= $newStart);";
        cmd.Parameters.AddWithValue("$newStart", startDate);
        cmd.Parameters.AddWithValue("$newEnd", endDate ?? "9999-12-31");
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>列出进行中周期,按开始日倒序。</summary>
    public static IReadOnlyList<PeriodRow> ListActive(LedgerSession s)
    {
        var list = new List<PeriodRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, start_date, end_date, status
FROM periods
WHERE status = 'active'
ORDER BY start_date DESC, id DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PeriodRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4)));
        }
        return list;
    }

    /// <summary>取覆盖某日的进行中周期(供顶栏提示;多期重叠时取开始最晚者)。</summary>
    public static PeriodRow? GetCoveringActive(LedgerSession s, string date)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, start_date, end_date, status
FROM periods
WHERE status = 'active' AND start_date <= $date
  AND (end_date IS NULL OR end_date >= $date)
ORDER BY start_date DESC, id DESC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$date", date);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new PeriodRow(
            r.GetInt64(0),
            r.GetString(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.GetString(4));
    }

    /// <summary>按 id 取周期;不存在返回 null。</summary>
    public static PeriodRow? Get(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, start_date, end_date, status FROM periods WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new PeriodRow(r.GetInt64(0), r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4));
    }

    /// <summary>列出全部周期(含已封存),按开始日倒序(周期管理用)。</summary>
    public static IReadOnlyList<PeriodRow> ListAll(LedgerSession s)
    {
        var list = new List<PeriodRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, start_date, end_date, status
FROM periods
ORDER BY start_date DESC, id DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PeriodRow(r.GetInt64(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4)));
        }
        return list;
    }

    /// <summary>封存一个进行中周期(status → 'sealed',只读)。已封存则 no-op。</summary>
    public static void Seal(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE periods SET status = 'sealed' WHERE id = $id AND status = 'active';";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>解除封存(status → 'active',恢复可改)。仅封存态可解。</summary>
    public static void Unseal(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE periods SET status = 'active' WHERE id = $id AND status = 'sealed';";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>改结束日(未设结束日的周期封存前补一个收尾日等)。</summary>
    public static void SetEndDate(LedgerSession s, long id, string endDate)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE periods SET end_date = $end WHERE id = $id;";
        cmd.Parameters.AddWithValue("$end", endDate);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>该日是否落在任一封存周期内(= 该日已冻结只读)。</summary>
    public static bool HasSealedCovering(LedgerSession s, string date)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*) FROM periods
WHERE status = 'sealed' AND start_date <= $date
  AND (end_date IS NULL OR end_date >= $date);";
        cmd.Parameters.AddWithValue("$date", date);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>该日落在封存周期内则抛 <see cref="LedgerReadonlyException"/>(写保护入口)。</summary>
    public static void ThrowIfSealed(LedgerSession s, string date)
    {
        if (HasSealedCovering(s, date))
            throw new LedgerReadonlyException(date);
    }

    /// <summary>进行中(未封存)且已结束的周期里,开始最晚的一个(供「到期推荐新建」提示)。</summary>
    public static PeriodRow? GetLatestExpiredActive(LedgerSession s, string today)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, start_date, end_date, status
FROM periods
WHERE status = 'active' AND end_date IS NOT NULL AND end_date < $today
ORDER BY start_date DESC, id DESC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$today", today);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new PeriodRow(r.GetInt64(0), r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4));
    }
}

/// <summary>某日已被封存周期覆盖,该日流水只读、不可增删改。</summary>
internal sealed class LedgerReadonlyException : Exception
{
    public LedgerReadonlyException(string date)
        : base(Friendly(date))
    {
        Date = date;
    }

    public string Date { get; }

    /// <summary>统一文案:UI 预检与异常共用,避免两份措辞漂移。</summary>
    public static string Friendly(string date)
        => $"该日期({date})属于已封存周期,处于只读状态,不能新增/修改/作废流水。\n如需改动请先到「工具 → 周期管理」解除封存。";
}
