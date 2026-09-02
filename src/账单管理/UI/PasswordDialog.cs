using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>口令输入小对话框(打开账本/启动自动加载时用)。</summary>
internal sealed class PasswordDialog : Form
{
    private readonly TextBox _passwordBox;

    public string Password => _passwordBox.Text;

    public PasswordDialog(string ledgerPath)
    {
        Text = "输入口令";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(400, 150);

        var info = new Label
        {
            Text = $"账本「{Path.GetFileName(ledgerPath)}」需要口令:",
            Location = new Point(16, 16),
            AutoSize = true
        };

        _passwordBox = new TextBox
        {
            Location = new Point(16, 42),
            Width = 368,
            UseSystemPasswordChar = true,
            Font = new Font("Microsoft YaHei UI", 11f)
        };

        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(240, 80), Width = 70 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(316, 80), Width = 70 };

        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { info, _passwordBox, ok, cancel });
        Shown += (_, _) =>
        {
            _passwordBox.Focus();
            _passwordBox.SelectAll();
        };
    }
}
