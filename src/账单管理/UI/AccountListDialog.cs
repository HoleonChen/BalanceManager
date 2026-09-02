using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 账户管理:列出全部账户(名称/平台/类型/入账余额;停用的灰显),可新建、停用、重新启用。
/// 停用后不再出现在记账/转账下拉;账户表不物理删除(流水外键约束 + 历史归属)。
/// </summary>
internal sealed class AccountListDialog : FormBase
{
    private readonly LedgerSession _ledger;
    private readonly Label _summaryLabel = new() { AutoSize = true };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.FixedSingle,
        HideSelection = false
    };

    public AccountListDialog(LedgerSession ledger)
    {
        _ledger = ledger;
        Text = "账户管理";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 460);
        MinimumSize = new Size(560, 360);

        _list.Columns.Add("名称", 190);
        _list.Columns.Add("平台", 90);
        _list.Columns.Add("类型", 150);
        _list.Columns.Add("当前余额(派生)", 110, HorizontalAlignment.Right);
        _list.Columns.Add("本周期变动", 110, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => ToggleEnabled();

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(10, 5, 10, 0)
        };
        var create = new Button { Text = "＋ 新建账户…", Width = 122, Height = 30 };
        create.Click += (_, _) => CreateAccount();
        var detail = new Button { Text = "详情…", Width = 76, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        detail.Click += (_, _) => ShowDetail();
        var disable = new Button { Text = "停用所选", Width = 88, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        disable.Click += (_, _) => DisableSelected();
        var enable = new Button { Text = "启用所选", Width = 88, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        enable.Click += (_, _) => EnableSelected();
        var calibrate = new Button { Text = "校准所选…", Width = 96, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        calibrate.Click += (_, _) => CalibrateSelected();
        top.Controls.AddRange(new Control[] { create, detail, disable, enable, calibrate });

        _summaryLabel.Location = new Point(14, 6);
        var summary = new Panel { Dock = DockStyle.Top, Height = 30 };
        summary.Controls.Add(_summaryLabel);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 12, 6)
        };
        var close = new Button { Text = "关闭", Width = 84, Height = 30, DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(close);

        Controls.Add(_list);
        Controls.Add(bottom);
        Controls.Add(summary);
        Controls.Add(top);

        RefreshList();
        RefreshSummary();
    }

    /// <summary>顶栏:净资产合计(启用账户)+ 账户数;双击行停用/启用。</summary>
    private void RefreshSummary()
    {
        var all = Accounts.ListAll(_ledger);
        int enabledCount = 0;
        foreach (var a in all)
        {
            if (a.Enabled)
                enabledCount++;
        }
        _summaryLabel.Text =
            $"净资产合计(启用账户):{Money.Yuan(Accounts.NetAssets(_ledger))}   启用 {enabledCount}/{all.Count} 个账户   双击行停用/启用 · 停用不计净资产";
    }

    /// <summary>本周期(覆盖今天的进行中周期)该账户净变动;无则空。</summary>
    private static string PeriodNetText(LedgerSession ledger, long accountId)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(ledger, today);
        if (p is null)
            return "";
        var to = p.EndDate ?? today;
        var mv = Accounts.MovementBetween(ledger, accountId, p.StartDate, to);
        return mv.NetCents == 0 ? "—" : Signed(mv.NetCents);
    }

    private static string Signed(long v)
        => (v > 0 ? "+" : v < 0 ? "-" : "") + Money.Yuan(Math.Abs(v));

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var a in Accounts.ListAll(_ledger))
        {
            var li = new ListViewItem(a.Enabled ? a.Name : a.Name + "(已停用)");
            if (!a.Enabled)
                li.ForeColor = SystemColors.GrayText;
            li.SubItems.Add(a.Platform.Length == 0 ? "—" : a.Platform);
            li.SubItems.Add(AccountDialog.TypeLabel(a.Type));
            li.SubItems.Add(Money.Yuan(AccountCalibration.BookCents(_ledger, a.Id)));
            li.SubItems.Add(PeriodNetText(_ledger, a.Id));
            li.Tag = a;
            _list.Items.Add(li);
        }
        _list.EndUpdate();
    }

    /// <summary>账户详情:余额构成 + 本周期收支转 + 流水 + 校准入口。</summary>
    private void ShowDetail()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not AccountRow a)
            return;
        using var dlg = new AccountDetailDialog(_ledger, a.Id);
        dlg.ShowDialog(this);
        RefreshList();
        RefreshSummary();
    }

    private void CreateAccount()
    {
        using var dlg = new AccountDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        Accounts.Insert(_ledger, dlg.AccountName, dlg.TypeKey, dlg.Platform, dlg.BalanceBaseCents);
        RefreshList();
    }

    private void DisableSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not AccountRow a || !a.Enabled)
            return;
        if (MessageBox.Show(this,
                $"停用账户「{a.Name}」?\n\n它将不再出现在记账/转账的下拉里;已记流水保留不受影响。",
                "停用账户", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;
        Accounts.Disable(_ledger, a.Id);
        RefreshList();
        RefreshSummary();
    }

    private void EnableSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not AccountRow a || a.Enabled)
            return;
        Accounts.Enable(_ledger, a.Id);
        RefreshList();
        RefreshSummary();
    }

    private void ToggleEnabled()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not AccountRow a)
            return;
        if (a.Enabled)
            DisableSelected();
        else
            EnableSelected();
    }

    /// <summary>校准所选账户余额(对准实际;含审计历史);完成后刷新余额列与净资产合计。</summary>
    private void CalibrateSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not AccountRow a)
            return;
        CalibrationDialog.Run(this, _ledger, a.Id);
        RefreshList();
        RefreshSummary();
    }
}
