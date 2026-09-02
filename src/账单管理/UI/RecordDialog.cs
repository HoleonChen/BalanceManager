using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 记一笔:支出 / 收入。
/// 转账语义复杂(本金+浮动+分类、双账户),按设计文档 §3.5 单独实现,不进本对话框。
/// </summary>
internal sealed class RecordDialog : Form
{
    private readonly LedgerSession _ledger;
    private readonly AppSettings _settings;

    private readonly Panel _body = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _outRadio = new() { Text = "支出", Checked = true };
    private readonly RadioButton _inRadio = new() { Text = "收入" };
    private readonly ComboBox _accountBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _accountButton = new() { Text = "新建账户…", Width = 96 };
    private readonly ComboBox _categoryBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _amountBox = new();
    private readonly TextBox _nameBox = new();
    private readonly ComboBox _channelBox = new();
    private readonly TextBox _noteBox = new();
    private readonly CheckBox _poolCheck = new() { Text = "计入资金池(由池预算扣除)", Checked = true };
    private readonly CheckBox _yesterdayCheck = new() { Text = "记到昨天(凌晨宽限补录)" };
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };

    private readonly List<AccountRow> _accounts = new();
    private readonly List<CategoryRow> _expenseCats;
    private readonly List<CategoryRow> _incomeCats;

    private int _y = 16;

    // ---- 校验通过后的输出 ----
    public string DateStr { get; private set; } = "";
    public string Direction { get; private set; } = "out";
    public long AccountId { get; private set; }
    public long CategoryId { get; private set; }
    public long AmountCents { get; private set; }
    public string TxnName { get; private set; } = "";
    public string Note { get; private set; } = "";
    public string Channel { get; private set; } = "";
    public bool InPool { get; private set; }

    public RecordDialog(LedgerSession ledger, AppSettings settings)
    {
        _ledger = ledger;
        _settings = settings;
        _expenseCats = new List<CategoryRow>(Categories.ListManual(_ledger, income: false));
        _incomeCats = new List<CategoryRow>(Categories.ListManual(_ledger, income: true));

        Text = "记一笔";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(460, 450);

        BuildUi();
        Controls.Add(_body);

        ReloadAccounts(selectNew: false);
        ApplyDirection();
        ReloadDateCheck();
    }

    private void BuildUi()
    {
        const int xl = 18, xf = 118, wf = 300;

        void Row(string text, Control c, int width)
        {
            var label = new Label { Text = text, Location = new Point(xl, _y + 3), AutoSize = true };
            c.Location = new Point(xf, _y);
            c.Width = width;
            _body.Controls.AddRange(new Control[] { label, c });
            _y += 34;
        }

        // 类型
        _body.Controls.Add(new Label { Text = "类型", Location = new Point(xl, _y + 3), AutoSize = true });
        _outRadio.Location = new Point(xf, _y);
        _inRadio.Location = new Point(xf + 80, _y);
        _outRadio.CheckedChanged += (_, _) => ApplyDirection();
        _body.Controls.Add(_outRadio);
        _body.Controls.Add(_inRadio);
        _y += 34;

        // 账户 + 新建账户
        _body.Controls.Add(new Label { Text = "账户", Location = new Point(xl, _y + 3), AutoSize = true });
        _accountBox.Location = new Point(xf, _y);
        _accountBox.Width = wf - 108;
        _accountButton.Location = new Point(xf + _accountBox.Width + 8, _y);
        _accountButton.Click += (_, _) => CreateAccount();
        _body.Controls.Add(_accountBox);
        _body.Controls.Add(_accountButton);
        _y += 34;

        Row("分类", _categoryBox, wf);

        // 金额
        _body.Controls.Add(new Label { Text = "金额", Location = new Point(xl, _y + 3), AutoSize = true });
        _amountBox.Location = new Point(xf, _y);
        _amountBox.Width = 200;
        _body.Controls.Add(new Label { Text = "元", Location = new Point(xf + 208, _y + 3), AutoSize = true });
        _body.Controls.Add(_amountBox);
        _y += 34;

        Row("名称", _nameBox, wf);

        _channelBox.DropDownStyle = ComboBoxStyle.DropDown;
        _channelBox.Items.AddRange(new object[] { "", "网购", "实体", "其他" });
        _channelBox.SelectedIndex = 0;
        Row("渠道", _channelBox, 180);

        Row("备注", _noteBox, wf);

        _poolCheck.Location = new Point(xl, _y);
        _yesterdayCheck.Location = new Point(xl, _y + 30);
        _body.Controls.Add(_poolCheck);
        _body.Controls.Add(_yesterdayCheck);
        _y += 60;

        var ok = new Button { Text = "保存", Width = 84, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel };
        ok.Location = new Point(ClientSize.Width - 18 - 84 * 2 - 8, _y + 10);
        cancel.Location = new Point(ClientSize.Width - 18 - 84, _y + 10);
        ok.Click += (_, _) => TryAccept();
        AcceptButton = ok;
        CancelButton = cancel;

        _errorLabel.Location = new Point(xl, _y + 44);
        _body.Controls.AddRange(new Control[] { ok, cancel, _errorLabel });

        _amountBox.TextChanged += ClearError;
        _nameBox.TextChanged += ClearError;
    }

    private void ReloadAccounts(bool selectNew)
    {
        _accounts.Clear();
        _accounts.AddRange(Accounts.ListEnabled(_ledger));

        _accountBox.DataSource = null;
        _accountBox.DataSource = _accounts;
        _accountBox.DisplayMember = nameof(AccountRow.Name);

        if (_accounts.Count == 0)
            _accountBox.SelectedIndex = -1;
        else
            _accountBox.SelectedIndex = selectNew ? _accounts.Count - 1 : 0;
    }

    private void CreateAccount()
    {
        using var dlg = new AccountDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        Accounts.Insert(_ledger, dlg.Name, dlg.TypeKey, dlg.Platform, dlg.BalanceBaseCents);
        ReloadAccounts(selectNew: true);
    }

    private void ApplyDirection()
    {
        var income = _inRadio.Checked;
        var list = income ? _incomeCats : _expenseCats;

        _categoryBox.DataSource = null;
        _categoryBox.DataSource = list;
        _categoryBox.DisplayMember = nameof(CategoryRow.Name);
        _categoryBox.SelectedIndex = list.Count == 0 ? -1 : 0;

        _poolCheck.Enabled = !income;
        _poolCheck.Checked = !income;   // 收入不入池
    }

    private void ReloadDateCheck()
    {
        // 凌晨宽限开关开着且当前在凌晨(0~6 点)时,默认记到昨天
        _yesterdayCheck.Checked = _settings.MidnightGraceEnabled && DateTime.Now.Hour < 6;
    }

    private void ClearError(object? sender, EventArgs e) => _errorLabel.Text = string.Empty;

    private void TryAccept()
    {
        if (_accountBox.SelectedIndex < 0 || _accountBox.SelectedItem is not AccountRow account)
        {
            _errorLabel.Text = "还没有账户 —— 点账户旁的「新建账户…」先建一个。";
            return;
        }

        if (_categoryBox.SelectedIndex < 0 || _categoryBox.SelectedItem is not CategoryRow category)
        {
            _errorLabel.Text = "请选择分类。";
            return;
        }

        var amountText = _amountBox.Text.Trim();
        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            && !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
        {
            _errorLabel.Text = "金额请填数字(元),如 12.5。";
            return;
        }
        if (value <= 0)
        {
            _errorLabel.Text = "金额需大于 0。";
            return;
        }

        if (_nameBox.Text.Trim().Length == 0)
        {
            _errorLabel.Text = "请填写名称(如「早餐」)。";
            return;
        }

        var income = _inRadio.Checked;
        var now = DateTime.Now;
        DateStr = (_yesterdayCheck.Checked ? now.AddDays(-1) : now).ToString("yyyy-MM-dd");
        Direction = income ? "in" : "out";
        AccountId = account.Id;
        CategoryId = category.Id;
        AmountCents = Money.ToCents(value);
        TxnName = _nameBox.Text.Trim();
        Note = _noteBox.Text.Trim();
        Channel = _channelBox.Text.Trim();
        InPool = !income && _poolCheck.Checked;

        DialogResult = DialogResult.OK;
    }
}
