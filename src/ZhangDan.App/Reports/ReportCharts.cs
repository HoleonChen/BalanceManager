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
    /// 跨周期堆叠面积:每列=一个周期(x 轴按时间序),类别自下而上堆叠为淡色半透明带,带顶勾鲜艳折线;
    /// 底轴 tick 打周期名,x 轴标题「周期(按时间序)」;图例=各分类色块。percent 时各列按自身总额归一 %。
    /// </summary>
    public static byte[]? StackedArea(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<(string Name, string Hex)> categories,
        double[,] cents,
        bool percent)
    {
        try
        {
            int n = columnNames.Count;
            int m = categories.Count;
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
                var vivid = HexColor(categories[i].Hex);
                var fill = plot.Add.FillY(xs, bottom, top);
                fill.FillColor = vivid.WithAlpha(60);
                // 带顶 = 本类上边界,鲜艳折线读趋势
                var line = plot.Add.ScatterLine(xs, top, vivid);
                line.LineWidth = 1.5f;
                line.LegendText = categories[i].Name;
                bottom = top;
            }

            plot.ShowLegend();
            StyleChinese(plot);

            plot.Axes.Bottom.SetTicks(xsOf(n), ToArray(columnNames));   // x=按时间序的周期
            plot.Axes.Bottom.TickLabelStyle.Rotation = 30;
            plot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleRight;
            plot.Axes.Bottom.MinimumSize = 30;
            plot.Axes.Bottom.Label.Text = "周期(按时间序)";
            plot.Axes.Left.Label.Text = percent ? "占比 %" : "金额(元)";
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

    /// <summary>注册一个含中文的字体(Windows 雅黑/黑体),返回可在 FontName 里用的名字;失败回退字符串。</summary>
    private static string ChineseFont()
    {
        if (_fontName is not null)
            return _fontName;
        string[] candidates =
        {
            @"C:\Windows\Fonts\msyh.ttc",   // 微软雅黑(常规)
            @"C:\Windows\Fonts\msyh.ttf",
            @"C:\Windows\Fonts\msyhbd.ttc", // 微软雅黑(粗)
            @"C:\Windows\Fonts\simhei.ttf"  // 黑体
        };
        foreach (var path in candidates)
        {
            if (!System.IO.File.Exists(path))
                continue;
            try
            {
                string name = "ZhangDanCjk" + path.GetHashCode();
                Fonts.AddFontFile(name, path);
                _fontName = name;
                return name;
            }
            catch { /* 试下一个 */ }
        }
        _fontName = "Microsoft YaHei";   // 注册不了就按名试(可能仍无字形)
        return _fontName;
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
