using System;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 旧 WinForms 过渡用的数据自检入口:真正逻辑在 Core 的 <see cref="SelfTest"/>,
/// 这里只负责把结果弹窗展示(新 WPF 版在设置/自检页内展示)。
/// </summary>
internal static class DbSelfTest
{
    public static void Run(IWin32Window owner)
    {
        var (ok, steps, error) = SelfTest.Run();
        if (ok)
        {
            var detail = string.Join("\n", steps);
            MessageBox.Show(owner, "数据自检通过。\n\n" + detail, "数据自检",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(owner, $"数据自检失败:\n{error ?? string.Join("\n", steps)}", "数据自检",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
