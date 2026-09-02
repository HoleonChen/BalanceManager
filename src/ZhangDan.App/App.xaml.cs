using System.Windows;
using Wpf.Ui.Appearance;

namespace ZhangDan.App;

/// <summary>应用入口:初始化 SQLCipher、载入偏好、打开主窗。</summary>
public partial class App : Application
{
    /// <summary>全局偏好(启动时载入)。</summary>
    internal static AppSettings Settings { get; private set; } = AppSettings.Load();

    /// <summary>当前打开的账本会话(未打开为 null)。</summary>
    internal static LedgerSession? Ledger { get; private set; }

    /// <summary>打开账本后置为当前会话。</summary>
    internal static void Open(LedgerSession session)
    {
        Ledger?.Dispose();
        Ledger = session;
    }

    /// <summary>关闭当前账本(无则空操作)。</summary>
    internal static void CloseLedger()
    {
        Ledger?.Dispose();
        Ledger = null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LedgerStore.Init();
        AppPaths.EnsureDirs();

        // 跟随系统深浅色(WPF-UI)
        ApplicationThemeManager.Apply(ApplicationThemeManager.GetAppTheme());

        var window = new MainWindow();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Ledger?.Dispose();
        base.OnExit(e);
    }
}
