using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 新建记账周期:名称 + 起止日期。周期内记的流水按日期自动归属到本期(见 Transactions.Add)。
/// 生活费周期是账本组织核心:结束日期可选(不设 = 长期进行,人工收尾)。
/// </summary>
internal sealed class PeriodDialog : FormBase
{
    private readonly TextBox _nameBox = new();
    private readonly DateTimePicker _startPicker = new();
    private readonly CheckBox _endCheck = new() { Text = "计划结束日期", Checked = true };
    private readonly DateTimePicker _endPicker = new();
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };

    public string PeriodName => _nameBox.Text.Trim();
    public string StartDate => _startPicker.Value.ToString("yyyy-MM-dd");
    public string? EndDate => _endCheck.Checked ? _endPicker.Value.ToString("yyyy-MM-dd") : null;

    public PeriodDialog()
    {
        Text = "新建记账周期";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(440, 240);

        _nameBox.Text = "生活费";

        var now = DateTime.Today;
        _startPicker.Format = DateTimePickerFormat.Custom;
        _startPicker.CustomFormat = "yyyy-MM-dd";
        _startPicker.Value = now;
        _endPicker.Format = DateTimePickerFormat.Custom;
        _endPicker.CustomFormat = "yyyy-MM-dd";
        _endPicker.Value = now.AddDays(30);

        _endCheck.CheckedChanged += (_, _) => _endPicker.Enabled = _endCheck.Checked;

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
        Row("开始日期", _startPicker, 180);

        _endCheck.Location = new Point(xl, y + 3);
        body.Controls.Add(_endCheck);
        _endPicker.Location = new Point(xl + 150, y);
        _endPicker.Width = 150;
        body.Controls.Add(_endPicker);
        y += 34;

        var hint = new Label
        {
            Text = "提示:本期记的流水会自动归属本周期(按日期找进行中周期)。",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(xl, y + 4)
        };
        body.Controls.Add(hint);
        y += 34;

        var ok = new Button { Text = "建立", Width = 84, DialogResult = DialogResult.None };
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
    }

    private void TryAccept()
    {
        if (_nameBox.Text.Trim().Length == 0)
        {
            _errorLabel.Text = "请填写周期名称(如「生活费」)。";
            return;
        }

        if (_endCheck.Checked && _endPicker.Value.Date < _startPicker.Value.Date)
        {
            _errorLabel.Text = "结束日期不能早于开始日期。";
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
