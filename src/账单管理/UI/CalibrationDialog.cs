using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 校准余额(设计 §3.2/已定):对准某账户账面与实际余额。三种处理——
///  1 记调整流水(推荐:差额自动生成「差额调整」,方向随差额,不入池)
///  2 补记真实明细(不自动改账,用户自己去补记;本次仅留审计)
///  3 仅更新基准(不动流水,直接平移基准余额)
/// 底部展示该账户校准历史(审计)。未指定账户时可下拉切换。
/// </summary>
internal sealed class CalibrationDialog : Form
{
    private readonly LedgerSession _ledger;
    private readonly List<AccountRow> _accounts;

    private readonly ComboBox _accountBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _bookLabel = new() { AutoSize = true, Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold) };
    private readonly TextBox _actualBox = new();
    private readonly Label _diffLabel = new() { AutoSize = true };
    private readonly RadioButton _adjRadio = new() { Text = "记调整流水(推荐)", Checked = true };
    private readonly RadioButton _realRadio = new() { Text = "补记真实明细" };
    private readonly RadioButton _baseRadio = new() { Text = "仅更新基准(不动流水)" };
    private readonly TextBox _noteBox = new();
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };
    private readonly ListView _history = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.FixedSingle,
        HideSelection = false
    };

    public long AccountId { get; private set; }
    public long ActualCents { get; private set; }
    public string Method { get; private set; } = CalibMethod.Adjustment;
    public string Note { get; private set; } = "";
    public string AccountName { get; private set; } = "";

    public CalibrationDialog(LedgerSession ledger, long? accountId = null)
    {
        _ledger = ledger;
        _accounts = new List<AccountRow>(Accounts.ListEnabled(ledger));

        Text = "校准余额";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(680, 560);

        _history.Columns.Add("时间", 140);
        _history.Columns.Add("账面", 110, HorizontalAlignment.Right);
        _history.Columns.Add("实际", 110, HorizontalAlignment.Right);
        _history.Columns.Add("差额", 100, HorizontalAlignment.Right);
        _history.Columns.Add("方式", 96);
        _history.Columns.Add("备注", 180);

        var top = new Panel { Dock = DockStyle.Top, Height = 296 };
        const int xl = 18, xf = 132;
        int y = 14;

        void Row(string text, Control c, int width = 420)
        {
            var label = new Label { Text = text, Location = new Point(xl, y + 3), AutoSize = true };
            c.Location = new Point(xf, y);
            c.Width = width;
            top.Controls.AddRange(new Control[] { label, c });
            y += 34;
        }

        // 账户:未指定时提供下拉切换
        _accountBox.DataSource = _accounts;
        _accountBox.DisplayMember = nameof(AccountRow.Name);
        var initial = accountId is null
            ? (_accounts.Count > 0 ? _accounts[0].Id : 0)
            : accountId.Value;
        SelectAccount(initial);
        Row("账户", _accountBox);
        _accountBox.SelectedIndexChanged += (_, _) => ReloadAccount();

        var bookTitle = new Label { Text = "当前账面", Location = new Point(xl, y + 4), AutoSize = true };
        _bookLabel.Location = new Point(xf, y);
        top.Controls.Add(bookTitle);
        top.Controls.Add(_bookLabel);
        y += 34;

        Row("实际余额(元)", _actualBox);

        _diffLabel.Location = new Point(xf, y);
        top.Controls.Add(_diffLabel);
        y += 30;

        _adjRadio.Location = new Point(xf, y);
        _realRadio.Location = new Point(xf + 150, y);
        _baseRadio.Location = new Point(xf + 300, y);
        top.Controls.AddRange(new Control[] { _adjRadio, _realRadio, _baseRadio });
        y += 34;

        var noteLabel = new Label { Text = "备注", Location = new Point(xl, y + 3), AutoSize = true };
        _noteBox.Location = new Point(xf, y);
        _noteBox.Width = 420;
        top.Controls.Add(noteLabel);
        top.Controls.Add(_noteBox);
        y += 36;

        var hint = new Label
        {
            Text = "记调整流水:差额自动记一笔「差额调整」(方向随差额,不计池);补记真实明细:去记真实收支后差额归零;仅更新基准:直接改基准余额对齐实际。",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(xl, y)
        };
        top.Controls.Add(hint);

        // 底部:校准历史 + 按钮
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var ok = new Button { Text = "执行校准", Width = 92, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel };
        ok.Location = new Point(ClientSize.Width - 18 - 92 - 8 - 84, 6);
        cancel.Location = new Point(ClientSize.Width - 18 - 84, 6);
        ok.Click += (_, _) => TryAccept();
        AcceptButton = ok;
        CancelButton = cancel;
        _errorLabel.Location = new Point(18, 12);
        bottom.Controls.Add(_errorLabel);
        bottom.Controls.Add(ok);
        bottom.Controls.Add(cancel);

        var histTitle = new Label
        {
            Text = "校准历史(审计)",
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(18, 6, 0, 0)
        };

        var historyHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 18, 6) };
        historyHost.Controls.Add(_history);

        Controls.Add(historyHost);
        Controls.Add(bottom);
        Controls.Add(histTitle);
        Controls.Add(top);

        _actualBox.TextChanged += (_, _) => { _errorLabel.Text = ""; UpdateDiff(); };
        _actualBox.Leave += (_, _) => UpdateDiff();
        ReloadAccount();
        UpdateDiff();
    }

    private void SelectAccount(long accountId)
    {
        var idx = _accounts.FindIndex(a => a.Id == accountId);
        if (idx >= 0)
            _accountBox.SelectedIndex = idx;
        else if (_accounts.Count > 0)
            _accountBox.SelectedIndex = 0;
    }

    private void ReloadAccount()
    {
        var hasAny = _accounts.Count > 0;
        _accountBox.Enabled = hasAny;
        _actualBox.Enabled = hasAny;
        _adjRadio.Enabled = hasAny;
        _realRadio.Enabled = hasAny;
        _baseRadio.Enabled = hasAny;
        _noteBox.Enabled = hasAny;

        if (!hasAny || _accountBox.SelectedItem is not AccountRow a)
        {
            _bookLabel.Text = "—";
            _history.Items.Clear();
            return;
        }

        AccountId = a.Id;
        AccountName = a.Name;
        var book = AccountCalibration.BookCents(_ledger, a.Id);
        _bookLabel.Text = Money.Yuan(book);
        if (_actualBox.Text.Trim().Length == 0)
            _actualBox.Text = (book / 100m).ToString("0.##", CultureInfo.InvariantCulture);
        ReloadHistory();
        UpdateDiff();
    }

    private void ReloadHistory()
    {
        _history.BeginUpdate();
        _history.Items.Clear();
        foreach (var e in AccountCalibration.History(_ledger, AccountId))
        {
            var li = new ListViewItem(e.RecordedAt);
            li.SubItems.Add(Money.Yuan(e.BookCents));
            li.SubItems.Add(Money.Yuan(e.ActualCents));
            var d = e.DiffCents;
            li.SubItems.Add((d > 0 ? "+" : d < 0 ? "-" : "") + Money.Yuan(Math.Abs(d)));
            li.SubItems.Add(CalibMethod.Label(e.Method));
            li.SubItems.Add(e.Note ?? "");
            _history.Items.Add(li);
        }
        _history.EndUpdate();
    }

    private void UpdateDiff()
    {
        if (AccountId == 0)
            return;
        var book = AccountCalibration.BookCents(_ledger, AccountId);
        if (ParseMoney(_actualBox.Text, out var actual))
        {
            var diff = Money.ToCents(actual) - book;
            _diffLabel.Text = diff == 0
                ? "差额 ¥0.00 —— 账面已与实际一致"
                : $"差额 = 实际 − 账面 = {(diff > 0 ? "+" : "-")}{Money.Yuan(Math.Abs(diff))}"
                  + (diff > 0 ? "(实际更高 → 记收入)" : diff < 0 ? "(实际更低 → 记支出)" : "");
            _diffLabel.ForeColor = diff == 0 ? SystemColors.GrayText : Color.Firebrick;
        }
        else
        {
            _diffLabel.Text = "";
        }
    }

    private static bool ParseMoney(string text, out decimal yuan)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            yuan = 0;
            return false;
        }
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out yuan)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out yuan);
    }

    private void TryAccept()
    {
        if (AccountId == 0)
        {
            _errorLabel.Text = "还没有账户 —— 请先到「工具 → 账户管理」新建。";
            return;
        }
        if (!ParseMoney(_actualBox.Text, out var actualValue))
        {
            _errorLabel.Text = "实际余额请填数字(元)。";
            return;
        }

        ActualCents = Money.ToCents(actualValue);
        Method = _baseRadio.Checked ? CalibMethod.BaseOnly
               : _realRadio.Checked ? CalibMethod.RealDetails
               : CalibMethod.Adjustment;
        Note = _noteBox.Text.Trim();
        DialogResult = DialogResult.OK;
    }

    /// <summary>完整跑一次校准(弹窗 → 执行 → 汇总提示)。账户可空 = 让用户在下拉里选。</summary>
    public static void Run(IWin32Window owner, LedgerSession ledger, long? accountId = null)
    {
        using var dlg = new CalibrationDialog(ledger, accountId);
        if (dlg.ShowDialog(owner) != DialogResult.OK)
            return;

        try
        {
            var diff = AccountCalibration.Apply(ledger, dlg.AccountId, dlg.ActualCents, dlg.Method, dlg.Note);
            string how = dlg.Method switch
            {
                CalibMethod.Adjustment => diff == 0
                    ? "账面已一致,无需调整流水。"
                    : $"已自动记一笔「差额调整」({(diff > 0 ? "收入 +" : "支出 -")}{Money.Yuan(Math.Abs(diff))}),不入池。",
                CalibMethod.RealDetails => "已留审计记录。请去记真实收支(差额归零后再来校准一次即可)。",
                CalibMethod.BaseOnly => diff == 0
                    ? "账面已一致,基准未变。"
                    : $"已仅更新基准余额(平移 {(diff > 0 ? "+" : "-")}{Money.Yuan(Math.Abs(diff))}),未动流水。",
                _ => ""
            };
            MessageBox.Show(owner, $"「{dlg.AccountName}」校准完成。\n\n{how}", "校准余额",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"校准失败:\n{ex.Message}", "校准余额",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
