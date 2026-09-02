using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace ZhangDan.App.Dialogs;

/// <summary>记一笔(支出/收入):方向/日期/账户/分类/金额/名称/渠道/备注/入池。可新建或就地编辑。</summary>
internal sealed class RecordDialog : Window
{
    private readonly LedgerSession _ledger;
    private readonly List<AccountRow> _accounts;
    private readonly List<CategoryRow> _expenseCats;
    private readonly List<CategoryRow> _incomeCats;

    private readonly RadioButton _outRadio = new() { Content = "支出", IsChecked = true };
    private readonly RadioButton _inRadio = new() { Content = "收入", Margin = new Thickness(14, 0, 0, 0) };
    private readonly ComboBox _categoryBox = new() { Width = 300 };
    private readonly ComboBox _accountBox = new() { Width = 300 };
    private readonly DatePicker _datePicker = new() { Width = 300 };
    private readonly TextBox _amountBox = new() { Width = 300 };
    private readonly TextBox _nameBox = new() { Width = 300 };
    private readonly ComboBox _channelBox = new() { Width = 300, IsEditable = true };
    private readonly TextBox _noteBox = new() { Width = 300 };
    private readonly CheckBox _poolCheck = new() { Content = "计入资金池", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    private readonly bool _editing;
    private bool _income;

    public string DateStr => _datePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
    public string Direction => _income ? "in" : "out";
    public long AccountId => ((AccountRow)_accountBox.SelectedItem).Id;
    public long? CategoryId => _categoryBox.SelectedItem is CategoryRow c ? c.Id : null;
    public long AmountCents { get; private set; }
    public string TxnName => _nameBox.Text.Trim();
    public string Channel => _channelBox.Text.Trim();
    public string Note => _noteBox.Text.Trim();
    public bool InPool => _poolCheck.IsChecked == true;

    public RecordDialog(LedgerSession ledger, DateTime defaultDate, AppSettings settings, TxnEditable? edit = null)
    {
        _ledger = ledger;
        _accounts = new List<AccountRow>(Accounts.ListEnabled(ledger));
        _expenseCats = new List<CategoryRow>(Categories.ListManual(ledger, income: false));
        _incomeCats = new List<CategoryRow>(Categories.ListManual(ledger, income: true));
        _editing = edit is not null;

        Title = edit is null ? "记一笔" : "编辑流水";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _channelBox.Items.Add("实体");
        _channelBox.Items.Add("网购");
        _channelBox.Items.Add("其他");
        _accountBox.ItemsSource = _accounts;
        _accountBox.DisplayMemberPath = "Name";
        if (_accounts.Count > 0)
            _accountBox.SelectedIndex = 0;

        _outRadio.Checked += (_, _) => ApplyDirection();
        _inRadio.Checked += (_, _) => ApplyDirection();

        var directionRow = new StackPanel { Orientation = Orientation.Horizontal };
        directionRow.Children.Add(_outRadio);
        directionRow.Children.Add(_inRadio);

        if (edit is null)
        {
            _datePicker.SelectedDate = defaultDate;
        }
        else
        {
            _datePicker.SelectedDate = DateTime.Parse(edit.Date);
            _datePicker.IsEnabled = false; // 日期与周期归属不改
            _inRadio.IsChecked = edit.Direction == "in";
            _outRadio.IsChecked = edit.Direction == "out";
            _amountBox.Text = (edit.AmountCents / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            _nameBox.Text = edit.Name;
            _channelBox.Text = edit.Channel;
            _noteBox.Text = edit.Note;
            _poolCheck.IsChecked = edit.InPool;
        }

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(FieldRow("类型", directionRow));
        panel.Children.Add(FieldRow("日期", _datePicker));
        panel.Children.Add(FieldRow("账户", _accountBox));
        panel.Children.Add(FieldRow("分类", _categoryBox));
        panel.Children.Add(FieldRow("金额(元)", _amountBox));
        panel.Children.Add(FieldRow("名称", _nameBox));
        panel.Children.Add(FieldRow("渠道", _channelBox));
        panel.Children.Add(FieldRow("备注", _noteBox));
        panel.Children.Add(FieldRow("", _poolCheck));

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
        ApplyDirection();
    }

    private void ApplyDirection()
    {
        _income = _inRadio.IsChecked == true;
        var cats = _income ? _incomeCats : _expenseCats;
        _categoryBox.ItemsSource = null;
        _categoryBox.Items.Clear();
        foreach (var c in cats)
            _categoryBox.Items.Add(c);
        _categoryBox.DisplayMemberPath = "Name";
        if (cats.Count > 0)
            _categoryBox.SelectedIndex = 0;
        _poolCheck.IsEnabled = !_income;
        if (_income)
            _poolCheck.IsChecked = false;
        else if (!_editing)
            _poolCheck.IsChecked = true;
    }

    private static UIElement FieldRow(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center };
        var panel = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(text, Dock.Left);
        panel.Children.Add(text);
        panel.Children.Add(input);
        return panel;
    }

    private void TryAccept()
    {
        if (_datePicker.SelectedDate is null)
        {
            _error.Text = "请选择日期。";
            return;
        }
        if (_accountBox.SelectedItem is not AccountRow)
        {
            _error.Text = "请先新建一个账户(工具 → 账户)。";
            return;
        }
        if (!ParseMoney(_amountBox.Text, out var yuan) || yuan <= 0)
        {
            _error.Text = "金额请填大于 0 的数字(元)。";
            return;
        }
        if (TxnName.Length == 0)
        {
            _error.Text = "请填写名称(如「早餐」)。";
            return;
        }
        if (_categoryBox.SelectedItem is not CategoryRow)
        {
            _error.Text = "请选择分类。";
            return;
        }
        AmountCents = Money.ToCents(yuan);
        DialogResult = true;
    }

    private static bool ParseMoney(string text, out decimal yuan)
    {
        var t = text.Trim();
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out yuan)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out yuan);
    }
}
