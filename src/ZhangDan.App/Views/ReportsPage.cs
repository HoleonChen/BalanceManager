using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ZhangDan;
using ZhangDan.App.Dialogs;
using ZhangDan.App.Reporting;

using WListView = Wpf.Ui.Controls.ListView;
using WGridView = Wpf.Ui.Controls.GridView;
using WGridViewColumn = Wpf.Ui.Controls.GridViewColumn;

namespace ZhangDan.App.Views;

/// <summary>报表:历史列表 + 生成/打开/重新生成/删除(报表目录 = AppPaths.ReportDir)。</summary>
internal sealed class ReportsPage : PageBase
{
    private LedgerSession S => App.Ledger!;
    private readonly WListView _list = new();
    private readonly TextBlock _empty = new()
    {
        Text = "还没有生成过报表。点上方「生成报表…」开始。",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13,
        Margin = new Thickness(0, 40, 0, 0)
    };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly List<Row> _rows = new();

    private sealed class Row
    {
        public required ReportHistoryEntry E { get; init; }
        public string Name => Path.GetFileName(E.PdfPath ?? E.XlsxPath ?? "");
        public string Scope => E.Request.ScopeLabel;
        public string Time => E.GeneratedAt;
        public string Types => (E.PdfPath is not null ? "PDF " : "") + (E.XlsxPath is not null ? "XLSX" : "");
        public string Paths => string.Join("  ", new[] { E.PdfPath, E.XlsxPath }.Where(x => x is not null));
    }

    public ReportsPage()
    {
        var gen = new Button { Content = "＋ 生成报表…", MinWidth = 128, Height = 34 };
        gen.Click += (_, _) => Generate();
        var open = new Button { Content = "打开", MinWidth = 76, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        open.Click += (_, _) => OpenSelected();
        var regen = new Button { Content = "重新生成", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        regen.Click += (_, _) => Regenerate();
        var del = new Button { Content = "删除", MinWidth = 76, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        del.Click += (_, _) => DeleteSelected();

        _empty.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        _count.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 10) };
        top.Children.Add(gen);
        top.Children.Add(open);
        top.Children.Add(regen);
        top.Children.Add(del);
        top.Children.Add(_count);

        var gv = new WGridView();
        gv.Columns.Add(new WGridViewColumn { Header = "文件", Width = 220, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new WGridViewColumn { Header = "范围", Width = 220, DisplayMemberBinding = Bind("Scope") });
        gv.Columns.Add(new WGridViewColumn { Header = "时间", Width = 130, DisplayMemberBinding = Bind("Time") });
        gv.Columns.Add(new WGridViewColumn { Header = "类型", Width = 90, DisplayMemberBinding = Bind("Types") });
        gv.Columns.Add(new WGridViewColumn { Header = "路径", Width = 320, DisplayMemberBinding = Bind("Paths") });
        _list.View = gv;
        _list.Margin = new Thickness(20, 0, 20, 12);
        _list.SelectionMode = SelectionMode.Single;
        _list.MouseDoubleClick += (_, _) => OpenSelected();

        var listHost = new Grid();
        listHost.Children.Add(_list);
        listHost.Children.Add(_empty);
        _empty.Visibility = Visibility.Collapsed;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(top, 0);
        Grid.SetRow(listHost, 1);
        grid.Children.Add(top);
        grid.Children.Add(listHost);
        Content = grid;
    }

    private static System.Windows.Data.Binding Bind(string p) => new(p) { Mode = System.Windows.Data.BindingMode.OneWay };

    public override void OnShown() => Reload();

    private Row? Selected() => _list.SelectedItem as Row;

    private void Reload()
    {
        var list = ReportStore.Load(S);
        _rows.Clear();
        foreach (var e in list)
            _rows.Add(new Row { E = e });
        _list.ItemsSource = null;
        _list.ItemsSource = _rows;
        _count.Text = list.Count == 0 ? "" : $"共 {list.Count} 份 · 目录 {AppPaths.ReportDir}";
        _empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Generate()
    {
        var dlg = new ReportGenerateDialog(S) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        var req = dlg.Request;
        try
        {
            Busy(true);
            var (pdf, xlsx) = ReportExporter.Generate(S, req);
            Busy(false);
            var paths = new[] { pdf, xlsx }.Where(x => x is not null).Select(x => x!).ToList();
            MessageBox.Show(Window.GetWindow(this),
                $"报表已生成:\n{string.Join("\n", paths)}",
                "生成报表", MessageBoxButton.OK, MessageBoxImage.Information);
            Reload();
        }
        catch (Exception ex)
        {
            Busy(false);
            Log.Error(ex, "生成报表");
            MessageBox.Show(Window.GetWindow(this), $"生成报表失败:\n{ex.Message}", "生成报表", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSelected()
    {
        if (Selected() is not { } r || (r.E.PdfPath ?? r.E.XlsxPath) is not { } path || !File.Exists(path))
            return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开报表");
        }
    }

    private void Regenerate()
    {
        if (Selected() is not { } r)
            return;
        try
        {
            Busy(true);
            var req = r.E.Request;
            var (pdf, xlsx) = ReportExporter.Generate(S, req);
            Busy(false);
            var list = ReportStore.Load(S);
            var entry = list.Find(x => x.Id == r.E.Id);
            if (entry is not null)
            {
                entry.GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                entry.PdfPath = pdf;
                entry.XlsxPath = xlsx;
                ReportStore.Save(S, list);
            }
            MessageBox.Show(Window.GetWindow(this), "已用最新数据重新生成。", "重新生成", MessageBoxButton.OK, MessageBoxImage.Information);
            Reload();
        }
        catch (Exception ex)
        {
            Busy(false);
            Log.Error(ex, "重新生成报表");
            MessageBox.Show(Window.GetWindow(this), $"重新生成失败:\n{ex.Message}", "重新生成", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelected()
    {
        if (Selected() is not { } r)
            return;
        if (MessageBox.Show(Window.GetWindow(this),
                $"删除这份报表记录?\n{r.E.Request.ScopeLabel}\n(同时删除已生成的 PDF/xlsx 文件)",
                "删除报表", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            ReportStore.Remove(S, r.E.Id);
            foreach (var p in new[] { r.E.PdfPath, r.E.XlsxPath })
            {
                if (p is not null && File.Exists(p))
                {
                    try { File.Delete(p); } catch { /* 忽略 */ }
                }
            }
            Reload();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除报表");
            MessageBox.Show(Window.GetWindow(this), $"删除失败:\n{ex.Message}", "删除报表", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Busy(bool on)
    {
        var w = Window.GetWindow(this);
        if (w is not null)
            w.Cursor = on ? System.Windows.Input.Cursors.Wait : null;
    }
}
