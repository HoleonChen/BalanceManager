using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZhangDan.App.Dialogs;

using WListView = Wpf.Ui.Controls.ListView;
using WGridView = Wpf.Ui.Controls.GridView;
using WGridViewColumn = Wpf.Ui.Controls.GridViewColumn;
namespace ZhangDan.App.Views;

/// <summary>账户:列表 + 净资产合计 + 选中账户详情(余额构成/本周期变动/校准历史);可新建/停用/校准。</summary>
internal sealed class AccountsPage : PageBase
{
    private LedgerSession S => App.Ledger!;

    /// <summary>跳流水页并预置「只看该账户」(由 MainWindow 注入)。</summary>
    public Action<long>? ViewAccountFlows { get; set; }

    private readonly TextBlock _summary = new() { FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(4, 0, 0, 0) };
    private readonly WListView _list = new();
    private readonly Border _detail = new();
    private readonly StackPanel _detailBody = new();
    private long? _selectedId;

    private sealed class Row
    {
        public required AccountRow A { get; init; }
        public string Name => A.Name;
        public string Type => TypeLabel(A.Type);
        public string Platform => A.Platform.Length == 0 ? "—" : A.Platform;
        public string Status => A.Enabled ? "启用" : "停用";
        public string Balance => Money.Yuan(AccountCalibration.BookCents(App.Ledger!, A.Id));
    }

