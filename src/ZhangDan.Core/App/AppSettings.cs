using System;
using System.IO;
using System.Text.Json;

namespace ZhangDan;

/// <summary>
/// 明文偏好设置,存 %APPDATA%\账单管理\app.json(账本文件之外)。
/// 口令本身永不落此文件——「记住口令」走 Windows 凭据管理器(后续实现)。
/// </summary>
internal sealed class AppSettings
{
    /// <summary>上次打开的账本路径(启动时自动加载用)。</summary>
    public string? LastLedgerPath { get; set; }

    /// <summary>是否记住口令(标记位;真正存储凭据在 Credential Manager,见备注)。</summary>
    public bool RememberPassword { get; set; }

    /// <summary>凌晨宽限开关:过了 0 点仍允许把流水记到「昨天」。</summary>
    public bool MidnightGraceEnabled { get; set; }

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(AppPaths.SettingsFile));
                if (loaded is not null)
                    settings = loaded;
            }
        }
        catch (Exception ex)
        {
            // 设置损坏只影响记忆,不挡启动
            System.Diagnostics.Debug.WriteLine($"读取设置失败:{ex.Message}");
        }
        return settings;
    }

    public void Save()
    {
        AppPaths.EnsureDirs();
        var tmp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(
            this,
            new JsonSerializerOptions { WriteIndented = true }));
        // 先写临时文件再覆盖,避免写入一半损坏
        File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
    }
}
