using System;
using System.Collections.Generic;
using ScottPlot;

namespace ZhangDan.App.Reporting;

/// <summary>
/// 图表渲染(ScottPlot 5 → PNG 字节,纯头less,供 PDF 嵌入)。任一出错返回 null,由调用方降级为文字。
/// </summary>
internal static class ReportCharts
{
    /// <summary>
    /// 跨周期堆叠面积:每一列 = 一个周期(x=按时间序);类别自下而上堆叠;
    /// 相邻两条上边界之间为淡色半透明带(categories.color vivid 线省略,见阅读说明)。
    /// percent=true 时每列按自身总额归一为 %。返回 PNG 字节,失败 null。
    /// </summary>
    public static byte[]? StackedArea(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<(string Name, string Hex)> categories,
        double[,] cents,        // cents[category, column]
        bool percent)
    {
        try
        {
            int n = columnNames.Count;
            int m = categories.Count;
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
                var fill = plot.Add.FillY(xs, bottom, top);
                fill.FillColor = HexColor(categories[i].Hex).WithAlpha(70);
                bottom = top;
            }

            plot.Axes.AutoScale();
            plot.YLabel(percent ? "占比 %" : "金额(元)");
            var image = plot.GetImageBytes(920, 430);
            return image;
        }
        catch
        {
            return null;
        }
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
