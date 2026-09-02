using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>分类行。</summary>
internal sealed record CategoryRow(long Id, string Name, string? Color, long SortOrder);

/// <summary>分类查询。</summary>
internal static class Categories
{
    // 骨架期:收入分类是种子固定 id 区间(10–14),支出大类 1–8。
    // 分类管理上线后改为 categories.kind 显式字段再迁移,此处先注释标注。
    private const long IncomeMin = 10;
    private const long IncomeMax = 14;

    public static bool IsIncome(long id) => id is >= IncomeMin and <= IncomeMax;

    /// <summary>取记账可用的顶层分类(排除子类,如「差额调整」)。</summary>
    public static IReadOnlyList<CategoryRow> ListManual(LedgerSession s, bool income)
    {
        var all = new List<CategoryRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, color, sort_order
FROM categories
WHERE parent_id IS NULL
ORDER BY sort_order, id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            all.Add(new CategoryRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt64(3)));
        }

        var result = new List<CategoryRow>();
        foreach (var c in all)
        {
            if (IsIncome(c.Id) == income)
                result.Add(c);
        }
        return result;
    }
}
