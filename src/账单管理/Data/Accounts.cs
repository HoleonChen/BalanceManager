using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>账户行(与 accounts 表列对应)。</summary>
internal sealed record AccountRow(
    long Id,
    string Name,
    string Platform,
    string Type,
    bool Enabled,
    long BalanceBaseCents);

/// <summary>账户查询(记账下拉、账户视图共用)。</summary>
internal static class Accounts
{
    /// <summary>新建账户(账户表不预置,由用户/导入创建);sort_order 取末位+1。</summary>
    public static long Insert(LedgerSession s, string name, string type, string platform, long balanceBaseCents)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO accounts (name, platform, type, enabled, balance_base_cents, balance_date, sort_order)
VALUES ($name, $platform, $type, 1, $base, $bd,
        COALESCE((SELECT MAX(sort_order) FROM accounts) + 1, 0));";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$platform", platform);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$base", balanceBaseCents);
        cmd.Parameters.AddWithValue("$bd", balanceBaseCents == 0 ? DBNull.Value : DateTime.Now.ToString("yyyy-MM-dd"));
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid();";
        return System.Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>列出启用账户,按 sort_order 排。</summary>
    public static IReadOnlyList<AccountRow> ListEnabled(LedgerSession s)
    {
        var list = new List<AccountRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, platform, type, enabled, balance_base_cents
FROM accounts
WHERE enabled = 1
ORDER BY sort_order, id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AccountRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? string.Empty : r.GetString(2),
                r.GetString(3),
                r.GetInt32(4) != 0,
                r.GetInt64(5)));
        }
        return list;
    }

    /// <summary>列出全部账户(含已停用),按 sort_order 排。</summary>
    public static IReadOnlyList<AccountRow> ListAll(LedgerSession s)
    {
        var list = new List<AccountRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, platform, type, enabled, balance_base_cents
FROM accounts
ORDER BY sort_order, id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AccountRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? string.Empty : r.GetString(2),
                r.GetString(3),
                r.GetInt32(4) != 0,
                r.GetInt64(5)));
        }
        return list;
    }

    /// <summary>停用账户(不再出现在记账/转账下拉;有流水约束,不物理删除)。</summary>
    public static void Disable(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET enabled = 0 WHERE id = $id AND enabled = 1;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>重新启用账户。</summary>
    public static void Enable(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET enabled = 1 WHERE id = $id AND enabled = 0;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
