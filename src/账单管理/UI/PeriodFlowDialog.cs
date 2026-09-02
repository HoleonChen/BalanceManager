using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 本周期流水(只读总览):当前周期起止内全部流水(日期/时间/名称/分类/账户/金额),
/// 顶栏给出周期支出/收入合计。为设计「流水 Tab(内容视图)」的先声。
/// </summary>
internal sealed class PeriodFlowDialog : Form
{
    private readonly LedgerSession _ledger;
    private readonly string _start;
    private readonly string _end;

    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.FixedSingle,
        HideSelection = false
    };

    public PeriodFlowDialog(LedgerSession ledger, string periodName, string start, string end)
    {
        _ledger = ledger;
        _start = start;
        _end = end;

        Text = $"周期流水 · {periodName}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 560);
        MinimumSize = new Size(620, 380);

        _list.Columns.Add("日期", 96);
        _list.Columns.Add("时间", 60);
        _list.Columns.Add("名称", 190);
        _list.Columns.Add("分类", 120);
        _list.Columns.Add("账户", 190);
        _list.Columns.Add("金额", 130, HorizontalAlignment.Right);

        var top = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 10, 0, 0) };
        var summary = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        top.Controls.Add(summary);

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

        // 内容
        _list.BeginUpdate();
        foreach (var t in Transactions.ListByRange(_ledger, start, end))
        {
            var li = new ListViewItem(t.Date);
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

        var (outCents, inCents) = Transactions.RangeTotals(_ledger, start, end);
        summary.Text =
            $"{periodName}:{Short(start)}~{Short(end)}   周期支出 {Money.Yuan(outCents)} · 周期收入 {Money.Yuan(inCents)} · {_list.Items.Count} 笔(不含作废)";
    }

    private static string Short(string iso)
    {
        var p = iso.Split('-');
        return $"{p[1]}/{p[2]}";
    }
}
