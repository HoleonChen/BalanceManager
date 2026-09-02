using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 新建账户:名称 + 类型 + 平台 + 可选当前余额。
/// 账户表默认为空,不预置资产账户——真实账户由用户按需创建、日后导入补全。
/// </summary>
internal sealed class AccountDialog : FormBase
{
    internal static readonly (string Label, string Key)[] TypeOptions =
    {
        ("钱包(零钱/余额)", "wallet"),
        ("货币基金(零钱通/余额宝)", "money_fund"),
        ("银行卡", "bank"),
        ("现金", "cash"),
        ("定存(整存整取)", "fixed_deposit"),
        ("基金", "fund"),
        ("储值卡(水卡等)", "prepaid"),
    };

    /// <summary>类型 key → 中文标签(账户管理列表展示用)。</summary>
    internal static string TypeLabel(string key)
    {
        foreach (var (label, k) in TypeOptions)
        {
            if (k == key)
                return label;
        }
        return key;
    }

    private static readonly string[] PlatformPresets =
        { "微信", "支付宝", "银行", "投资", "现金", "储值卡" };

    private readonly TextBox _nameBox = new();
    private readonly ComboBox _typeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _platformBox = new();
    private readonly TextBox _balanceBox = new();
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };

    public string AccountName => _nameBox.Text.Trim();
    public string TypeKey => TypeOptions[_typeBox.SelectedIndex].Key;
    public string Platform => _platformBox.Text.Trim();
    public long BalanceBaseCents { get; private set; }

    public AccountDialog()
    {
        Text = "新建账户";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(440, 280);

        foreach (var (label, _) in TypeOptions)
            _typeBox.Items.Add(label);
        _typeBox.SelectedIndex = 0;

        foreach (var p in PlatformPresets)
            _platformBox.Items.Add(p);
        _platformBox.SelectedIndex = 0;

        var body = new Panel { Dock = DockStyle.Fill };
        const int xl = 18, xf = 118, wf = 300;
        int y = 16;

        void Row(string text, Control c, int width = wf)
        {
            var label = new Label { Text = text, Location = new Point(xl, y + 3), AutoSize = true };
            c.Location = new Point(xf, y);
            c.Width = width;
            body.Controls.AddRange(new Control[] { label, c });
            y += 34;
        }

        Row("名称", _nameBox);
        Row("类型", _typeBox);
        Row("平台", _platformBox);
        Row("当前余额(可选,元)", _balanceBox);

        // 说明:初始余额仅作基准,之后靠对账/校准拉齐
        var hint = new Label
        {
            Text = "提示:首次新建可不填余额,日后通过「校准」对准实际。",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(xl, y + 4)
        };
        body.Controls.Add(hint);
        y += 34;

        var ok = new Button { Text = "创建", Width = 84, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel };
        ok.Location = new Point(ClientSize.Width - 18 - 84 * 2 - 8, y + 8);
        cancel.Location = new Point(ClientSize.Width - 18 - 84, y + 8);
        ok.Click += (_, _) => TryAccept();
        AcceptButton = ok;
        CancelButton = cancel;

        _errorLabel.Location = new Point(xl, y + 40);
        body.Controls.AddRange(new Control[] { ok, cancel, _errorLabel });
        Controls.Add(body);

        _nameBox.TextChanged += (_, _) => _errorLabel.Text = string.Empty;
        _balanceBox.TextChanged += (_, _) => _errorLabel.Text = string.Empty;
    }

    private void TryAccept()
    {
        if (_nameBox.Text.Trim().Length == 0)
        {
            _errorLabel.Text = "请填写账户名称(如「微信零钱」「建行储蓄卡」)。";
            return;
        }

        var text = _balanceBox.Text.Trim();
        long cents = 0;
        if (text.Length > 0)
        {
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                _errorLabel.Text = "余额请填数字(元)。";
                return;
            }
            cents = Money.ToCents(value);
        }

        BalanceBaseCents = cents;
        DialogResult = DialogResult.OK;
    }
}
