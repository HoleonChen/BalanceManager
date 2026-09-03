namespace ZhangDan.App;

/// <summary>
/// 主题笔刷键清单(编译期防拼错)。
/// 结构/语义色放在 Light.xaml 与 Dark.xaml(键集一致,运行时按主题换源);
/// 强调色族键由 ThemeService 的 AccentOverlay 字典独占提供(默认钢蓝,可自选派生)。
/// 代码里着色一律 <c>ctrl.SetResourceReference(Prop, UiKeys.Xxx)</c>,换肤即时生效而不重建页面。
/// </summary>
internal static class UiKeys
{
    // —— 结构 / 中性色(浅/深字典)——
    public const string WindowBg = "WindowBg";          // 侧栏 / 窗口底色
    public const string PanelBg = "PanelBg";            // 内容区 / 面板 / 卡片底
    public const string TextPrimary = "TextPrimary";    // 主文字 / 标题
    public const string TextSecondary = "TextSecondary";// 次要 / 提示文字
    public const string TextMuted = "TextMuted";        // 更淡的副标题
    public const string TextSection = "TextSection";    // 节标题 / 净资产
    public const string Divider = "Divider";            // 分隔条
    public const string BorderLine = "BorderLine";      // 卡片 / 单元格边框
    public const string ControlTrack = "ControlTrack";  // 进度条轨道
    public const string Expense = "Expense";            // 支出 / 负值
    public const string Income = "Income";              // 收入
    public const string Error = "Error";                // 错误提示
    public const string Success = "Success";            // 成功 / 通过 / 启用
    public const string Warn = "Warn";                  // 逾期 / 警告
    public const string CalendarBlankCell = "CalendarBlankCell"; // 月历留白格

    // —— 强调色族(AccentOverlay 提供)——
    public const string Accent = "Accent";                  // 强调 / 链接 / 选中
    public const string AccentHover = "AccentHover";        // 强调悬停
    public const string AccentSubtleBg = "AccentSubtleBg";  // 强调淡底(焦点)
    public const string CalendarTodayBg = "CalendarTodayBg";// 月历「今天」格底
    public const string CalendarPeriodBg = "CalendarPeriodBg"; // 月历周期内格底
}
