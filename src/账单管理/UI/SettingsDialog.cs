using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 设置(工具菜单):凌晨宽限开关 + 数据目录信息。
/// 偏好存 %APPDATA%\账单管理\app.json;口令等敏感内容不落文件。
/// </summary>
internal sealed class SettingsDialog : FormBase
{
    private readonly AppSettings _settings;
    private readonly CheckBox _graceCheck;

    public SettingsDialog(AppSettings settings)
    {
        _settings = settings;

        Text = "设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(500, 300);

        var body = new Panel { Dock = DockStyle.Fill };
        const int xl = 18, xf = 22;
        int y = 18;

        // 凌晨宽限
        _graceCheck = new CheckBox
        {
            Text = "凌晨宽限:0:00~6:00 记一笔时默认记到「昨天」",
            Checked = _settings.MidnightGraceEnabled,
            AutoSize = true,          // 长文本不被默认窄宽度截断(实测曾只显示「凌晨宽」)
            Location = new Point(xl, y)
        };
        var graceHint = new Label
        {
            Text = "避免过了 0 点就把晚归/宵夜的账挂到新的一天。",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(xf + 2, y + 26)
        };
        body.Controls.Add(_graceCheck);
        body.Controls.Add(graceHint);
        y += 60;

        // 分隔线信息:数据都在哪
        var infoTitle = new Label
        {
            Text = "数据位置(随程序迁移,不影响用户数据)",
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(xl, y)
        };
        body.Controls.Add(infoTitle);
        y += 30;

        void Info(string text, string value)
        {
            var label = new Label { Text = text, AutoSize = true, Location = new Point(xl, y) };
            var v = new Label
            {
                Text = value,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(xf, y),
                MaximumSize = new Size(ClientSize.Width - xf - 24, 0)
            };
            body.Controls.Add(label);
            body.Controls.Add(v);
            y += 34;
        }

        Info("账本目录:", AppPaths.UserDataDir);
        Info("报表目录:", AppPaths.ReportDir);
        Info("偏好文件:", AppPaths.SettingsFile);

        var ok = new Button { Text = "保存", Width = 84, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel };
        ok.Location = new Point(ClientSize.Width - 18 - 84 * 2 - 8, ClientSize.Height - 46);
        cancel.Location = new Point(ClientSize.Width - 18 - 84, ClientSize.Height - 46);
        ok.Click += (_, _) => SaveAndClose();
        AcceptButton = ok;
        CancelButton = cancel;
        body.Controls.Add(ok);
        body.Controls.Add(cancel);
        Controls.Add(body);
    }

    private void SaveAndClose()
    {
        _settings.MidnightGraceEnabled = _graceCheck.Checked;
        _settings.Save();
        DialogResult = DialogResult.OK;
    }
}
