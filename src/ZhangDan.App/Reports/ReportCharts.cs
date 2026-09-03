using System;
using System.Collections.Generic;
using ScottPlot;

namespace ZhangDan.App.Reporting;

/// <summary>
/// 图表渲染(ScottPlot 5 → PNG 字节,headless,供 PDF 嵌入)。任一出错返回 null,由调用方降级为文字。
/// 中文:Windows 上给各文字元素设 FontName=Microsoft YaHei(或 Font.Automatic 自动挑选)。
/// </summary>
internal static class ReportCharts
{
    /// <summary>
    /// 跨周期堆叠面积图:每列=一个周期(横轴 P0…Pn-1),类别自下而上堆叠为淡色半透明带、带顶勾鲜艳折线。
    /// <b>图内不含中文</b>——周期名/图例语义由 PDF 在图下方以 QuestPDF 中文文字+色块表给出(ScottPlot 中文字体渲染不可靠)。
    /// </summary>
    public static byte[]? StackedArea(int columns, IReadOnlyList<string> hexes, double[,] cents, bool percent)
    {
        try
        {
            int n = columns;
            int m = hexes.Count;
            if (n == 0 || m == 0)
                return null;
            var plot = new Plot();

            double[] totals = new double[n];
            for (int j = 0; j < n; j++)
            {
                double s = 0;
                for (int i = 0; i < m; i++) s += cents[i, j];
                totals[j] = s;
            }

            double[] bottom = new double[n];
            for (int i = 0; i < m; i++)
            {
                var top = new double[n];
                for (int j = 0; j < n; j++)
                {
                    double v = cents[i, j];
                    if (percent && totals[j] > 0)
                        v = v / totals[j] * 100.0;
                    top[j] = bottom[j] + v;
                }
                double[] xs = ScottPlot.Generate.Consecutive(n);
                var vivid = HexColor(hexes[i]);
                var fill = plot.Add.FillY(xs, bottom, top);
                fill.FillColor = vivid.WithAlpha(60);
                // 带顶 = 本类上边界,鲜艳折线读趋势
                var line = plot.Add.ScatterLine(xs, top, vivid);
                line.LineWidth = 1.5f;
                bottom = top;
            }

            // 横轴仅 ASCII 占位(P0…),语义映射由 PDF 文字说明
            var labels = new string[n];
            for (int i = 0; i < n; i++) labels[i] = "P" + i;
            plot.Axes.Bottom.SetTicks(xsOf(n), labels);
            plot.Axes.AutoScale();
            return plot.GetImageBytes(960, 460);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>支出分类占比饼图(每类一块,颜色取分类色;未归类灰块)。返回 PNG,失败 null。</summary>
    public static byte[]? Pie(IReadOnlyList<(string Name, string Hex, double Cents)> slices)
    {
        try
        {
            if (slices.Count == 0)
                return null;
            var plot = new Plot();
            var pieSlices = new List<PieSlice>();
            foreach (var s in slices)
            {
                pieSlices.Add(new PieSlice
                {
                    Value = s.Cents,
                    FillColor = HexColor(s.Hex),
                    LegendText = s.Name
                });
            }
            var pie = plot.Add.Pie(pieSlices);
            pie.ExplodeFraction = 0.02;
            plot.ShowLegend();
            plot.Axes.Frameless();
            plot.HideGrid();
            StyleChinese(plot);
            return plot.GetImageBytes(520, 380);
        }
        catch
        {
            return null;
        }
    }

    private static string? _fontName;

    /// <summary>
    /// 中文字体名固定用系统族名「Microsoft YaHei」,并把 Windows 的 msyh 字体文件注册到该名字下,
    /// 让「按系统名解析」与「按注册名解析」两条路径都命中同一份含中文字形的字体。失败时仍按名试。
    /// </summary>
    private static string ChineseFont()
    {
        if (_fontName is not null)
            return _fontName;
        const string systemName = "Microsoft YaHei";
        string[] candidates =
        {
            @"C:\Windows\Fonts\msyh.ttc",
            @"C:\Windows\Fonts\msyh.ttf",
            @"C:\Windows\Fonts\msyhbd.ttc",
            @"C:\Windows\Fonts\simhei.ttf"
        };
        foreach (var path in candidates)
        {
            if (!System.IO.File.Exists(path))
                continue;
            try
            {
                Fonts.AddFontFile(systemName, path);
                _fontName = systemName;
                return systemName;
            }
            catch { /* 试下一个 */ }
        }
        _fontName = systemName;
        return systemName;
    }

    private static void StyleChinese(Plot plot)
    {
        var font = ChineseFont();
        try
        {
            plot.Axes.Left.TickLabelStyle.FontName = font;
            plot.Axes.Bottom.TickLabelStyle.FontName = font;
            plot.Axes.Left.Label.FontName = font;
            plot.Axes.Bottom.Label.FontName = font;
            plot.Legend.FontName = font;
        }
        catch { /* 低版本字段差异忽略 */ }
    }

    private static double[] xsOf(int n)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = i;
        return a;
    }

    private static string[] ToArray(IReadOnlyList<string> xs)
    {
        var a = new string[xs.Count];
        for (int i = 0; i < xs.Count; i++) a[i] = xs[i];
        return a;
    }

    private static Color HexColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new Color(r, g, b);
            }
        }
        catch { /* 走灰底 */ }
        return new Color(176, 190, 197);
    }
}
