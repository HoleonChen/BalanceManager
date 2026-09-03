using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>资金池预留项(reserve_items;报表「预留到期提醒」用)。</summary>
internal sealed record ReserveItem(string? Due, string Item, long AmountCents);

/// <summary>资金池设置行(fund_pools;单池 = 每周期至多一条)。</summary>
internal sealed record PoolRow(
    long Id,
    long PeriodId,
    string Name,
    long AccountId,
    long BudgetCents,
    long ReserveCents);

/// <summary>
/// 资金池派生结果(由流水实时算出,不落库):
/// 已花 = 池账户本周期 in_pool 的直支出 + 勾「计入池」的转出本金;作废/退款不算。
/// 剩余 = 预算 − 已花;可自由支配 = 剩余 − 预计保留。
/// 收入与转入一律不进池(进池只此一次,设计 §3.6)。
/// </summary>
internal sealed class PoolState
{
    public required long BudgetCents { get; init; }
    public required long ReserveCents { get; init; }
    public required long SpentCents { get; init; }

    public long RemainingCents => BudgetCents - SpentCents;

    /// <summary>可自由支配 = 剩余 − 预计保留。</summary>
    public long DisposableCents => RemainingCents - ReserveCents;
}

/// <summary>资金池写读与派生(设计 §3.6/§4)。</summary>
internal static class Pools
{
    /// <summary>取某周期的池;无返回 null。</summary>
    public static PoolRow? Get(LedgerSession s, long periodId)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, period_id, name, account_id, budget_cents, reserve_cents
FROM fund_pools
WHERE period_id = $pid
ORDER BY id DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$pid", periodId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return ReadRow(r);
    }

    /// <summary>建/改某周期的池(单池 upsert,唯一键 = period_id)。</summary>
    public static void Save(LedgerSession s, long periodId, string name, long accountId,
        long budgetCents, long reserveCents)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO fund_pools (period_id, name, account_id, budget_cents, reserve_cents, created_at)
VALUES ($pid, $name, $acct, $budget, $reserve, $created)
ON CONFLICT(period_id) DO UPDATE SET
  name = $name, account_id = $acct,
  budget_cents = $budget, reserve_cents = $reserve;";
        cmd.Parameters.AddWithValue("$pid", periodId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$acct", accountId);
        cmd.Parameters.AddWithValue("$budget", budgetCents);
        cmd.Parameters.AddWithValue("$reserve", reserveCents);
        cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>计算某池当前已花(分)。已花 = 池账户本期 in_pool 直支出合计 + 勾「计入池」的转出本金。</summary>
    public static long SpentCents(LedgerSession s, PoolRow pool)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT COALESCE(SUM(m), 0) FROM (
  SELECT amount_cents AS m FROM transactions
   WHERE period_id = $pid AND account_id = $acct
     AND direction = 'out' AND in_pool = 1 AND status = 'normal'
  UNION ALL
  SELECT COALESCE(principal_cents, 0) FROM transactions
   WHERE period_id = $pid AND account_id = $acct
     AND direction = 'transfer' AND in_pool = 1 AND status = 'normal'
);";
        cmd.Parameters.AddWithValue("$pid", pool.PeriodId);
        cmd.Parameters.AddWithValue("$acct", pool.AccountId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>汇总一个池的当前派生状态。</summary>
    public static PoolState State(LedgerSession s, PoolRow pool)
        => new()
        {
            BudgetCents = pool.BudgetCents,
            ReserveCents = pool.ReserveCents,
            SpentCents = SpentCents(s, pool)
        };

    /// <summary>某账户在池对应周期内的入账合计(生活费/工资等;仅供池预算默认建议,不算已花)。</summary>
    public static long IncomeInto(LedgerSession s, long periodId, long accountId)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT COALESCE(SUM(m), 0) FROM (
  SELECT amount_cents AS m FROM transactions
   WHERE period_id = $pid AND account_id = $acct
     AND direction = 'in' AND status = 'normal'
  UNION ALL
  SELECT amount_cents + COALESCE(delta_cents, 0) FROM transactions
   WHERE period_id = $pid AND to_account_id = $acct
     AND direction = 'transfer' AND status = 'normal'
);";
        cmd.Parameters.AddWithValue("$pid", periodId);
        cmd.Parameters.AddWithValue("$acct", accountId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>某池的预留项清单(按到期日排序;报表「预留到期提醒」用)。</summary>
    public static IReadOnlyList<ReserveItem> ReserveItems(LedgerSession s, long poolId)
    {
        var list = new List<ReserveItem>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT due, item, amount_cents FROM reserve_items WHERE pool_id = $pid ORDER BY due, id;";
        cmd.Parameters.AddWithValue("$pid", poolId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ReserveItem(r.IsDBNull(0) ? null : r.GetString(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    private static PoolRow ReadRow(SqliteDataReader r)
        => new(
            r.GetInt64(0),
            r.GetInt64(1),
            r.GetString(2),
            r.GetInt64(3),
            r.GetInt64(4),
            r.GetInt64(5));
}
