using System;
using System.IO;

namespace ZhangDan;

/// <summary>
/// 各数据目录的固定约定。
/// 账本与程序文件分离:程序可整体替换,publish 文件夹内不落任何用户数据。
/// </summary>
internal static class AppPaths
{
    /// <summary>程序设置目录:%APPDATA%\账单管理(明文偏好 app.json 所在)。</summary>
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "账单管理");

    /// <summary>用户数据目录:文档\账单管理(账本文件默认存放处)。</summary>
    public static string UserDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "账单管理");

    /// <summary>报表导出目录。</summary>
    public static string ReportDir { get; } = Path.Combine(UserDataDir, "报表");

    /// <summary>明文偏好文件路径。</summary>
    public static string SettingsFile { get; } = Path.Combine(AppDataDir, "app.json");

    /// <summary>确保上述目录存在(幂等)。</summary>
    public static void EnsureDirs()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(UserDataDir);
        Directory.CreateDirectory(ReportDir);
    }
}
