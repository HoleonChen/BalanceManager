using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 账户详情:当前余额(派生)= 基准 + 基准日后净变动,本周期(覆盖今天的进行中周期)收支转构成,
/// 及该账户在对应范围内的流水。停用账户灰显、不计净资产(净资产合计见账户管理顶栏)。
/// </summary>
internal sealed class AccountDetailDialog : Form
{
    private readonly LedgerSession _ledger;
    private readonly long _accountId;
    private readonly Label _balanceLabel = new() { Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold) };
    private readonly Label _compositionLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label _periodLabel = new() { AutoSize = true };
    private readonly Label _rangeLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.FixedSingle,
        HideSelection = false
    };

    public AccountDetailDialog(LedgerSession ledger, long accountId)
    {
        _ledger = ledger;
        _accountId = accountId;

        var acc = FindAccount(accountId);
        Text = $"账户详情 · {acc.Name}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 560);
        MinimumSize = new Size(680, 420);

        _list.Columns.Add("日期", 96);
        _list.Columns.Add("时间", 60);
        _list.Columns.Add("名称", 200);
        _list.Columns.Add("分类", 120);
        _list.Columns.Add("账户", 200);
        _list.Columns.Add("金额", 130, HorizontalAlignment.Right);

        var top = new Panel { Dock = DockStyle.Top, Height = 176 };
        var head = new Label
        {
            Text = $"账户 · {acc.Name}   ({AccountDialog.TypeLabel(acc.Type)} · {(acc.Platform.Length == 0 ? "—" : acc.Platform)}"
                   + (acc.Enabled ? " · 启用" : " · 已停用(不计净资产)") + ")",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 12f),
            Location = new Point(16, 10)
        };
        _balanceLabel.Location = new Point(16, 44);
        _compositionLabel.Location = new Point(16, 78);
        _compositionLabel.MaximumSize = new Size(740, 0);
        _periodLabel.Location = new Point(16, 104);
        _periodLabel.MaximumSize = new Size(740, 0);
        _rangeLabel.Location = new Point(16, 152);
        top.Controls.AddRange(new Control[] { head, _balanceLabel, _compositionLabel, _periodLabel, _rangeLabel });

        var mid = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(16, 10, 16, 0) };
        var calibrate = new Button { Text = "校准余额…", Width = 104, Height = 30, Dock = DockStyle.Right };
        calibrate.Click += (_, _) => Calibrate();
        mid.Controls.Add(calibrate);

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
        Controls.Add(mid);
        Controls.Add(top);

        Reload();
    }

    private AccountRow FindAccount(long id)
    {
        foreach (var a in Accounts.ListAll(_ledger))
        {
            if (a.Id == id)
                return a;
        }
        throw new InvalidOperationException("账户不存在。");
    }

    private void Reload()
    {
        var acc = FindAccount(_accountId);
        var (baseCents, baseDate) = Accounts.BaseOf(_ledger, _accountId);
        var book = AccountCalibration.BookCents(_ledger, _accountId);
        _balanceLabel.Text = Money.Yuan(book);
        _balanceLabel.ForeColor = acc.Enabled ? Color.SteelBlue : SystemColors.GrayText;
        _compositionLabel.Text =
            $"= 基准 {Money.Yuan(baseCents)}(基准日 {(baseDate ?? "未设 · 自最早流水起算")}) + 净变动 {Signed(book - baseCents)}";

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var p = Periods.GetCoveringActive(_ledger, today);
        string from, to, rangeCaption;
        if (p is not null)
        {
            from = p.StartDate;
            to = p.EndDate ?? today;
            rangeCaption = p.EndDate is null
                ? $"长期(至今日 {Short(to)})"
                : $"{Short(p.StartDate)} ~ {Short(p.EndDate)}";
            var mv = Accounts.MovementBetween(_ledger, _accountId, from, to);
            _periodLabel.ForeColor = Color.DimGray;
            _periodLabel.Text =
                $"本周期「{p.Name}」({rangeCaption}):收入 {Signed(mv.InCents)} · 支出 {Signed(-mv.OutCents)} · "
                + $"转入 {Signed(mv.TransferInCents)} · 转出 {Signed(-mv.TransferOutCents)} → 净 {Signed(mv.NetCents)}";
            _rangeLabel.Text = $"流水范围:{rangeCaption}(非作废)";
        }
        else
        {
            from = "0000-01-01";
            to = "9999-12-31";
            _periodLabel.ForeColor = Color.DarkOrange;
            _periodLabel.Text = "当前没有覆盖今天的进行中周期 —— 不展示本周期变动,下方流水列全部。";
            _rangeLabel.Text = "流水范围:全部(非作废)";
        }

        ReloadFlows(acc.Name, from, to);
    }

    private void ReloadFlows(string accountName, string from, string to)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var t in Transactions.ListByAccountRange(_ledger, _accountId, from, to))
        {
            var li = new ListViewItem(t.Date);
            li.Tag = t;
            li.SubItems.Add(t.Time);

            if (t.Direction == "transfer")
            {
                li.SubItems.Add(t.Name);
                li.SubItems.Add("转账");
                li.SubItems.Add($"{t.Account} → {t.AccountTo}");
                li.SubItems.Add(TransferAmountText(t, accountName));
            }
            else
            {
                li.SubItems.Add(t.Name);
                li.SubItems.Add(t.Category);
                li.SubItems.Add(t.Account);
                var isOut = t.Direction == "out";
                var sub = li.SubItems.Add(isOut ? "-" + Money.Yuan(t.AmountCents) : "+" + Money.Yuan(t.AmountCents));
                sub.ForeColor = isOut ? Color.Firebrick : Color.ForestGreen;
            }
            _list.Items.Add(li);
        }
        _list.EndUpdate();
    }

    /// <summary>转账金额按「本账户方位」标号:我方转出 −本金(浮动记对方),我方转入 +(本金+浮动)。</summary>
    private ListViewItem.ListViewSubItem TransferAmountText(TxnListItem t, string accountName)
    {
        var sub = new ListViewItem.ListViewSubItem();
        if (t.Account == accountName)
        {
            sub.Text = "-" + Money.Yuan(t.AmountCents);
            sub.ForeColor = Color.Firebrick;
            if (t.DeltaCents != 0)
                sub.Text += $" (Δ{(t.DeltaCents > 0 ? "+" : "-")}{Money.Yuan(Math.Abs(t.DeltaCents))})";
        }
        else if (t.AccountTo == accountName)
        {
            var value = t.AmountCents + t.DeltaCents;
            sub.Text = (value >= 0 ? "+" : "-") + Money.Yuan(Math.Abs(value));
            sub.ForeColor = Color.ForestGreen;
        }
        else
        {
            sub.Text = Money.Yuan(t.AmountCents);
            sub.ForeColor = Color.DarkSlateBlue;
        }
        return sub;
    }

    private void Calibrate()
    {
        CalibrationDialog.Run(this, _ledger, _accountId);
        Reload();
    }

    private static string Signed(long v)
        => (v > 0 ? "+" : v < 0 ? "-" : "") + Money.Yuan(Math.Abs(v));

    private static string Short(string iso)
    {
        var p = iso.Split('-');
        return $"{p[0]}/{int.Parse(p[1])}/{int.Parse(p[2])}";
    }
}
