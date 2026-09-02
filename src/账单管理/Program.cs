using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // WinForms 统一初始化(高 DPI / 视觉样式等)
        ApplicationConfiguration.Initialize();

        // 全应用默认字体:微软雅黑(中文)
        Application.SetDefaultFont(new Font("Microsoft YaHei UI", 9f));

        // SQLCipher 原生库初始化(打开/新建账本前必须调用一次)
        LedgerStore.Init();

        Application.Run(new MainForm());
    }
}
