using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZhangDan.App.Views;

/// <summary>设置:账本信息 / 偏好(凌晨宽限)/ 数据自检 / CSV 导出 / 数据目录。</summary>
internal sealed class SettingsPage : PageBase
{
    private readonly TextBlock _ledgerInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _grace = new() { Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _selfResult = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    public SettingsPage()
    {
        _grace.Content = "凌晨宽限:0:00 ~ 6:00 记一笔时默认记到「昨天」(补录凌晨前账目)";
        _grace.IsChecked = App.Settings.MidnightGraceEnabled;
        _grace.Checked += (_, _) => SaveGrace(true);
        _grace.Unchecked += (_, _) => SaveGrace(false);

        var selfBtn = new Button { Content = "运行数据自检", MinWidth = 130, Height = 34, HorizontalAlignment = HorizontalAlignment.Left };
        selfBtn.Click += (_, _) => RunSelfTest(selfBtn);

        var exportBtn = new Button { Content = "导出全部流水 CSV…", MinWidth = 150, Height = 34, HorizontalAlignment = HorizontalAlignment.Left };
        exportBtn.Click += (_, _) => ExportCsv();

        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Title("设置"));

        panel.Children.Add(Section("账本"));
        panel.Children.Add(_ledgerInfo);

        panel.Children.Add(Section("偏好"));
        panel.Children.Add(_grace);
        panel.Children.Add(new TextBlock
        {
            Text = "深夜记前一晚的账时,不必手动把日期改回昨天。此偏好存于 %APPDATA%\\账单管理\\app.json。",
            Foreground = Brushes.Gray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4)
        });

        panel.Children.Add(Section("数据自检"));
        panel.Children.Add(selfBtn);
        panel.Children.Add(_selfResult);

        panel.Children.Add(Section("导出"));
        panel.Children.Add(exportBtn);
        panel.Children.Add(new TextBlock
        {
            Text = "单文件导出全量流水(含已作废/退款/转账),UTF-8 BOM 可直接用 Excel 打开。",
            Foreground = Brushes.Gray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4)
        });

        panel.Children.Add(Section("数据目录"));
        panel.Children.Add(new TextBlock
        {
            Text =
                $"账本默认目录:{AppPaths.UserDataDir}\n" +
                $"报表目录:{AppPaths.ReportDir}\n" +
                $"设置文件:{AppPaths.SettingsFile}",
            Foreground = Brushes.Gray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 14)
    };

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.DimGray,
        Margin = new Thickness(0, 10, 0, 4)
    };

    public override void OnShown()
    {
        if (App.Ledger is null)
        {
            _ledgerInfo.Text = "未打开账本。";
            return;
        }
        var size = new FileInfo(App.Ledger.Path).Length;
        _ledgerInfo.Text = $"账本名:{App.Ledger.Name}\n账本文件:{App.Ledger.Path}  ({size / 1024.0:0.0} KB)";
    }

    private void SaveGrace(bool enabled)
    {
        App.Settings.MidnightGraceEnabled = enabled;
        App.Settings.Save();
    }

    private void RunSelfTest(Button btn)
    {
        btn.IsEnabled = false;
        _selfResult.Text = "自检运行中…";
        _selfResult.Foreground = Brushes.Gray;
        try
        {
            var (ok, steps, err) = SelfTest.Run();
            if (ok)
            {
                var sb = new System.Text.StringBuilder($"数据自检通过({steps.Count} 项):\n");
                foreach (var step in steps)
                    sb.Append("· ").Append(step).Append('\n');
                _selfResult.Text = sb.ToString();
                _selfResult.Foreground = Brushes.SeaGreen;
            }
            else
            {
                _selfResult.Text = $"数据自检失败:\n{err}\n\n已执行步骤:\n{string.Join("\n", steps)}";
                _selfResult.Foreground = Brushes.Firebrick;
            }
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private void ExportCsv()
    {
        if (App.Ledger is null)
            return;
        var pick = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出全部流水",
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"{SafeName(App.Ledger.Name)}_流水_{DateTime.Now:yyyyMMdd}.csv",
            InitialDirectory = AppPaths.UserDataDir
        };
        if (pick.ShowDialog(Window.GetWindow(this)) != true)
            return;
        try
        {
            var rows = Transactions.ExportAll(App.Ledger);
            CsvExporter.Save(pick.FileName, CsvExporter.Build(rows));
            MessageBox.Show(Window.GetWindow(this),
                $"已导出 {rows.Count} 笔流水到:\n{pick.FileName}",
                "导出 CSV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"导出失败:\n{ex.Message}",
                "导出 CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
