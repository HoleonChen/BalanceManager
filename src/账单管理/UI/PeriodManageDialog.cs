using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 周期管理:列出全部周期(进行中/已到期未封存/已封存),支持封存、解除封存、查看流水、新建。
/// 生命周期见设计 §1/§6:到期 → 推荐新建;封存 = 真正终结(只读);可解除封存恢复。
/// </summary>
internal sealed class PeriodManageDialog : Form
{
    private readonly LedgerSession _ledger;
    private readonly Label _hint = new() { AutoSize = true, ForeColor = Color.DarkOrange };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.FixedSingle,
        HideSelection = false
    };

    private readonly string _today;

    public PeriodManageDialog(LedgerSession ledger)
    {
        _ledger = ledger;
        _today = DateTime.Today.ToString("yyyy-MM-dd");

        Text = "周期管理";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 480);
        MinimumSize = new Size(640, 360);

        _list.Columns.Add("名称", 150);
        _list.Columns.Add("起止", 240);
        _list.Columns.Add("状态", 120);
        _list.Columns.Add("天数", 60, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => ViewSelectedFlow();

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(10, 6, 10, 0)
        };
        var create = new Button { Text = "＋ 新建周期…", Width = 116, Height = 30 };
        create.Click += (_, _) => CreatePeriod();
        var seal = new Button { Text = "封存所选", Width = 92, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        seal.Click += (_, _) => SealSelected();
        var unseal = new Button { Text = "解除封存", Width = 92, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        unseal.Click += (_, _) => UnsealSelected();
        var flow = new Button { Text = "查看流水", Width = 92, Height = 30, Margin = new Padding(8, 0, 0, 0) };
        flow.Click += (_, _) => ViewSelectedFlow();
        top.Controls.AddRange(new Control[] { create, seal, unseal, flow });

        _hint.Location = new Point(12, 46);
        var topHost = new Panel { Dock = DockStyle.Top, Height = 74 };
        topHost.Controls.Add(top);
        topHost.Controls.Add(_hint);

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
        Controls.Add(topHost);

        RefreshList();
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        var expiredNames = new List<string>();
        foreach (var p in Periods.ListAll(_ledger))
        {
            var li = new ListViewItem(p.Name);
            var end = p.EndDate;
            li.SubItems.Add(end is null
                ? $"{Short(p.StartDate)} ~ 长期"
                : $"{Short(p.StartDate)} ~ {Short(end)}");

            var sealedP = p.Status == "sealed";
            var expired = !sealedP && end is not null && string.CompareOrdinal(end, _today) < 0;
            var coversToday = string.CompareOrdinal(p.StartDate, _today) <= 0
                && (end is null || string.CompareOrdinal(end, _today) >= 0);

            var statusText = sealedP ? "已封存(只读)"
                : expired ? "已到期·未封存"
                : "进行中" + (coversToday ? "(今天)" : "");
            var statusSub = li.SubItems.Add(statusText);
            statusSub.ForeColor = sealedP ? SystemColors.GrayText
                : expired ? Color.DarkOrange
                : coversToday ? Color.SteelBlue : SystemColors.WindowText;

            if (end is null)
                li.SubItems.Add("—");
            else
            {
                var days = (DateTime.Parse(end) - DateTime.Parse(p.StartDate)).Days + 1;
                li.SubItems.Add(days.ToString());
            }

            li.Tag = p;
            _list.Items.Add(li);

            if (expired)
                expiredNames.Add($"「{p.Name}」({Short(p.StartDate)}~{Short(end!)})");
        }
        _list.EndUpdate();

        _hint.Text = expiredNames.Count == 0
            ? "进行中周期可随时封存;封存后只读,可解除。"
            : "建议:" + string.Join("、", expiredNames)
              + " 已到结束日未封存 —— 到期的账先封存归档,再新建下一周期(之后日期自动归新周期)。";
    }

    private static string Short(string iso)
    {
        var p = iso.Split('-');
        return $"{p[0]}/{int.Parse(p[1])}/{int.Parse(p[2])}";
    }

    private PeriodRow? Selected()
        => _list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not PeriodRow p
            ? null
            : p;

    private void CreatePeriod()
    {
        using var dlg = new PeriodDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        // 与进行中周期日期重叠提醒(同日在场取开始最晚者归属)
        foreach (var p in Periods.ListActive(_ledger))
        {
            if (Overlaps(p, dlg.StartDate, dlg.EndDate))
            {
                var rng = $"{Short(p.StartDate)}~{(p.EndDate is null ? "长期" : Short(p.EndDate))}";
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
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"建立周期失败:\n{ex.Message}", "账单管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool Overlaps(PeriodRow p, string start, string? end)
    {
        var s = DateTime.Parse(p.StartDate);
        var e = p.EndDate is null ? DateTime.MaxValue : DateTime.Parse(p.EndDate);
        var ns = DateTime.Parse(start);
        var ne = end is null ? DateTime.MaxValue : DateTime.Parse(end);
        return s <= ne && e >= ns;
    }

    private void SealSelected()
    {
        var p = Selected();
        if (p is null || p.Status != "active")
        {
            MessageBox.Show(this, "请选一个进行中周期来封存。", "周期管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SealFlow(this, _ledger, p);
        RefreshList();
    }

    /// <summary>封存一个进行中周期(含无结束日/提前封存提醒;成功后返回 true)。供周期管理与主菜单共用。</summary>
    internal static bool SealFlow(IWin32Window owner, LedgerSession ledger, PeriodRow p)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        if (p.EndDate is null)
        {
            if (MessageBox.Show(owner,
                    $"周期「{p.Name}」还没有结束日期。\n\n封存会把收尾日设为今天({today}),之后的日期不再归本期(将成空档或归新周期)。确定封存?",
                    "封存周期", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return false;
            Periods.SetEndDate(ledger, p.Id, today);
            p = Periods.Get(ledger, p.Id)!;
        }
        else if (string.CompareOrdinal(p.EndDate, today) > 0)
        {
            if (MessageBox.Show(owner,
                    $"周期「{p.Name}」计划结束日是 {Short(p.EndDate)},在今天之后。\n\n提前封存后,{Short(p.EndDate)} 之前的日期是本期范围但今天还没到,这些未来日子无法自动归属本期。建议改结束日或到期再封存。仍要提前封存?",
                    "提前封存提醒", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return false;
        }

        if (MessageBox.Show(owner,
                $"封存周期「{p.Name}」?\n\n封存后该周期只读——期内流水不能再新增/修改/作废(可随时到周期管理解除封存恢复)。",
                "封存周期", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return false;

        Periods.Seal(ledger, p.Id);
        return true;
    }

    private void UnsealSelected()
    {
        var p = Selected();
        if (p is null || p.Status != "sealed")
        {
            MessageBox.Show(this, "请选一个已封存周期来解除。", "周期管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"解除封存「{p.Name}」?\n\n恢复为进行中、期内流水可增删改。注意:若报表曾按封存口径导出,解除后口径变化需重新生成。",
                "解除封存", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        Periods.Unseal(_ledger, p.Id);
        RefreshList();
    }

    private void ViewSelectedFlow()
    {
        var p = Selected();
        if (p is null)
            return;
        if (p.EndDate is null)
        {
            MessageBox.Show(this, $"周期「{p.Name}」没有结束日期,暂无法框定查看范围。",
                "周期管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new PeriodFlowDialog(_ledger, p.Name, p.StartDate, p.EndDate,
            readOnly: p.Status == "sealed");
        dlg.ShowDialog(this);
    }
}
