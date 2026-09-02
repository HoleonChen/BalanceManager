using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 本周期流水总览:当前周期起止内全部流水(日期/时间/名称/分类/账户/金额),
/// 顶栏给出周期支出/收入合计;支持右键/Delete 作废(撤出统计,即时刷新)。
/// 为设计「流水 Tab(内容视图)」的先声。
/// </summary>
internal sealed class PeriodFlowDialog : FormBase
{
    private readonly LedgerSession _ledger;
    private readonly string _periodName;
    private readonly string _start;
    private readonly string _end;
    private readonly bool _readOnly;
    private readonly Label _summary = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.FixedSingle,
        HideSelection = false
    };

    public PeriodFlowDialog(LedgerSession ledger, string periodName, string start, string end,
        bool readOnly = false)
    {
        _ledger = ledger;
        _periodName = periodName;
        _start = start;
        _end = end;
        _readOnly = readOnly;

        Text = $"周期流水 · {periodName}";
        if (readOnly)
            Text += "(已封存·只读)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 560);
        MinimumSize = new Size(620, 380);

        _list.Columns.Add("日期", 96);
        _list.Columns.Add("时间", 60);
        _list.Columns.Add("名称", 190);
        _list.Columns.Add("分类", 120);
        _list.Columns.Add("账户", 190);
        _list.Columns.Add("金额", 130, HorizontalAlignment.Right);

        // 右键/Delete 作废;封存周期整窗只读,不提供作废入口
        var ctx = new ContextMenuStrip();
        var cancel = new ToolStripMenuItem("作废/删除这笔…");
        cancel.Enabled = !_readOnly;
        cancel.Click += (_, _) => CancelSelected();
        ctx.Items.Add(cancel);
        _list.ContextMenuStrip = ctx;
        _list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
                return;
            _list.SelectedItems.Clear();
            var hit = _list.GetItemAt(e.X, e.Y);
            if (hit != null)
                hit.Selected = true;
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                CancelSelected();
                e.Handled = true;
            }
        };

        var top = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 10, 0, 0) };
        top.Controls.Add(_summary);

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

        Reload();
    }

    /// <summary>重建列表与顶栏合计。</summary>
    private void Reload()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var t in Transactions.ListByRange(_ledger, _start, _end))
        {
            var li = new ListViewItem(t.Date);
            li.Tag = t;
            li.SubItems.Add(t.Time);

            if (t.Direction == "transfer")
            {
                li.SubItems.Add(t.Name);
                li.SubItems.Add("转账");
                li.SubItems.Add($"{t.Account} → {t.AccountTo}");
                var amount = Money.Yuan(t.AmountCents);
                if (t.DeltaCents != 0)
                {
                    var sign = t.DeltaCents > 0 ? "+" : "-";
                    amount += $" (Δ{sign}{Money.Yuan(Math.Abs(t.DeltaCents))})";
                }
                var sub = li.SubItems.Add(amount);
                sub.ForeColor = Color.DarkSlateBlue;
            }
            else
            {
                li.SubItems.Add(t.Name);
                li.SubItems.Add(t.Category);
                li.SubItems.Add(t.Account);
                var isOut = t.Direction == "out";
                var sub = li.SubItems.Add(isOut
                    ? "-" + Money.Yuan(t.AmountCents)
                    : "+" + Money.Yuan(t.AmountCents));
                sub.ForeColor = isOut ? Color.Firebrick : Color.ForestGreen;
            }
            _list.Items.Add(li);
        }
        _list.EndUpdate();

        var (outCents, inCents) = Transactions.RangeTotals(_ledger, _start, _end);
        _summary.Text =
            $"{_periodName}:{Short(_start)}~{Short(_end)}   周期支出 {Money.Yuan(outCents)} · 周期收入 {Money.Yuan(inCents)} · {_list.Items.Count} 笔(不含作废)";
    }

    private void CancelSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not TxnListItem t)
            return;
        if (Periods.HasSealedCovering(_ledger, t.Date))
        {
            MessageBox.Show(this, LedgerReadonlyException.Friendly(t.Date), _readOnly ? "已封存周期(只读)" : "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var head = t.Direction == "transfer"
            ? $"作废这笔转账?\n\n  {t.Name} · {t.Account} → {t.AccountTo}\n  {Money.Yuan(t.AmountCents)}"
            : $"作废这笔并撤出统计?\n\n  {t.Name}\n  {(t.Direction == "out" ? "-" : "+")}{Money.Yuan(t.AmountCents)} · {t.Account}";
        if (MessageBox.Show(this,
                head + "\n\n记录仍留在库中(标记作废),只是不再计入统计。",
                "作废流水", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        Transactions.Cancel(_ledger, t.Id);
        Reload();
    }

    private static string Short(string iso)
    {
        var p = iso.Split('-');
        return $"{p[1]}/{p[2]}";
    }
}
