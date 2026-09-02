using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>一条校准审计记录(calibration_log)。</summary>
internal sealed record CalibrationEntry(
    long Id,
    long AccountId,
    string RecordedAt,
    long BookCents,
    long ActualCents,
    long DiffCents,
    string Method,
    string? Note);

/// <summary>校准处理方式常量(设计 §3.2)。</summary>
internal static class CalibMethod
{
    public const string Adjustment = "adjustment";   // 记调整流水(推荐)
    public const string RealDetails = "real_details"; // 补记真实明细
    public const string BaseOnly = "base_only";       // 仅更新基准

    public static string Label(string m) => m switch
    {
        Adjustment => "记调整流水",
        RealDetails => "补记真实明细",
        BaseOnly => "仅更新基准",
        _ => m
    };
}

/// <summary>
/// 账户账面余额与校准。
/// 账面 = 基准余额 + 基准日后该账户相关流水(收支 + 转账出入,含到账浮动);
/// 校准三种处理见设计 §3.2,每次均留审计日志(calibration_log)。
/// </summary>
internal static class AccountCalibration
{
    /// <summary>读基准余额与其基准日(可能为 null = 无基准日)。</summary>
    private static (long BaseCents, string? Date) ReadBase(LedgerSession s, long accountId)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT balance_base_cents, balance_date FROM accounts WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", accountId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            throw new InvalidOperationException("账户不存在。");
        return (r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1));
    }

    /// <summary>某账户从某日起(含当日)的净变动(分)。基准日为空则从最早算。</summary>
    public static long NetSince(LedgerSession s, long accountId, string? fromDate)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT COALESCE(SUM(CASE
    WHEN direction = 'in'  THEN amount_cents
    WHEN direction = 'out' THEN -amount_cents
    WHEN direction = 'transfer' AND account_id = $acct
         THEN -COALESCE(principal_cents, 0)
    WHEN direction = 'transfer' AND to_account_id = $acct
         THEN amount_cents + COALESCE(delta_cents, 0)
    ELSE 0 END), 0)
FROM transactions
WHERE status = 'normal' AND date >= $from
  AND ((direction IN ('in','out') AND account_id = $acct)
    OR (direction = 'transfer' AND (account_id = $acct OR to_account_id = $acct)));";
        cmd.Parameters.AddWithValue("$acct", accountId);
        cmd.Parameters.AddWithValue("$from", fromDate ?? "0000-01-01");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>账面余额 = 基准 + 基准日后净变动(设计 §3.2「账面余额 = balance_base + Σ 流水」)。</summary>
    public static long BookCents(LedgerSession s, long accountId)
    {
        var (baseCents, date) = ReadBase(s, accountId);
        return baseCents + NetSince(s, accountId, date);
    }

    /// <summary>执行一次校准(实际余额对齐)。method 三选一;记审计日志。返回差额(=实际−账面)。</summary>
    public static long Apply(LedgerSession s, long accountId, long actualCents,
        string method, string? note)
    {
        var book = BookCents(s, accountId);
        var diff = actualCents - book;

        if (method == CalibMethod.Adjustment && diff != 0)
        {
            InsertAdjustment(s, accountId, diff, note);
        }
        else if (method == CalibMethod.BaseOnly && diff != 0)
        {
            // 直接平移基准,不动流水;账面随之对齐实际
            using var cmd = s.Connection.CreateCommand();
            cmd.CommandText = "UPDATE accounts SET balance_base_cents = balance_base_cents + $d WHERE id = $id;";
            cmd.Parameters.AddWithValue("$d", diff);
            cmd.Parameters.AddWithValue("$id", accountId);
            cmd.ExecuteNonQuery();
        }

        Log(s, accountId, book, actualCents, diff, method, note);
        return diff;
    }

    /// <summary>记调整流水:差额≠0 生成一笔,方向=实际&gt;账面→收入/实际&lt;账面→支出,分类固定「差额调整」。</summary>
    private static void InsertAdjustment(LedgerSession s, long accountId, long diffCents, string? note)
    {
        var income = diffCents > 0;
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO transactions
  (period_id, date, account_id, category_id, channel, name, note, amount_cents,
   direction, source, status, in_pool, created_at)
VALUES
  ((SELECT id FROM periods WHERE status = 'active'
     AND start_date <= $date AND (end_date IS NULL OR end_date >= $date)
     ORDER BY start_date DESC, id DESC LIMIT 1),
   $date, $acct, $cat, '', $name, $note, $amt,
   $dir, 'calibration', 'normal', 0, $created);";
        cmd.Parameters.AddWithValue("$date", DateTime.Now.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$acct", accountId);
        cmd.Parameters.AddWithValue("$cat", AdjustmentCategoryId(s));
        cmd.Parameters.AddWithValue("$name", "差额调整");
        cmd.Parameters.AddWithValue("$note", note is { Length: > 0 } ? note : "校准差额");
        cmd.Parameters.AddWithValue("$amt", Math.Abs(diffCents));
        cmd.Parameters.AddWithValue("$dir", income ? "in" : "out");
        cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>「差额调整」分类 id(系统保留;缺则补建在支出「其他」下)。</summary>
    private static long AdjustmentCategoryId(LedgerSession s)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM categories WHERE name = '差额调整' ORDER BY id LIMIT 1;";
        var hit = cmd.ExecuteScalar();
        if (hit is not null)
            return Convert.ToInt64(hit);

        // 补建:挂在支出「其他」下(父 id 取 name='其他' 且 parent_id IS NULL 的最小 id;无则空)
        cmd.CommandText = "SELECT id FROM categories WHERE name = '其他' AND parent_id IS NULL ORDER BY id LIMIT 1;";
        var parent = cmd.ExecuteScalar() as long?;
        cmd.CommandText = @"
INSERT INTO categories (parent_id, name, keyword, color, sort_order)
VALUES ($p, '差额调整', NULL, '#B0BEC5',
  (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM categories));";
        cmd.Parameters.AddWithValue("$p", parent is null ? DBNull.Value : parent.Value);
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>写一条校准审计。</summary>
    private static void Log(LedgerSession s, long accountId, long bookCents,
        long actualCents, long diffCents, string method, string? note)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO calibration_log
  (account_id, recorded_at, book_cents, actual_cents, diff_cents, method, note)
VALUES ($acct, $now, $book, $actual, $diff, $method, $note);";
        cmd.Parameters.AddWithValue("$acct", accountId);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$book", bookCents);
        cmd.Parameters.AddWithValue("$actual", actualCents);
        cmd.Parameters.AddWithValue("$diff", diffCents);
        cmd.Parameters.AddWithValue("$method", method);
        cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>某账户校准历史(倒序)。</summary>
    public static IReadOnlyList<CalibrationEntry> History(LedgerSession s, long accountId)
    {
        var list = new List<CalibrationEntry>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, account_id, recorded_at, book_cents, actual_cents, diff_cents, method, note
FROM calibration_log
WHERE account_id = $acct
ORDER BY id DESC;";
        cmd.Parameters.AddWithValue("$acct", accountId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CalibrationEntry(
                r.GetInt64(0),
                r.GetInt64(1),
                r.GetString(2),
                r.GetInt64(3),
                r.GetInt64(4),
                r.GetInt64(5),
                r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return list;
    }
}
