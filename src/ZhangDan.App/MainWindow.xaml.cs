using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ZhangDan.App.Views;

namespace ZhangDan.App;

public partial class MainWindow
{
    private readonly PageBase[] _pages;

    public MainWindow()
    {
        InitializeComponent();

        // 各导航页(P2/P3 逐个填充实现;P1 先占位)
        _pages = new PageBase[]
        {
            new OverviewPage(),
            new FlowPage(),
            new PeriodsPage(),
            new AccountsPage(),
            new CategoriesPage(),
            new SettingsPage()
        };
        ((OverviewPage)_pages[0]).GoTo = NavTo;
        var flowPage = (FlowPage)_pages[1];
        ((AccountsPage)_pages[3]).ViewAccountFlows = id =>
        {
            flowPage.PresetAccount(id);
            NavTo(1);
        };

        _fileBtn.Content = App.Ledger is null ? "＋ 打开账本…" : "✕ 关闭账本";
        ShowStartOrContent();

        Loaded += (_, _) => TryAutoOpenLastLedger();
    }

    private void ShowStartOrContent()
    {
        bool has = App.Ledger is not null;
        _startPanel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        _content.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        _navPanel.IsEnabled = has;
        _fileBtn.Content = has ? "✕ 关闭账本" : "＋ 打开账本…";
        _ledgerNameText.Text = has ? App.Ledger!.Name : "未打开账本";

        if (has)
            NavTo(0);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string key)
            NavTo(Index(key));
    }

    private static int Index(string key) => key switch
    {
        "overview" => 0,
        "flow" => 1,
        "periods" => 2,
        "accounts" => 3,
        "categories" => 4,
        "settings" => 5,
        _ => 0
    };

    private void NavTo(int index)
    {
        if (index < 0 || index >= _pages.Length)
            return;
        _content.Content = _pages[index];
        _pages[index].OnShown();
    }

    private void FileAction_Click(object sender, RoutedEventArgs e)
    {
        if (App.Ledger is null)
            OpenLedgerFlow();
        else
            CloseLedger();
    }

    private void NewLedger_Click(object sender, RoutedEventArgs e) => NewLedgerFlow();

    private void OpenLedger_Click(object sender, RoutedEventArgs e) => OpenLedgerFlow();

    private void NewLedgerFlow()
    {
        var dlg = new Dialogs.CreateLedgerDialog { Owner = this };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            App.CloseLedger();
            var session = LedgerStore.Create(dlg.FilePath, dlg.LedgerName, dlg.Password);
            App.Open(session);
            App.Settings.LastLedgerPath = session.Path;
            App.Settings.Save();
            ShowStartOrContent();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"新建账本失败:\n{ex.Message}", "账单管理",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLedgerFlow()
    {
        var pick = new Microsoft.Win32.OpenFileDialog
        {
            Title = "打开账本",
            Filter = "账本文件 (*.lbook)|*.lbook|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = AppPaths.UserDataDir
        };
        if (pick.ShowDialog(this) != true)
            return;
        PromptOpen(pick.FileName);
    }

    private void TryAutoOpenLastLedger()
    {
        var path = App.Settings.LastLedgerPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        PromptOpen(path);
    }

    private void PromptOpen(string path)
    {
        var dlg = new Dialogs.PasswordDialog(path) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            App.CloseLedger();
            var session = LedgerStore.Open(path, dlg.Password);
            App.Open(session);
            App.Settings.LastLedgerPath = path;
            App.Settings.Save();
            ShowStartOrContent();
        }
        catch (ZhangDan.LedgerPasswordException)
        {
            MessageBox.Show(this, "口令错误,无法打开该账本。\n口令即密钥,遗忘无法找回。",
                "账单管理", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"打开账本失败:\n{ex.Message}", "账单管理",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseLedger()
    {
        App.CloseLedger();
        ShowStartOrContent();
    }
}
