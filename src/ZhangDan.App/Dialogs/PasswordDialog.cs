using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ZhangDan.App.Dialogs;

/// <summary>口令输入(打开账本/启动自动加载)。</summary>
internal sealed class PasswordDialog : Window
{
    private readonly PasswordBox _box = new() { Width = 360 };

    public string Password => _box.Password;

    public PasswordDialog(string ledgerPath)
    {
        Title = "输入口令";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var info = new TextBlock
        {
            Text = $"账本「{Path.GetFileName(ledgerPath)}」需要口令:",
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };

        var ok = new Button { Content = "确定", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(info);
        panel.Children.Add(_box);
        panel.Children.Add(row);

        Content = panel;
        Loaded += (_, _) => { _box.Focus(); };
    }
}
