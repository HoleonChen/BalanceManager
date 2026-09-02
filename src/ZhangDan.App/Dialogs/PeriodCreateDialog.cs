using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZhangDan.App.Dialogs;

/// <summary>
/// 新建/编辑周期。建周期时可一并录入「初始收入」与「资金池」——
/// 这是周期作为记账枢纽的初始化动作(设计 §6 向导三步的精简落地)。
/// </summary>
internal sealed class PeriodCreateDialog : Window
{
    private readonly List<AccountRow> _accounts;
    private readonly List<CategoryRow> _incomeCats;

    private readonly TextBox _name = new() { Text = "生活费", Width = 300 };
    private readonly DatePicker _start = new() { Width = 300, SelectedDate = DateTime.Today };
    private readonly CheckBox _endCheck = new() { Content = "计划结束日期", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly DatePicker _end = new() { Width = 180, SelectedDate = DateTime.Today.AddDays(30) };

    private readonly CheckBox _incomeCheck = new() { Content = "初始收入(可选)", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _incomeAccount = new() { Width = 200 };
    private readonly ComboBox _incomeCat = new() { Width = 200 };
    private readonly TextBox _incomeAmount = new() { Width = 120 };
    private readonly StackPanel _incomeBody = new();

    private readonly CheckBox _poolCheck = new() { Content = "建立资金池(可选)", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _poolAccount = new() { Width = 200 };
    private readonly TextBox _budgetBox = new() { Width = 120 };
    private readonly TextBox _reserveBox = new() { Width = 120 };
    private readonly StackPanel _poolBody = new();

    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly bool _editing;

    public string PeriodName => _name.Text.Trim();
    public string StartDate => _start.SelectedDate!.Value.ToString("yyyy-MM-dd");
    public string? EndDate => _endCheck.IsChecked == true ? _end.SelectedDate?.ToString("yyyy-MM-dd") : null;

    public bool UseInitialIncome => _incomeCheck.IsChecked == true;
    public long IncomeAccountId => ((AccountRow)_incomeAccount.SelectedItem).Id;
    public long IncomeCategoryId => ((CategoryRow)_incomeCat.SelectedItem).Id;
    public long IncomeCents { get; private set; }

    public bool UsePool => _poolCheck.IsChecked == true;
    public long PoolAccountId => ((AccountRow)_poolAccount.SelectedItem).Id;
    public long PoolBudgetCents { get; private set; }
    public long PoolReserveCents { get; private set; }

    public PeriodCreateDialog(LedgerSession ledger, PeriodRow? existing = null)
    {
        _accounts = new List<AccountRow>(Accounts.ListEnabled(ledger));
        _incomeCats = new List<CategoryRow>(Categories.ListManual(ledger, income: true));
        _editing = existing is not null;

        Title = existing is null ? "新建记账周期" : "编辑周期";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _endCheck.Checked += (_, _) => _end.IsEnabled = true;
        _endCheck.Unchecked += (_, _) => _end.IsEnabled = false;
        _incomeCheck.Checked += (_, _) => ToggleBody(_incomeBody, true);
        _incomeCheck.Unchecked += (_, _) => ToggleBody(_incomeBody, false);
        _poolCheck.Checked += (_, _) => ToggleBody(_poolBody, true);
        _poolCheck.Unchecked += (_, _) => ToggleBody(_poolBody, false);

        FillCombo(_incomeAccount, _accounts);
        FillCombo(_incomeCat, _incomeCats);
        FillCombo(_poolAccount, _accounts);

        // 编辑时只改属性;初始收入/资金池属于「新建期」的初始化,编辑态不提供
        if (existing is not null)
        {
            _name.Text = existing.Name;
            _start.SelectedDate = DateTime.Parse(existing.StartDate);
            if (existing.EndDate is not null)
                _end.SelectedDate = DateTime.Parse(existing.EndDate);
            else
            {
                _endCheck.IsChecked = false;
                _end.IsEnabled = false;
            }
        }

        // 初始收入:逐字段一行
        _incomeAmount.Width = 160;
        _incomeCat.Width = 300;
        _incomeAccount.Width = 300;
        _incomeBody.Children.Add(SubField("账户", _incomeAccount));
        _incomeBody.Children.Add(SubField("收入分类", _incomeCat));
        _incomeBody.Children.Add(SubField("金额(元)", _incomeAmount));

        // 资金池:逐字段一行
        _budgetBox.Width = 160;
        _reserveBox.Width = 160;
        _poolAccount.Width = 300;
        _poolBody.Children.Add(SubField("池账户", _poolAccount));
        _poolBody.Children.Add(SubField("预算(元)", _budgetBox));
        _poolBody.Children.Add(SubField("保留(元)", _reserveBox));

        var ok = new Button { Content = existing is null ? "建立" : "保存", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Field("名称", _name));
        panel.Children.Add(Field("开始日期", _start));
        panel.Children.Add(Field("结束", EndRow()));
        if (existing is null)
        {
            panel.Children.Add(_incomeCheck);
            panel.Children.Add(_incomeBody);
            panel.Children.Add(_poolCheck);
            panel.Children.Add(_poolBody);
        }
        panel.Children.Add(_error);
        panel.Children.Add(row);
        Content = panel;

        _incomeCheck.IsChecked = false;
        _poolCheck.IsChecked = false;
        ToggleBody(_incomeBody, false);
        ToggleBody(_poolBody, false);
        _incomeAmount.Text = "0";
        _budgetBox.Text = "0";
        _reserveBox.Text = "0";
        _incomeBody.IsEnabled = _incomeCheck.IsChecked == true;
    }

    private static void FillCombo(ComboBox box, System.Collections.IList items)
    {
        box.ItemsSource = items;
        box.DisplayMemberPath = "Name";
        if (items.Count > 0)
            box.SelectedIndex = 0;
    }

    private static void ToggleBody(StackPanel body, bool on) => body.IsEnabled = on;

    private UIElement EndRow()
    {
        var row = new DockPanel();
        DockPanel.SetDock(_endCheck, Dock.Left);
        row.Children.Add(_endCheck);
        row.Children.Add(_end);
        return row;
    }

    private static UIElement Field(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(text, Dock.Left);
        d.Children.Add(text);
        d.Children.Add(input);
        return d;
    }

    /// <summary>分组下的子字段:名称 + 单控件,带左缩进,避免多控件堆一行溢出。</summary>
    private static UIElement SubField(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        var d = new DockPanel { Margin = new Thickness(24, 2, 0, 2) };
        DockPanel.SetDock(text, Dock.Left);
        d.Children.Add(text);
        d.Children.Add(input);
        return d;
    }

    private static bool ParseMoney(string text, out decimal v) =>
        decimal.TryParse(text.Trim(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out v)
        || decimal.TryParse(text.Trim(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.CurrentCulture, out v);

    private void Accept()
    {
        if (PeriodName.Length == 0) { _error.Text = "请填写周期名称。"; return; }
        if (_start.SelectedDate is null || (_endCheck.IsChecked == true && _end.SelectedDate is null)) { _error.Text = "请选择完整起止日期。"; return; }
        if (_endCheck.IsChecked == true && _end.SelectedDate!.Value.Date < _start.SelectedDate!.Value.Date) { _error.Text = "结束日期不能早于开始日期。"; return; }

        IncomeCents = 0;
        if (UseInitialIncome)
        {
            if (_incomeAccount.SelectedItem is not AccountRow || _incomeCat.SelectedItem is not CategoryRow) { _error.Text = "初始收入请先建账户与收入分类(分类页新建收入分类)。"; return; }
            if (!ParseMoney(_incomeAmount.Text, out var amt) || amt <= 0) { _error.Text = "初始收入金额请填大于 0 的数字。"; return; }
            IncomeCents = Money.ToCents(amt);
        }

        PoolBudgetCents = 0; PoolReserveCents = 0;
        if (UsePool)
        {
            if (_poolAccount.SelectedItem is not AccountRow) { _error.Text = "资金池需选择账户。"; return; }
            if (!ParseMoney(_budgetBox.Text, out var b) || b <= 0) { _error.Text = "资金池预算请填大于 0 的数字。"; return; }
            ParseMoney(_reserveBox.Text, out var r);
            PoolBudgetCents = Money.ToCents(b);
            PoolReserveCents = Money.ToCents(r);
        }

        DialogResult = true;
    }
}
