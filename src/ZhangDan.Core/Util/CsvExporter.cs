using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ZhangDan;

/// <summary>全量流水 CSV 导出:单文件含全部流水(含已作废/退款),UTF-8 BOM 供 Excel 直开。</summary>
internal static class CsvExporter
{
    private static readonly string[] Header =
    {
        "ID", "日期", "时间", "方向", "名称", "分类", "账户", "转入账户",
        "金额(元)", "浮动(元)", "转账类别", "渠道", "备注", "计入池", "状态", "周期", "创建时间"
    };

    /// <summary>行 → 文本。金额带方向符号:收入 +、支出 −、转账 −本金(账户列为转出方)。</summary>
    public static string Build(IReadOnlyList<Transactions.TxnExportRow> rows)
    {
        var sb = new StringBuilder();
        AppendLine(sb, Header);
        foreach (var r in rows)
        {
            var cells = new[]
            {
                r.Id.ToString(),
                r.Date,
                r.Time,
                DirectionLabel(r.Direction),
                r.Name,
                r.Category,
                r.Account,
                r.AccountTo,
                SignedYuan(SignedAmountCents(r)),
                r.Direction == "transfer" ? SignedYuan(r.DeltaCents) : "",
                r.Kind,
                r.Channel,
                r.Note,
                r.InPool ? "是" : "否",
                StatusLabel(r.Status),
                r.Period,
                r.CreatedAt
            };
            AppendLine(sb, cells);
        }
        return sb.ToString();
    }

    public static void Save(string path, string content)
    {
        // UTF-8 BOM:Excel 双击直接识别中文,不出现乱码
        using var w = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        w.Write(content);
    }

    /// <summary>金额符号按记账方向推导:转账行 = 从「账户」转出本金(负)。</summary>
    private static long SignedAmountCents(Transactions.TxnExportRow r) => r.Direction switch
    {
        "in" => r.AmountCents,
        "out" => -r.AmountCents,
        "transfer" => -r.AmountCents,
        _ => r.AmountCents
    };

    private static string DirectionLabel(string d) => d switch
    {
        "in" => "收入",
        "out" => "支出",
        "transfer" => "转账",
        _ => d
    };

    private static string StatusLabel(string s) => s switch
    {
        "normal" => "正常",
        "refunded" => "已退款",
        "cancelled" => "已作废",
        _ => s
    };

    private static string SignedYuan(long cents)
        => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static void AppendLine(StringBuilder sb, IEnumerable<string> cells)
    {
        bool first = true;
        foreach (var c in cells)
        {
            if (!first)
                sb.Append(',');
            first = false;
            sb.Append(Escape(c));
        }
        sb.AppendLine();
    }

    private static string Escape(string cell)
    {
        if (cell.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return cell;
        return "\"" + cell.Replace("\"", "\"\"") + "\"";
    }
}
