using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZhangDan.App.Views;

/// <summary>账户:列表 + 净资产合计;可新建、停用/启用、校准入口(校准/详情后续页做)。</summary>
internal sealed class AccountsPage : PageBase
{
    private LedgerSession S => App.Ledger!;
    private readonly TextBlock _summary = new() { FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(4, 0, 0, 0) };
    private readonly ListView _list = new();

    private sealed class Row
    {
        public required AccountRow A { get; init; }
        public string Name => A.Enabled ? A.Name : A.Name + "(已停用)";
        public string Type => TypeLabel(A.Type);
        public string Platform => A.Platform.Length == 0 ? "—" : A.Platform;
        public string Balance => Money.Yuan(AccountCalibration.BookCents(App.Ledger!, A.Id));
    }

    public AccountsPage()
    {
        var create = new Button { Content = "＋ 新建账户…", MinWidth = 128, Height = 34 };
        create.Click += (_, _) => CreateAccount();
        var disable = new Button { Content = "停用所选", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        disable.Click += (_, _) => Toggle(false);
        var enable = new Button { Content = "启用所选", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        enable.Click += (_, _) => Toggle(true);

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 10) };
        top.Children.Add(create);
        top.Children.Add(disable);
        top.Children.Add(enable);
        top.Children.Add(_summary);

        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "名称", Width = 200, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new GridViewColumn { Header = "类型", Width = 150, DisplayMemberBinding = Bind("Type") });
        gv.Columns.Add(new GridViewColumn { Header = "平台", Width = 110, DisplayMemberBinding = Bind("Platform") });
        gv.Columns.Add(new GridViewColumn { Header = "当前余额(派生)", Width = 140, DisplayMemberBinding = Bind("Balance") });
        _list.View = gv;
        _list.Margin = new Thickness(20, 0, 20, 12);
        _list.SelectionMode = SelectionMode.Single;
        _list.MouseDoubleClick += (_, _) => Toggle(!(Selected()?.A.Enabled ?? true));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(top, 0);
        Grid.SetRow(_list, 1);
        grid.Children.Add(top);
        grid.Children.Add(_list);
        Content = grid;
    }

    private static System.Windows.Data.Binding Bind(string p) => new(p) { Mode = System.Windows.Data.BindingMode.OneWay };

    public override void OnShown() => Reload();

    private Row? Selected() => _list.SelectedItem as Row;

    private void Reload()
    {
        var rows = new List<Row>();
        foreach (var a in Accounts.ListAll(S))
            rows.Add(new Row { A = a });
        _list.ItemsSource = rows;
        _summary.Text = $"净资产合计(启用账户):{Money.Yuan(Accounts.NetAssets(S))}";
    }

    private void Toggle(bool enable)
    {
        var row = Selected();
        if (row is null)
            return;
        if (enable)
            Accounts.Enable(S, row.A.Id);
        else
        {
            if (MessageBox.Show($"停用账户「{row.A.Name}」?\n\n它不再出现在记账/转账下拉;已记流水保留。",
                    "停用账户", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
            Accounts.Disable(S, row.A.Id);
        }
        Reload();
    }

    private void CreateAccount()
    {
        var dlg = new AccountCreateDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Accounts.Insert(S, dlg.AccountName, dlg.TypeKey, dlg.Platform, dlg.BalanceCents);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"新建账户失败:\n{ex.Message}", "账户", MessageBoxButton.OK, MessageBoxImage.Error);
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
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public string AccountName => _name.Text.Trim();
    public string TypeKey => Types[_type.SelectedIndex].Key;
    public string Platform => _platform.Text.Trim();
    public long BalanceCents { get; private set; }

    public AccountCreateDialog()
    {
        Title = "新建账户";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        foreach (var (label, _) in Types)
            _type.Items.Add(label);
        _type.SelectedIndex = 0;
        foreach (var p in Platforms)
            _platform.Items.Add(p);
        _platform.SelectedIndex = 0;

        var ok = new Button { Content = "创建", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Row("名称", _name));
        panel.Children.Add(Row("类型", _type));
        panel.Children.Add(Row("平台", _platform));
        panel.Children.Add(Row("当前余额(可选,元)", _balance));
        panel.Children.Add(_error);
        panel.Children.Add(row);
        Content = panel;
    }

    private static UIElement Row(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center };
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
