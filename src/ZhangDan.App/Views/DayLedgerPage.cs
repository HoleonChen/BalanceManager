using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZhangDan.App.Dialogs;

namespace ZhangDan.App.Views;

/// <summary>今日记账页:日期导航 + 记一笔/转账 + 当日流水列表(右键作废/双击编辑)+ 周期/资金池标签。</summary>
internal sealed class DayLedgerPage : PageBase
{
    /// <summary>让主窗切页(0 今日 /1 流水 /2 周期)。Day 页自己不持有 MainWindow。</summary>
    public Action<int>? GoTo { get; set; }

    private DateTime _viewDate = DateTime.Today;
    private LedgerSession S => App.Ledger!;

    private readonly TextBlock _dateLabel = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _periodChip = new() { Margin = new Thickness(4, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
    private readonly TextBlock _poolChip = new() { Margin = new Thickness(4, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
    private readonly TextBlock _summary = new() { Foreground = Brushes.Gray, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ListView _list = new();

    public DayLedgerPage()
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
        var record = new Button { Content = "＋ 记一笔", Margin = new Thickness(0, 0, 10, 0), MinWidth = 110, Height = 34, Padding = new Thickness(12, 0, 12, 0) };
        record.Click += (_, _) => Record();
        var transfer = new Button { Content = "⇄ 转账", Margin = new Thickness(0, 0, 16, 0), MinWidth = 92, Height = 34 };
        transfer.Click += (_, _) => Transfer();

        var prev = new Button { Content = "◀", Width = 40, Height = 30, Margin = new Thickness(0, 0, 4, 0) };
        prev.Click += (_, _) => { _viewDate = _viewDate.AddDays(-1); RefreshAll(); };
        var next = new Button { Content = "▶", Width = 40, Height = 30, Margin = new Thickness(4, 0, 0, 0) };
        next.Click += (_, _) => { _viewDate = _viewDate.AddDays(1); RefreshAll(); };
        var today = new Button { Content = "今天", Width = 70, Height = 30, Margin = new Thickness(10, 0, 18, 0) };
        today.Click += (_, _) => { _viewDate = DateTime.Today; RefreshAll(); };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 12, 20, 6) };
        top.Children.Add(record);
        top.Children.Add(transfer);
        top.Children.Add(prev);
        top.Children.Add(_dateLabel);
        top.Children.Add(next);
        top.Children.Add(today);
        top.Children.Add(_periodChip);
        top.Children.Add(_poolChip);
        top.Children.Add(_summary);

        _list.View = BuildColumns();
        _list.Margin = new Thickness(20, 0, 20, 4);
        _list.SelectionMode = SelectionMode.Single;
        _list.MouseDoubleClick += (_, _) => EditSelected();

        var menu = new ContextMenu();
        var cancelItem = new MenuItem { Header = "作废 / 删除这笔…" };
        cancelItem.Click += (_, _) => CancelSelected();
        menu.Items.Add(cancelItem);
        _list.ContextMenu = menu;

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(top, 0);
        Grid.SetRow(_list, 1);
        grid.Children.Add(top);
        grid.Children.Add(_list);

        _periodChip.MouseLeftButtonUp += (_, _) => PeriodChipClick();
        _poolChip.MouseLeftButtonUp += (_, _) => PoolChipClick();

        Content = grid;
    }

    private static GridView BuildColumns()
    {
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "时间", Width = 72, DisplayMemberBinding = Bind("Time") });
        gv.Columns.Add(new GridViewColumn { Header = "名称", Width = 200, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new GridViewColumn { Header = "分类", Width = 120, DisplayMemberBinding = Bind("Category") });
        gv.Columns.Add(new GridViewColumn { Header = "账户", Width = 200, DisplayMemberBinding = Bind("Account") });
        gv.Columns.Add(new GridViewColumn { Header = "金额", Width = 130, DisplayMemberBinding = Bind("Amount") });
        return gv;
    }

    private static System.Windows.Data.Binding Bind(string p)
        => new(p) { Mode = System.Windows.Data.BindingMode.OneWay };

    private sealed class Row
    {
        public required TxnListItem T { get; init; }
        public string Time => T.Time;
        public string Name => T.Direction == "transfer" ? T.Name : T.Name;
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

        var rows = new System.Collections.Generic.List<Row>();
        foreach (var t in Transactions.ListByDate(S, dateStr))
            rows.Add(new Row { T = t });
        _list.ItemsSource = rows;

        var (outC, inC) = Transactions.DayTotals(S, dateStr);
        _summary.Text = $"支出 {Money.Yuan(outC)} · 收入 {Money.Yuan(inC)} · {rows.Count} 笔";

        RefreshChips(dateStr);
    }

    private void RefreshChips(string dateStr)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(S, today);
        if (p is not null)
        {
            _periodChip.Text = $"· 周期:{p.Name}({Short(p.StartDate)}~{(p.EndDate is null ? "长期" : Short(p.EndDate))})";
            _periodChip.Foreground = Brushes.SteelBlue;
        }
        else
        {
            var expired = Periods.GetLatestExpiredActive(S, today);
            if (expired is not null)
            {
                _periodChip.Text = $"· 上一周期「{expired.Name}」已到期未封存(点击处理)";
                _periodChip.Foreground = Brushes.DarkOrange;
            }
            else
            {
                _periodChip.Text = "· 无进行中周期(点击新建)";
                _periodChip.Foreground = Brushes.Gray;
            }
        }

        var pool = Pools.Get(S, p?.Id ?? -1);
        if (p is not null && pool is not null)
        {
            var st = Pools.State(S, pool);
            _poolChip.Text = $"· 池:剩余 {Money.Yuan(st.RemainingCents)} / 可支配 {Money.Yuan(st.DisposableCents)}";
            _poolChip.Foreground = Brushes.SteelBlue;
        }
        else if (p is not null)
        {
            _poolChip.Text = "· 资金池未设置";
            _poolChip.Foreground = Brushes.Gray;
        }
        else
        {
            _poolChip.Text = "";
        }
    }

    private static string Short(string iso)
    {
        var p = iso.Split('-');
        return $"{p[0]}/{int.Parse(p[1])}/{int.Parse(p[2])}";
    }

    private void PeriodChipClick()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        GoTo?.Invoke(Periods.GetCoveringActive(S, today) is not null ? 1 : 2);
    }

    private void PoolChipClick()
    {
        // 资金池对话框 P3 落地;先切到周期页让用户处理
        GoTo?.Invoke(2);
    }

    private Row? SelectedRow() => _list.SelectedItem as Row;

    private void Record()
    {
        var dlg = new RecordDialog(S, defaultDate: _viewDate.Date, settings: App.Settings);
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            var id = Transactions.Add(S, new TxnDraft
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
            _ = id;
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
