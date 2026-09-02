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
    /// <summary>新建进行中周期(status='active');end 为空 = 不设结束日期。</summary>
    public static long Insert(LedgerSession s, string name, string startDate, string? endDate)
    {
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

        // 补归属:此前已记、落在本期范围内且仍属「未归属」的流水,一并归入本期
        cmd.CommandText = endDate is null
            ? "UPDATE transactions SET period_id = $pid WHERE period_id IS NULL AND date >= $start;"
            : "UPDATE transactions SET period_id = $pid WHERE period_id IS NULL AND date BETWEEN $start AND $end;";
        cmd.Parameters.AddWithValue("$pid", id);
        cmd.ExecuteNonQuery();

        return id;
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
}
