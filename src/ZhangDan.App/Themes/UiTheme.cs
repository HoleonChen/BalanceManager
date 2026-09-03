using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ZhangDan.App;

/// <summary>
/// 窗口表层主题化:WPF-UI 只给控件上主题,普通 Window 的窗底默认仍是系统白。
/// 用类级 Loaded 钩子统一给每个弹出窗口打主题底色(PanelBg)与默认前景(TextPrimary,
/// 未显式着色的文字继承它),均为动态资源 → 深浅切换时已开着的对话框也即时跟随。
/// 主窗自己已分区打底,跳过。
/// </summary>
internal static class UiTheme
{
    private static bool _hooked;

    internal static void HookWindowSurfaces()
    {
        if (_hooked)
            return;
        _hooked = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window w)
            return;
        if (ReferenceEquals(w, Application.Current?.MainWindow))
            return;
        w.SetResourceReference(Control.BackgroundProperty, UiKeys.PanelBg);
        w.SetResourceReference(TextElement.ForegroundProperty, UiKeys.TextPrimary);
    }
}
