using System;
using System.Linq;
using ClosedXML.Excel;

namespace ZhangDan.App.Reporting;

/// <summary>把组合好的报表内容写成 .xlsx(每块一个工作表)。</summary>
internal static class ReportXlsx
{
    public static void Generate(string path, ReportContent content)
    {
        using var wb = new XLWorkbook();
        foreach (var sheet in content.Sheets)
        {
            var ws = wb.AddWorksheet(SheetName(sheet.Title));
            int r = 1;
            int c = 1;
            foreach (var h in sheet.Headers)
            {
                var cell = ws.Cell(r, c);
                cell.Value = h;
                cell.Style.Font.Bold = true;
                c++;
            }
            r++;
            foreach (var row in sheet.Rows)
            {
                c = 1;
                foreach (var v in row)
                    ws.Cell(r, c++).Value = v ?? "";
                r++;
            }
            ws.Columns().AdjustToContents();
        }
        wb.SaveAs(path);
    }

    private static string SheetName(string title)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var name = new string(title.Where(ch => !invalid.Contains(ch)).ToArray());
        if (name.Length == 0) name = "Sheet";
        return name.Length > 31 ? name[..31] : name;
    }
}
