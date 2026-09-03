using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ZhangDan.App.Dialogs;

namespace ZhangDan.App.Views;

/// <summary>
/// 流水页:顶部筛选(范围/方向/账户/分类/含作废/关键词)+ 列表按日分组(组头 = 日期·星期 + 当日收支)。
/// 双击行编辑、右键可作废;落在封存周期内的日期只读(与今日页同一套守卫)。
/// </summary>
internal sealed class FlowPage : PageBase
{
    private LedgerSession S => App.Ledger!;

    private readonly ComboBox _scopeBox = new() { Width = 200 };
    private readonly ComboBox _dirBox = new() { Width = 84 };
    private readonly ComboBox _acctBox = new() { Width = 140 };
    private readonly ComboBox _catBox = new() { Width = 124 };
    private readonly CheckBox _cancelledCheck = new() { Content = "含作废", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 0) };
    private readonly TextBox _kwBox = new() { Width = 140 };
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly StackPanel _groups = new();
    private readonly ScrollViewer _scroll;
    private bool _suppress;

    // 范围选择状态:all=全部 / unassigned=未归属 / period=某周期(取 _scopePeriodId)
    private string _scope = "auto";
    private long? _scopePeriodId;
    private string? _dir;
    private long? _acct;
    private long? _cat;
    private bool _showCancelled;
    private string _kw = "";

    private sealed record ScopeOpt(string Label, long? PeriodId, bool Unassigned);
    private sealed record DirOpt(string Label, string Code);
    private sealed record PickOpt(string Label, long? Id);

    public FlowPage()
    {
        var reset = new Button { Content = "重置", Width = 60, Height = 30, Margin = new Thickness(8, 0, 0, 0) };
        reset.Click += (_, _) => ResetFilters();

        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        bar.Children.Add(new LabelBox("范围", _scopeBox));
        bar.Children.Add(new LabelBox("方向", _dirBox));
        bar.Children.Add(new LabelBox("账户", _acctBox));
        bar.Children.Add(new LabelBox("分类", _catBox));
        bar.Children.Add(_cancelledCheck);
        bar.Children.Add(new LabelBox("关键词", _kwBox));
        bar.Children.Add(reset);
        bar.Children.Add(_summary);

        _scopeBox.SelectionChanged += (_, _) => OnFilterChanged();
        _dirBox.SelectionChanged += (_, _) => OnFilterChanged();
        _acctBox.SelectionChanged += (_, _) => OnFilterChanged();
        _catBox.SelectionChanged += (_, _) => OnFilterChanged();
        _cancelledCheck.Checked += (_, _) => OnFilterChanged();
        _cancelledCheck.Unchecked += (_, _) => OnFilterChanged();
        _kwBox.TextChanged += (_, _) => OnKeywordChanged();

        var top = new StackPanel { Margin = new Thickness(20, 12, 20, 8) };
        top.Children.Add(bar);

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(20, 0, 20, 10)
        };
        _scroll.Content = _groups;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(top, 0);
        Grid.SetRow(_scroll, 1);
        grid.Children.Add(top);
        grid.Children.Add(_scroll);
        _summary.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        Content = grid;
    }

    private sealed class LabelBox : DockPanel
    {
        public LabelBox(string label, UIElement input)
        {
            var t = new TextBlock { Text = label, Width = 42, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
            Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(t, Dock.Left);
            Children.Add(t);
            Children.Add(input);
        }
    }

    private void OnKeywordChanged()
    {
        if (_suppress)
            return;
        _kw = _kwBox.Text.Trim();
        Refresh();
    }

    private void OnFilterChanged()
    {
        if (_suppress)
            return;
        CaptureFilters();
        Refresh();
    }

    private void CaptureFilters()
    {
        var s = _scopeBox.SelectedItem as ScopeOpt;
        if (s is not null)
        {
            if (s.Unassigned)
                _scope = "unassigned";
            else if (s.PeriodId is long pid)
            {
                _scope = "period";
                _scopePeriodId = pid;
            }
            else
                _scope = "all";
        }
        _dir = (_dirBox.SelectedItem as DirOpt)?.Code;
        _acct = (_acctBox.SelectedItem as PickOpt)?.Id;
        _cat = (_catBox.SelectedItem as PickOpt)?.Id;
        _showCancelled = _cancelledCheck.IsChecked == true;
        _kw = _kwBox.Text.Trim();
    }

    private Transactions.FlowFilter CurrentFilter() => new()
    {
        PeriodId = _scope == "period" ? _scopePeriodId : null,
        UnassignedOnly = _scope == "unassigned",
        Direction = _dir,
        AccountId = _acct,
        CategoryId = _cat,
        ShowCancelled = _showCancelled,
        Keyword = _kw
    };

    public override void OnShown()
    {
        if (App.Ledger is null)
            return;
        RebuildOptions();
    }

    private void RebuildOptions()
    {
        _suppress = true;
        try
        {
            _scopeBox.ItemsSource = null;
            var scopes = new List<ScopeOpt> { new("全部", null, false), new("未归属", null, true) };
            foreach (var p in Periods.ListAll(S))
                scopes.Add(new ScopeOpt(PeriodLabel(p), p.Id, false));
            _scopeBox.DisplayMemberPath = "Label";
            _scopeBox.ItemsSource = scopes;
            ApplyScopeSelection();

            _dirBox.DisplayMemberPath = "Label";
            _dirBox.ItemsSource = new[]
            {
                new DirOpt("全部", ""), new DirOpt("支出", "out"),
                new DirOpt("收入", "in"), new DirOpt("转账", "transfer")
            };
            Select(_dirBox, (DirOpt o) => o.Code == (_dir ?? ""));

            var accts = new List<PickOpt> { new("全部账户", null) };
            foreach (var a in Accounts.ListAll(S))
                accts.Add(new PickOpt(a.Enabled ? a.Name : a.Name + "(停用)", a.Id));
            _acctBox.DisplayMemberPath = "Label";
            _acctBox.ItemsSource = accts;
            Select(_acctBox, (PickOpt o) => o.Id == _acct);

            var cats = new List<PickOpt> { new("全部分类", null) };
            foreach (var c in Categories.ListManual(S, income: false))
                cats.Add(new PickOpt(c.Name, c.Id));
            foreach (var c in Categories.ListManual(S, income: true))
                cats.Add(new PickOpt("收入·" + c.Name, c.Id));
            _catBox.DisplayMemberPath = "Label";
            _catBox.ItemsSource = cats;
            Select(_catBox, (PickOpt o) => o.Id == _cat);

            _cancelledCheck.IsChecked = _showCancelled;
            _kwBox.Text = _kw;
        }
        finally
        {
            _suppress = false;
        }
        Refresh();
    }

    /// <summary>把范围下拉恢复到当前 _scope 状态;auto(首次)优先当前进行中周期,否则全部。</summary>
    private void ApplyScopeSelection()
    {
        ScopeOpt? target = null;
        foreach (ScopeOpt o in _scopeBox.Items)
        {
            bool hit = _scope switch
            {
                "unassigned" => o.Unassigned,
                "period" => o.PeriodId == _scopePeriodId && !o.Unassigned,
                "all" => !o.Unassigned && o.PeriodId is null,
                _ => false
            };
            if (hit)
            {
                target = o;
                break;
            }
        }

        // auto 或原选择不存在:回退到「当前进行中周期,没有则全部」
        if (target is null)
        {
            var active = Periods.GetCoveringActive(S, DateTime.Today.ToString("yyyy-MM-dd"));
            foreach (ScopeOpt o in _scopeBox.Items)
            {
                if (active is not null && o.PeriodId == active.Id)
                {
                    target = o;
                    _scope = "period";
                    _scopePeriodId = active.Id;
                    break;
                }
            }
            if (target is null)
            {
                foreach (ScopeOpt o in _scopeBox.Items)
                {
                    if (!o.Unassigned && o.PeriodId is null)
                    {
                        target = o;
                        _scope = "all";
                        break;
                    }
                }
            }
        }
        _scopeBox.SelectedItem = target;
    }

    private static void Select<T>(ComboBox box, Func<T, bool> match)
    {
        foreach (var item in box.Items)
        {
            if (item is T t && match(t))
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0)
            box.SelectedIndex = 0;
    }

    private static string PeriodLabel(PeriodRow p)
        => p.EndDate is null
            ? $"{p.Name} · 长期"
            : $"{p.Name} · {Short(p.StartDate)}~{Short(p.EndDate)}";

    private static string Short(string iso)
    {
        var p = iso.Split('-');
        return $"{p[0]}/{int.Parse(p[1])}/{int.Parse(p[2])}";
    }

    /// <summary>外部(账户详情)跳来:预置只看该账户的全部流水,切「全部」范围。</summary>
    internal void PresetAccount(long accountId)
    {
        _scope = "all";
        _acct = accountId;
    }

    private void ResetFilters()
    {
        _scope = "all";
        _dir = "";
        _acct = null;
        _cat = null;
        _showCancelled = false;
        _kw = "";
        _suppress = true;
        try
        {
            ApplyScopeSelection();
            Select(_dirBox, (DirOpt o) => o.Code == "");
            Select(_acctBox, (PickOpt o) => o.Id == null);
            Select(_catBox, (PickOpt o) => o.Id == null);
            _cancelledCheck.IsChecked = false;
            _kwBox.Text = "";
        }
        finally
        {
            _suppress = false;
        }
        Refresh();
    }

    private void Refresh()
    {
        CaptureFilters();
        var rows = Transactions.ListFlows(S, CurrentFilter());

        var keepOffset = _scroll.VerticalOffset;
        _groups.Children.Clear();

        long outC = 0, inC = 0;
        var byDay = new List<(string Date, List<Row> Rows)>();
        string curDate = "";
        List<Row>? cur = null;
        foreach (var t in rows)
        {
            if (t.Date != curDate)
            {
                curDate = t.Date;
                cur = new List<Row>();
                byDay.Add((curDate, cur));
            }
            cur!.Add(new Row(t));
        }

        foreach (var (date, dayRows) in byDay)
        {
            foreach (var r in dayRows)
            {
                if (r.T.Status == "cancelled")
                    continue;
                if (r.T.Direction == "out") outC += r.T.AmountCents;
                else if (r.T.Direction == "in") inC += r.T.AmountCents;
            }
            _groups.Children.Add(BuildDayGroup(date, dayRows));
        }

        _summary.Text = byDay.Count == 0
            ? "无匹配流水"
            : $"共 {rows.Count} 笔 · 支出 {Money.Yuan(outC)} · 收入 {Money.Yuan(inC)}";

        if (keepOffset > 0)
            Dispatcher.BeginInvoke(() => _scroll.ScrollToVerticalOffset(keepOffset));
    }

    private UIElement BuildDayGroup(string date, List<Row> dayRows)
    {
        var parsed = DateTime.Parse(date);
        var wk = parsed.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN"));
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var rel = date == today ? "今天" : date == DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd") ? "昨天" : "";
        long dOut = 0, dIn = 0;
        foreach (var r in dayRows)
        {
            if (r.T.Status == "cancelled")
                continue;
            if (r.T.Direction == "out") dOut += r.T.AmountCents;
            else if (r.T.Direction == "in") dIn += r.T.AmountCents;
        }

        var header = new TextBlock
        {
            Text = $"{(rel.Length > 0 ? rel + " " : "")}{Short(date)} {wk}"
                + (dOut > 0 || dIn > 0 ? $"   支出 {Money.Yuan(dOut)} · 收入 {Money.Yuan(dIn)} · {dayRows.Count} 笔" : ""),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 10, 0, 2)
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Accent);

        var list = new ListView { ItemsSource = dayRows, SelectionMode = SelectionMode.Single };
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "时间", Width = 62, DisplayMemberBinding = Bind("Time") });
        gv.Columns.Add(new GridViewColumn { Header = "名称", Width = 190, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new GridViewColumn { Header = "分类", Width = 108, DisplayMemberBinding = Bind("Category") });
        gv.Columns.Add(new GridViewColumn { Header = "账户", Width = 210, DisplayMemberBinding = Bind("Account") });
        gv.Columns.Add(new GridViewColumn { Header = "金额", Width = 130, DisplayMemberBinding = Bind("Amount") });
        gv.Columns.Add(new GridViewColumn { Header = "标注", Width = 76, DisplayMemberBinding = Bind("Tag") });
        list.View = gv;
        list.MouseDoubleClick += (_, _) => EditRow(list.SelectedItem as Row);
        list.ContextMenu = RowMenu(list);

        var inner = new StackPanel();
        inner.Children.Add(header);
        inner.Children.Add(list);
        var wrap = new Border
        {
            Child = inner,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 0, 0, 2)
        };
        wrap.SetResourceReference(Border.BorderBrushProperty, UiKeys.Divider);
        return wrap;
    }

    private ContextMenu RowMenu(ListView list)
    {
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "编辑…" };
        edit.Click += (_, _) => EditRow(list.SelectedItem as Row);
        var cancel = new MenuItem { Header = "作废 / 删除这笔…" };
        cancel.Click += (_, _) => CancelRow(list.SelectedItem as Row);
        menu.Items.Add(edit);
        menu.Items.Add(cancel);
        return menu;
    }

    private static System.Windows.Data.Binding Bind(string p) => new(p) { Mode = System.Windows.Data.BindingMode.OneWay };

    private void EditRow(Row? row)
    {
        if (row is null || row.T.Status == "cancelled")
            return;
        if (Periods.HasSealedCovering(S, row.T.Date))
        {
            MessageBox.Show(LedgerReadonlyException.Friendly(row.T.Date), "账单管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (row.T.Direction == "transfer")
            EditTransfer(row.T.Id, row.T.Date);
        else
            EditNormal(row.T.Id, row.T.Date);
    }

    private void EditNormal(long id, string date)
    {
        var e = Transactions.GetEditable(S, id);
        if (e is null)
            return;
        var dlg = new RecordDialog(S, defaultDate: DateTime.Parse(date), settings: App.Settings, edit: e,
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
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "流水·保存失败");
            MessageBox.Show($"保存失败:\n{ex.Message}", "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditTransfer(long id, string date)
    {
        var e = Transactions.GetTransfer(S, id);
        if (e is null)
            return;
        var dlg = new TransferDialog(S, defaultDate: DateTime.Parse(date), settings: App.Settings, edit: e);
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
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "流水·保存失败");
            MessageBox.Show($"保存失败:\n{ex.Message}", "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelRow(Row? row)
    {
        if (row is null || row.T.Status == "cancelled")
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
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "流水·作废流水");
            MessageBox.Show(ex.Message, "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class Row
    {
        public Row(Transactions.FlowListItem t) => T = t;
        public Transactions.FlowListItem T { get; }
        public string Time => T.Time;
        public string Name => T.Direction == "transfer" ? T.Kind : T.Name;
        public string Category => T.Direction == "transfer" ? "转账" : T.Category;
        public string Account => T.Direction == "transfer" && T.AccountTo.Length > 0
            ? $"{T.Account} → {T.AccountTo}"
            : T.Account;
        public string Amount => T.Direction == "transfer"
            ? Money.Yuan(T.AmountCents) + (T.DeltaCents != 0 ? $" (Δ{(T.DeltaCents > 0 ? "+" : "-")}{Money.Yuan(Math.Abs(T.DeltaCents))})" : "")
            : (T.Direction == "out" ? "-" : "+") + Money.Yuan(T.AmountCents);
        public string Tag => T.Status switch
        {
            "cancelled" => "已作废",
            "refunded" => "已退款",
            _ => T.Source == "calibration" ? "校准" : T.PeriodName.Length == 0 ? "未归属" : ""
        };
    }
}
