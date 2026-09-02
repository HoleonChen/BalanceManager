using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace ZhangDan.App.Dialogs;

/// <summary>记转账:转出账户 → 转入账户,本金与实际到账之差自动得浮动(理财+/手续费-)。</summary>
internal sealed class TransferDialog : Window
{
    private static readonly string[] Kinds = { "互转", "充值", "提现", "理财结算", "存取" };

    private readonly List<AccountRow> _accounts;
    private readonly ComboBox _fromBox = new() { Width = 300 };
    private readonly ComboBox _toBox = new() { Width = 300 };
    private readonly DatePicker _datePicker = new() { Width = 300 };
    private readonly TextBox _principalBox = new() { Width = 300 };
    private readonly TextBox _deltaBox = new() { Text = "0", Width = 300 };
    private readonly ComboBox _kindBox = new() { Width = 300 };
    private readonly TextBox _noteBox = new() { Width = 300 };
    private readonly TextBlock _actualLabel = new() { Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) };
    private readonly CheckBox _poolCheck = new() { Content = "计入资金池(转出池账户时勾选)", IsChecked = false };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public string DateStr => _datePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
    public long FromAccountId => ((AccountRow)_fromBox.SelectedItem).Id;
    public long ToAccountId => ((AccountRow)_toBox.SelectedItem).Id;
    public long PrincipalCents { get; private set; }
    public long DeltaCents { get; private set; }
    public string Kind => _kindBox.Text;
    public string Note => _noteBox.Text.Trim();
    public bool InPool => _poolCheck.IsChecked == true;

    public TransferDialog(LedgerSession ledger, DateTime defaultDate, AppSettings settings, TransferEditable? edit = null)
    {
        _accounts = new List<AccountRow>(Accounts.ListEnabled(ledger));
        Title = edit is null ? "记转账" : "编辑转账";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _fromBox.ItemsSource = _accounts;
        _toBox.ItemsSource = _accounts;
        _fromBox.DisplayMemberPath = "Name";
        _toBox.DisplayMemberPath = "Name";
        foreach (var k in Kinds)
            _kindBox.Items.Add(k);
        _kindBox.SelectedIndex = 0;
        if (_accounts.Count > 0)
        {
            _fromBox.SelectedIndex = 0;
            _toBox.SelectedIndex = _accounts.Count > 1 ? 1 : 0;
        }

        _principalBox.TextChanged += (_, _) => RefreshActual();
        _deltaBox.TextChanged += (_, _) => RefreshActual();

        if (edit is null)
        {
            _datePicker.SelectedDate = defaultDate;
        }
        else
        {
            _datePicker.SelectedDate = DateTime.Parse(edit.Date);
            _datePicker.IsEnabled = false; // 日期与周期归属不改
            Select(_fromBox, edit.FromAccountId);
            Select(_toBox, edit.ToAccountId);
            _principalBox.Text = (edit.PrincipalCents / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            _deltaBox.Text = (edit.DeltaCents / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            _kindBox.Text = string.IsNullOrEmpty(edit.Kind) ? "互转" : edit.Kind;
            _noteBox.Text = edit.Note;
            _poolCheck.IsChecked = edit.InPool;
        }

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(FieldRow("转出账户", _fromBox));
        panel.Children.Add(FieldRow("转入账户", _toBox));
        panel.Children.Add(FieldRow("日期", _datePicker));
        panel.Children.Add(FieldRow("转出本金(元)", _principalBox));
        panel.Children.Add(FieldRow("浮动Δ(元;收益记+,手续费记-)", _deltaBox, labelWidth: 230));
        panel.Children.Add(FieldRow("", _actualLabel));
        panel.Children.Add(FieldRow("类别", _kindBox));
        panel.Children.Add(FieldRow("备注", _noteBox));
        panel.Children.Add(FieldRow("", _poolCheck));
        RefreshActual();

        var ok = new Button { Content = "保存", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => TryAccept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        _error.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_error);
        panel.Children.Add(row);
        Content = panel;
    }

    private void Select(ComboBox box, long id)
    {
        for (int i = 0; i < _accounts.Count; i++)
        {
            if (_accounts[i].Id == id)
            {
                box.SelectedIndex = i;
                return;
            }
        }
    }

    private static UIElement FieldRow(string label, UIElement input, int labelWidth = 140)
    {
        var text = new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center };
        var panel = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(text, Dock.Left);
        panel.Children.Add(text);
        panel.Children.Add(input);
        return panel;
    }

    private static bool ParseMoney(string text, out decimal yuan)
    {
        var t = text.Trim();
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out yuan)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out yuan);
    }

    private void RefreshActual()
    {
        if (ParseMoney(_principalBox.Text, out var p) && ParseMoney(_deltaBox.Text, out var d))
            _actualLabel.Text = $"→ 实际到账 ≈ {Money.Yuan(Money.ToCents(p) + Money.ToCents(d))}";
        else
            _actualLabel.Text = "";
    }

    private void TryAccept()
    {
        if (_datePicker.SelectedDate is null)
        {
            _error.Text = "请选择日期。";
            return;
        }
        if (_fromBox.SelectedItem is not AccountRow a || _toBox.SelectedItem is not AccountRow b)
        {
            _error.Text = "请选择转出与转入账户。";
            return;
        }
        if (a.Id == b.Id)
        {
            _error.Text = "转出与转入不能是同一账户。";
            return;
        }
        if (!ParseMoney(_principalBox.Text, out var principal) || principal <= 0)
        {
            _error.Text = "转出本金请填大于 0 的数字(元)。";
            return;
        }
        if (_deltaBox.Text.Trim().Length > 0 && !ParseMoney(_deltaBox.Text, out _))
        {
            _error.Text = "浮动 Δ 请填数字(元,可负)。";
            return;
        }
        ParseMoney(_deltaBox.Text, out var delta);

        PrincipalCents = Money.ToCents(principal);
        DeltaCents = Money.ToCents(delta);
        DialogResult = true;
    }
}