    public AccountsPage()
    {
        var create = new Button { Content = "＋ 新建账户…", MinWidth = 128, Height = 34 };
        create.Click += (_, _) => CreateAccount();
        var edit = new Button { Content = "编辑…", MinWidth = 76, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        edit.Click += (_, _) => EditSelected();
        var disable = new Button { Content = "停用所选", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        disable.Click += (_, _) => Toggle(false);
        var enable = new Button { Content = "启用所选", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        enable.Click += (_, _) => Toggle(true);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(create);
        buttons.Children.Add(edit);
        buttons.Children.Add(disable);
        buttons.Children.Add(enable);

        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        _summary.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid { Margin = new Thickness(20, 16, 20, 10) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var summaryHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        summaryHost.Children.Add(_summary);
        Grid.SetColumn(buttons, 0);
        Grid.SetColumn(summaryHost, 2);
        top.Children.Add(buttons);
        top.Children.Add(summaryHost);

        var menu = new ContextMenu();
        var mEdit = new MenuItem { Header = "编辑账户…" }; mEdit.Click += (_, _) => EditSelected();
        var mCalib = new MenuItem { Header = "校准余额…" };
        mCalib.Click += (_, _) => { var rr = Selected(); if (rr is not null) Calibrate(rr.A.Id); };
        var mDisable = new MenuItem { Header = "停用" }; mDisable.Click += (_, _) => Toggle(false);
        var mEnable = new MenuItem { Header = "启用" }; mEnable.Click += (_, _) => Toggle(true);
        menu.Items.Add(mEdit);
        menu.Items.Add(mCalib);
        menu.Items.Add(mDisable);
        menu.Items.Add(mEnable);
        _list.ContextMenu = menu;

        var gv = new WGridView();
        gv.Columns.Add(new WGridViewColumn { Header = "名称", Width = 200, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new WGridViewColumn { Header = "类型", Width = 150, DisplayMemberBinding = Bind("Type") });
        gv.Columns.Add(new WGridViewColumn { Header = "状态", Width = 90, DisplayMemberBinding = Bind("Status") });
        gv.Columns.Add(new WGridViewColumn { Header = "平台", Width = 110, DisplayMemberBinding = Bind("Platform") });
        gv.Columns.Add(new WGridViewColumn { Header = "当前余额(派生)", Width = 140, DisplayMemberBinding = Bind("Balance") });
        _list.View = gv;
        _list.Margin = new Thickness(20, 0, 20, 12);
        _list.SelectionMode = SelectionMode.Extended;
        _list.MouseDoubleClick += (_, e) => RowDoubleToggle(e);

        _list.SelectionChanged += (_, _) => ShowDetail(Selected());

        _detail.SetResourceReference(Border.BorderBrushProperty, UiKeys.Divider);
        _detail.BorderThickness = new Thickness(0, 1, 0, 0);
        _detail.Padding = new Thickness(20, 10, 20, 14);
        _detail.Child = new ScrollViewer
        {
            MaxHeight = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _detailBody
        };
        _detail.Visibility = Visibility.Collapsed;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(top, 0);
        Grid.SetRow(_list, 1);
        Grid.SetRow(_detail, 2);
        grid.Children.Add(top);
        grid.Children.Add(_list);
        grid.Children.Add(_detail);
        Content = grid;
    }

    private static System.Windows.Data.Binding Bind(string p) => new(p) { Mode = System.Windows.Data.BindingMode.OneWay };

    public override void OnShown() => Reload();

    private Row? Selected() => _list.SelectedItem as Row;

    private void Reload()
    {
        var keepId = _selectedId ?? Selected()?.A.Id;
        var rows = new List<Row>();
        foreach (var a in Accounts.ListAll(S))
            rows.Add(new Row { A = a });
        _list.ItemsSource = rows;
        if (keepId is long id)
        {
            foreach (var r in rows)
            {
                if (r.A.Id == id)
                {
                    _list.SelectedItem = r;
                    break;
                }
            }
        }
        _summary.Text = $"净资产合计(启用账户):{Money.Yuan(Accounts.NetAssets(S))}";
    }

    /// <summary>批量启用/停用「所选」账户;停用需一次确认。无选中则空操作。</summary>
    private void Toggle(bool enable)
    {
        var rows = _list.SelectedItems.OfType<Row>().ToList();
        if (rows.Count == 0)
            return;
        if (!enable)
        {
            var preview = string.Join("、", rows.Take(3).Select(r => r.A.Name));
            if (rows.Count > 3) preview += $" 等 {rows.Count} 个";
            if (MessageBox.Show($"停用所选 {rows.Count} 个账户: {preview}?\n\n停用后不再出现在记账/转账下拉;已记流水保留。",
                    "停用账户", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
        }
        foreach (var r in rows)
        {
            if (enable) Accounts.Enable(S, r.A.Id);
            else Accounts.Disable(S, r.A.Id);
        }
        Reload();
    }

    /// <summary>双击某一账户行 → 只切换该账户(与批量选择互不影响)。</summary>
    private void RowDoubleToggle(MouseButtonEventArgs e)
    {
        if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { DataContext: Row r })
            ToggleEnable(r.A.Id, !r.A.Enabled);
    }

    private static T? FindAncestor<T>(DependencyObject? o) where T : DependencyObject
    {
        while (o is not null)
        {
            if (o is T t)
                return t;
            o = VisualTreeHelper.GetParent(o);
        }
        return null;
    }

    private void ToggleEnable(long id, bool enable)
    {
        if (enable)
        {
            Accounts.Enable(S, id);
        }
        else
        {
            if (MessageBox.Show($"停用账户「{AccountNameOf(id)}」?\n\n它不再出现在记账/转账下拉;已记流水保留。",
                    "停用账户", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
            Accounts.Disable(S, id);
        }
        Reload();
    }

    private string AccountNameOf(long id)
    {
        using var cmd = S.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM accounts WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() as string ?? "(账户不存在)";
    }

    private Row? FindRow(long id)
    {
        if (_list.ItemsSource is List<Row> rows)
        {
            foreach (var r in rows)
            {
                if (r.A.Id == id)
                    return r;
            }
        }
        return null;
    }

    private void EditAccount(long id)
    {
        var r = FindRow(id);
        if (r is null)
            return;
        var dlg = new AccountCreateDialog(existing: r.A) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Accounts.UpdateInfo(S, r.A.Id, dlg.AccountName, dlg.TypeKey, dlg.Platform);
            Reload();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "账户·编辑保存");
            MessageBox.Show($"保存失败:\n{ex.Message}", "账户", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Calibrate(long id)
    {
        var r = FindRow(id);
        if (r is null)
            return;
        var dlg = new CalibrateDialog(S, r.A) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            var diff = AccountCalibration.Apply(S, id, dlg.ActualCents, dlg.Method, dlg.Note);
            if (dlg.Method == CalibMethod.RealDetails && diff != 0)
            {
                // 「补记真实明细」不会自动写流水:必须把漏记的真实收支/转账记进账本,账面才对齐;
                // 不补则差额永远挂账,成为坏账。给可行动引导:确认后直接打开「记一笔」。
                var go = MessageBox.Show(
                    $"本次仅记录了审计,不会自动产生流水。\n\n账面与实际仍差 {Money.Yuan(Math.Abs(diff))}。" +
                    (diff > 0
                        ? "\n实际比账面高——通常是漏记了收入/转入,补记方向多为收入。"
                        : "\n账面比实际高——通常是漏记了支出/转出,补记方向多为支出。") +
                    "\n\n是否现在就打开「记一笔」补记漏记流水?",
                    "补记真实明细", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (go == MessageBoxResult.Yes)
                    OpenSupplementalRecord(id, Math.Abs(diff));
            }
            Reload();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "账户·校准余额");
            MessageBox.Show($"校准失败:\n{ex.Message}", "校准余额", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>补记引导:预置账户与差额金额,打开记一笔把漏记的真实流水入账。</summary>
    private void OpenSupplementalRecord(long accountId, long hintCents)
    {
        var dlg = new RecordDialog(S, defaultDate: DateTime.Today, settings: App.Settings,
            presetAccountId: accountId, presetAmountCents: hintCents)
        { Owner = Window.GetWindow(this) };
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
            MessageBox.Show("补记已入账。若仍有差额,可再次「校准余额」核对。",
                "补记真实明细", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "账户·补记入账");
            MessageBox.Show($"补记保存失败:\n{ex.Message}", "补记真实明细", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>选中账户 → 下方面板:余额构成 + 本周期变动 + 校准历史 + 操作按钮。</summary>
    private void ShowDetail(Row? row)
    {
        if (row is null)
        {
            _selectedId = null;
            _detail.Visibility = Visibility.Collapsed;
            _detailBody.Children.Clear();
            return;
        }
        _selectedId = row.A.Id;
        var body = _detailBody;
        body.Children.Clear();
        var s = S;
        var id = row.A.Id;

        var name = new TextBlock { Text = row.A.Name, FontSize = 16, FontWeight = FontWeights.SemiBold };
        name.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Accent);
        var meta = new TextBlock
        {
            Text = $"{TypeLabel(row.A.Type)} · 平台 {row.A.Platform}",
            Margin = new Thickness(10, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        var status = new TextBlock
        {
            Text = row.A.Enabled ? "启用中" : "已停用(不计净资产)",
            Margin = new Thickness(12, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        status.SetResourceReference(TextBlock.ForegroundProperty,
            row.A.Enabled ? UiKeys.Success : UiKeys.TextSecondary);
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(name);
        head.Children.Add(meta);
        head.Children.Add(status);
        body.Children.Add(head);

        var book = AccountCalibration.BookCents(s, id);
        var (baseCents, baseDate) = Accounts.BaseOf(s, id);
        body.Children.Add(new TextBlock
        {
            Text = $"当前账面(派生):{Money.Yuan(book)}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 2)
        });
        var baseLine = new TextBlock
        {
            Text = $"基准余额:{Money.Yuan(baseCents)} · 基准日:{(baseDate is null ? "(自建账起算)" : baseDate)}"
        };
        baseLine.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        body.Children.Add(baseLine);

        body.Children.Add(Rule());

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(s, today);
        body.Children.Add(Section(p is null ? "本周期变动" : $"本周期变动 · {p.Name}({Short(p.StartDate)}~{(p.EndDate is null ? "长期" : Short(p.EndDate))})"));
        if (p is not null)
        {
            var mv = Accounts.MovementBetween(s, id, p.StartDate, p.EndDate ?? "9999-12-31");
            body.Children.Add(Line($"收入 {Money.Yuan(mv.InCents)} · 支出 {Money.Yuan(mv.OutCents)} · 转入 {Money.Yuan(mv.TransferInCents)} · 转出 {Money.Yuan(mv.TransferOutCents)}"));
            body.Children.Add(Line($"净变动 {Money.Yuan(mv.NetCents)}", gray: true));
        }
        else
        {
            body.Children.Add(Line("当前无进行中的周期。", gray: true));
        }

        var all = Accounts.MovementBetween(s, id, baseDate ?? "0000-01-01", "9999-12-31");
        body.Children.Add(Section("自基准日累计构成(账面 = 基准 + 净变动)"));
        body.Children.Add(Line($"收入 {Money.Yuan(all.InCents)} · 支出 {Money.Yuan(all.OutCents)} · 转入 {Money.Yuan(all.TransferInCents)} · 转出 {Money.Yuan(all.TransferOutCents)}"));
        body.Children.Add(Line($"累计净变动 {Money.Yuan(all.NetCents)}", gray: true));

        var bEdit = new Button { Content = "编辑账户…", MinWidth = 112, Height = 30, Margin = new Thickness(0, 10, 10, 0) };
        bEdit.Click += (_, _) => EditAccount(id);
        var bCalib = new Button { Content = "校准余额…", MinWidth = 112, Height = 30, Margin = new Thickness(0, 10, 10, 0) };
        bCalib.Click += (_, _) => Calibrate(id);
        var bTog = new Button { Content = row.A.Enabled ? "停用" : "启用", MinWidth = 92, Height = 30, Margin = new Thickness(0, 10, 0, 0) };
        bTog.Click += (_, _) => ToggleEnable(id, !row.A.Enabled);
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        btns.Children.Add(bEdit);
        btns.Children.Add(bCalib);
        btns.Children.Add(bTog);
        if (ViewAccountFlows is not null)
        {
            var bFlow = new Button { Content = "该账户流水 →", MinWidth = 116, Height = 30, Margin = new Thickness(0, 10, 0, 0) };
            bFlow.Click += (_, _) => ViewAccountFlows(id);
            btns.Children.Add(bFlow);
        }
        body.Children.Add(btns);

        body.Children.Add(Rule());

        var hist = AccountCalibration.History(s, id);
        body.Children.Add(Section($"校准历史({hist.Count} 次)"));
        body.Children.Add(hist.Count == 0 ? Line("尚无校准记录。账面与真实不符时,点「校准余额」对齐。", gray: true) : BuildHistory(hist));

        _detail.Visibility = Visibility.Visible;
    }

    private static UIElement Rule()
    {
        var rule = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
        rule.SetResourceReference(Border.BackgroundProperty, UiKeys.Divider);
        return rule;
    }

    private static TextBlock Section(string text)
    {
        var t = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 2)
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSection);
        return t;
    }

    private static TextBlock Line(string text, bool gray = false)
    {
        var t = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, gray ? UiKeys.TextSecondary : UiKeys.TextPrimary);
        return t;
    }

    private static string Short(string iso)
    {
        var p = iso.Split('-');
        return $"{p[0]}/{int.Parse(p[1])}/{int.Parse(p[2])}";
    }

    /// <summary>校准历史表(只读)。</summary>
    private static UIElement BuildHistory(IReadOnlyList<CalibrationEntry> entries)
    {
        var lv = new WListView { MaxHeight = 150 };
        var gv = new WGridView();
        gv.Columns.Add(new WGridViewColumn { Header = "时间", Width = 126, DisplayMemberBinding = Bind("When") });
        gv.Columns.Add(new WGridViewColumn { Header = "账面", Width = 78, DisplayMemberBinding = Bind("Book") });
        gv.Columns.Add(new WGridViewColumn { Header = "实际", Width = 78, DisplayMemberBinding = Bind("Actual") });
        gv.Columns.Add(new WGridViewColumn { Header = "差额", Width = 88, DisplayMemberBinding = Bind("Diff") });
        gv.Columns.Add(new WGridViewColumn { Header = "方式", Width = 120, DisplayMemberBinding = Bind("Method") });
        gv.Columns.Add(new WGridViewColumn { Header = "备注", Width = 200, DisplayMemberBinding = Bind("Note") });
        lv.View = gv;
        var rows = new List<HistoryRow>();
        foreach (var e in entries)
            rows.Add(new HistoryRow(e));
        lv.ItemsSource = rows;
        return lv;
    }

    private sealed class HistoryRow
    {
        public HistoryRow(CalibrationEntry e) => E = e;
        private readonly CalibrationEntry E;
        public string When => E.RecordedAt;
        public string Book => Money.Yuan(E.BookCents);
        public string Actual => Money.Yuan(E.ActualCents);
        public string Diff => (E.DiffCents > 0 ? "+" : "") + Money.Yuan(E.DiffCents);
        public string Method => CalibMethod.Label(E.Method);
        public string Note => E.Note ?? "";
    }

    private void CreateAccount()
    {
        var dlg = new AccountCreateDialog(existing: null) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Accounts.Insert(S, dlg.AccountName, dlg.TypeKey, dlg.Platform, dlg.BalanceCents);
            Reload();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "账户·新建账户");
            MessageBox.Show($"新建账户失败:\n{ex.Message}", "账户", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditSelected()
    {
        var r = Selected();
        if (r is null)
            return;
        var dlg = new AccountCreateDialog(existing: r.A) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Accounts.UpdateInfo(S, r.A.Id, dlg.AccountName, dlg.TypeKey, dlg.Platform);
            Reload();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "账户·编辑保存");
            MessageBox.Show($"保存失败:\n{ex.Message}", "账户", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string TypeLabel(string key) => key switch
    {
        "wallet" => "钱包(零钱/余额)",
        "money_fund" => "货币基金(零钱通/余额宝)",
        "bank" => "银行卡",
        "cash" => "现金",
        "fixed_deposit" => "定存(整存整取)",
        "fund" => "基金",
        "prepaid" => "储值卡(水卡等)",
        _ => key
    };
}

/// <summary>新建账户小窗。</summary>
internal sealed class AccountCreateDialog : Window
{
    private static readonly (string Label, string Key)[] Types =
    {
        ("钱包(零钱/余额)", "wallet"), ("货币基金(零钱通/余额宝)", "money_fund"),
        ("银行卡", "bank"), ("现金", "cash"), ("定存(整存整取)", "fixed_deposit"),
        ("基金", "fund"), ("储值卡(水卡等)", "prepaid")
    };
    private static readonly string[] Platforms = { "微信", "支付宝", "银行", "投资", "现金", "储值卡" };

    private readonly TextBox _name = new() { Width = 300 };
    private readonly ComboBox _type = new() { Width = 300 };
    private readonly ComboBox _platform = new() { Width = 300, IsEditable = true };
    private readonly TextBox _balance = new() { Width = 300 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };

    public string AccountName => _name.Text.Trim();
    public string TypeKey => Types[_type.SelectedIndex].Key;
    public string Platform => _platform.Text.Trim();
    public long BalanceCents { get; private set; }

    public AccountCreateDialog(AccountRow? existing = null)
    {
        Title = existing is null ? "新建账户" : "编辑账户";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        _error.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);

        foreach (var (label, _) in Types)
            _type.Items.Add(label);
        _type.SelectedIndex = 0;
        foreach (var p in Platforms)
            _platform.Items.Add(p);
        _platform.SelectedIndex = 0;

        if (existing is not null)
        {
            _name.Text = existing.Name;
            _platform.Text = existing.Platform;
            for (int i = 0; i < Types.Length; i++)
            {
                if (Types[i].Key == existing.Type)
                {
                    _type.SelectedIndex = i;
                    break;
                }
            }
        }

        var ok = new Button { Content = existing is null ? "创建" : "保存", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Row("名称", _name));
        panel.Children.Add(Row("类型", _type));
        panel.Children.Add(Row("平台", _platform));
        if (existing is null)
            panel.Children.Add(Row("当前余额(可选,元)", _balance));   // 余额只走「校准」,编辑时不显示
        panel.Children.Add(_error);
        panel.Children.Add(row);
        Content = panel;
    }

    private static UIElement Row(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center };
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(text, Dock.Left);
        d.Children.Add(text);
        d.Children.Add(input);
        return d;
    }

    private void Accept()
    {
        if (AccountName.Length == 0)
        {
            _error.Text = "请填写账户名称。";
            return;
        }
        BalanceCents = 0;
        var t = _balance.Text.Trim();
        if (t.Length > 0)
        {
            if (!decimal.TryParse(t, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                _error.Text = "余额请填数字(元)。";
                return;
            }
            BalanceCents = Money.ToCents(v);
        }
        DialogResult = true;
    }
}

/// <summary>校准余额对话框:当前账面 / 填实际余额 / 实时差额,三种处理方式三选一(设计 §3.2)。</summary>
internal sealed class CalibrateDialog : Window
{
    private static readonly (string Key, string Title, string Desc)[] Methods =
    {
        (CalibMethod.Adjustment, "记调整流水(推荐)",
            "差额自动生成一笔「差额调整」流水(实际&gt;账面记收入 / 实际&lt;账面记支出);账面随即对齐实际,留档可查。"),
        (CalibMethod.RealDetails, "补记真实明细",
            "差额来自漏记的真实流水:补记后账面自对齐,本次仅记录审计日志。"),
        (CalibMethod.BaseOnly, "仅更新基准",
            "不写流水,直接把基准余额平移对齐(适合历史账对不上、不想留调整痕迹时)。"),
    };

    private readonly List<(RadioButton Rb, string Key)> _radios = new();
    private readonly TextBlock _diff = new() { Margin = new Thickness(2, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _note = new() { Width = 300, Height = 64, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _actual = new() { Width = 180 };
    private readonly long _bookCents;

    public long ActualCents { get; private set; }
    public string Method
    {
        get
        {
            foreach (var (rb, key) in _radios)
            {
                if (rb.IsChecked == true)
                    return key;
            }
            return CalibMethod.Adjustment;
        }
    }
    public string Note => _note.Text.Trim();

    public CalibrateDialog(LedgerSession s, AccountRow account)
    {
        _bookCents = AccountCalibration.BookCents(s, account.Id);
        Title = $"校准余额 · {account.Name}";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = $"当前账面(派生):{Money.Yuan(_bookCents)}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _actual.Text = (_bookCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        _actual.TextChanged += (_, _) => RefreshDiff();

        body.Children.Add(Field("实际余额(元)", _actual));
        RefreshDiff();
        body.Children.Add(_diff);

        body.Children.Add(Rule());

        body.Children.Add(new TextBlock { Text = "如何处理差额", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        bool first = true;
        foreach (var (key, title, desc) in Methods)
        {
            var rb = new RadioButton
            {
                Content = title,
                GroupName = "calibMethod",
                IsChecked = first,
                FontWeight = first ? FontWeights.SemiBold : FontWeights.Normal
            };
            first = false;
            _radios.Add((rb, key));
            body.Children.Add(rb);
            var descText = new TextBlock
            {
                Text = desc,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(22, 0, 0, 6)
            };
            descText.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
            body.Children.Add(descText);
        }

        body.Children.Add(new TextBlock { Text = "备注(可空,写入校准审计)", Margin = new Thickness(0, 4, 0, 4) });
        body.Children.Add(_note);

        var ok = new Button { Content = "校准", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(body);
        panel.Children.Add(row);
        Content = panel;
    }

    private void RefreshDiff()
    {
        if (!TryParse(_actual.Text, out var yuan))
        {
            _diff.Text = "实际余额请填数字(元)。";
            _diff.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);
            return;
        }
        var diff = Money.ToCents(yuan) - _bookCents;
        _diff.Text = diff == 0
            ? "差额:0 —— 账面与实际一致,无需调整。"
            : $"差额:{Money.Yuan(diff)}(实际 − 账面)。{(diff > 0 ? "账面少记了钱" : "账面多记了钱")}";
        _diff.SetResourceReference(TextBlock.ForegroundProperty, diff == 0 ? UiKeys.Success : UiKeys.Warn);
    }

    private static bool TryParse(string text, out decimal v)
        => decimal.TryParse(text.Trim(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out v)
        || decimal.TryParse(text.Trim(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.CurrentCulture, out v);

    private void Accept()
    {
        if (!TryParse(_actual.Text, out var yuan))
        {
            _diff.Text = "实际余额请填数字(元)。";
            _diff.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);
            return;
        }
        ActualCents = Money.ToCents(yuan);
        DialogResult = true;
    }

    private static UIElement Field(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(text, Dock.Left);
        d.Children.Add(text);
        d.Children.Add(input);
        return d;
    }

    private static UIElement Rule()
    {
        var rule = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 8) };
        rule.SetResourceReference(Border.BackgroundProperty, UiKeys.Divider);
        return rule;
    }
}
