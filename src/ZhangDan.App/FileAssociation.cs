using System;
using Microsoft.Win32;

namespace ZhangDan.App;

/// <summary>
/// .lbook 文件关联(免管理员,HKCU):首次启动注册,此后双击 .lbook 由本程序经命令行参数打开。
/// 单文件发布时 exe 为本体路径。失败仅记日志,不挡启动。
/// </summary>
internal static class FileAssociation
{
    private const string ProgId = "ZhangDan.Ledger";
    private const string Ext = ".lbook";

    public static void EnsureRegistered()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;

            using (var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Ext))
            {
                ext.SetValue("", ProgId);
                using var openWith = ext.CreateSubKey("OpenWithProgids");
                openWith.SetValue(ProgId, Array.Empty<byte>());
            }
            using (var prog = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                prog.SetValue("", "账单管理账本");
                using var icon = prog.CreateSubKey("DefaultIcon");
                icon.SetValue("", exe + ",0");
            }
            using (var cmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
                cmd.SetValue("", $"\"{exe}\" \"%1\"");

            Log.Info("文件关联已注册(.lbook → " + exe + ")");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "注册 .lbook 文件关联");
        }
    }
}
