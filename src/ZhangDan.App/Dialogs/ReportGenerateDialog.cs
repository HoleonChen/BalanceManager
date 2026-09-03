using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ZhangDan;
using ZhangDan.App.Reporting;

namespace ZhangDan.App.Dialogs;

/// <summary>
/// 生成报表对话框:范围三模式(单周期/自定义日期含空档/周期对比)+ 内容块勾选 + PDF/xlsx 输出 + 文件名预览。
/// </summary>
internal sealed class ReportGenerateDialog : Window
{
    private readonly LedgerSession _ledger;
    private readonly List<PeriodRow> _periods;      // 按开始日升序

    private readonly ComboBox _mode = new() { Width = 250 };
    private readonly ComboBox _single = new() { Width = 320 };
    private readonly TextBox _from = new() { Width = 120 };
    private readonly TextBox _to = new() { Width = 120 };
    private readonly ListBox _compare = new() { Width = 360, Height = 130, SelectionMode = SelectionMode.Extended };
    private readonly StackPanel _pSingle = new();
    private readonly StackPanel _pCustom = new();
    private readonly StackPanel _pCompare = new();

    private readonly CheckBox _bOverview = Ck("总览", true);
    private readonly CheckBox _bShare = Ck("支出分类占比", true);
    private readonly CheckBox _bTrend = Ck("跨周期趋势", true);
    private readonly CheckBox _bAccounts = Ck("账户与净资产", true);
    private readonly CheckBox _bTop = Ck("大额收支 TOP", true);
    private readonly CheckBox _bDaily = Ck("每日收支", true);
    private readonly CheckBox _bPool = Ck("资金池", true);
    private readonly CheckBox _bTransfer = Ck("转账汇总", true);
    private readonly ComboBox _percent = new() { Width = 120 };

