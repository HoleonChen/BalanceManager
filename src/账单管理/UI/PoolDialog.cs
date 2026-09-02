using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 资金池设置(工具菜单/顶栏入口):为当前周期建/改单池。
/// 单池 = 一个池绑一个账户(默认=本期生活费入账账户),预算(可花额度)+ 预计保留(留多少不动)。
/// 已花由账目实时派生,本对话框只改设置,不改流水。
/// </summary>
internal sealed class PoolDialog : FormBase
{
    private readonly LedgerSession _ledger;
    private readonly PeriodRow _period;
    private readonly PoolRow? _existing;

    private readonly TextBox _nameBox = new();
    private readonly ComboBox _accountBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _budgetBox = new();
    private readonly TextBox _reserveBox = new();
    private readonly Label _derivedLabel = new() { ForeColor = SystemColors.GrayText, AutoSize = true };
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };

    private readonly List<AccountRow> _accounts = new();

    public string PoolName { get; private set; } = "";
    public long AccountId { get; private set; }
    public long BudgetCents { get; private set; }
    public long ReserveCents { get; private set; }

    public PoolDialog(LedgerSession ledger, PeriodRow period, PoolRow? existing)
    {
        _ledger = ledger;
        _period = period;
        _existing = existing;

        Text = existing is null ? $"设置资金池 · {period.Name}" : $"资金池 · {period.Name}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(460, 330);

        _nameBox.Text = existing?.Name ?? "生活费池";

        _accounts.AddRange(Accounts.ListEnabled(_ledger));
        _accountBox.DataSource = _accounts;
        _accountBox.DisplayMember = nameof(AccountRow.Name);

        // 默认账户:已有池沿用;无则选本期入账合计最大的启用账户(生活费入账户),否则第一个
        if (existing is not null)
        {
            SelectAccount(existing.AccountId);
        }
        else
        {
            SelectAccount(SuggestAccount(period.Id));
        }
        _accountBox.SelectedIndexChanged += (_, _) => UpdateDerived();

        // 预算/保留默认
        if (existing is not null)
        {
            _budgetBox.Text = Yuan(existing.BudgetCents);
            _reserveBox.Text = Yuan(existing.ReserveCents);
        }
        else
        {
            var income = CurrentAccountIncome();
            if (income > 0)
            {
                _budgetBox.Text = (income / 100m).ToString("0.##", CultureInfo.InvariantCulture);
                _reserveBox.Text = (income / 100m * 0.3m).ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        var body = new Panel { Dock = DockStyle.Fill };
        const int xl = 18, xf = 128, wf = 300;
        int y = 14;

        void Row(string text, Control c, int width = wf)
        {
            var label = new Label { Text = text, Location = new Point(xl, y + 3), AutoSize = true };
            c.Location = new Point(xf, y);
            c.Width = width;
            body.Controls.AddRange(new Control[] { label, c });
            y += 34;
        }

        Row("名称", _nameBox);
        Row("池账户", _accountBox);
        Row("预算(可花,元)", _budgetBox);
        Row("预计保留(元)", _reserveBox);

        _derivedLabel.Location = new Point(xl, y + 2);
        body.Controls.Add(_derivedLabel);
        y += 62;

        _budgetBox.TextChanged += (_, _) => { _errorLabel.Text = string.Empty; UpdateDerived(); };
        _reserveBox.TextChanged += (_, _) => { _errorLabel.Text = string.Empty; UpdateDerived(); };

        var ok = new Button { Text = "保存", Width = 84, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel };
        ok.Location = new Point(ClientSize.Width - 18 - 84 * 2 - 8, y + 6);
        cancel.Location = new Point(ClientSize.Width - 18 - 84, y + 6);
        ok.Click += (_, _) => TryAccept();
        AcceptButton = ok;
        CancelButton = cancel;

        _errorLabel.Location = new Point(xl, y + 38);
        body.Controls.AddRange(new Control[] { ok, cancel, _errorLabel });
        Controls.Add(body);

        UpdateDerived();
    }

    /// <summary>预算建议账户:本期入账合计最大的启用账户;都为 0 则第一个。</summary>
    private long SuggestAccount(long periodId)
    {
        long best = 0, bestId = -1;
        foreach (var a in _accounts)
        {
            var inc = Pools.IncomeInto(_ledger, periodId, a.Id);
            if (inc > best)
            {
                best = inc;
                bestId = a.Id;
            }
        }
        if (bestId >= 0)
            return bestId;
        return _accounts.Count > 0 ? _accounts[0].Id : 0;
    }

    private void SelectAccount(long accountId)
    {
        var idx = _accounts.FindIndex(a => a.Id == accountId);
        if (idx >= 0)
            _accountBox.SelectedIndex = idx;
        else if (_accounts.Count > 0)
            _accountBox.SelectedIndex = 0;
    }

    private long CurrentAccountIncome()
    {
        if (_accountBox.SelectedItem is not AccountRow a)
            return 0;
        return Pools.IncomeInto(_ledger, _period.Id, a.Id);
    }

    private void UpdateDerived()
    {
        if (_accountBox.SelectedItem is not AccountRow a)
        {
            _derivedLabel.Text = "";
            return;
        }
        long spent;
        if (_existing is not null && _existing.AccountId == a.Id)
        {
            spent = Pools.SpentCents(_ledger, _existing);
        }
        else
        {
            // 换了账户(或尚未建池):按本周期该账户 in_pool 支出试算参考
            spent = SpentOnAccount(_ledger, _period.Id, a.Id);
        }

        var budget = ParseYuan(_budgetBox.Text);
        var reserve = ParseYuan(_reserveBox.Text);
        var remaining = budget - spent;
        _derivedLabel.Text =
            $"该账户本期已花 ¥{Money.Yuan(spent)}(实时派生,含勾入池转出)\n" +
            $"剩余 = 预算−已花 = {Money.Yuan(remaining)}\n" +
            $"可自由支配 = 剩余−预计保留 = {Money.Yuan(remaining - reserve)}";
    }

    /// <summary>试算某账户在本周期的已花(即使尚未绑定池)。</summary>
    private static long SpentOnAccount(LedgerSession s, long periodId, long accountId)
    {
        if (Pools.Get(s, periodId) is { } p && p.AccountId == accountId)
            return Pools.SpentCents(s, p);
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT COALESCE(SUM(m), 0) FROM (
  SELECT amount_cents AS m FROM transactions
   WHERE period_id = $pid AND account_id = $acct
     AND direction = 'out' AND in_pool = 1 AND status = 'normal'
  UNION ALL
  SELECT COALESCE(principal_cents, 0) FROM transactions
   WHERE period_id = $pid AND account_id = $acct
     AND direction = 'transfer' AND in_pool = 1 AND status = 'normal'
);";
        cmd.Parameters.AddWithValue("$pid", periodId);
        cmd.Parameters.AddWithValue("$acct", accountId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long ParseYuan(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
            return 0;
        if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out v))
            return Money.ToCents(v);
        return -1;
    }

    private static string Yuan(long cents) => (cents / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    private void TryAccept()
    {
        if (_nameBox.Text.Trim().Length == 0)
        {
            _errorLabel.Text = "请填写资金池名称。";
            return;
        }
        if (_accountBox.SelectedItem is not AccountRow account)
        {
            _errorLabel.Text = "还没有账户 —— 请先在「工具 → 账户管理」里新建,再设置资金池。";
            return;
        }
        var budget = ParseYuan(_budgetBox.Text);
        if (budget <= 0)
        {
            _errorLabel.Text = "预算请填大于 0 的数字(元)。";
            return;
        }
        var reserve = ParseYuan(_reserveBox.Text);
        if (reserve < 0)
        {
            _errorLabel.Text = "预计保留请填 ≥ 0 的数字(元)。";
            return;
        }
        if (reserve > budget)
        {
            _errorLabel.Text = "预计保留不能超过预算。";
            return;
        }

        PoolName = _nameBox.Text.Trim();
        AccountId = account.Id;
        BudgetCents = budget;
        ReserveCents = reserve;
        DialogResult = DialogResult.OK;
    }
}
