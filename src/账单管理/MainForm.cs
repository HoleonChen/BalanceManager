using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 主窗体:文件菜单(新建/打开/关闭/退出)+ 状态栏。
/// 启动时自动加载上次账本(见设计文档「账本文件与启动」)。
/// </summary>
internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly ToolStripStatusLabel _statusLabel = new();
    private ToolStripMenuItem _closeLedgerItem = null!;
    private LedgerSession? _ledger;

    public MainForm()
    {
        _settings = AppSettings.Load();
        Text = "账单管理";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 720);

        BuildMenu();
        BuildStatus();
        BuildHint();

        TryAutoLoadLastLedger();
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.Add(MakeItem("新建账本(&N)…", Keys.Control | Keys.N, OnNewLedger));
        file.DropDownItems.Add(MakeItem("打开账本(&O)…", Keys.Control | Keys.O, OnOpenLedger));
        _closeLedgerItem = MakeItem("关闭账本", null, (_, _) => CloseLedger());
        _closeLedgerItem.Enabled = false;
        file.DropDownItems.Add(_closeLedgerItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MakeItem("退出(&X)", null, (_, _) => Close()));

        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(MakeItem("关于(&A)", null, (_, _) => ShowAbout()));

        menu.Items.Add(file);
        menu.Items.Add(help);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private void BuildStatus()
    {
        _statusLabel.Text = "尚未打开账本";
        var status = new StatusStrip();
        status.Items.Add(_statusLabel);
        Controls.Add(status);
    }

    private void BuildHint()
    {
        var hint = new Label
        {
            Text = "尚无账本 —— 通过「文件 → 新建账本」开始;或「文件 → 打开账本」打开已有 .lbook",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Microsoft YaHei UI", 12f)
        };
        Controls.Add(hint);
        hint.BringToFront();
    }

    // ---------- 动作 ----------

    private void OnNewLedger(object? sender, EventArgs e)
    {
        using var wizard = new CreateLedgerWizard();
        if (wizard.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var session = LedgerStore.Create(wizard.FilePath, wizard.LedgerName, wizard.Password);
            SetLedger(session);
            Remember(session.Path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"新建账本失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnOpenLedger(object? sender, EventArgs e)
    {
        using var pick = new OpenFileDialog
        {
            Title = "打开账本",
            Filter = "账本文件 (*.lbook)|*.lbook|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = AppPaths.UserDataDir
        };
        if (pick.ShowDialog(this) != DialogResult.OK)
            return;

        PromptAndOpen(pick.FileName);
    }

    private void TryAutoLoadLastLedger()
    {
        var path = _settings.LastLedgerPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        PromptAndOpen(path);
    }

    /// <summary>弹口令框并打开;用户取消则不动作。</summary>
    private void PromptAndOpen(string path)
    {
        using var dlg = new PasswordDialog(path);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        OpenLedger(path, dlg.Password);
    }

    private void OpenLedger(string path, string password)
    {
        try
        {
            var session = LedgerStore.Open(path, password);
            SetLedger(session);
            Remember(session.Path);
        }
        catch (LedgerPasswordException)
        {
            MessageBox.Show(this,
                "口令错误,无法打开该账本。\n口令即密钥,遗忘无法找回。",
                "账单管理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(this, ex.Message, "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"打开账本失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseLedger()
    {
        _ledger?.Dispose();
        _ledger = null;
        _closeLedgerItem.Enabled = false;
        Text = "账单管理";
        _statusLabel.Text = "尚未打开账本";
    }

    private void SetLedger(LedgerSession session)
    {
        _ledger?.Dispose();
        _ledger = session;
        _closeLedgerItem.Enabled = true;
        Text = $"{session.Name} —— 账单管理";
        _statusLabel.Text = $"已打开:{session.Path}";
    }

    private void Remember(string path)
    {
        _settings.LastLedgerPath = path;
        _settings.Save();
    }

    // ---------- 杂项 ----------

    private static ToolStripMenuItem MakeItem(string text, Keys? shortcut, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text, null, handler);
        if (shortcut.HasValue)
            item.ShortcutKeys = shortcut.Value;
        return item;
    }

    private static void ShowAbout()
    {
        MessageBox.Show(
            "账单管理 0.1.0(骨架)\n\n个人离线账本:SQLCipher 全库加密,\n按「记账周期」管理收支。\n\n开发:macOS 编写 → Windows 编译运行",
            "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _ledger?.Dispose();
        base.OnFormClosed(e);
    }
}
