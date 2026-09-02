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
    private ToolStripMenuItem _newPeriodItem = null!;
    private ToolStripMenuItem _accountsItem = null!;
    private ToolStripMenuItem _categoriesItem = null!;
    private ToolStripMenuItem _calibrateItem = null!;
    private ToolStripMenuItem _flowItem = null!;
    private ToolStripMenuItem _poolItem = null!;
    private ToolStripMenuItem _sealItem = null!;
    private ToolStripMenuItem _periodsItem = null!;
    private LedgerSession? _ledger;

    private Panel _home = null!;
    private Label _hintLabel = null!;
    private Label _summaryLabel = null!;
    private Label _dateLabel = null!;
    private Label _periodChip = null!;
    private Label _poolLabel = null!;
    private ListView _todayList = null!;
    private DateTime _viewDate;

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
        _newPeriodItem = MakeItem("新建记账周期…", null, (_, _) => OnNewPeriod());
        _newPeriodItem.Enabled = false;
        file.DropDownItems.Add(_newPeriodItem);
        _closeLedgerItem = MakeItem("关闭账本", null, (_, _) => CloseLedger());
        _closeLedgerItem.Enabled = false;
        file.DropDownItems.Add(_closeLedgerItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MakeItem("打开账本目录…", null, (_, _) => OpenDataDir()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MakeItem("退出(&X)", null, (_, _) => Close()));

        var tools = new ToolStripMenuItem("工具(&T)");
        tools.DropDownItems.Add(MakeItem("设置…", null, (_, _) => OnSettings()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        _accountsItem = MakeItem("账户管理…", null, (_, _) => OnManageAccounts());
        _accountsItem.Enabled = false;
        tools.DropDownItems.Add(_accountsItem);
        _categoriesItem = MakeItem("分类管理…", null, (_, _) => OnManageCategories());
        _categoriesItem.Enabled = false;
        tools.DropDownItems.Add(_categoriesItem);
        _calibrateItem = MakeItem("校准余额…", null, (_, _) => OnCalibrate());
        _calibrateItem.Enabled = false;
        tools.DropDownItems.Add(_calibrateItem);
        _flowItem = MakeItem("查看本周期流水…", null, (_, _) => OnViewFlow());
        _flowItem.Enabled = false;
        tools.DropDownItems.Add(_flowItem);
        _poolItem = MakeItem("设置资金池…", null, (_, _) => OnPool());
        _poolItem.Enabled = false;
        tools.DropDownItems.Add(_poolItem);
        _sealItem = MakeItem("封存当前周期…", null, (_, _) => OnSealCurrentPeriod());
        _sealItem.Enabled = false;
        tools.DropDownItems.Add(_sealItem);
        _periodsItem = MakeItem("周期管理…", null, (_, _) => OnManagePeriods());
        _periodsItem.Enabled = false;
        tools.DropDownItems.Add(_periodsItem);
        tools.DropDownItems.Add(new ToolStripSeparator());
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

    /// <summary>打开账本后的主区:单日流水(记一笔 + 日期导航 + 合计 + 列表)。周期总览后续替换/扩展。</summary>
    private void BuildHome()
    {
        _home = new Panel { Dock = DockStyle.Fill, Visible = false };

        // 顶栏:记一笔 | ◀ 日期 ▶ 今天 | 合计
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(10, 10, 10, 0)
        };

        var record = new Button
        {
            Text = "＋ 记一笔",
            Width = 118,
            Height = 32,
            Font = new Font("Microsoft YaHei UI", 11f),
            Margin = new Padding(0, 0, 14, 0)
        };
        record.Click += (_, _) => OnRecordOne();

        var transfer = new Button
        {
            Text = "⇄ 转账",
            Width = 96,
            Height = 32,
            Margin = new Padding(0, 0, 14, 0),
            Font = new Font("Microsoft YaHei UI", 11f)
        };
        transfer.Click += (_, _) => OnTransfer();

        var prev = new Button { Text = "◀", Width = 34, Height = 30 };
        prev.Click += (_, _) => { _viewDate = _viewDate.AddDays(-1); RefreshView(); };

        _dateLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 12f),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(6, 5, 6, 0)
        };

        var next = new Button { Text = "▶", Width = 34, Height = 30 };
        next.Click += (_, _) => { _viewDate = _viewDate.AddDays(1); RefreshView(); };

        var today = new Button { Text = "今天", Width = 56, Height = 30, Margin = new Padding(8, 0, 18, 0) };
        today.Click += (_, _) => { _viewDate = DateTime.Today; RefreshView(); };

        _periodChip = new Label
        {
            AutoSize = true,
            ForeColor = Color.SteelBlue,
            Margin = new Padding(6, 10, 0, 0),
            Cursor = Cursors.Hand
        };
        _periodChip.Click += (_, _) => OnPeriodChipClick();

        _summaryLabel = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(12, 10, 0, 0)
        };

        _poolLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.SteelBlue,
            Margin = new Padding(14, 10, 0, 0),
            Cursor = Cursors.Hand
        };
        _poolLabel.Click += (_, _) => OnPool();

        top.Controls.AddRange(new Control[] { record, transfer, prev, _dateLabel, next, today, _periodChip, _summaryLabel, _poolLabel });

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

        // 右键:删除/作废一笔(先选中光标下的行)
        var ctx = new ContextMenuStrip();
        var delete = new ToolStripMenuItem("作废/删除这笔…");
        delete.Click += (_, _) => DeleteSelected();
        ctx.Items.Add(delete);
        _todayList.ContextMenuStrip = ctx;
        _todayList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelected();
                e.Handled = true;
            }
        };
        _todayList.DoubleClick += (_, _) => EditSelected();
        _todayList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
                return;
            _todayList.SelectedItems.Clear();
            var hit = _todayList.GetItemAt(e.X, e.Y);
            if (hit != null)
                hit.Selected = true;
        };

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
        _newPeriodItem.Enabled = false;
        _accountsItem.Enabled = false;
        _categoriesItem.Enabled = false;
        _calibrateItem.Enabled = false;
        _flowItem.Enabled = false;
        _poolItem.Enabled = false;
        _sealItem.Enabled = false;
        _periodsItem.Enabled = false;
        Text = "账单管理";
        _statusLabel.Text = "尚未打开账本";
        HideHome();
    }

    private void SetLedger(LedgerSession session)
    {
        _ledger?.Dispose();
        _ledger = session;
        _closeLedgerItem.Enabled = true;
        _newPeriodItem.Enabled = true;
        _accountsItem.Enabled = true;
        _categoriesItem.Enabled = true;
        _calibrateItem.Enabled = true;
        _flowItem.Enabled = true;
        _poolItem.Enabled = true;
        _sealItem.Enabled = true;
        _periodsItem.Enabled = true;
        Text = $"{session.Name} —— 账单管理";
        _statusLabel.Text = $"已打开:{session.Path}";
        ShowHome();
    }

    private void ShowHome()
    {
        _viewDate = DateTime.Today;
        _home.Visible = true;
        _hintLabel.Visible = false;
        RefreshView();
        RefreshPeriodChip();
    }

    /// <summary>新建记账周期;此后记的流水按日期自动归属(见 Transactions.Add)。</summary>
    private void OnNewPeriod()
    {
        if (_ledger is null)
            return;
        using var dlg = new PeriodDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        // 与进行中周期日期重叠时提醒(可并存,但同日流水只归属开始最晚者)
        foreach (var p in Periods.ListActive(_ledger))
        {
            if (PeriodOverlaps(p, dlg.StartDate, dlg.EndDate))
            {
                var rng = $"{p.StartDate}~{(p.EndDate is null ? "长期" : p.EndDate)}";
                if (MessageBox.Show(this,
                        $"进行中周期「{p.Name}」({rng})与新周期日期重叠。\n\n重叠期间同一天的流水只自动归属到开始较晚的周期。确定继续建立?",
                        "日期重叠提醒", MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
                break;
            }
        }

        try
        {
            Periods.Insert(_ledger, dlg.PeriodName, dlg.StartDate, dlg.EndDate);
            RefreshPeriodChip();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"建立周期失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>两个区间(任一 end 为 null = 长期)是否重叠。</summary>
    private static bool PeriodOverlaps(PeriodRow p, string start, string? end)
    {
        var s = DateTime.Parse(p.StartDate);
        var e = p.EndDate is null ? DateTime.MaxValue : DateTime.Parse(p.EndDate);
        var ns = DateTime.Parse(start);
        var ne = end is null ? DateTime.MaxValue : DateTime.Parse(end);
        return s <= ne && e >= ns;
    }

    /// <summary>设置窗口:凌晨宽限等全局偏好。</summary>
    private void OnSettings()
    {
        using var dlg = new SettingsDialog(_settings);
        dlg.ShowDialog(this);
    }

    /// <summary>在资源管理器打开账本目录(默认文档\账单管理)。</summary>
    private static void OpenDataDir()
    {
        AppPaths.EnsureDirs();
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", AppPaths.UserDataDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(null, $"打开目录失败:\n{ex.Message}",
                "打开账本目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>账户管理窗口:列出/新建/停用账户。</summary>
    private void OnManageAccounts()
    {
        if (_ledger is null)
            return;
        using var dlg = new AccountListDialog(_ledger);
        dlg.ShowDialog(this);
    }

    /// <summary>分类管理:新建/重命名/改色/关键词/排序/合并/删除。</summary>
    private void OnManageCategories()
    {
        if (_ledger is null)
            return;
        using var dlg = new CategoryManageDialog(_ledger);
        dlg.ShowDialog(this);
    }

    /// <summary>校准余额(对准某账户实际;含审计历史)。</summary>
    private void OnCalibrate()
    {
        if (_ledger is null)
            return;
        CalibrationDialog.Run(this, _ledger);
    }

    /// <summary>查看覆盖今天的进行中周期的整期流水(只读总览)。</summary>
    private void OnViewFlow()
    {
        if (_ledger is null)
            return;

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(_ledger, today);
        if (p is null)
        {
            MessageBox.Show(this,
                "当前没有覆盖今天的进行中周期。\n请先在「文件 → 新建记账周期」建立周期,即可按周期查看流水。",
                "账单管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (p.EndDate is null)
        {
            MessageBox.Show(this,
                $"周期「{p.Name}」没有计划结束日期,暂无法框定查看范围。\n(长期周期查看可后续补)",
                "账单管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new PeriodFlowDialog(_ledger, p.Name, p.StartDate, p.EndDate);
        dlg.ShowDialog(this);
    }

    /// <summary>设置资金池:作用于覆盖今天的进行中周期(补建/修改池)。</summary>
    private void OnPool()
    {
        if (_ledger is null)
            return;

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(_ledger, today);
        if (p is null)
        {
            MessageBox.Show(this,
                "资金池绑定在记账周期上。当前没有覆盖今天的进行中周期。\n请先在「文件 → 新建记账周期」建立周期,即可为本周期设置资金池。",
                "账单管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new PoolDialog(_ledger, p, Pools.Get(_ledger, p.Id));
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Pools.Save(_ledger, p.Id, dlg.PoolName, dlg.AccountId, dlg.BudgetCents, dlg.ReserveCents);
            RefreshPoolLabel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存资金池失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>顶栏资金池标签:覆盖今天周期的池的 剩余/可支配;未设池给出灰色提示(可点设置)。</summary>
    private void RefreshPoolLabel()
    {
        if (_ledger is null)
        {
            _poolLabel.Text = string.Empty;
            return;
        }

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(_ledger, today);
        if (p is null)
        {
            _poolLabel.Text = string.Empty;
            return;
        }

        var pool = Pools.Get(_ledger, p.Id);
        if (pool is null)
        {
            _poolLabel.ForeColor = SystemColors.GrayText;
            _poolLabel.Text = "· 资金池未设置(点击设置)";
            return;
        }

        _poolLabel.ForeColor = Color.SteelBlue;
        var st = Pools.State(_ledger, pool);
        _poolLabel.Text =
            $"· 池:剩余 {Money.Yuan(st.RemainingCents)} / 可支配 {Money.Yuan(st.DisposableCents)}";
    }

    /// <summary>顶栏周期 chip:覆盖今天的进行中周期;无覆盖但已有到期未封存 → 橙色提示;都无 → 灰色提示。</summary>
    private void RefreshPeriodChip()
    {
        if (_ledger is null)
        {
            _periodChip.Text = string.Empty;
            return;
        }

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(_ledger, today);
        if (p is not null)
        {
            _periodChip.ForeColor = Color.SteelBlue;
            _periodChip.Text =
                $"· 周期:{p.Name}({ShortDate(p.StartDate)}~{(p.EndDate is null ? "长期" : ShortDate(p.EndDate))})";
            return;
        }

        var expired = Periods.GetLatestExpiredActive(_ledger, today);
        if (expired is not null)
        {
            _periodChip.ForeColor = Color.DarkOrange;
            _periodChip.Text = $"· 上一周期「{expired.Name}」已到期未封存(点此封存/新建)";
            return;
        }

        _periodChip.ForeColor = SystemColors.GrayText;
        _periodChip.Text = "· 无进行中周期(点击新建)";
    }

    /// <summary>点周期 chip:覆盖今天的周期 → 看本期流水;否则进周期管理(封存/新建)。</summary>
    private void OnPeriodChipClick()
    {
        if (_ledger is null)
            return;
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (Periods.GetCoveringActive(_ledger, today) is not null)
        {
            OnViewFlow();
            return;
        }
        OnManagePeriods();
    }

    /// <summary>封存当前周期(覆盖今天的进行中周期;无则提示)。</summary>
    private void OnSealCurrentPeriod()
    {
        if (_ledger is null)
            return;
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(_ledger, today);
        if (p is null)
        {
            MessageBox.Show(this,
                "当前没有覆盖今天的进行中周期可封存。\n请到「工具 → 周期管理」查看并处理(含已到期未封存的周期)。",
                "账单管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        PeriodManageDialog.SealFlow(this, _ledger, p);
        RefreshPeriodChip();
        RefreshView();
    }

    /// <summary>周期管理:列出全部周期,可封存/解除封存/看流水/新建。</summary>
    private void OnManagePeriods()
    {
        if (_ledger is null)
            return;
        using var dlg = new PeriodManageDialog(_ledger);
        dlg.ShowDialog(this);
        RefreshPeriodChip();
        RefreshView();
    }

    private static string ShortDate(string iso)
    {
        var p = iso.Split('-');
        return $"{int.Parse(p[1])}月{int.Parse(p[2])}日";
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

            // 视图跳到该笔所在日期(例如凌晨宽限记到昨天,补录后立刻可见)
            var p = dlg.DateStr.Split('-');
            _viewDate = new DateTime(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]));
            RefreshView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>记转账:转出 −本金、转入 +(本金+浮动),保存后跳到该笔日期。</summary>
    private void OnTransfer()
    {
        if (_ledger is null)
            return;

        using var dlg = new TransferDialog(_ledger, _settings);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Transactions.Transfer(_ledger, new TransferDraft
            {
                Date = dlg.Date.ToString("yyyy-MM-dd"),
                FromAccountId = dlg.FromAccountId,
                ToAccountId = dlg.ToAccountId,
                PrincipalCents = dlg.PrincipalCents,
                DeltaCents = dlg.DeltaCents,
                Kind = dlg.Kind,
                Note = dlg.Note,
                InPool = dlg.InPool
            });

            _viewDate = dlg.Date.Date;
            RefreshView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>刷新当前查看日期(_viewDate)的日期标注 + 流水列表 + 合计。</summary>
    private void RefreshView()
    {
        if (_ledger is null)
            return;

        var day = _viewDate.Date;
        var dateStr = day.ToString("yyyy-MM-dd");
        var diff = (day - DateTime.Today).Days;
        _dateLabel.Text = diff switch
        {
            0 => dateStr + " · 今天",
            1 => dateStr + " · 明天",
            -1 => dateStr + " · 昨天",
            _ => dateStr
        };

        var items = Transactions.ListByDate(_ledger, dateStr);
        var (outCents, inCents) = Transactions.DayTotals(_ledger, dateStr);

        _todayList.BeginUpdate();
        _todayList.Items.Clear();
        foreach (var t in items)
        {
            var li = new ListViewItem(t.Time);
            li.Tag = t;

            if (t.Direction == "transfer")
            {
                // 转账:名称=类别,账户列显示 转出→转入,金额=本金,浮动(Δ)附注
                li.SubItems.Add(t.Name);
                li.SubItems.Add("转账");
                li.SubItems.Add($"{t.Account} → {t.AccountTo}");
                var text = Money.Yuan(t.AmountCents);
                if (t.DeltaCents != 0)
                {
                    var sign = t.DeltaCents > 0 ? "+" : "-";
                    text += $" (Δ{sign}{Money.Yuan(Math.Abs(t.DeltaCents))})";
                }
                var sub = li.SubItems.Add(text);
                sub.ForeColor = Color.DarkSlateBlue;
            }
            else
            {
                li.SubItems.Add(t.Name);
                li.SubItems.Add(t.Category);
                li.SubItems.Add(t.Account);
                var isOut = t.Direction == "out";
                var amountSub = li.SubItems.Add(isOut
                    ? "-" + Money.Yuan(t.AmountCents)
                    : "+" + Money.Yuan(t.AmountCents));
                amountSub.ForeColor = isOut ? Color.Firebrick : Color.ForestGreen;
            }
            _todayList.Items.Add(li);
        }
        _todayList.EndUpdate();

        _summaryLabel.Text =
            $"支出 {Money.Yuan(outCents)} · 收入 {Money.Yuan(inCents)} · {items.Count} 笔";

        RefreshPoolLabel();
    }

    private void DeleteSelected()
    {
        if (_ledger is null || _todayList.SelectedItems.Count == 0)
            return;
        if (_todayList.SelectedItems[0].Tag is not TxnListItem t)
            return;
        if (NotifySealedDate(t.Date))
            return;

        string head = t.Direction == "transfer"
            ? $"作废这笔转账?\n\n  {t.Name} · {t.Account} → {t.AccountTo}\n  {Money.Yuan(t.AmountCents)}"
            : $"作废这笔并撤出统计?\n\n  {t.Name}\n  {(t.Direction == "out" ? "-" : "+")}{Money.Yuan(t.AmountCents)} · {t.Account}";
        if (MessageBox.Show(this,
                head + "\n\n记录仍留在库中(标记作废),只是不再计入统计。",
                "作废流水", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        Transactions.Cancel(_ledger, t.Id);
        RefreshView();
    }

    /// <summary>该日已被封存周期覆盖时提示并返回 true(调用方据此中止写操作,避免异常后置弹出)。</summary>
    private bool NotifySealedDate(string date)
    {
        if (_ledger is not null && Periods.HasSealedCovering(_ledger, date))
        {
            MessageBox.Show(this, LedgerReadonlyException.Friendly(date), "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        return false;
    }

    /// <summary>双击列表行:就地编辑该笔支出/收入;转账走 EditTransfer。</summary>
    private void EditSelected()
    {
        if (_ledger is null || _todayList.SelectedItems.Count == 0)
            return;
        if (_todayList.SelectedItems[0].Tag is not TxnListItem t)
            return;
        if (NotifySealedDate(t.Date))
            return;

        if (t.Direction == "transfer")
        {
            EditTransfer(t.Id);
            return;
        }

        var edit = Transactions.GetEditable(_ledger, t.Id);
        if (edit is null)
            return;

        using var dlg = new RecordDialog(_ledger, _settings, edit);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Transactions.Update(_ledger, edit with
            {
                Direction = dlg.Direction,
                AccountId = dlg.AccountId,
                CategoryId = dlg.CategoryId,
                AmountCents = dlg.AmountCents,
                Name = dlg.TxnName,
                Channel = dlg.Channel,
                Note = dlg.Note,
                InPool = dlg.InPool
            });
            RefreshView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>就地编辑一笔转账(改转出/转入/本金/Δ/类别等;日期固定)。</summary>
    private void EditTransfer(long id)
    {
        if (_ledger is null)
            return;

        var edit = Transactions.GetTransfer(_ledger, id);
        if (edit is null)
            return;

        using var dlg = new TransferDialog(_ledger, _settings, edit);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Transactions.UpdateTransfer(_ledger, edit with
            {
                FromAccountId = dlg.FromAccountId,
                ToAccountId = dlg.ToAccountId,
                PrincipalCents = dlg.PrincipalCents,
                DeltaCents = dlg.DeltaCents,
                Kind = dlg.Kind,
                Note = dlg.Note,
                InPool = dlg.InPool
            });
            RefreshView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
            "账单管理 0.1.0\n\n个人离线账本:SQLCipher 全库加密,口令即密钥。\n\n已具备:记收支/转账(本金+Δ)/记账周期自动归属/\n账户管理(停用·启用)/就地编辑·作废/周期流水总览。\n\n开发:macOS 编写 → Windows 编译运行",
            "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _ledger?.Dispose();
        base.OnFormClosed(e);
    }
}
