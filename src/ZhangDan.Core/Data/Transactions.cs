using System;
using System.Collections.Generic;
using System.Text;
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
        if (t.Direction == "out")
            ThrowIfOverdraft(s, t.AccountId, t.AmountCents);   // 非负债账户余额不可为负
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
        ThrowIfOverdraft(s, t.FromAccountId, t.PrincipalCents);   // 非负债账户不可透支转出
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

    /// <summary>某账户在某日期范围内的流水(含收/支/转账;转账双向参与),按日期倒序。</summary>
    public static IReadOnlyList<TxnListItem> ListByAccountRange(
        LedgerSession s, long accountId, string startDate, string endDate)
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
WHERE t.status <> 'cancelled'
  AND (t.account_id = $acct OR t.to_account_id = $acct)
  AND t.date BETWEEN $start AND $end
ORDER BY t.date DESC, t.id DESC;";
        cmd.Parameters.AddWithValue("$acct", accountId);
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

    /// <summary>某日期范围内逐日支出/收入合计(键 yyyy-MM-dd;不含转账、取消)。月历热力用,一次查询免 N+1。</summary>
    public static IReadOnlyDictionary<string, (long OutCents, long InCents)> DayTotalsMap(
        LedgerSession s, string startDate, string endDate)
    {
        var map = new Dictionary<string, (long, long)>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT date,
  COALESCE(SUM(CASE WHEN direction = 'out' THEN amount_cents ELSE 0 END), 0),
  COALESCE(SUM(CASE WHEN direction = 'in'  THEN amount_cents ELSE 0 END), 0)
FROM transactions
WHERE date BETWEEN $start AND $end AND direction <> 'transfer' AND status <> 'cancelled'
GROUP BY date;";
        cmd.Parameters.AddWithValue("$start", startDate);
        cmd.Parameters.AddWithValue("$end", endDate);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[r.GetString(0)] = (r.GetInt64(1), r.GetInt64(2));
        return map;
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

    /// <summary>CSV 全量导出行(含作废/退款,供外部分析/归档)。</summary>
    internal sealed record TxnExportRow(
        long Id,
        string Date,
        string Time,
        string Direction,      // in | out | transfer
        string Name,
        string Category,
        string Account,        // 转账=转出账户
        string AccountTo,
        long AmountCents,      // 转账=本金
        long DeltaCents,
        string Kind,           // 转账类别(收支为空)
        string Channel,
        string Note,
        bool InPool,
        string Status,         // normal | refunded | cancelled
        string Period,         // 周期名(未归属为空)
        string CreatedAt);

    /// <summary>全量流水(含已作废/退款,转账同表),按日期、录入升序。</summary>
    public static IReadOnlyList<TxnExportRow> ExportAll(LedgerSession s)
    {
        var list = new List<TxnExportRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.date, substr(t.created_at, 12, 5), t.direction,
       t.name, c.name, a.name, b.name, t.amount_cents,
       COALESCE(t.delta_cents, 0), COALESCE(t.transfer_kind, ''),
       t.channel, t.note, t.in_pool, t.status, p.name, t.created_at
FROM transactions t
LEFT JOIN categories c ON c.id = t.category_id
LEFT JOIN accounts a  ON a.id  = t.account_id
LEFT JOIN accounts b  ON b.id  = t.to_account_id
LEFT JOIN periods   p ON p.id  = t.period_id
ORDER BY t.date, t.id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TxnExportRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? string.Empty : r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.IsDBNull(5) ? string.Empty : r.GetString(5),
                r.IsDBNull(6) ? string.Empty : r.GetString(6),
                r.IsDBNull(7) ? string.Empty : r.GetString(7),
                r.GetInt64(8),
                r.GetInt64(9),
                r.GetString(10),
                r.IsDBNull(11) ? string.Empty : r.GetString(11),
                r.IsDBNull(12) ? string.Empty : r.GetString(12),
                r.GetInt32(13) != 0,
                r.GetString(14),
                r.IsDBNull(15) ? string.Empty : r.GetString(15),
                r.IsDBNull(16) ? string.Empty : r.GetString(16)));
        }
        return list;
    }

    /// <summary>流水页筛选条件。</summary>
    internal sealed class FlowFilter
    {
        public long? PeriodId { get; init; }       // 只看某周期(按其归属)
        public bool UnassignedOnly { get; init; }  // 只看未归属(period_id 为空)
        public string? Direction { get; init; }    // in | out | transfer | null=全部
        public long? AccountId { get; init; }
        public long? CategoryId { get; init; }
        public bool ShowCancelled { get; init; }   // false 时隐藏已作废
        public string? Keyword { get; init; }      // 名称/备注/账户名/分类名 模糊
    }

    /// <summary>流水页展示行:带状态/来源/归属周期名/分类颜色/入池。</summary>
    internal sealed class FlowListItem
    {
        public long Id { get; init; }
        public required string Direction { get; init; }   // in | out | transfer
        public long AmountCents { get; init; }
        public required string Name { get; init; }
        public required string Account { get; init; }     // 转账=转出账户
        public required string AccountTo { get; init; }
        public required string Category { get; init; }
        public string CategoryColor { get; init; } = "";
        public string Kind { get; init; } = "";
        public long DeltaCents { get; init; }
        public required string Date { get; init; }        // yyyy-MM-dd
        public required string Time { get; init; }
        public required string Status { get; init; }      // normal | refunded | cancelled
        public required string Source { get; init; }      // manual | calibration
        public string PeriodName { get; init; } = "";
        public bool InPool { get; init; }
    }

    /// <summary>流水页通用筛选:周期/未归属/方向/账户/分类/含作废/关键词,按日期倒序。</summary>
    public static IReadOnlyList<FlowListItem> ListFlows(LedgerSession s, FlowFilter f)
    {
        var sql = new StringBuilder(@"
SELECT t.id, t.direction, t.amount_cents, t.name,
       COALESCE(a.name, ''), COALESCE(c.name, ''),
       substr(t.created_at, 12, 5),
       COALESCE(b.name, ''),
       COALESCE(t.delta_cents, 0), COALESCE(t.transfer_kind, ''),
       t.date, t.status, t.source, t.in_pool, COALESCE(p.name, ''), COALESCE(c.color, '')
FROM transactions t
LEFT JOIN accounts a   ON a.id  = t.account_id
LEFT JOIN accounts b   ON b.id  = t.to_account_id
LEFT JOIN categories c ON c.id  = t.category_id
LEFT JOIN periods   p ON p.id  = t.period_id
WHERE 1 = 1");

        if (!f.ShowCancelled)
            sql.Append(" AND t.status <> 'cancelled'");
        if (f.PeriodId is long pid)
            sql.Append(" AND t.period_id = $pid");
        else if (f.UnassignedOnly)
            sql.Append(" AND t.period_id IS NULL");
        if (f.Direction is string dir && dir.Length > 0)
        {
            sql.Append(" AND t.direction = $dir");
        }
        if (f.AccountId is long acct)
            sql.Append(" AND (t.account_id = $acct OR t.to_account_id = $acct)");
        if (f.CategoryId is long cat)
            sql.Append(" AND t.category_id = $cat");
        if (!string.IsNullOrWhiteSpace(f.Keyword))
            sql.Append(" AND (t.name LIKE $kw OR t.note LIKE $kw OR a.name LIKE $kw OR c.name LIKE $kw)");
        sql.Append(" ORDER BY t.date DESC, t.id DESC;");

        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = sql.ToString();
        if (f.PeriodId is long pid2)
            cmd.Parameters.AddWithValue("$pid", pid2);
        if (f.Direction is string dir2 && dir2.Length > 0)
            cmd.Parameters.AddWithValue("$dir", dir2);
        if (f.AccountId is long acct2)
            cmd.Parameters.AddWithValue("$acct", acct2);
        if (f.CategoryId is long cat2)
            cmd.Parameters.AddWithValue("$cat", cat2);
        if (!string.IsNullOrWhiteSpace(f.Keyword))
            cmd.Parameters.AddWithValue("$kw", "%" + f.Keyword.Trim() + "%");

        var list = new List<FlowListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new FlowListItem
            {
                Id = r.GetInt64(0),
                Direction = r.GetString(1),
                AmountCents = r.GetInt64(2),
                Name = r.GetString(3),
                Account = r.GetString(4),
                Category = r.GetString(5),
                Time = r.GetString(6),
                AccountTo = r.GetString(7),
                DeltaCents = r.GetInt64(8),
                Kind = r.GetString(9),
                Date = r.GetString(10),
                Status = r.GetString(11),
                Source = r.GetString(12),
                InPool = r.GetInt32(13) != 0,
                PeriodName = r.GetString(14),
                CategoryColor = r.GetString(15)
            });
        }
        return list;
    }

    /// <summary>非负债账户不允许余额为负(零钱/银行/现金…);负债型(信用卡等)以后放开。</summary>
    private static void ThrowIfOverdraft(LedgerSession s, long accountId, long spendCents)
    {
        if (IsLiabilityType(s, accountId))
            return;
        var book = AccountCalibration.BookCents(s, accountId);
        if (book - spendCents < 0)
        {
            throw new InvalidOperationException(
                $"余额不足:该账户当前余额 {Money.Yuan(book)},不足以支出 {Money.Yuan(spendCents)}。");
        }
    }

    private static bool IsLiabilityType(LedgerSession s, long accountId)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT type FROM accounts WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", accountId);
        var type = cmd.ExecuteScalar() as string;
        return type is "credit_card";   // 负债型账户才允许负余额(类型暂未开放)
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
