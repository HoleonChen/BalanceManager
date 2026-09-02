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
}
