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

    private Panel _home = null!;
    private Label _hintLabel = null!;
    private Label _summaryLabel = null!;
    private ListView _todayList = null!;

    public MainForm()
    {
        _settings = AppSettings.Load();
        Text = "账单管理";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 720);

        BuildMenu();
        BuildStatus();
        BuildHint();
        BuildHome();

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

        var tools = new ToolStripMenuItem("工具(&T)");
        tools.DropDownItems.Add(MakeItem("数据自检…", null, (_, _) => DbSelfTest.Run(this)));

        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(MakeItem("关于(&A)", null, (_, _) => ShowAbout()));

        menu.Items.Add(file);
        menu.Items.Add(tools);
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
        _hintLabel = new Label
        {
            Text = "尚无账本 —— 通过「文件 → 新建账本」开始;或「文件 → 打开账本」打开已有 .lbook",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Microsoft YaHei UI", 12f)
        };
        Controls.Add(_hintLabel);
        _hintLabel.BringToFront();
    }

    /// <summary>打开账本后的主区:今日流水(记一笔 + 合计 + 列表)。周期总览后续替换/扩展。</summary>
    private void BuildHome()
    {
        _home = new Panel { Dock = DockStyle.Fill, Visible = false };

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            ColumnCount = 3,
            Padding = new Padding(10, 10, 10, 0)
        };
        var record = new Button { Text = "＋ 记一笔", Width = 120, Height = 34, Font = new Font("Microsoft YaHei UI", 11f) };
        record.Click += (_, _) => OnRecordOne();
        _summaryLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(10, 0, 0, 0) };
        var title = new Label
        {
            Text = "今日流水",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Right,
            Font = new Font("Microsoft YaHei UI", 11f)
        };
        top.Controls.Add(record, 0, 0);
        top.Controls.Add(_summaryLabel, 1, 0);
        top.Controls.Add(title, 2, 0);
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        _todayList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            BorderStyle = BorderStyle.FixedSingle,
            HideSelection = false
        };
        _todayList.Columns.Add("时间", 70);
        _todayList.Columns.Add("名称", 220);
        _todayList.Columns.Add("分类", 130);
        _todayList.Columns.Add("账户", 180);
        _todayList.Columns.Add("金额", 140, HorizontalAlignment.Right);

        _home.Controls.Add(_todayList);
        _home.Controls.Add(top);
        Controls.Add(_home);
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
        HideHome();
    }

    private void SetLedger(LedgerSession session)
    {
        _ledger?.Dispose();
        _ledger = session;
        _closeLedgerItem.Enabled = true;
        Text = $"{session.Name} —— 账单管理";
        _statusLabel.Text = $"已打开:{session.Path}";
        ShowHome();
    }

    private void ShowHome()
    {
        _home.Visible = true;
        _hintLabel.Visible = false;
        RefreshToday();
    }

    private void HideHome()
    {
        _home.Visible = false;
        _hintLabel.Visible = true;
    }

    private void OnRecordOne()
    {
        if (_ledger is null)
            return;

        using var dlg = new RecordDialog(_ledger, _settings);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Transactions.Add(_ledger, new TxnDraft
            {
                Date = dlg.DateStr,
                Direction = dlg.Direction,
                AccountId = dlg.AccountId,
                CategoryId = dlg.CategoryId,
                AmountCents = dlg.AmountCents,
                Name = dlg.TxnName,
                Note = dlg.Note,
                Channel = dlg.Channel,
                InPool = dlg.InPool
            });
            RefreshToday();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshToday()
    {
        if (_ledger is null)
            return;

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var items = Transactions.ListByDate(_ledger, today);
        var (outCents, inCents) = Transactions.DayTotals(_ledger, today);

        _todayList.BeginUpdate();
        _todayList.Items.Clear();
        foreach (var t in items)
        {
            var li = new ListViewItem(t.Time);
            li.SubItems.Add(t.Name);
            li.SubItems.Add(t.Category);
            li.SubItems.Add(t.Account);
            var isOut = t.Direction == "out";
            var amountSub = li.SubItems.Add(isOut
                ? "-" + Money.Yuan(t.AmountCents)
                : "+" + Money.Yuan(t.AmountCents));
            amountSub.ForeColor = isOut ? Color.Firebrick : Color.ForestGreen;
            _todayList.Items.Add(li);
        }
        _todayList.EndUpdate();

        _summaryLabel.Text =
            $"今日支出 {Money.Yuan(outCents)} · 今日收入 {Money.Yuan(inCents)} · {items.Count} 笔";
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
