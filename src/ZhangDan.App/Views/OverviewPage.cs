using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZhangDan.App.Dialogs;

namespace ZhangDan.App.Views;

/// <summary>总览页(排第一,吸收今日记账):摘要带(周期/资金池/净资产)+ 选定日流水(记/转/双击编辑/作废)+ 右侧自然月月历。</summary>
internal sealed class OverviewPage : PageBase
{
    /// <summary>让主窗切页(1 流水 /2 周期)。总览页自己不持有 MainWindow。</summary>
    public Action<int>? GoTo { get; set; }

    private DateTime _viewDate = DateTime.Today;
    private LedgerSession S => App.Ledger!;

    private readonly TextBlock _dateLabel = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _daySummary = new() { Foreground = Brushes.Gray, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _monthSummary = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Border _summaryBand = new() { Padding = new Thickness(12, 8, 12, 8), BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly StackPanel _bandContent = new();
    private readonly ListView _list = new();
    private readonly MonthCalendar _calendar = new();

    public OverviewPage()
    {
        BuildUi();
    }

    public override void OnShown()
    {
        if (App.Ledger is not null)
            RefreshAll();
    }

    private void BuildUi()
    {
        var record = new Button { Content = "＋ 记一笔", MinWidth = 110, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        record.Click += (_, _) => Record();
        var transfer = new Button { Content = "⇄ 转账", MinWidth = 92, Height = 32 };
        transfer.Click += (_, _) => Transfer();
        var recRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        recRow.Children.Add(record);
        recRow.Children.Add(transfer);

        var prev = new Button { Content = "◀", Width = 36, Height = 28, Margin = new Thickness(0, 0, 3, 0) };
        prev.Click += (_, _) => { _viewDate = _viewDate.AddDays(-1); RefreshAll(); };
        var next = new Button { Content = "▶", Width = 36, Height = 28, Margin = new Thickness(3, 0, 0, 0) };
        next.Click += (_, _) => { _viewDate = _viewDate.AddDays(1); RefreshAll(); };
        var today = new Button { Content = "今天", Width = 66, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        today.Click += (_, _) => { _viewDate = DateTime.Today; RefreshAll(); };

        var navRow = new StackPanel { Orientation = Orientation.Horizontal };
        navRow.Children.Add(prev);
        navRow.Children.Add(_dateLabel);
        navRow.Children.Add(next);
        navRow.Children.Add(today);
        navRow.Children.Add(_daySummary);

        var leftTop = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
        leftTop.Children.Add(recRow);
        leftTop.Children.Add(navRow);

        _list.View = BuildColumns();
        _list.SelectionMode = SelectionMode.Single;
        _list.MouseDoubleClick += (_, _) => EditSelected();
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        var menu = new ContextMenu();
        var cancelItem = new MenuItem { Header = "作废 / 删除这笔…" };
        cancelItem.Click += (_, _) => CancelSelected();
        menu.Items.Add(cancelItem);
        _list.ContextMenu = menu;

        // 右列:月历 + 本月合计
        var rightInner = new StackPanel { Margin = new Thickness(16, 10, 0, 0) };
        rightInner.Children.Add(_calendar);
        rightInner.Children.Add(_monthSummary);
        var rightScroll = new ScrollViewer
        {
            Content = rightInner,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MonthCalendar.RightWidth + 16) });
        var leftHost = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        leftHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(leftTop, 0);
        Grid.SetRow(_list, 1);
        leftHost.Children.Add(leftTop);
        leftHost.Children.Add(_list);

        Grid.SetColumn(leftHost, 0);
        Grid.SetColumn(rightScroll, 1);
        columns.Children.Add(leftHost);
        columns.Children.Add(rightScroll);

        _summaryBand.Child = _bandContent;
        var root = new Grid { Margin = new Thickness(18, 8, 18, 4) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_summaryBand, 0);
        Grid.SetRow(columns, 1);
        root.Children.Add(_summaryBand);
        root.Children.Add(columns);

        _calendar.DayChosen += d => { _viewDate = d.Date; RefreshAll(); };
        _calendar.MonthStep += delta => MoveMonth(delta);

        Content = root;
    }

    private static GridView BuildColumns()
    {
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "时间", Width = 60, DisplayMemberBinding = Bind("Time") });
        gv.Columns.Add(new GridViewColumn { Header = "名称", Width = 165, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new GridViewColumn { Header = "分类", Width = 95, DisplayMemberBinding = Bind("Category") });
        gv.Columns.Add(new GridViewColumn { Header = "账户", Width = 150, DisplayMemberBinding = Bind("Account") });
        gv.Columns.Add(new GridViewColumn { Header = "金额", Width = 120, DisplayMemberBinding = Bind("Amount") });
        return gv;
    }

    private static System.Windows.Data.Binding Bind(string p)
        => new(p) { Mode = System.Windows.Data.BindingMode.OneWay };

    private sealed class Row
    {
        public required TxnListItem T { get; init; }
        public string Time => T.Time;
        public string Name => T.Name;
        public string Category => T.Direction == "transfer" ? "转账" : T.Category;
        public string Account => T.Direction == "transfer" ? $"{T.Account} → {T.AccountTo}" : T.Account;
        public string Amount => T.Direction == "transfer"
            ? Money.Yuan(T.AmountCents) + (T.DeltaCents != 0 ? $" (Δ{(T.DeltaCents > 0 ? "+" : "-")}{Money.Yuan(Math.Abs(T.DeltaCents))})" : "")
            : (T.Direction == "out" ? "-" : "+") + Money.Yuan(T.AmountCents);
    }

    private void RefreshAll()
    {
        var day = _viewDate.Date;
        var dateStr = day.ToString("yyyy-MM-dd");
        var diff = (day - DateTime.Today).Days;
        _dateLabel.Text = diff switch { 0 => $"{dateStr} · 今天", 1 => $"{dateStr} · 明天", -1 => $"{dateStr} · 昨天", _ => dateStr };

        var rows = new List<Row>();
        foreach (var t in Transactions.ListByDate(S, dateStr))
            rows.Add(new Row { T = t });
        _list.ItemsSource = rows;

        var (outC, inC) = Transactions.DayTotals(S, dateStr);
        _daySummary.Text = $"支出 {Money.Yuan(outC)} · 收入 {Money.Yuan(inC)} · {rows.Count} 笔";

        RefreshSummaryBand();
        RefreshCalendar();
    }

    /// <summary>摘要带(锚今天):周期状态 + 资金池预算/进度 + 净资产。</summary>
    private void RefreshSummaryBand()
    {
        _bandContent.Children.Clear();
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        var chip = PeriodChipFor(today);
        var topRow = new StackPanel { Orientation = Orientation.Horizontal };
        chip.Margin = new Thickness(0, 0, 16, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        topRow.Children.Add(chip);

        var net = new TextBlock
        {
            Text = $"净资产(启用账户):{Money.Yuan(Accounts.NetAssets(S))}",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center
        };
        var topRowDock = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(net, Dock.Right);
        topRowDock.Children.Add(net);
        topRowDock.Children.Add(topRow);
        _bandContent.Children.Add(topRowDock);

        // 池:已设置 → 进度条 + 预算/已花/剩余/可支配;未设置 → 灰字引导去周期页
        var p = Periods.GetCoveringActive(S, today);
        var pool = p is null ? null : Pools.Get(S, p.Id);
        if (p is not null && pool is null)
        {
            var hint = new TextBlock
            {
                Text = "资金池未设置 · 点此到周期页补建",
                Foreground = Brushes.Gray,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 4, 0, 0)
            };
            hint.MouseLeftButtonUp += (_, _) => GoTo?.Invoke(2);
            _bandContent.Children.Add(hint);
        }
        else if (pool is not null)
        {
            var st = Pools.State(S, pool);
            var bar = ProgressBar(pool.BudgetCents, st.SpentCents);
            bar.Margin = new Thickness(0, 6, 0, 0);
            bar.HorizontalAlignment = HorizontalAlignment.Left;
            var txt = new TextBlock
            {
                Text = $"池 · 预算 {Money.Yuan(pool.BudgetCents)} / 已花 {Money.Yuan(st.SpentCents)} / 剩余 {Money.Yuan(st.RemainingCents)} / 可支配 {Money.Yuan(st.DisposableCents)}",
                Foreground = st.DisposableCents < 0 ? Brushes.Firebrick : Brushes.Gray,
                Margin = new Thickness(0, 3, 0, 0)
            };
            var poolCol = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            poolCol.Children.Add(bar);
            poolCol.Children.Add(txt);
            poolCol.MouseLeftButtonUp += (_, _) => GoTo?.Invoke(2);
            poolCol.Cursor = Cursors.Hand;
            _bandContent.Children.Add(poolCol);
        }
    }

    /// <summary>周期 pill(点击事件已在内部接好跳转):进行中→蓝(流水页);已到期未封存→橙(周期页);无→灰(周期页)。</summary>
    private TextBlock PeriodChipFor(string today)
    {
        var p = Periods.GetCoveringActive(S, today);
        if (p is not null)
        {
            var chip = new TextBlock
            {
                Text = $"周期 · {p.Name} ({Short(p.StartDate)}~{(p.EndDate is null ? "长期" : Short(p.EndDate))})",
                Foreground = Brushes.SteelBlue,
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };
            chip.MouseLeftButtonUp += (_, _) => GoTo?.Invoke(1);
            return chip;
        }
        var expired = Periods.GetLatestExpiredActive(S, today);
        if (expired is not null)
        {
            var chip2 = new TextBlock
            {
                Text = $"上一周期「{expired.Name}」已到期未封存 · 点此处理",
                Foreground = Brushes.DarkOrange,
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };
            chip2.MouseLeftButtonUp += (_, _) => GoTo?.Invoke(2);
            return chip2;
        }
        var chip3 = new TextBlock
        {
            Text = "无进行中周期 · 点此新建",
            Foreground = Brushes.Gray,
            Cursor = Cursors.Hand
        };
        chip3.MouseLeftButtonUp += (_, _) => GoTo?.Invoke(2);
        return chip3;
    }

    /// <summary>迷你进度条:已花 / 预算(超预算满格转红;预算 0 返回空)。</summary>
    private static FrameworkElement ProgressBar(long budgetCents, long spentCents)
    {
        if (budgetCents <= 0)
            return new Border();
        const double total = 240;
        var track = new Border { Width = total, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xE7, 0xEC)), Child = null };
        var frac = Math.Min(1.0, (double)spentCents / budgetCents);
        var fill = new Border
        {
            Width = total * frac,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = spentCents > budgetCents ? Brushes.Firebrick : Brushes.SteelBlue,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var grid = new Grid();
        grid.Children.Add(track);
        grid.Children.Add(fill);
        return grid;
    }

