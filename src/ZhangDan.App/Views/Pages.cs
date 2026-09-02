using System.Windows;
using System.Windows.Controls;

namespace ZhangDan.App.Views;

/// <summary>各导航页基类:页在成为当前内容时收到 <see cref="OnShown"/>。</summary>
internal abstract class PageBase : UserControl
{
    protected PageBase() { }

    /// <summary>该页被切入前台时刷新数据(此时 App.Ledger 必非空)。</summary>
    public virtual void OnShown() { }
}

/// <summary>P1 占位页;P2/P3 将逐个替换为真实实现。</summary>
internal static class Placeholder
{
    public static UIElement Build(string title, string note)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(28, 24, 0, 0)
        };
        var noteText = new TextBlock
        {
            Text = note,
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(30, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var panel = new StackPanel();
        panel.Children.Add(titleText);
        panel.Children.Add(noteText);
        return panel;
    }
}

internal sealed class DayLedgerPage : PageBase
{
    public DayLedgerPage() => Content = Placeholder.Build("今日记账", "记一笔/转账/当日流水、日期导航将在 P2 落地。");
}

internal sealed class FlowPage : PageBase
{
    public FlowPage() => Content = Placeholder.Build("流水", "周期/自定义范围流水与只读视图将在 P3 落地。");
}

internal sealed class PeriodsPage : PageBase
{
    public PeriodsPage() => Content = Placeholder.Build("周期", "新建/封存/解除封存/周期管理将在 P3 落地。");
}

internal sealed class AccountsPage : PageBase
{
    public AccountsPage() => Content = Placeholder.Build("账户", "净资产/派生余额/详情/校准将在 P3 落地。");
}

internal sealed class CategoriesPage : PageBase
{
    public CategoriesPage() => Content = Placeholder.Build("分类", "分类管理将在 P3 落地。");
}

internal sealed class SettingsPage : PageBase
{
    public SettingsPage() => Content = Placeholder.Build("设置", "偏好/目录信息/数据自检将在 P3 落地。");
}
