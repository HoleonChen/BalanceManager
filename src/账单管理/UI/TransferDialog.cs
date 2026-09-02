using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 记转账(设计 §3.5):转出账户 −本金,转入账户 +(本金+浮动)。
/// 五类:互转/充值/提现/理财结算/存取;「实际到账」与本金之差即浮动 Δ
/// (Δ&gt;0 记收益、Δ&lt;0 记手续费)。转出账户若为池账户,默认不入池,可勾「计入池」。
/// </summary>
internal sealed class TransferDialog : Form
{
    private static readonly string[] Kinds = { "互转", "充值", "提现", "理财结算", "存取" };

    private readonly LedgerSession _ledger;
    private readonly AppSettings _settings;

    private readonly RadioButton _yesterdayRadio = new() { Text = "今天", Checked = true };
    private readonly RadioButton _backRadio = new() { Text = "昨天(凌晨宽限)" };
    private readonly ComboBox _fromBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _toBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _principalBox = new();
    private readonly TextBox _receivedBox = new();
    private readonly ComboBox _kindBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _poolCheck = new() { Text = "计入池(由池预算扣除)", Checked = false };
    private readonly Label _poolHint = new() { ForeColor = SystemColors.GrayText, AutoSize = true };
    private readonly TextBox _noteBox = new();
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };
    private readonly Label _deltaLabel = new() { ForeColor = SystemColors.GrayText, AutoSize = true };

    private readonly List<AccountRow> _accounts = new();
    private DateTime _date;

    public DateTime Date => _date;
    public long FromAccountId { get; private set; }
    public long ToAccountId { get; private set; }
    public long PrincipalCents { get; private set; }
    public long DeltaCents { get; private set; }
    public string Kind { get; private set; } = "";
    public string Note { get; private set; } = "";
    public bool InPool { get; private set; }

    public TransferDialog(LedgerSession ledger, AppSettings settings)
    {
        _ledger = ledger;
        _settings = settings;

        Text = "记转账";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(460, 380);

        var body = new Panel { Dock = DockStyle.Fill };
        const int xl = 18, xf = 118, wf = 300;
        int y = 14;

        void Row(string text, Control c, int width = wf)
        {
            var label = new Label { Text = text, Location = new Point(xl, y + 3), AutoSize = true };
            c.Location = new Point(xf, y);
            c.Width = width;
            body.Controls.AddRange(new Control[] { label, c });
            y += 34;
        }

        // 日期:今天 / 昨天(凌晨宽限)
        _yesterdayRadio.Location = new Point(xf, y + 2);
        _backRadio.Location = new Point(xf + 80, y + 2);
        body.Controls.Add(_yesterdayRadio);
        body.Controls.Add(_backRadio);
        y += 34;

        _accounts.AddRange(Accounts.ListEnabled(_ledger));

        _fromBox.DataSource = _accounts;
        _fromBox.DisplayMember = nameof(AccountRow.Name);
        _fromBox.SelectedIndex = _accounts.Count > 1 ? 0 : -1;
        _toBox.DataSource = _accounts;
        _toBox.DisplayMember = nameof(AccountRow.Name);
        _toBox.SelectedIndex = _accounts.Count > 1 ? 1 : -1;
        _fromBox.SelectedIndexChanged += (_, _) => FixSelection(_fromBox, _toBox);
        _toBox.SelectedIndexChanged += (_, _) => FixSelection(_toBox, _fromBox);
        Row("转出账户", _fromBox);
        Row("转入账户", _toBox);

        Row("本金(转出,元)", _principalBox);
        Row("实际到账(元)", _receivedBox);
        _deltaLabel.Location = new Point(xf, y);
        body.Controls.Add(_deltaLabel);
        y += 34;

        _kindBox.Items.AddRange(Kinds);
        _kindBox.SelectedIndex = 0;
        Row("类别", _kindBox);

        // 提示 + 计入池勾选
        _poolHint.Location = new Point(xl, y + 4);
        _poolHint.Text = "提示:仅当转出账户是资金池账户时,才需要勾选「计入池」。";
        body.Controls.Add(_poolHint);
        y += 30;
        _poolCheck.Location = new Point(xf, y);
        body.Controls.Add(_poolCheck);
        y += 34;

        Row("备注", _noteBox);

        var ok = new Button { Text = "保存", Width = 84, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel };
        ok.Location = new Point(ClientSize.Width - 18 - 84 * 2 - 8, y + 8);
        cancel.Location = new Point(ClientSize.Width - 18 - 84, y + 8);
        ok.Click += (_, _) => TryAccept();
        AcceptButton = ok;
        CancelButton = cancel;

        _errorLabel.Location = new Point(xl, y + 40);
        body.Controls.AddRange(new Control[] { ok, cancel, _errorLabel });
        Controls.Add(body);

        _principalBox.TextChanged += (_, _) => { _errorLabel.Text = string.Empty; UpdateDelta(); };
        _receivedBox.TextChanged += (_, _) => { _errorLabel.Text = string.Empty; UpdateDelta(); };
        UpdateDelta();

        // 凌晨宽限:默认记昨天
        if (_settings.MidnightGraceEnabled && DateTime.Now.Hour < 6)
        {
            _backRadio.Checked = true;
            _yesterdayRadio.Checked = false;
        }

        // 账户不足两个时给出明确引导
        if (_accounts.Count < 2)
        {
            _errorLabel.Text = _accounts.Count == 0
                ? "还没有账户 —— 请先在记一笔或「工具 → 账户管理」里新建。"
                : "只有一个账户,无法转账 —— 请再建一个转入账户。";
        }
    }

    private static void FixSelection(ComboBox changed, ComboBox other)
    {
        // 两账户不能相同
        if (changed.SelectedIndex >= 0 && changed.SelectedIndex == other.SelectedIndex)
        {
            var idx = (other.SelectedIndex + 1) % other.Items.Count;
            if (idx == changed.SelectedIndex)
                idx = (idx + 1) % other.Items.Count;
            other.SelectedIndex = idx;
        }
    }

    private void UpdateDelta()
    {
        if (ParseMoney(_principalBox.Text, out var principal)
            && ParseMoney(_receivedBox.Text, out var received))
        {
            var delta = received - principal;
            _deltaLabel.Text = delta == 0
                ? "浮动 Δ = ¥0.00"
                : $"浮动 Δ = {(delta > 0 ? "+" : "-")}{Money.Yuan(Money.ToCents(Math.Abs(delta)))}   ← 实际到账 − 本金";
        }
        else if (_principalBox.Text.Trim().Length > 0)
        {
            _deltaLabel.Text = _receivedBox.Text.Trim().Length == 0
                ? "实际到账留空 = 与本金一致(Δ0)"
                : "";
        }
    }

    private static bool ParseMoney(string text, out decimal yuan)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            yuan = 0m;
            return true;
        }
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out yuan)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out yuan);
    }

    private void TryAccept()
    {
        if (_fromBox.SelectedIndex < 0 || _fromBox.SelectedItem is not AccountRow from)
        {
            _errorLabel.Text = "请选择转出账户。";
            return;
        }
        if (_toBox.SelectedIndex < 0 || _toBox.SelectedItem is not AccountRow to)
        {
            _errorLabel.Text = "请选择转入账户。";
            return;
        }
        if (from.Id == to.Id)
        {
            _errorLabel.Text = "转出与转入不能是同一账户。";
            return;
        }

        if (!ParseMoney(_principalBox.Text, out var principalValue) || principalValue <= 0)
        {
            _errorLabel.Text = "本金请填大于 0 的数字(元)。";
            return;
        }

        // 实际到账留空按本金算(Δ0)
        if (_receivedBox.Text.Trim().Length == 0)
        {
            _receivedBox.Text = _principalBox.Text;
            return;
        }
        if (!ParseMoney(_receivedBox.Text, out var receivedValue) || receivedValue <= 0)
        {
            _errorLabel.Text = "实际到账请填大于 0 的数字(元)。";
            return;
        }

        var now = DateTime.Now;
        _date = _backRadio.Checked ? now.AddDays(-1) : now;
        FromAccountId = from.Id;
        ToAccountId = to.Id;
        PrincipalCents = Money.ToCents(principalValue);
        DeltaCents = Money.ToCents(receivedValue) - PrincipalCents;
        Kind = Kinds[_kindBox.SelectedIndex];
        Note = _noteBox.Text.Trim();
        InPool = _poolCheck.Checked;

        DialogResult = DialogResult.OK;
    }
}
