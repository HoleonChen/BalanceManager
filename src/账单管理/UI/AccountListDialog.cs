using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 账户管理:列出启用账户(名称/平台/类型/入账余额),可新建、可停用。
/// 停用后不再出现在记账/转账下拉;账户表不物理删除(流水外键约束 + 历史归属)。
/// </summary>
internal sealed class AccountListDialog : Form
{
    private readonly LedgerSession _ledger;
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
        Size = new Size(560, 380);
        MinimumSize = new Size(500, 300);

        _list.Columns.Add("名称", 170);
        _list.Columns.Add("平台", 90);
        _list.Columns.Add("类型", 150);
        _list.Columns.Add("入账余额", 110, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => DisableSelected();

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(10, 6, 10, 0)
        };
        var create = new Button { Text = "＋ 新建账户…", Width = 120, Height = 30 };
        create.Click += (_, _) => CreateAccount();
        var disable = new Button { Text = "停用所选", Width = 90, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        disable.Click += (_, _) => DisableSelected();
        var hint = new Label
        {
            Text = "停用 = 移出下拉,不作废历史流水。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(14, 8, 0, 0)
        };
        top.Controls.AddRange(new Control[] { create, disable, hint });

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
        Controls.Add(top);
        Controls.Add(bottom);

        RefreshList();
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var a in Accounts.ListEnabled(_ledger))
        {
            var li = new ListViewItem(a.Name);
            li.SubItems.Add(a.Platform.Length == 0 ? "—" : a.Platform);
            li.SubItems.Add(AccountDialog.TypeLabel(a.Type));
            li.SubItems.Add(a.BalanceBaseCents == 0 ? "—" : Money.Yuan(a.BalanceBaseCents));
            li.Tag = a;
            _list.Items.Add(li);
        }
        _list.EndUpdate();
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
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not AccountRow a)
            return;
        if (MessageBox.Show(this,
                $"停用账户「{a.Name}」?\n\n它将不再出现在记账/转账的下拉里;已记流水保留不受影响。",
                "停用账户", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;
        Accounts.Disable(_ledger, a.Id);
        RefreshList();
    }
}
