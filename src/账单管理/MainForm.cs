using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>主窗体(骨架版:文件菜单 + 状态栏;账本新建/打开后续 commit 接入)。</summary>
internal sealed class MainForm : Form
{
    private readonly ToolStripStatusLabel _statusLabel = new();

    public MainForm()
    {
        Text = "账单管理";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 720);

        // 菜单:文件 → 退出(其余项后续接入)
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.Add(new ToolStripMenuItem("退出(&X)", null, (_, _) => Close()));
        menu.Items.Add(file);
        MainMenuStrip = menu;
        Controls.Add(menu);

        // 状态栏
        _statusLabel.Text = "尚未打开账本";
        var status = new StatusStrip();
        status.Items.Add(_statusLabel);
        Controls.Add(status);

        // 空态占位(账本功能上线后替换为周期视图)
        var hint = new Label
        {
            Text = "尚无账本 —— 请通过菜单「文件 → 新建账本」开始",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Microsoft YaHei UI", 12f)
        };
        Controls.Add(hint);
        hint.BringToFront();
    }
}
