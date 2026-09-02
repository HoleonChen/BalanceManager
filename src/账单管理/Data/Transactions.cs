using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>待写入的一笔流水(本阶段仅支出/收入;转账单独实现)。</summary>
internal sealed class TxnDraft
{
    public required string Date { get; init; }        // yyyy-MM-dd
    public required string Direction { get; init; }   // in | out
    public required long AccountId { get; init; }
    public long? CategoryId { get; init; }
    public required long AmountCents { get; init; }
    public required string Name { get; init; }
    public string Note { get; init; } = "";
    public string Channel { get; init; } = "";
    public bool InPool { get; init; } = true;
}

/// <summary>流水展示行(带账户/分类名、时间 HH:mm)。</summary>
internal sealed class TxnListItem
{
    public long Id { get; init; }
    public required string Direction { get; init; }
    public long AmountCents { get; init; }
    public required string Name { get; init; }
    public string Account { get; init; } = "";
    public string Category { get; init; } = "";
    public string Time { get; init; } = "";
}

/// <summary>流水写读(记账、今日列表、合计)。</summary>
internal static class Transactions
{
    /// <summary>插入一笔,返回自增 id。</summary>
    public static long Add(LedgerSession s, TxnDraft t)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO transactions
  (date, account_id, category_id, channel, name, note, amount_cents, direction,
   source, status, in_pool, created_at)
VALUES
  ($date, $acct, $cat, $channel, $name, $note, $amount, $direction,
   'manual', 'normal', $pool, $created);";
        cmd.Parameters.AddWithValue("$date", t.Date);
        cmd.Parameters.AddWithValue("$acct", t.AccountId);
        cmd.Parameters.AddWithValue("$cat", (object?)t.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$channel", t.Channel);
        cmd.Parameters.AddWithValue("$name", t.Name);
        cmd.Parameters.AddWithValue("$note", t.Note);
        cmd.Parameters.AddWithValue("$amount", t.AmountCents);
        cmd.Parameters.AddWithValue("$direction", t.Direction);
        cmd.Parameters.AddWithValue("$pool", t.InPool ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>作废一笔(软删:置 status='cancelled',流水与合计均不再计入,记录仍留库备查)。</summary>
    public static void Cancel(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE transactions SET status = 'cancelled' WHERE id = $id AND status <> 'cancelled';";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>某日流水(非转账、非取消),按录入倒序。</summary>
    public static IReadOnlyList<TxnListItem> ListByDate(LedgerSession s, string date)
    {
        var list = new List<TxnListItem>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.direction, t.amount_cents, t.name,
       a.name, c.name, substr(t.created_at, 12, 5)
FROM transactions t
LEFT JOIN accounts a   ON a.id  = t.account_id
LEFT JOIN categories c ON c.id  = t.category_id
WHERE t.date = $date AND t.direction <> 'transfer' AND t.status <> 'cancelled'
ORDER BY t.id DESC;";
        cmd.Parameters.AddWithValue("$date", date);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TxnListItem
            {
                Id = r.GetInt64(0),
                Direction = r.GetString(1),
                AmountCents = r.GetInt64(2),
                Name = r.GetString(3),
                Account = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                Category = r.IsDBNull(5) ? string.Empty : r.GetString(5),
                Time = r.IsDBNull(6) ? string.Empty : r.GetString(6)
            });
        }
        return list;
    }

    /// <summary>某日支出/收入合计(不含转账、取消)。</summary>
    public static (long OutCents, long InCents) DayTotals(LedgerSession s, string date)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT
  COALESCE(SUM(CASE WHEN direction = 'out' THEN amount_cents ELSE 0 END), 0),
  COALESCE(SUM(CASE WHEN direction = 'in'  THEN amount_cents ELSE 0 END), 0)
FROM transactions
WHERE date = $date AND direction <> 'transfer' AND status <> 'cancelled';";
        cmd.Parameters.AddWithValue("$date", date);

        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt64(0), r.GetInt64(1));
    }
}
