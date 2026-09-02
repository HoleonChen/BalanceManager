using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 新建账本向导(单页表单):账本名 + 存放位置 + 口令设定。
/// 点「创建」由调用方真正建库(此处只负责收集与校验)。
/// </summary>
internal sealed class CreateLedgerWizard : Form
{
    private const int MinPasswordLength = 6;

    private readonly TextBox _nameBox = new();
    private readonly TextBox _pathBox = new();
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirmBox = new() { UseSystemPasswordChar = true };
    private readonly Label _errorLabel = new() { ForeColor = Color.Firebrick, AutoSize = true };
    private Button _okButton = null!;
    private readonly Panel _body = new() { Dock = DockStyle.Fill };

    private int _y = 18;
    private string? _validatedPath;

    public string LedgerName => _nameBox.Text.Trim();

    /// <summary>校验通过后的完整路径(含 .lbook 后缀)。</summary>
    public string FilePath => _validatedPath ?? _pathBox.Text.Trim();

    public string Password => _passwordBox.Text;

    public CreateLedgerWizard()
    {
        Text = "新建账本";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(460, 280);

        _nameBox.Text = "我的账本";
        _pathBox.Text = Path.Combine(AppPaths.UserDataDir, "我的账本.lbook");

        BuildUi();
        Controls.Add(_body);
    }

    private void BuildUi()
    {
        var browse = new Button { Text = "浏览…" };
        browse.Click += (_, _) => PickPath();

        AddRow("账本名称", _nameBox, 300);
        AddRow("保存为", _pathBox, 200, browse);
        AddRow("口令", _passwordBox, 300);
        AddRow("确认口令", _confirmBox, 300);

        var warning = new Label
        {
            Text = "口令即密钥,遗忘无法找回,请务必记牢。",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(18, _y + 4)
        };
        _body.Controls.Add(warning);

        // 按钮与错误提示用确定坐标(不依赖尚未参与布局的 AutoSize 尺寸)
        int buttonsY = _y + 42;
        _okButton = new Button { Text = "创建", Width = 84, Location = new Point(ClientSize.Width - 18 - 84 * 2 - 8, buttonsY) };
        var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel, Location = new Point(ClientSize.Width - 18 - 84, buttonsY) };
        _errorLabel.Location = new Point(18, buttonsY + 34);

        _okButton.Click += (_, _) => TryAccept();
        AcceptButton = _okButton;
        CancelButton = cancel;

        _nameBox.TextChanged += ClearError;
        _pathBox.TextChanged += ClearError;
        _passwordBox.TextChanged += ClearError;
        _confirmBox.TextChanged += ClearError;

        _body.Controls.AddRange(new Control[] { _okButton, cancel, _errorLabel });
    }

    private void AddRow(string labelText, Control field, int fieldWidth, Control? extra = null)
    {
        var label = new Label { Text = labelText, Location = new Point(18, _y + 3), AutoSize = true };
        field.Location = new Point(118, _y);
        field.Width = fieldWidth;
        _body.Controls.Add(label);
        _body.Controls.Add(field);
        if (extra is not null)
        {
            extra.Location = new Point(118 + fieldWidth + 8, _y);
            extra.Width = 84;
            _body.Controls.Add(extra);
        }
        _y += 36;
    }

    private void PickPath()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "保存账本到…",
            Filter = "账本文件 (*.lbook)|*.lbook",
            DefaultExt = "lbook",
            FileName = _nameBox.Text.Trim() + ".lbook",
            InitialDirectory = AppPaths.UserDataDir,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _pathBox.Text = dlg.FileName;
    }

    private void ClearError(object? sender, EventArgs e) => _errorLabel.Text = string.Empty;

    private void TryAccept()
    {
        var name = LedgerName;
        if (name.Length == 0)
        {
            _errorLabel.Text = "请填写账本名称。";
            return;
        }

        if (_passwordBox.Text.Length < MinPasswordLength)
        {
            _errorLabel.Text = $"口令至少 {MinPasswordLength} 位。";
            return;
        }

        if (_passwordBox.Text != _confirmBox.Text)
        {
            _errorLabel.Text = "两次输入的口令不一致。";
            return;
        }

        string full;
        try
        {
            var path = _pathBox.Text.Trim();
            if (path.Length == 0)
                throw new FormatException("路径为空。");
            if (!Path.HasExtension(path))
                path += ".lbook";
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            _errorLabel.Text = $"保存位置无效:{ex.Message}";
            return;
        }

        if (File.Exists(full))
        {
            _errorLabel.Text = $"文件已存在:{full}\n请换一个名称或位置。";
            return;
        }

        _validatedPath = full;
        DialogResult = DialogResult.OK;
    }
}