    private readonly CheckBox _pdf = Ck("PDF", true);
    private readonly CheckBox _xlsx = Ck("Excel(xlsx)", true);
    private readonly TextBox _dir = new() { Width = 300 };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };

    public ReportRequest Request { get; private set; } = new();

    public ReportGenerateDialog(LedgerSession ledger)
    {
        _ledger = ledger;
        _periods = Periods.ListAll(ledger).OrderBy(p => p.StartDate).ToList();
        Title = "生成报表";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _mode.Items.Add("周期报表(进行中/已封存)");
        _mode.Items.Add("自定义日期范围(可跨周期·含未归属)");
        _mode.Items.Add("周期对比(勾选 ≥2 个)");
        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) => UpdateMode();

        foreach (var p in _periods)
        {
            _single.Items.Add(new PeriodOpt(p, PeriodLabel(p)));
            _compare.Items.Add(new PeriodOpt(p, PeriodLabel(p)));
        }
        DefaultSelectPeriod();

        _from.Text = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd");
        _to.Text = DateTime.Today.ToString("yyyy-MM-dd");
        _percent.Items.Add("金额");
        _percent.Items.Add("100% 占比");
        _percent.SelectedIndex = 0;

        _dir.Text = AppPaths.ReportDir;
        var browse = new Button { Content = "浏览…", Width = 74, Height = 30 };
        browse.Click += (_, _) => BrowseDir();

        var blocks = new WrapPanel { Width = 560 };
        foreach (var c in new[] { _bOverview, _bShare, _bTrend, _bAccounts, _bTop, _bDaily, _bPool, _bTransfer })
        {
            c.Margin = new Thickness(0, 2, 12, 2);
            blocks.Children.Add(c);
        }
        _bTrend.Checked += (_, _) => UpdateMode();
        _bTrend.Unchecked += (_, _) => UpdateMode();

        var panel = new StackPanel { Margin = new Thickness(20), Width = 600 };
        panel.Children.Add(T("范围"));
        panel.Children.Add(Field("模式", _mode));
        _pSingle.Children.Add(Field("周期", _single));
        _pCustom.Children.Add(Row(_from, "起", _to, "止"));
        var note = new TextBlock { Text = "自定义范围含周期外(未归属)流水。", FontSize = 12, Margin = new Thickness(140, 0, 0, 4) };
        note.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        _pCustom.Children.Add(note);
        _pCompare.Children.Add(Field("周期(可多选)", _compare));
        var quick = new Button { Content = "与上个周期比", Height = 30, Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        quick.Click += (_, _) => CompareWithPrevious();
        _pCompare.Children.Add(quick);
        panel.Children.Add(_pSingle);
        panel.Children.Add(_pCustom);
        panel.Children.Add(_pCompare);

        panel.Children.Add(T("内容块"));
        panel.Children.Add(blocks);
        var pRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        pRow.Children.Add(new TextBlock { Text = "趋势单位:", VerticalAlignment = VerticalAlignment.Center });
        pRow.Children.Add(_percent);
        panel.Children.Add(pRow);

        panel.Children.Add(T("输出"));
        var outRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        outRow.Children.Add(_pdf);
        outRow.Children.Add(_xlsx);
        panel.Children.Add(outRow);
        var dirLbl = new TextBlock { Text = "保存目录", Width = 140, VerticalAlignment = VerticalAlignment.Center };
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal };
        dirRow.Children.Add(_dir);
        dirRow.Children.Add(browse);
        var dirDock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(dirLbl, Dock.Left);
        dirDock.Children.Add(dirLbl);
        dirDock.Children.Add(dirRow);
        panel.Children.Add(dirDock);
        panel.Children.Add(_preview);

        var ok = new Button { Content = "生成", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 8, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true, Margin = new Thickness(0, 8, 0, 0) };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        panel.Children.Add(btns);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        UpdateMode();
        UpdatePreview();
        _single.SelectionChanged += (_, _) => UpdatePreview();
        _from.TextChanged += (_, _) => UpdatePreview();
        _to.TextChanged += (_, _) => UpdatePreview();
        _compare.SelectionChanged += (_, _) => UpdatePreview();
    }

    // ---------- UI helpers ----------

    private static CheckBox Ck(string text, bool on) => new() { Content = text, IsChecked = on };

    private static TextBlock T(string text) => new()
    {
        Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4)
    };

    private static UIElement Field(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center };
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(text, Dock.Left);
        d.Children.Add(text);
        d.Children.Add(input);
        return d;
    }

    private static UIElement Row(UIElement a, string la, UIElement b, string lb)
    {
        var textA = new TextBlock { Text = la, Width = 60, VerticalAlignment = VerticalAlignment.Center };
        var textB = new TextBlock { Text = lb, Width = 60, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(textA, Dock.Left);
        DockPanel.SetDock(a, Dock.Left);
        DockPanel.SetDock(textB, Dock.Left);
        DockPanel.SetDock(b, Dock.Left);
        d.Children.Add(textA);
        d.Children.Add(a);
        d.Children.Add(textB);
        d.Children.Add(b);
        return d;
    }

    private void UpdateMode()
    {
        bool single = _mode.SelectedIndex == 0;
        bool custom = _mode.SelectedIndex == 1;
        bool compare = _mode.SelectedIndex == 2;
        _pSingle.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        _pCustom.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _pCompare.Visibility = compare ? Visibility.Visible : Visibility.Collapsed;
        _percent.IsEnabled = _bTrend.IsChecked == true;
    }

    private void DefaultSelectPeriod()
    {
        if (_periods.Count == 0)
            return;
        var active = _periods.Where(p => p.Status == "active").OrderByDescending(p => p.StartDate).FirstOrDefault()
                     ?? _periods[^1];
        _single.SelectedItem = _compare.Items.OfType<PeriodOpt>().FirstOrDefault(o => o.Row.Id == active.Id);
    }

    private void CompareWithPrevious()
    {
        if (_periods.Count < 2)
            return;
        var anchor = SelectedSingle();
        int idx = _periods.FindIndex(p => p.Id == anchor?.Id);
        if (idx <= 0)
            return;
        var prev = _periods[idx - 1];
        _compare.SelectedItems.Clear();
        SelectIn(_compare, prev);
        SelectIn(_compare, _periods[idx]);
    }

    private static void SelectIn(ListBox box, PeriodRow p)
    {
        var opt = box.Items.OfType<PeriodOpt>().FirstOrDefault(o => o.Row.Id == p.Id);
        if (opt is not null)
            box.SelectedItems.Add(opt);
    }

    private PeriodRow? SelectedSingle()
    {
        var first = _periods.FirstOrDefault(p => p.Status == "active") ?? _periods.LastOrDefault();
        return first;
    }

    private void BrowseDir()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择报表保存目录" };
        if (dlg.ShowDialog(this) == true)
            _dir.Text = dlg.FolderName;
    }

    private void Accept()
    {
        var kind = (ReportRangeKind)_mode.SelectedIndex;   // 0/1/2 与枚举序一致
        long[]? ids = null;
        string label;
        switch (kind)
        {
            case ReportRangeKind.Period:
                if (_single.SelectedItem is not PeriodOpt so)
                {
                    Msg("请选择周期。");
                    return;
                }
                ids = new[] { so.Row.Id };
                label = so.Row.Name;
                break;
            case ReportRangeKind.Custom:
                if (!Date(_from.Text, out var f) || !Date(_to.Text, out var t2) || t2 < f)
                {
                    Msg("自定义范围日期无效(起 ≤ 止,yyyy-MM-dd)。");
                    return;
                }
                label = $"{_from.Text.Trim()}~{_to.Text.Trim()}";
                break;
            default:
                var sel = _compare.SelectedItems.OfType<PeriodOpt>().ToList();
                if (sel.Count < 2)
                {
                    Msg("周期对比至少选 2 个周期。");
                    return;
                }
                ids = sel.OrderBy(o => o.Row.StartDate).Select(o => o.Row.Id).ToArray();
                label = string.Join("对比", sel.OrderBy(o => o.Row.StartDate).Select(o => o.Row.Name));
                break;
        }

        if (!_pdf.IsChecked == true && !_xlsx.IsChecked == true)
        {
            Msg("至少勾选一种输出(PDF / Excel)。");
            return;
        }
        if (!(_bOverview.IsChecked == true || _bShare.IsChecked == true || _bTrend.IsChecked == true ||
              _bAccounts.IsChecked == true || _bTop.IsChecked == true || _bDaily.IsChecked == true ||
              _bPool.IsChecked == true || _bTransfer.IsChecked == true))
        {
            Msg("至少勾选一个内容块。");
            return;
        }

        Request = new ReportRequest
        {
            Kind = kind,
            Start = kind == ReportRangeKind.Custom ? _from.Text.Trim() : null,
            End = kind == ReportRangeKind.Custom ? _to.Text.Trim() : null,
            PeriodIds = ids,
            BlockOverview = _bOverview.IsChecked == true,
            BlockShare = _bShare.IsChecked == true,
            BlockTrend = _bTrend.IsChecked == true,
            BlockAccounts = _bAccounts.IsChecked == true,
            BlockTop = _bTop.IsChecked == true,
            BlockDaily = _bDaily.IsChecked == true,
            BlockPool = _bPool.IsChecked == true,
            BlockTransfer = _bTransfer.IsChecked == true,
            PercentMode = _percent.SelectedIndex == 1,
            ToPdf = _pdf.IsChecked == true,
            ToXlsx = _xlsx.IsChecked == true,
            SaveDir = _dir.Text.Trim(),
            ScopeLabel = label,
            BaseName = $"{Safe(_ledger.Name)}_report_{label}_{DateTime.Now:yyyyMMdd}"
        };
        DialogResult = true;
    }

    private void UpdatePreview()
    {
        var label = _mode.SelectedIndex switch
        {
            1 => $"{_from.Text.Trim()}~{_to.Text.Trim()}",
            2 => string.Join("对比", _compare.SelectedItems.OfType<PeriodOpt>().Select(o => o.Row.Name)),
            _ => _single.SelectedItem is PeriodOpt o ? o.Row.Name : "?"
        };
        var baseName = $"{Safe(_ledger.Name)}_report_{Safe(label)}_{DateTime.Now:yyyyMMdd}";
        _preview.Text = $"文件名:{baseName}.pdf / .xlsx\n范围:{label}";
    }

    private static bool Date(string s, out DateTime d) => DateTime.TryParse(s.Trim(), out d);

    private static string PeriodLabel(PeriodRow p) =>
        $"{p.Name} ({p.StartDate} ~ {(p.EndDate ?? "长期")}{(p.Status == "sealed" ? " · 已封存" : "")})";

    private static string Safe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    private void Msg(string m) => MessageBox.Show(this, m, "生成报表", MessageBoxButton.OK, MessageBoxImage.Warning);

    private sealed record PeriodOpt(PeriodRow Row, string Label)
    {
        public override string ToString() => Label;
    }
}
