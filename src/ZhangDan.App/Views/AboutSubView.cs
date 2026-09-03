using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ZhangDan;

namespace ZhangDan.App.Views;

/// <summary>设置 →「关于账单管理」子页:版本/运行环境/数据位置/第三方许可/隐私口令说明。</summary>
internal sealed class AboutSubView : UserControl
{
    private readonly Action _back;

    public AboutSubView(Action back)
    {
        _back = back;
        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Title("关于"));

        panel.Children.Add(Section("版本"));
        panel.Children.Add(Info(VersionText));

        panel.Children.Add(Section("运行环境"));
        panel.Children.Add(Info("net8.0-windows · WPF-UI 3.0.5\nSQLCipher(SQLitePCLRaw) · Microsoft.Data.Sqlite · QuestPDF · ClosedXML · ScottPlot"));

        panel.Children.Add(Section("数据位置"));
        panel.Children.Add(LocationRow("账本目录", AppPaths.UserDataDir));
        panel.Children.Add(LocationRow("报表目录", AppPaths.ReportDir));
        panel.Children.Add(LocationRow("日志目录", AppPaths.LogDir));
        panel.Children.Add(Info($"设置文件:{AppPaths.SettingsFile}(明文偏好,不含口令)"));

        panel.Children.Add(Section("第三方许可"));
        panel.Children.Add(Hint("QuestPDF Community(免费门槛:年收入<$1M/非商用;含水印即 Community 标识,勿去除)——报表 PDF 由此生成。"));
        panel.Children.Add(Hint("ClosedXML · ScottPlot · WPF-UI —— MIT。SQLitePCLRaw(bundle_e_sqlcipher):SQLite(公有领域)+ SQLCipher(BSD 风格)。"));

        panel.Children.Add(Section("隐私与口令"));
        panel.Children.Add(Hint("全离线、无任何网络上报。账本为 SQLCipher 加密单文件;口令即密钥,遗忘无法找回。报表历史仅记录在本账本内。"));

        var backBtn = new Button { Content = "← 返回设置", Width = 130, Height = 34, Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        backBtn.Click += (_, _) => _back();
        panel.Children.Add(backBtn);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static string VersionText
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version?.ToString() ?? "?";
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return $"账单管理 v{v}" + (string.IsNullOrEmpty(info) ? "" : $"  ({info})") +
                   $"\n账本 schema v{Schema.CurrentVersion}";
        }
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
        var t = new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSection);
        return t;
    }

    private static TextBlock Info(string text) => Hint(text, new Thickness(0, 0, 0, 4));

    private static TextBlock Hint(string text) => Hint(text, new Thickness(0, 0, 0, 4));

    private static TextBlock Hint(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = margin };
        t.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.TextSecondary);
        return t;
    }

    private UIElement LocationRow(string label, string dir)
    {
        var text = new TextBlock { Text = dir, Width = 460, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var open = new Button { Content = "打开", Width = 70, Height = 30 };
        open.Click += (_, _) => OpenFolder(dir);
        var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(open, Dock.Right);
        dock.Children.Add(open);
        dock.Children.Add(text);
        return dock;
    }

    private static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "关于页打开目录");
        }
    }
}