    private void RefreshCalendar()
    {
        var month1 = new DateTime(_viewDate.Year, _viewDate.Month, 1);
        var start = month1.ToString("yyyy-MM-dd");
        var end = month1.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var map = Transactions.DayTotalsMap(S, start, end);

        long mOut = 0, mIn = 0;
        foreach (var (o, i) in map.Values)
        {
            mOut += o;
            mIn += i;
        }
        _monthSummary.Text = $"本月支出 {Money.Yuan(mOut)} · 收入 {Money.Yuan(mIn)}";

        // 高亮集合:参照周期 = 覆盖当前选中日的进行中周期,否则覆盖今天的进行中周期
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var refP = Periods.GetCoveringActive(S, _viewDate.ToString("yyyy-MM-dd"))
                   ?? Periods.GetCoveringActive(S, today);
        var inPeriod = new HashSet<string>();
        if (refP is not null)
        {
            var from = DateTime.Parse(start);
            var to = DateTime.Parse(end);
            var pStart = DateTime.Parse(refP.StartDate);
            var pEnd = refP.EndDate is null ? DateTime.MaxValue.Date : DateTime.Parse(refP.EndDate);
            var cur = from < pStart ? pStart : from;
            while (cur <= to && cur <= pEnd)
            {
                inPeriod.Add(cur.ToString("yyyy-MM-dd"));
                cur = cur.AddDays(1);
            }
        }

        _calendar.ShowMonth(month1, _viewDate, inPeriod, map);
    }

