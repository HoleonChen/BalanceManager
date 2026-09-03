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

internal sealed class FlowPage : PageBase
{
    public FlowPage() => Content = Placeholder.Build("流水", "周期/自定义范围流水与只读视图将在 P3 落地。");
}


