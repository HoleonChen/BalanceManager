using System.Threading.Tasks;
using System.Windows;

namespace ZhangDan.App;

/// <summary>应用入口:初始化 SQLCipher、载入偏好、打开主窗。</summary>
public partial class App : Application
{
    private bool _fatalShown;
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
        Log.Configure(Log.ParseLevel(App.Settings.LogLevel), AppPaths.LogDir, console: false);
        HookGlobalErrors();

        // 外观(浅/深/跟随系统 + 强调色)——统一走 ThemeService
        ThemeService.Apply(ThemeService.ParseMode(App.Settings.ThemeMode), App.Settings.Accent);

        var window = new MainWindow();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Ledger?.Dispose();
        base.OnExit(e);
    }

    /// <summary>全局未捕获异常兜底:界面线程弹一次窗(其余仅落日志),致命/后台异常全部入日志。</summary>
    private void HookGlobalErrors()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "未处理的界面异常");
            if (_fatalShown)
            {
                args.Handled = true;
                return;
            }
            _fatalShown = true;
            try
            {
                MessageBox.Show($"发生未处理错误:\n{args.Exception.Message}",
                    "账单管理", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* 弹窗本身失败不再兜 */ }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error(args.ExceptionObject as Exception
                      ?? new Exception(args.ExceptionObject?.ToString()), "致命异常(进程即将退出)");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "后台任务未观察异常");
            args.SetObserved();
        };
    }
}