    private void MoveMonth(int delta)
    {
        var target = _viewDate.AddMonths(delta);
        var day = Math.Min(_viewDate.Day, DateTime.DaysInMonth(target.Year, target.Month));
        _viewDate = new DateTime(target.Year, target.Month, day);
        RefreshAll();
    }

    private static string Short(string iso)
    {
        var p = iso.Split('-');
        return $"{p[0]}/{int.Parse(p[1])}/{int.Parse(p[2])}";
    }

    private Row? SelectedRow() => _list.SelectedItem as Row;

    private void Record()
    {
        var dlg = new RecordDialog(S, defaultDate: _viewDate.Date, settings: App.Settings);
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Transactions.Add(S, new TxnDraft
            {
                Date = dlg.DateStr,
                Direction = dlg.Direction,
                AccountId = dlg.AccountId,
                CategoryId = dlg.CategoryId,
                AmountCents = dlg.AmountCents,
                Name = dlg.TxnName,
                Channel = dlg.Channel,
                Note = dlg.Note,
                InPool = dlg.InPool
            });
            _viewDate = DateTime.Parse(dlg.DateStr);
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败:\n{ex.Message}", "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Transfer()
    {
        var dlg = new TransferDialog(S, defaultDate: _viewDate.Date, settings: App.Settings);
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Transactions.Transfer(S, new TransferDraft
            {
                Date = dlg.DateStr,
                FromAccountId = dlg.FromAccountId,
                ToAccountId = dlg.ToAccountId,
                PrincipalCents = dlg.PrincipalCents,
                DeltaCents = dlg.DeltaCents,
                Kind = dlg.Kind,
                Note = dlg.Note,
                InPool = dlg.InPool
            });
            _viewDate = DateTime.Parse(dlg.DateStr);
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败:\n{ex.Message}", "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditSelected()
    {
        var row = SelectedRow();
        if (row is null)
            return;
        if (Periods.HasSealedCovering(S, row.T.Date))
        {
            MessageBox.Show(LedgerReadonlyException.Friendly(row.T.Date), "账单管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (row.T.Direction == "transfer")
            EditTransfer(row.T.Id);
        else
            EditNormal(row.T.Id);
    }

    private void EditNormal(long id)
    {
        var e = Transactions.GetEditable(S, id);
        if (e is null)
            return;
        var dlg = new RecordDialog(S, defaultDate: _viewDate.Date, settings: App.Settings, edit: e,
            presetAccountId: e.AccountId, presetCategoryId: e.CategoryId);
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Transactions.Update(S, e with
            {
                Direction = dlg.Direction,
                AccountId = dlg.AccountId,
                CategoryId = dlg.CategoryId,
                AmountCents = dlg.AmountCents,
                Name = dlg.TxnName,
                Channel = dlg.Channel,
                Note = dlg.Note,
                InPool = dlg.InPool
            });
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败:\n{ex.Message}", "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditTransfer(long id)
    {
        var e = Transactions.GetTransfer(S, id);
        if (e is null)
            return;
        var dlg = new TransferDialog(S, defaultDate: _viewDate.Date, settings: App.Settings, edit: e);
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Transactions.UpdateTransfer(S, e with
            {
                FromAccountId = dlg.FromAccountId,
                ToAccountId = dlg.ToAccountId,
                PrincipalCents = dlg.PrincipalCents,
                DeltaCents = dlg.DeltaCents,
                Kind = dlg.Kind,
                Note = dlg.Note,
                InPool = dlg.InPool
            });
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败:\n{ex.Message}", "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelSelected()
    {
        var row = SelectedRow();
        if (row is null)
            return;
        if (Periods.HasSealedCovering(S, row.T.Date))
        {
            MessageBox.Show(LedgerReadonlyException.Friendly(row.T.Date), "账单管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"作废这笔并撤出统计?\n\n{row.T.Name}\n{row.Amount} · {row.T.Account}",
                "作废流水", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            Transactions.Cancel(S, row.T.Id);
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
