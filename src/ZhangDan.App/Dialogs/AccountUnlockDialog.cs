using System.Windows;
using System.Windows.Controls;
using ZhangDan;

namespace ZhangDan.App.Dialogs;

/// <summary>
/// 账户页二次验证(防窥屏):需再次输入本账本口令才放行。每次进入账户页都弹。
/// 验证方式 = 用输入的口令重开账本(成功即对,失败即错),不保存口令。
/// </summary>
internal sealed class AccountUnlockDialog : Window
{
    private readonly string _ledgerPath;
    private readonly PasswordBox _pw = new() { Width = 280 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };

    public AccountUnlockDialog(string ledgerPath)
    {
        _ledgerPath = ledgerPath;
        Title = "账户页验证";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        _error.SetResourceReference(TextBlock.ForegroundProperty, UiKeys.Error);

        var ok = new Button { Content = "验证", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Verify();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "账户页会展示各账户余额与净资产,为防他人窥屏需再次输入账本口令。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "账本口令:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(_pw);
        panel.Children.Add(row);
        panel.Children.Add(_error);
        panel.Children.Add(btns);
        Content = panel;

        Loaded += (_, _) => _pw.Focus();
    }

    private void Verify()
    {
        var password = _pw.Password;
        if (password.Length == 0)
        {
            _error.Text = "请输入口令。";
            return;
        }
        try
        {
            using var session = LedgerStore.Open(_ledgerPath, password);   // 口令对才打得开
            DialogResult = true;
        }
        catch (LedgerPasswordException)
        {
            _error.Text = "口令错误。";
            _pw.Clear();
            _pw.Focus();
        }
        catch
        {
            _error.Text = "验证失败(账本不可用)。";
        }
    }
}
