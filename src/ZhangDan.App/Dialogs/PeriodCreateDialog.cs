using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ZhangDan.App.Dialogs;

/// <summary>
/// 新建/编辑周期。建周期时可一并录入「初始收入」与「资金池」;
/// 编辑周期时只改属性 + 资金池(预算/保留),初始收入视为流水在其日期处编辑。
/// 保留支持「金额 + 占预算比例」双联动输入(编辑任一自动同步另一)。
/// </summary>
internal sealed class PeriodCreateDialog : Window
{
    private readonly List<AccountRow> _accounts;
    private readonly List<CategoryRow> _incomeCats;
    private readonly PoolRow? _existingPool;   // 编辑周期且该周期已有池
    private readonly bool _poolFixed;          // 已有池:池块直接展示、账户只读

    private readonly TextBox _name = new() { Text = "生活费", Width = 300 };
    private readonly DatePicker _start = new() { Width = 300, SelectedDate = DateTime.Today };
    private readonly CheckBox _endCheck = new() { Content = "计划结束日期", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    // 结束日初始为「自动推导」:随开始日 = 下月同日的前一天(见 AutoEndDate);用户手动挑过(或编辑已存值)后不再自动跟随。
    private readonly DatePicker _end = new() { Width = 180 };

    private readonly CheckBox _incomeCheck = new() { Content = "初始收入(可选)", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _incomeAccount = new() { Width = 200 };
    private readonly ComboBox _incomeCat = new() { Width = 200 };
    private readonly TextBox _incomeAmount = new() { Width = 120 };
    private readonly StackPanel _incomeBody = new();

    private readonly CheckBox _poolCheck = new() { Content = "建立资金池(可选)", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _poolAccount = new() { Width = 200 };
    private readonly TextBox _budgetBox = new() { Width = 120 };
    private readonly TextBox _reserveAmount = new() { Width = 140 };   // 保留(元)
    private readonly TextBox _reservePercent = new() { Width = 90 };   // 保留占预算 %
    private readonly TextBlock _reserveHint = new()
    {
        Text = "保留 ≤ 资金池大小(预算);建议 ≤ 预算的一半。",
        FontSize = 12,
        Margin = new Thickness(24, 0, 0, 2),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly StackPanel _poolBody = new();
    private bool _syncingReserve;
    private bool _settingEnd;   // SetAutoEnd 期间置位,避免把程序设值误判为“用户手动挑过”
    private bool _endManual;    // 用户手动挑过结束日 → 结束日不再随开始日自动推

    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };

    public string PeriodName => _name.Text.Trim();
    public string StartDate => _start.SelectedDate!.Value.ToString("yyyy-MM-dd");
    public string? EndDate => _endCheck.IsChecked == true ? _end.SelectedDate?.ToString("yyyy-MM-dd") : null;

    public bool UseInitialIncome => _incomeCheck.IsChecked == true;
    public long IncomeAccountId => ((AccountRow)_incomeAccount.SelectedItem).Id;
    public long IncomeCategoryId => ((CategoryRow)_incomeCat.SelectedItem).Id;
    public long IncomeCents { get; private set; }

    public bool UsePool => _poolFixed || _poolCheck.IsChecked == true;
    public long PoolAccountId => _poolFixed ? _existingPool!.AccountId : ((AccountRow)_poolAccount.SelectedItem).Id;
    public long PoolBudgetCents { get; private set; }
    public long PoolReserveCents { get; private set; }

    public PeriodCreateDialog(LedgerSession ledger, PeriodRow? existing = null)
    {
        _accounts = new List<AccountRow>(Accounts.ListEnabled(ledger));
        _incomeCats = new List<CategoryRow>(Categories.ListManual(ledger, income: true));
        _existingPool = existing is null ? null : Pools.Get(ledger, existing.Id);
        _poolFixed = _existingPool is not null;

        Title = existing is null ? "新建记账周期" : "编辑周期";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        _error.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);
        _reserveHint.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);

        _endCheck.Checked += (_, _) => _end.IsEnabled = true;
        _endCheck.Unchecked += (_, _) => _end.IsEnabled = false;
        _incomeCheck.Checked += (_, _) => ToggleBody(_incomeBody, true);
        _incomeCheck.Unchecked += (_, _) => ToggleBody(_incomeBody, false);
        _poolCheck.Checked += (_, _) => ToggleBody(_poolBody, true);
        _poolCheck.Unchecked += (_, _) => ToggleBody(_poolBody, false);

        // 保留双联动:金额↔占预算比例
        _reserveAmount.TextChanged += (_, _) => SyncFromAmount();
        _reservePercent.TextChanged += (_, _) => SyncFromPercent();
        _budgetBox.TextChanged += (_, _) => { SyncFromAmount(); RefreshReserveHint(); };

        // 结束日自动推导:开始日每变一次、结束日跟随到「下月同日的前一天」;
        // 仅新建向导生效;编辑态结束日取自存储值/用户手调,不自动跟随。
        _end.SelectedDateChanged += (_, _) => { if (!_settingEnd) _endManual = true; };
        _start.SelectedDateChanged += (_, _) =>
        {
            if (existing is not null || _endManual || _start.SelectedDate is not { } s)
                return;
            SetAutoEnd(AutoEndDate(s));
        };

        FillCombo(_incomeAccount, _accounts);
        FillCombo(_incomeCat, _incomeCats);

        // 编辑时只改属性 + 资金池(初始收入属新建期初始化,不提供)
        if (existing is not null)
        {
            _name.Text = existing.Name;
            _start.SelectedDate = DateTime.Parse(existing.StartDate);
            if (existing.EndDate is not null)
            {
                _settingEnd = true;
                try { _end.SelectedDate = DateTime.Parse(existing.EndDate); }
                finally { _settingEnd = false; }
                _endManual = true;   // 已存结束日按实际值,不随开始日自动推
            }
            else
            {
                _endCheck.IsChecked = false;
                _end.IsEnabled = false;
            }
        }
        else
        {
            // 新建:结束日按默认开始日(今天)自动推导一次。
            SetAutoEnd(AutoEndDate(_start.SelectedDate!.Value));
        }

        if (existing is null)
            BuildIncomeBody();   // 初始收入仅新建期提供(账户/分类/金额子字段逐行放)
        BuildPoolBody(ledger, _poolFixed);

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

        if (_poolFixed)
        {
            panel.Children.Add(_poolBody);
        }
        else
        {
            if (existing is null)
            {
                panel.Children.Add(_incomeCheck);
                panel.Children.Add(_incomeBody);
            }
            panel.Children.Add(_poolCheck);
            panel.Children.Add(_poolBody);
        }
        panel.Children.Add(_error);
        panel.Children.Add(row);
        Content = panel;

        if (!_poolFixed)
        {
            _incomeCheck.IsChecked = false;
            _poolCheck.IsChecked = false;
            ToggleBody(_incomeBody, false);
            ToggleBody(_poolBody, false);
            _incomeAmount.Text = "0";
            _budgetBox.Text = "0";
            _reserveAmount.Text = "0";
            _incomeBody.IsEnabled = _incomeCheck.IsChecked == true;
        }
    }

    /// <summary>初始收入块:账户/分类/金额 逐字段成行(此前曾漏建导致勾选后看不到输入)。</summary>
    private void BuildIncomeBody()
    {
        _incomeAccount.Width = 240;
        _incomeCat.Width = 240;
        _incomeAmount.Width = 150;
        _incomeBody.Children.Clear();
        _incomeBody.Children.Add(SubField("账户", _incomeAccount));
        _incomeBody.Children.Add(SubField("分类", _incomeCat));
        _incomeBody.Children.Add(SubField("金额(元)", _incomeAmount));
    }

    /// <summary>资金池块:新建/补建 = 账户可选;编辑已有池 = 账户只读,预算/保留可改。</summary>
    private void BuildPoolBody(LedgerSession ledger, bool accountReadOnly)
    {
        _poolAccount.Width = 300;
        _budgetBox.Width = 160;
        _poolBody.Children.Clear();

        if (accountReadOnly)
        {
            var acctName = AccountName(ledger, _existingPool!.AccountId);
            var acctText = new TextBlock { Text = acctName, VerticalAlignment = VerticalAlignment.Center };
            _poolBody.Children.Add(SubField("池账户", acctText));
        }
        else
        {
            FillCombo(_poolAccount, _accounts);
            _poolBody.Children.Add(SubField("池账户", _poolAccount));
        }
        _poolBody.Children.Add(SubField("预算(元)", _budgetBox));
        _poolBody.Children.Add(SubField("保留(元)", ReserveRow()));
        _poolBody.Children.Add(_reserveHint);

        if (accountReadOnly)
        {
            _budgetBox.Text = FormatNum(_existingPool!.BudgetCents / 100m);
            _reserveAmount.Text = FormatNum(_existingPool.ReserveCents / 100m);
        }
        RefreshReserveHint();
    }

    /// <summary>保留行:金额(元) + 「≈」+ 占预算比例(%) 双输入联动。</summary>
    private UIElement ReserveRow()
    {
        var eq = new TextBlock { Text = "≈", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        eq.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        var pctMark = new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        pctMark.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_reserveAmount);
        row.Children.Add(eq);
        row.Children.Add(_reservePercent);
        row.Children.Add(pctMark);
        return row;
    }

    /// <summary>自动推结束日 = 开始日的「下月同日 − 1 天」:9/3 起的周期到 10/2 收,
    /// 10/3 正好开下一期——周期开始日总落在每月的同一天,更整齐。</summary>
    private static DateTime AutoEndDate(DateTime start) => start.AddMonths(1).AddDays(-1);

    /// <summary>程序性设定结束日(置位 _settingEnd,不触发 _endManual)。</summary>
    private void SetAutoEnd(DateTime end)
    {
        _settingEnd = true;
        try { _end.SelectedDate = end; }
        finally { _settingEnd = false; }
    }

    private static string AccountName(LedgerSession s, long id)
    {
        foreach (var a in Accounts.ListAll(s))
        {
            if (a.Id == id)
                return a.Name;
        }
        return "(账户不存在)";
    }

    /// <summary>金额→比例:保留/预算×100。</summary>
    private void SyncFromAmount()
    {
        if (_syncingReserve)
            return;
        _syncingReserve = true;
        try
        {
            if (ParseMoney(_budgetBox.Text, out var b) && b > 0
                && ParseMoney(_reserveAmount.Text, out var amt))
            {
                var pct = amt / b * 100m;
                _reservePercent.Text = FormatNum(pct);
            }
            else if (_reservePercent.Text.Length > 0 && _reservePercent.Text != "0")
            {
                _reservePercent.Text = "";
            }
        }
        finally { _syncingReserve = false; }
        RefreshReserveHint();
    }

    /// <summary>比例→金额:比例×预算/100。</summary>
    private void SyncFromPercent()
    {
        if (_syncingReserve)
            return;
        _syncingReserve = true;
        try
        {
            if (ParseMoney(_reservePercent.Text, out var pct) && ParseMoney(_budgetBox.Text, out var b) && b > 0 && pct >= 0)
                _reserveAmount.Text = FormatNum(pct / 100m * b);
        }
        finally { _syncingReserve = false; }
        RefreshReserveHint();
    }

    /// <summary>保留提示:超池(硬)红字;>50% 橙色建议;否则灰字基线。</summary>
    private void RefreshReserveHint()
    {
        if (!ParseMoney(_budgetBox.Text, out var b) || !ParseMoney(_reserveAmount.Text, out var r))
        {
            _reserveHint.Text = "保留 ≤ 资金池大小(预算);建议 ≤ 预算的一半。";
            _reserveHint.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
            return;
        }
        if (b > 0 && r > b)
        {
            _reserveHint.Text = "保留已超过资金池大小(预算),需调低。";
            _reserveHint.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);
        }
        else if (b > 0 && r > b / 2m)
        {
            _reserveHint.Text = "保留占预算超过一半——建议 ≤ 一半,请确认这是预期。";
            _reserveHint.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Warn);
        }
        else
        {
            _reserveHint.Text = "保留 ≤ 资金池大小(预算);建议 ≤ 预算的一半。";
            _reserveHint.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        }
    }

    /// <summary>数字框展示:最多两位小数、去掉多余尾零。</summary>
    private static string FormatNum(decimal v)
        => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

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

    /// <summary>取保留金额(元):金额为真则用它;金额空但有比例则按「比例×预算」;两者皆空视为 0。</summary>
    private bool TryReserveYuan(out decimal reserve)
    {
        if (ParseMoney(_reserveAmount.Text, out reserve))
            return true;
        if (ParseMoney(_reservePercent.Text, out var pct)
            && ParseMoney(_budgetBox.Text, out var b) && b > 0)
        {
            reserve = pct / 100m * b;
            return true;
        }
        if (_reserveAmount.Text.Trim().Length == 0 && _reservePercent.Text.Trim().Length == 0)
        {
            reserve = 0;
            return true;
        }
        reserve = 0;
        return false;
    }

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
            if (!_poolFixed && _poolAccount.SelectedItem is not AccountRow) { _error.Text = "资金池需选择账户。"; return; }
            if (!ParseMoney(_budgetBox.Text, out var b) || b <= 0) { _error.Text = "资金池预算(大小)请填大于 0 的数字。"; return; }
            if (!TryReserveYuan(out var r)) { _error.Text = "保留金额请填数字(元),或比例请填数字(%)。"; return; }
            if (r < 0) { _error.Text = "保留不能为负。"; return; }
            if (r > b) { _error.Text = $"保留({FormatNum(r)} 元)不能超过资金池大小(预算 {FormatNum(b)} 元)。"; return; }
            PoolBudgetCents = Money.ToCents(b);
            PoolReserveCents = Money.ToCents(r);
        }

        DialogResult = true;
    }
}
