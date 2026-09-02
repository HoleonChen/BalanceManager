using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>待写入的一笔流水(支出/收入)。</summary>
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

/// <summary>待写入的一笔转账(direction='transfer')。本金转出,转入 = 本金 + 浮动。</summary>
internal sealed class TransferDraft
{
    public required string Date { get; init; }        // yyyy-MM-dd
    public required long FromAccountId { get; init; }
    public required long ToAccountId { get; init; }
    public required long PrincipalCents { get; init; }
    public long DeltaCents { get; init; }             // +记收益 / -记手续费,默认 0
    public required string Kind { get; init; }        // 互转/充值/提现/理财结算/存取
    public string Note { get; init; } = "";
    public bool InPool { get; init; }                 // 转出池账户默认不计池,可勾
}

/// <summary>可编辑的一笔支出/收入(编辑表单回填用;转账独立处理)。</summary>
internal sealed record TxnEditable(
    long Id,
    string Date,          // yyyy-MM-dd(编辑不改日期)
    string Direction,     // in | out
    long AccountId,
    long? CategoryId,
    long AmountCents,
    string Name,
    string Channel,
    string Note,
    bool InPool);

/// <summary>可编辑的一笔转账(编辑表单回填用)。</summary>
internal sealed record TransferEditable(
    long Id,
    string Date,             // yyyy-MM-dd(编辑不改日期)
    long FromAccountId,
    long ToAccountId,
    long PrincipalCents,
    long DeltaCents,
    string Kind,
    string Note,
    bool InPool);

/// <summary>流水展示行(带账户/分类名、时间 HH:mm;转账含对端账户/浮动/类别)。</summary>
internal sealed class TxnListItem
{
    public long Id { get; init; }
    public required string Direction { get; init; }   // in | out | transfer
    public long AmountCents { get; init; }            // 转账=本金
    public required string Name { get; init; }
    public string Account { get; init; } = "";        // 转账=转出账户
    public string AccountTo { get; init; } = "";      // 转账=转入账户
    public string Category { get; init; } = "";
    public string Kind { get; init; } = "";           // 转账类别(互转/充值/…)
    public long DeltaCents { get; init; }             // 转账浮动
    public string Date { get; init; } = "";           // yyyy-MM-dd(范围查询用)
    public string Time { get; init; } = "";
}

