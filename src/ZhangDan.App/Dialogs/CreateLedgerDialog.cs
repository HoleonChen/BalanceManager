using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ZhangDan.App.Dialogs;

/// <summary>新建账本:名称 + 存放位置 + 口令设定。校验通过后返回,由调用方建库。</summary>
internal sealed class CreateLedgerDialog : Window
{
    private const int MinPasswordLength = 6;

    private readonly TextBox _nameBox = new() { Text = "我的账本" };
    private readonly TextBox _pathBox;
    private readonly PasswordBox _passwordBox = new();
    private readonly PasswordBox _confirmBox = new();
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public string LedgerName => _nameBox.Text.Trim();
    public string Password => _passwordBox.Password;
    public string FilePath { get; private set; } = "";

    public CreateLedgerDialog()
    {
        Title = "新建账本";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _pathBox = new TextBox { Text = Path.Combine(AppPaths.UserDataDir, "我的账本.lbook") };

        var browse = new Button { Content = "浏览…", Width = 88, Height = 34, Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => PickPath();

        var pathRow = new DockPanel { Margin = new Thickness(0, 4, 0, 12) };
        DockPanel.SetDock(browse, Dock.Right);
        pathRow.Children.Add(browse);
        pathRow.Children.Add(_pathBox);

        var ok = new Button { Content = "创建", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => TryAccept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Field("账本名称", _nameBox));
        panel.Children.Add(Field("保存为", pathRow));
        panel.Children.Add(Field("口令(至少 6 位)", _passwordBox));
        panel.Children.Add(Field("确认口令", _confirmBox));

        var hint = new TextBlock
        {
            Text = "口令即密钥,遗忘无法找回,请务必记牢。",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _error.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(hint);
        panel.Children.Add(_error);
        panel.Children.Add(row);

        Content = panel;
    }

    private static UIElement Field(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Margin = new Thickness(0, 2, 0, 4) };
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(text);
        panel.Children.Add(input);
        return panel;
    }

    private void PickPath()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存账本到…",
            Filter = "账本文件 (*.lbook)|*.lbook",
            DefaultExt = "lbook",
            FileName = _nameBox.Text.Trim() + ".lbook",
            InitialDirectory = AppPaths.UserDataDir,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) == true)
            _pathBox.Text = dlg.FileName;
    }

    private void TryAccept()
    {
        if (LedgerName.Length == 0)
        {
            _error.Text = "请填写账本名称。";
            return;
        }
        if (_passwordBox.Password.Length < MinPasswordLength)
        {
            _error.Text = $"口令至少 {MinPasswordLength} 位。";
            return;
        }
        if (_passwordBox.Password != _confirmBox.Password)
        {
            _error.Text = "两次输入的口令不一致。";
            return;
        }

        string full;
        try
        {
            var p = _pathBox.Text.Trim();
            if (p.Length == 0)
                throw new FormatException("路径为空。");
            if (!Path.HasExtension(p))
                p += ".lbook";
            full = Path.GetFullPath(p);
        }
        catch (Exception ex)
        {
            _error.Text = $"保存位置无效:{ex.Message}";
            return;
        }
        if (File.Exists(full))
        {
            _error.Text = $"文件已存在:{full}\n请换一个名称或位置。";
            return;
        }

        FilePath = full;
        DialogResult = true;
    }
}
