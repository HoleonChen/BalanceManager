using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace ZhangDan.App;

/// <summary>主题三态:跟随系统 / 浅色 / 深色。</summary>
internal enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>
/// 主题控制器(组合根职责之一)。
/// 换肤= ① 让 WPF-UI 镀层切肤(ApplicationThemeManager.Apply)
///       ② 把自绘语义色字典 Palette 原地换源 Light/Dark(键不变 → DynamicResource 即时重着色)
///       ③ 重建强调色叠层 AccentOverlay(默认钢蓝 #4682B4,用户可自选派生)。
/// 页面控件一律经 DynamicResource 引用这些键,故换肤无需重建页面、不丢页面状态。
/// </summary>
internal static class ThemeService
{
    /// <summary>默认强调色(浅色=原 SteelBlue;深色自动提亮)。用户未自选时观感与旧版一致。</summary>
    internal const string DefaultAccentHex = "#4682B4";

    internal static ThemeMode Mode { get; private set; } = ThemeMode.System;

    internal static string AccentHex { get; private set; } = DefaultAccentHex;

    /// <summary>当前生效的实际深浅(WPF-UI 枚举)。</summary>
    internal static ApplicationTheme Effective { get; private set; } = ApplicationTheme.Light;

    internal static ResourceDictionary Palette { get; } = new();
    internal static ResourceDictionary AccentOverlay { get; } = new();

    private static bool _pushed;
    private static bool _parityChecked;
    private static bool _systemHooked;

    /// <summary>解析设置里的模式串(system/light/dark;未知或缺省 → System)。</summary>
    internal static ThemeMode ParseMode(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.System
    };

    /// <summary>唯一换肤入口:模式(必)与强调色(选)。首次调用同时把两个字典挂进 Application 资源。</summary>
    internal static void Apply(ThemeMode mode, string? accentHex = null)
    {
        Mode = mode;
        if (!string.IsNullOrWhiteSpace(accentHex))
            AccentHex = accentHex;

        EnsureMerged();
        Effective = ResolveEffective();
        ApplicationThemeManager.Apply(Effective);                       // WPF-UI 控件镀层
        Palette.Source = Effective == ApplicationTheme.Dark
            ? new Uri("Themes/Dark.xaml", UriKind.Relative)
            : new Uri("Themes/Light.xaml", UriKind.Relative);          // 自绘语义色原地换源
        RebuildAccentOverlay();
        CheckParityOnce();
    }

    /// <summary>取当前设置里的强调色;空 → 默认钢蓝。</summary>
    internal static Color AccentColor() => ParseColor(AccentHex, DefaultAccentHex);

    private static ApplicationTheme ResolveEffective()
    {
        if (Mode != ThemeMode.System)
            return Mode == ThemeMode.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        // 跟随系统:OS 深色 → 深;其余(含高对比)按浅处理
        var sys = ApplicationThemeManager.GetSystemTheme();
        return sys is SystemTheme.Dark or SystemTheme.HCBlack
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;
    }

    private static void EnsureMerged()
    {
        if (_pushed)
            return;
        _pushed = true;
        Application.Current.Resources.MergedDictionaries.Add(Palette);
        Application.Current.Resources.MergedDictionaries.Add(AccentOverlay);
        HookSystemFollow();
    }

    private static void RebuildAccentOverlay()
    {
        var dark = Effective == ApplicationTheme.Dark;
        var panel = ParseColor(dark ? "#242424" : "#FAFBFD");
        var accent = AccentColor();
        bool isDefault = AccentHex == DefaultAccentHex;

        // 自定义色在深色下提亮,保证按钮/链接在深底上够亮
        if (!isDefault && dark && Luminance(accent) < 0.28)
            accent = Lerp(accent, Colors.White, 0.25);

        var hover = isDefault
            ? ParseColor(dark ? "#8FC0E8" : "#3A6E9E")
            : (dark ? Scale(accent, 1.18f) : Scale(accent, 0.82f));

        Color subtle, period;
        if (isDefault)
        {
            // 默认钢蓝:浅色复刻重构前「今天/期内」格底;深色给一组可读的蓝灰
            subtle = ParseColor(dark ? "#2A3B52" : "#EAF2FB");
            period = ParseColor(dark ? "#2C3743" : "#F4F8FC");
        }
        else
        {
            subtle = Lerp(panel, accent, dark ? 0.20 : 0.09);
            period = Lerp(panel, accent, dark ? 0.10 : 0.033);
        }

        AccentOverlay[UiKeys.Accent] = new SolidColorBrush(accent);
        AccentOverlay[UiKeys.AccentHover] = new SolidColorBrush(hover);
        AccentOverlay[UiKeys.AccentSubtleBg] = new SolidColorBrush(subtle);
        AccentOverlay[UiKeys.CalendarTodayBg] = new SolidColorBrush(subtle);
        AccentOverlay[UiKeys.CalendarPeriodBg] = new SolidColorBrush(period);
    }

    /// <summary>跟随系统实时切肤:仅 System 模式生效;失败仅失去实时跟随(重启/重进设置仍正确)。</summary>
    private static void HookSystemFollow()
    {
        if (_systemHooked)
            return;
        _systemHooked = true;
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // 某些无交互会话拿不到 SystemEvents,忽略
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            var app = Application.Current;
            if (app?.Dispatcher is not { } d)
                return;
            if (d.CheckAccess())
                ReapplyIfSystem();
            else
                d.BeginInvoke(ReapplyIfSystem);
        }
        catch { /* 忽略 */ }
    }

    private static void ReapplyIfSystem()
    {
        if (Mode == ThemeMode.System)
            Apply(ThemeMode.System);
    }

    /// <summary>浅/深字典键集一致性自检(仅 Windows 运行时一次;防漏键静默不着色)。</summary>
    private static void CheckParityOnce()
    {
        if (_parityChecked)
            return;
        _parityChecked = true;
        try
        {
            var light = new ResourceDictionary { Source = new Uri("Themes/Light.xaml", UriKind.Relative) };
            var dark = new ResourceDictionary { Source = new Uri("Themes/Dark.xaml", UriKind.Relative) };
            var miss = light.Keys.Cast<object>().Where(k => !dark.Contains(k)).ToList();
            var extra = dark.Keys.Cast<object>().Where(k => !light.Contains(k)).ToList();
            if (miss.Count > 0 || extra.Count > 0)
                Log.Warn($"主题字典键集不一致——缺 {miss.Count}/{extra.Count} 个键,相关控件换肤将不着色。");
        }
        catch (Exception ex)
        {
            Log.Warn("主题字典自检失败:" + ex.Message);
        }
    }

    // ---------- 颜色工具 ----------

    private static Color ParseColor(string hex, string fallback = "#808080")
    {
        try
        {
            hex = hex.Trim();
            if (hex.Length == 7 && hex[0] == '#')
                return Color.FromRgb(
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16));
        }
        catch { /* 交给兜底 */ }
        return (Color)ColorConverter.ConvertFromString(fallback);
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static byte Blend(byte v, byte w, double t) => (byte)Math.Clamp(v + (w - v) * t, 0, 255);

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        Blend(a.R, b.R, t), Blend(a.G, b.G, t), Blend(a.B, b.B, t));

    private static Color Scale(Color c, float f) => Color.FromRgb(
        (byte)Math.Clamp((int)(c.R * f), 0, 255),
        (byte)Math.Clamp((int)(c.G * f), 0, 255),
        (byte)Math.Clamp((int)(c.B * f), 0, 255));
}
