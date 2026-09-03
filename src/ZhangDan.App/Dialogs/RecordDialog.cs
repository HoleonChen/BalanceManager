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
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };

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

    /// <summary>「保存并记下一笔」已内部保存的笔数;调用方在对话框关闭后据它判断是否刷新列表。</summary>
    public int SavedCount { get; private set; }

    /// <summary>最近一次内部保存所用日期(yyyy-MM-dd);批量录入后把页面翻到该日。</summary>
    public string LastSavedDate { get; private set; } = "";

    public RecordDialog(LedgerSession ledger, DateTime defaultDate, AppSettings settings,
        TxnEditable? edit = null,
        long? presetAccountId = null, long? presetCategoryId = null, long? presetAmountCents = null)
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
        _error.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);

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
            // 凌晨宽限(设置):0~6 点记一笔默认记到「昨天」——清晨补录前一晚的账
            if (settings.MidnightGraceEnabled && DateTime.Now.Hour < 6 && defaultDate.Date == DateTime.Today)
                defaultDate = DateTime.Today.AddDays(-1);
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
        if (edit is null)
        {
            // 批量录入(晚间整理一天账目):保存当前一笔、留在对话框继续录下一笔
            var next = new Button
            {
                Content = "保存并记下一笔",
                Width = 150,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
                ToolTip = "保存这笔后清空金额/名称,继续录下一笔(日期/账户/分类沿用)——适合晚间一次性整理全天账目"
            };
            next.Click += (_, _) => SaveAndRecordNext();
            row.Children.Add(next);
        }
        row.Children.Add(ok);
        row.Children.Add(cancel);

        _error.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_error);
        panel.Children.Add(row);

        Content = panel;
        ApplyDirection();
        ApplyPresets(presetAccountId, presetCategoryId, presetAmountCents, edit);
    }

    /// <summary>预置账户/分类(编辑回填原值;补记按账户+差额金额),在分类列按方向就绪后再设置。</summary>
    private void ApplyPresets(long? accountId, long? categoryId, long? amountCents, TxnEditable? edit)
    {
        var wantAccount = accountId ?? edit?.AccountId;
        if (wantAccount is long aid)
        {
            for (int i = 0; i < _accountBox.Items.Count; i++)
            {
                if (_accountBox.Items[i] is AccountRow a && a.Id == aid)
                {
                    _accountBox.SelectedIndex = i;
                    break;
                }
            }
        }
        var wantCategory = categoryId ?? edit?.CategoryId;
        if (wantCategory is long cid)
        {
            for (int i = 0; i < _categoryBox.Items.Count; i++)
            {
                if (_categoryBox.Items[i] is CategoryRow c && c.Id == cid)
                {
                    _categoryBox.SelectedIndex = i;
                    break;
                }
            }
        }
        // 补记引导可预填差额金额(编辑态金额已由 edit 回填,不覆盖)
        if (edit is null && amountCents is long amt)
            _amountBox.Text = (amt / 100m).ToString("0.##", CultureInfo.InvariantCulture);
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
        if (Validate())
            DialogResult = true;
    }

    /// <summary>校验当前字段;通过则写入 AmountCents。失败在 _error 提示并返回 false。</summary>
    private bool Validate()
    {
        _error.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);
        if (_datePicker.SelectedDate is null)
        {
            _error.Text = "请选择日期。";
            return false;
        }
        if (_accountBox.SelectedItem is not AccountRow)
        {
            _error.Text = "请先新建一个账户(工具 → 账户)。";
            return false;
        }
        if (!ParseMoney(_amountBox.Text, out var yuan) || yuan <= 0)
        {
            _error.Text = "金额请填大于 0 的数字(元)。";
            return false;
        }
        if (TxnName.Length == 0)
        {
            _error.Text = "请填写名称(如「早餐」)。";
            return false;
        }
        if (_categoryBox.SelectedItem is not CategoryRow)
        {
            _error.Text = "请选择分类。";
            return false;
        }
        AmountCents = Money.ToCents(yuan);
        return true;
    }

    /// <summary>保存当前一笔并留在对话框录下一笔(晚间批量整理):日期/账户/分类沿用,金额/名称/渠道/备注清空。</summary>
    private void SaveAndRecordNext()
    {
        if (!Validate())
            return;
        try
        {
            Transactions.Add(_ledger, new TxnDraft
            {
                Date = DateStr,
                Direction = Direction,
                AccountId = AccountId,
                CategoryId = CategoryId,
                AmountCents = AmountCents,
                Name = TxnName,
                Channel = Channel,
                Note = Note,
                InPool = InPool
            });
            SavedCount++;
            LastSavedDate = DateStr;
            _amountBox.Text = "";
            _nameBox.Clear();
            _channelBox.Text = "";
            _noteBox.Clear();
            _error.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Success);
            _error.Text = $"已保存 {SavedCount} 笔 —— 日期/账户/分类沿用,可直接录下一笔;可随时改。";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "记一笔·保存失败");
            _error.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);
            _error.Text = $"保存失败:{ex.Message}";
        }
    }

    private static bool ParseMoney(string text, out decimal yuan)
    {
        var t = text.Trim();
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out yuan)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out yuan);
    }
}
