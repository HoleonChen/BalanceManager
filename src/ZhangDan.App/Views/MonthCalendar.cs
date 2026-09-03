using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZhangDan.App.Views;

/// <summary>
/// 自然月月历(周一为首列):42 格固定行高;当月真实日可显示 当日支出(红)/收入(绿) 小字;
/// 状态着色优先级:已选(描边)> 今天 > 周期内(淡蓝)> 周期外/空白。点某天触发 <see cref="DayChosen"/>。
/// 月份 ‹/› 由宿主(总览)驱动——切月事件 <see cref="MonthStep"/> 交给宿主改 _viewDate 后整体刷新,保持「月历跟随左侧日期」。
/// </summary>
internal sealed class MonthCalendar : UserControl
{
    public const int RightWidth = 316;

    private const double CellHeight = 48;
    private static readonly string[] Weekdays = { "一", "二", "三", "四", "五", "六", "日" };

    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _grid = new();
    private IReadOnlyDictionary<string, (long OutCents, long InCents)> _money = new Dictionary<string, (long, long)>();

    /// <summary>点某格(当月真实日)。</summary>
    public event Action<DateTime>? DayChosen;

    /// <summary>点 ‹/›:参数 = 上/下个月。宿主负责把选中日翻到对应月并刷新。</summary>
    public event Action<int>? MonthStep;

    public MonthCalendar()
    {
        // 标题行:‹ 2026年9月 ›(‹/› 等宽,标题居中于整个月历宽度)
        var prev = new Button { Content = "‹", Width = 30, Height = 26, Margin = new Thickness(0, 0, 6, 0) };
        prev.Click += (_, _) => MonthStep?.Invoke(-1);
        var next = new Button { Content = "›", Width = 30, Height = 26, Margin = new Thickness(6, 0, 0, 0) };
        next.Click += (_, _) => MonthStep?.Invoke(1);
        var header = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(prev);
        Grid.SetColumn(prev, 0);
        header.Children.Add(_title);
        Grid.SetColumn(_title, 1);
        header.Children.Add(next);
        Grid.SetColumn(next, 2);

        // 星期头(周一为首列)
        var headRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        for (int i = 0; i < 7; i++)
        {
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var w = new TextBlock { Text = Weekdays[i], HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
            w.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
            Grid.SetColumn(w, i);
            headRow.Children.Add(w);
        }

        // 6×7 日格
        for (int i = 0; i < 7; i++)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 6; r++)
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(headRow);
        panel.Children.Add(_grid);
        Content = panel;
    }

    /// <summary>按月重建 42 格并着色。</summary>
    public void ShowMonth(DateTime month1st, DateTime? selected,
        IReadOnlySet<string> inPeriodIso,
        IReadOnlyDictionary<string, (long OutCents, long InCents)> dayMoney)
    {
        _money = dayMoney;
        _title.Text = $"{month1st.Year}年{month1st.Month}月";
        _grid.Children.Clear();

        var first = new DateTime(month1st.Year, month1st.Month, 1);
        int lead = ((int)first.DayOfWeek + 6) % 7;   // 周一为首列
        var todayIso = DateTime.Today.ToString("yyyy-MM-dd");
        string? selIso = selected?.ToString("yyyy-MM-dd");

        for (int i = 0; i < 42; i++)
        {
            int dayNum = i - lead + 1;
            var day = dayNum >= 1 && dayNum <= DateTime.DaysInMonth(first.Year, first.Month)
                ? first.AddDays(dayNum - 1)
                : (DateTime?)null;

            Border bg;
            if (day is null)
            {
                // 留白位:维持周对齐,不显示邻月灰字
                bg = new Border { Margin = new Thickness(1) };
                bg.SetResourceReference(Border.BackgroundProperty, UiKeys.CalendarBlankCell);
            }
            else
            {
                var iso = day.Value.ToString("yyyy-MM-dd");
                bool inPeriod = inPeriodIso.Contains(iso);
                bool isToday = iso == todayIso;
                bool isSelected = iso == selIso;

                var dayText = new TextBlock
                {
                    Text = dayNum.ToString(),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 2, 4, 0),
                    FontWeight = isToday || isSelected ? FontWeights.Bold : FontWeights.Normal
                };
                dayText.SetResourceReference(TextBlock.ForegroundProperty,
                    isToday ? UiKeys.Accent : inPeriod ? UiKeys.TextPrimary : UiKeys.TextSecondary);

                string? outLine = null, inLine = null;
                if (_money.TryGetValue(iso, out var mv))
                {
                    if (mv.OutCents > 0) outLine = "-" + Money.Yuan(mv.OutCents).TrimStart('¥');
                    if (mv.InCents > 0) inLine = "+" + Money.Yuan(mv.InCents).TrimStart('¥');
                }

                var stack = new StackPanel();
                stack.Children.Add(dayText);
                if (outLine is not null)
                    stack.Children.Add(Small(outLine, UiKeys.Expense));
                if (inLine is not null)
                    stack.Children.Add(Small(inLine, UiKeys.Income));
                if (outLine is null && inLine is null)
                    stack.Children.Add(new Border { Height = 15 });

                bg = new Border
                {
                    Child = stack,
                    Margin = new Thickness(1),
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = isSelected ? new Thickness(2) : new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{(inPeriod ? "" : "周期外 ")}{iso}\n" +
                              (outLine is null && inLine is null ? "无收支记录"
                                  : $"支出{(outLine ?? "0")} · 收入{(inLine is null ? "+0" : inLine)}")
                };
                if (isToday || inPeriod)
                    bg.SetResourceReference(Border.BackgroundProperty,
                        isToday ? UiKeys.CalendarTodayBg : UiKeys.CalendarPeriodBg);
                if (isSelected)
                    bg.SetResourceReference(Border.BorderBrushProperty, UiKeys.Accent);
                var chosen = day.Value;
                bg.MouseLeftButtonUp += (_, _) => DayChosen?.Invoke(chosen);
            }
            Grid.SetColumn(bg, i % 7);
            Grid.SetRow(bg, i / 7);
            _grid.Children.Add(bg);
        }
    }

    private static TextBlock Small(string text, string key)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 4, 0)
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, key);
        return t;
    }
}
