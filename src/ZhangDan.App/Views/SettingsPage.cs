using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZhangDan.App.Views;

/// <summary>设置:账本信息 / 外观(深浅/跟系统)/ 偏好(凌晨宽限)/ 数据自检 / CSV 导出 / 数据目录。</summary>
internal sealed class SettingsPage : PageBase
{
    private readonly TextBlock _ledgerInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _grace = new() { Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _selfResult = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>强调色预设(首个 = 默认钢蓝,对应旧版外观,点它即还原)。</summary>
    private static readonly string[] AccentPresets =
    {
        "#4682B4", "#E91E63", "#9C27B0", "#3F51B5", "#009688", "#4CAF50", "#FF9800", "#795548"
    };

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

        var logDirBtn = new Button { Content = "打开日志目录…", MinWidth = 130, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        logDirBtn.Click += (_, _) => OpenLogDir();
        var clearLogBtn = new Button { Content = "清空日志…", MinWidth = 130, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 0, 0, 0) };
        clearLogBtn.Click += (_, _) => ClearLogs();
        var logRow = new StackPanel { Orientation = Orientation.Horizontal };
        logRow.Children.Add(logDirBtn);
        logRow.Children.Add(clearLogBtn);

        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Title("设置"));

        panel.Children.Add(Section("账本"));
        panel.Children.Add(_ledgerInfo);

        panel.Children.Add(Section("偏好"));
        panel.Children.Add(_grace);
        panel.Children.Add(Hint("深夜记前一晚的账时,不必手动把日期改回昨天。此偏好存于 %APPDATA%\\账单管理\\app.json。", new Thickness(0, 2, 0, 4)));

        panel.Children.Add(Section("外观"));
        var mode = App.Settings.ThemeMode ?? "system";
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (tag, label) in new[] { ("system", "跟随系统"), ("light", "浅色"), ("dark", "深色") })
        {
            var rb = new RadioButton
            {
                Content = label,
                Margin = new Thickness(0, 0, 18, 0),
                Tag = tag,
                IsChecked = mode == tag
            };
            rb.Checked += (_, _) => SaveAppearance(rb);
            themeRow.Children.Add(rb);
        }
        panel.Children.Add(themeRow);
        panel.Children.Add(Hint("选「跟随系统」时,随 Windows 深浅色实时切换。", new Thickness(0, 2, 0, 4)));

        // —— 强调色:预设圆点选择 + 即时预览(预览控件本身用动态强调色键,换色即变)——
        var accentCaption = new TextBlock { Text = "强调色", FontSize = 13, Margin = new Thickness(0, 6, 0, 2) };
        accentCaption.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSection);
        panel.Children.Add(accentCaption);

        var chips = new List<(Border Holder, string Hex)>();
        var currentAccent = App.Settings.Accent ?? ThemeService.DefaultAccentHex;
        var accentWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var hex in AccentPresets)
        {
            var dot = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(HexColor(hex)),
                Cursor = Cursors.Hand,
                Tag = hex
            };
            var holder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(0, 0, 4, 6),
                Background = Brushes.Transparent,
                Child = dot
            };
            holder.SetResourceReference(Border.BorderBrushProperty, UiKeys.TextPrimary);
            holder.BorderThickness = hex == currentAccent ? new Thickness(2) : new Thickness(0);
            var tag = hex;
            dot.MouseLeftButtonUp += (_, _) => SaveAccent(tag, chips);
            chips.Add((holder, hex));
            accentWrap.Children.Add(holder);
        }
        panel.Children.Add(accentWrap);

        var previewText = new TextBlock { Text = "预览:今天 · 链接 · 选中高亮", FontSize = 12 };
        previewText.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Accent);
        var preview = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Child = previewText
        };
        preview.SetResourceReference(Border.BackgroundProperty, UiKeys.AccentSubtleBg);
        panel.Children.Add(preview);

        panel.Children.Add(Section("数据自检"));
        panel.Children.Add(selfBtn);
        panel.Children.Add(_selfResult);

        panel.Children.Add(Section("导出"));
        panel.Children.Add(exportBtn);
        panel.Children.Add(Hint("单文件导出全量流水(含已作废/退款/转账),UTF-8 BOM 可直接用 Excel 打开。", new Thickness(0, 2, 0, 4)));

        panel.Children.Add(Section("数据目录"));
        panel.Children.Add(Hint(
            $"账本默认目录:{AppPaths.UserDataDir}\n" +
            $"报表目录:{AppPaths.ReportDir}\n" +
            $"设置文件:{AppPaths.SettingsFile}\n" +
            $"日志目录:{AppPaths.LogDir}", new Thickness(0, 0, 0, 4)));
        panel.Children.Add(logRow);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 14)
    };

    private static TextBlock Section(string text)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 4)
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSection);
        return t;
    }

    private static TextBlock Hint(string text, Thickness margin)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        return t;
    }

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

    private void SaveAppearance(RadioButton rb)
    {
        if (rb.Tag is not string tag || rb.IsChecked != true)
            return;
        App.Settings.ThemeMode = tag;
        App.Settings.Save();
        ThemeService.Apply(ThemeService.ParseMode(tag));
    }

    private void SaveAccent(string hex, List<(Border Holder, string Hex)> chips)
    {
        // 默认钢蓝等价于未自选(此时 WPF-UI 控件吃系统强调色)
        App.Settings.Accent = hex == ThemeService.DefaultAccentHex ? null : hex;
        App.Settings.Save();

        var cur = App.Settings.Accent ?? ThemeService.DefaultAccentHex;
        foreach (var (holder, h) in chips)
            holder.BorderThickness = h == cur ? new Thickness(2) : new Thickness(0);

        ThemeService.Apply(ThemeService.Mode, hex);
    }

    private static Color HexColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString(ThemeService.DefaultAccentHex);
        }
    }

    private void SaveGrace(bool enabled)
    {
        App.Settings.MidnightGraceEnabled = enabled;
        App.Settings.Save();
    }

    private void OpenLogDir()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            Directory.CreateDirectory(AppPaths.LogDir);
            Process.Start(new ProcessStartInfo { FileName = AppPaths.LogDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开日志目录");
            MessageBox.Show($"打开日志目录失败:\n{ex.Message}", "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearLogs()
    {
        if (MessageBox.Show("清空日志目录下所有日志文件(app-*.log)?\n\n清空后新日志会继续写入。",
                "清空日志", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        try
        {
            Log.Clear();
            MessageBox.Show("日志已清空。", "清空日志", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "清空日志");
            MessageBox.Show($"清空日志失败:\n{ex.Message}", "清空日志", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RunSelfTest(Button btn)
    {
        btn.IsEnabled = false;
        _selfResult.Text = "自检运行中…";
        _selfResult.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        try
        {
            var (ok, steps, err) = SelfTest.Run();
            if (ok)
            {
                var sb = new System.Text.StringBuilder($"数据自检通过({steps.Count} 项):\n");
                foreach (var step in steps)
                    sb.Append("· ").Append(step).Append('\n');
                _selfResult.Text = sb.ToString();
                _selfResult.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Success);
            }
            else
            {
                _selfResult.Text = $"数据自检失败:\n{err}\n\n已执行步骤:\n{string.Join("\n", steps)}";
                _selfResult.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);
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
            Log.Error(ex, "导出CSV");
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