/// <summary>流水写读(记账、单日流水、合计、作废)。</summary>
internal static class Transactions
{
    /// <summary>插入一笔支出/收入,按日期自动归属进行中周期,返回自增 id。</summary>
    public static long Add(LedgerSession s, TxnDraft t)
    {
        Periods.ThrowIfSealed(s, t.Date);   // 封存周期内的日期只读
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO transactions
  (period_id, date, account_id, category_id, channel, name, note, amount_cents,
   direction, source, status, in_pool, created_at)
VALUES
  ((SELECT id FROM periods WHERE status = 'active'
     AND start_date <= $date AND (end_date IS NULL OR end_date >= $date)
     ORDER BY start_date DESC, id DESC LIMIT 1),
   $date, $acct, $cat, $channel, $name, $note, $amount, $direction,
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

    /// <summary>插入一笔转账(direction='transfer');转出 −本金,转入 +(本金+浮动)。</summary>
    public static long Transfer(LedgerSession s, TransferDraft t)
    {
        Periods.ThrowIfSealed(s, t.Date);   // 封存周期内的日期只读
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO transactions
  (period_id, date, account_id, to_account_id, name, note, amount_cents,
   direction, source, status, in_pool, principal_cents, delta_cents, transfer_kind, created_at)
VALUES
  ((SELECT id FROM periods WHERE status = 'active'
     AND start_date <= $date AND (end_date IS NULL OR end_date >= $date)
     ORDER BY start_date DESC, id DESC LIMIT 1),
   $date, $from, $to, $name, $note, $principal, 'transfer',
   'manual', 'normal', $pool, $principal, $delta, $kind, $created);";
        cmd.Parameters.AddWithValue("$date", t.Date);
        cmd.Parameters.AddWithValue("$from", t.FromAccountId);
        cmd.Parameters.AddWithValue("$to", t.ToAccountId);
        cmd.Parameters.AddWithValue("$name", t.Kind);   // 名称=类别;列表「账户 A→B」已示路径
        cmd.Parameters.AddWithValue("$note", t.Note);
        cmd.Parameters.AddWithValue("$principal", t.PrincipalCents);
        cmd.Parameters.AddWithValue("$delta", t.DeltaCents);
        cmd.Parameters.AddWithValue("$kind", t.Kind);
        cmd.Parameters.AddWithValue("$pool", t.InPool ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>作废一笔(软删:置 status='cancelled',流水与合计均不再计入,记录仍留库备查)。</summary>
    public static void Cancel(LedgerSession s, long id)
    {
        var date = RowDate(s, id);
        if (date is null)
            return;                       // 记录不存在/已删,无事可做
        Periods.ThrowIfSealed(s, date);   // 封存周期内的流水只读
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE transactions SET status = 'cancelled' WHERE id = $id AND status <> 'cancelled';";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>按 id 读一笔转账(非转账/已作废返回 null)。</summary>
    public static TransferEditable? GetTransfer(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, date, account_id, to_account_id, principal_cents, delta_cents, transfer_kind, note, in_pool
FROM transactions
WHERE id = $id AND direction = 'transfer' AND status <> 'cancelled';";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new TransferEditable(
            r.GetInt64(0),
            r.GetString(1),
            r.GetInt64(2),
            r.GetInt64(3),
            r.IsDBNull(4) ? 0 : r.GetInt64(4),
            r.IsDBNull(5) ? 0 : r.GetInt64(5),
            r.IsDBNull(6) ? string.Empty : r.GetString(6),
            r.GetString(7),
            r.GetInt32(8) != 0);
    }

    /// <summary>就地修改一笔转账(改转出/转入/本金/Δ/类别/备注/入池;日期与周期归属不变)。</summary>
    public static void UpdateTransfer(LedgerSession s, TransferEditable t)
    {
        var date = RowDate(s, t.Id);
        if (date is null)
            return;
        Periods.ThrowIfSealed(s, date);   // 封存周期内的流水只读
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
UPDATE transactions
SET account_id = $from, to_account_id = $to, amount_cents = $principal,
    principal_cents = $principal, delta_cents = $delta, transfer_kind = $kind,
    name = $kind, note = $note, in_pool = $pool
WHERE id = $id;";
        cmd.Parameters.AddWithValue("$from", t.FromAccountId);
        cmd.Parameters.AddWithValue("$to", t.ToAccountId);
        cmd.Parameters.AddWithValue("$principal", t.PrincipalCents);
        cmd.Parameters.AddWithValue("$delta", t.DeltaCents);
        cmd.Parameters.AddWithValue("$kind", t.Kind);
        cmd.Parameters.AddWithValue("$note", t.Note);
        cmd.Parameters.AddWithValue("$pool", t.InPool ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", t.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>按 id 读一笔支出/收入(转账/已作废返回 null)。</summary>
    public static TxnEditable? GetEditable(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, date, direction, account_id, category_id, amount_cents, name, channel, note, in_pool
FROM transactions
WHERE id = $id AND direction <> 'transfer' AND status <> 'cancelled';";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new TxnEditable(
            r.GetInt64(0),
            r.GetString(1),
            r.GetString(2),
            r.GetInt64(3),
            r.IsDBNull(4) ? (long?)null : r.GetInt64(4),
            r.GetInt64(5),
            r.GetString(6),
            r.GetString(7),
            r.GetString(8),
            r.GetInt32(9) != 0);
    }

    /// <summary>就地修改一笔支出/收入(改方向/账户/分类/金额/名称/渠道/备注/入池;日期与周期归属保持不变)。</summary>
    public static void Update(LedgerSession s, TxnEditable t)
    {
        var date = RowDate(s, t.Id);
        if (date is null)
            return;
        Periods.ThrowIfSealed(s, date);   // 封存周期内的流水只读
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
UPDATE transactions
SET direction = $dir, account_id = $acct, category_id = $cat, amount_cents = $amount,
    name = $name, note = $note, channel = $channel, in_pool = $pool
WHERE id = $id;";
        cmd.Parameters.AddWithValue("$dir", t.Direction);
        cmd.Parameters.AddWithValue("$acct", t.AccountId);
        cmd.Parameters.AddWithValue("$cat", (object?)t.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$amount", t.AmountCents);
        cmd.Parameters.AddWithValue("$name", t.Name);
        cmd.Parameters.AddWithValue("$note", t.Note);
        cmd.Parameters.AddWithValue("$channel", t.Channel);
        cmd.Parameters.AddWithValue("$pool", t.InPool ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", t.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>某日流水(支出/收入/转账;非取消),按录入倒序。</summary>
    public static IReadOnlyList<TxnListItem> ListByDate(LedgerSession s, string date)
    {
        var list = new List<TxnListItem>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.direction, t.amount_cents, t.name,
       a.name, c.name, substr(t.created_at, 12, 5),
       b.name, t.delta_cents, t.transfer_kind, t.date
FROM transactions t
LEFT JOIN accounts a   ON a.id  = t.account_id
LEFT JOIN accounts b   ON b.id  = t.to_account_id
LEFT JOIN categories c ON c.id  = t.category_id
WHERE t.date = $date AND t.status <> 'cancelled'
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
                Time = r.IsDBNull(6) ? string.Empty : r.GetString(6),
                AccountTo = r.IsDBNull(7) ? string.Empty : r.GetString(7),
                DeltaCents = r.IsDBNull(8) ? 0 : r.GetInt64(8),
                Kind = r.IsDBNull(9) ? string.Empty : r.GetString(9),
                Date = r.IsDBNull(10) ? string.Empty : r.GetString(10)
            });
        }
        return list;
    }

    /// <summary>某日期范围流水(含起止、非取消),按日期倒序、同日按录入倒序。</summary>
    public static IReadOnlyList<TxnListItem> ListByRange(LedgerSession s, string startDate, string endDate)
    {
        var list = new List<TxnListItem>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.direction, t.amount_cents, t.name,
       a.name, c.name, substr(t.created_at, 12, 5),
       b.name, t.delta_cents, t.transfer_kind, t.date
FROM transactions t
LEFT JOIN accounts a   ON a.id  = t.account_id
LEFT JOIN accounts b   ON b.id  = t.to_account_id
LEFT JOIN categories c ON c.id  = t.category_id
WHERE t.date BETWEEN $start AND $end AND t.status <> 'cancelled'
ORDER BY t.date DESC, t.id DESC;";
        cmd.Parameters.AddWithValue("$start", startDate);
        cmd.Parameters.AddWithValue("$end", endDate);

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
                Time = r.IsDBNull(6) ? string.Empty : r.GetString(6),
                AccountTo = r.IsDBNull(7) ? string.Empty : r.GetString(7),
                DeltaCents = r.IsDBNull(8) ? 0 : r.GetInt64(8),
                Kind = r.IsDBNull(9) ? string.Empty : r.GetString(9),
                Date = r.IsDBNull(10) ? string.Empty : r.GetString(10)
            });
        }
        return list;
    }

    /// <summary>某日期范围支出/收入合计(不含转账、取消)。</summary>
    public static (long OutCents, long InCents) RangeTotals(LedgerSession s, string startDate, string endDate)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT
  COALESCE(SUM(CASE WHEN direction = 'out' THEN amount_cents ELSE 0 END), 0),
  COALESCE(SUM(CASE WHEN direction = 'in'  THEN amount_cents ELSE 0 END), 0)
FROM transactions
WHERE date BETWEEN $start AND $end AND direction <> 'transfer' AND status <> 'cancelled';";
        cmd.Parameters.AddWithValue("$start", startDate);
        cmd.Parameters.AddWithValue("$end", endDate);

        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt64(0), r.GetInt64(1));
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

    /// <summary>取一笔的日期(写保护/回填用);不存在返回 null。</summary>
    private static string? RowDate(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT date FROM transactions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() as string;
    }
}
