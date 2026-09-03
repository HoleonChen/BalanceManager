using System;
using System.IO;
using ZhangDan;

namespace ZhangDan.App.Reporting;

/// <summary>报表编排:取数组合 → 写 PDF/xlsx → 记历史。同步执行(会话连接非线程安全),量大时由调用方给等待光标。</summary>
internal static class ReportExporter
{
    public static (string? PdfPath, string? XlsxPath) Generate(LedgerSession s, ReportRequest req)
    {
        var dir = string.IsNullOrWhiteSpace(req.SaveDir) ? AppPaths.ReportDir : req.SaveDir!;
        Directory.CreateDirectory(dir);

        var baseName = Sanitize(req.BaseName ?? "报表");
        var content = ReportComposer.Compose(s, req);

        string? pdf = null, xlsx = null;
        if (req.ToPdf)
        {
            pdf = Path.Combine(dir, baseName + ".pdf");
            ReportPdf.EnsureLicense();
            ReportPdf.Generate(pdf, Title(s, req), content);
        }
        if (req.ToXlsx)
        {
            xlsx = Path.Combine(dir, baseName + ".xlsx");
            ReportXlsx.Generate(xlsx, content);
        }

        ReportStore.Append(s, new ReportHistoryEntry
        {
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Request = req,
            PdfPath = pdf,
            XlsxPath = xlsx
        });
        return (pdf, xlsx);
    }

    private static string Title(LedgerSession s, ReportRequest req)
    {
        var scope = string.IsNullOrWhiteSpace(req.ScopeLabel) ? req.Kind.ToString() : req.ScopeLabel;
        return $"{s.Name} · {scope}";
    }

    private static string Sanitize(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return name;
    }
}
