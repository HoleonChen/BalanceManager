using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>分类行(记账下拉 / 分类管理共用)。</summary>
internal sealed record CategoryRow(long Id, string Name, string? Color, long SortOrder);

/// <summary>
/// 分类写读与管理(设计 §3.3 / §9):顶层大类按 kind(income/expense)区分,
/// 子类(seed 仅「差额调整」挂其他下)不参与记账下拉,也不在分类管理列表出现。
/// </summary>
internal static class Categories
{
    public const string KindExpense = "expense";
    public const string KindIncome = "income";

    /// <summary>取记账可用的顶层分类(排除子类,如「差额调整」);income 为 true 取收入类。</summary>
    public static IReadOnlyList<CategoryRow> ListManual(LedgerSession s, bool income)
    {
        var result = new List<CategoryRow>();
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, color, sort_order
FROM categories
WHERE parent_id IS NULL AND kind = $kind
ORDER BY sort_order, id;";
        cmd.Parameters.AddWithValue("$kind", income ? KindIncome : KindExpense);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new CategoryRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt64(3)));
        }
        return result;
    }

    /// <summary>新建顶层分类(排在本类末尾);kind 由 income 决定;color/keyword 可空。</summary>
    public static long Insert(LedgerSession s, string name, bool income, string? color, string? keyword)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO categories (parent_id, name, keyword, color, sort_order, kind)
VALUES (NULL, $name, $keyword, $color,
        COALESCE((SELECT MAX(sort_order) FROM categories
                  WHERE parent_id IS NULL AND kind = $kind), -1) + 1,
        $kind);";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$keyword", (object?)keyword ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$color", (object?)color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", income ? KindIncome : KindExpense);
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static void Rename(LedgerSession s, long id, string newName)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE categories SET name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", newName);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void SetColor(LedgerSession s, long id, string? color)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE categories SET color = $color WHERE id = $id;";
        cmd.Parameters.AddWithValue("$color", (object?)color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void SetKeyword(LedgerSession s, long id, string? keyword)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE categories SET keyword = $kw WHERE id = $id;";
        cmd.Parameters.AddWithValue("$kw", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword.Trim());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>某分类被引用流水数(含已作废;作废也保留分类归属)。</summary>
    public static long UsedCount(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM transactions WHERE category_id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>某分类下子分类数(seed 的「差额调整」挂在其他下)。</summary>
    public static long ChildCount(LedgerSession s, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM categories WHERE parent_id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>同 kind 顶层分类内上移/下移一位(交换 sort_order)。</summary>
    public static void Move(LedgerSession s, long id, bool up)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT kind FROM categories WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        var kind = cmd.ExecuteScalar() as string;
        if (kind is null)
            return;

        // 同级(同 kind 顶层)按 sort_order,id 排
        var rows = new List<(long Id, long Order)>();
        cmd.CommandText = @"
SELECT id, sort_order FROM categories
WHERE parent_id IS NULL AND kind = $kind
ORDER BY sort_order, id;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$kind", kind);
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                rows.Add((r.GetInt64(0), r.GetInt64(1)));
        }

        int i = rows.FindIndex(x => x.Id == id);
        int j = up ? i - 1 : i + 1;
        if (i < 0 || j < 0 || j >= rows.Count)
            return;

        SwapOrders(s, rows[i].Id, rows[i].Order, rows[j].Id, rows[j].Order);
    }

    private static void SwapOrders(LedgerSession s, long aId, long aOrder, long bId, long bOrder)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "UPDATE categories SET sort_order = $o WHERE id = $id;";
        cmd.Parameters.AddWithValue("$o", bOrder);
        cmd.Parameters.AddWithValue("$id", aId);
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$o", aOrder);
        cmd.Parameters.AddWithValue("$id", bId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>把 source 并入 target:流水改挂 target,关键词并入(颜色以 target 为准,即 B 色丢弃),source 删除。</summary>
    public static void Merge(LedgerSession s, long sourceId, long targetId)
    {
        if (sourceId == targetId)
            return;
        if (ChildCount(s, sourceId) > 0)
            throw new InvalidOperationException("该分类下还有子分类,不能合并(子分类需先处理)。");

        using var cmd = s.Connection.CreateCommand();

        // 流水改挂
        cmd.CommandText = "UPDATE transactions SET category_id = $target WHERE category_id = $source;";
        cmd.Parameters.AddWithValue("$target", targetId);
        cmd.Parameters.AddWithValue("$source", sourceId);
        cmd.ExecuteNonQuery();

        // 关键词并入(去重)
        cmd.CommandText = "SELECT keyword FROM categories WHERE id IN ($a, $b) AND keyword IS NOT NULL;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$a", sourceId);
        cmd.Parameters.AddWithValue("$b", targetId);
        var seen = new HashSet<string>();
        var merged = new List<string>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                foreach (var token in (r.GetString(0) ?? "").Split(new[] { ' ', '，', ',' },
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    if (seen.Add(token))
                        merged.Add(token);
                }
            }
        }
        cmd.CommandText = "UPDATE categories SET keyword = $kw WHERE id = $id;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$kw", merged.Count > 0 ? string.Join(" ", merged) : DBNull.Value);
        cmd.Parameters.AddWithValue("$id", targetId);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DELETE FROM categories WHERE id = $id;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$id", sourceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除分类(仅当无流水引用且无子分类;否则抛错,提示先合并)。</summary>
    public static void Delete(LedgerSession s, long id)
    {
        var used = UsedCount(s, id);
        var children = ChildCount(s, id);
        if (used > 0 || children > 0)
        {
            throw new InvalidOperationException(
                $"该分类仍被 {used} 笔流水使用{(children > 0 ? $",且下有 {children} 个子分类" : "")},不能直接删除。\n请先「合并」到其他分类(如「其他」)再删。");
        }
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM categories WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
