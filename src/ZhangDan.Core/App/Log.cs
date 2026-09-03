using System;
using System.IO;
using System.Text;

namespace ZhangDan;

/// <summary>日志级别(数值越小越详细)。</summary>
internal enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

/// <summary>当前日志配置快照(供自检存/还原)。</summary>
internal sealed record LogConfig(LogLevel Level, string? Dir, bool Console);

/// <summary>
/// 极简文件 + 控制台日志器(线程安全)。零第三方依赖,供各壳与命令行工具复用。
/// 安全约定:只记路径 / 操作上下文 / 异常文本;绝不落口令、账本内容、原始金额(由调用点克制)。
/// 永不抛、不阻塞业务:文件/控制台写入的任何失败都静默吞掉;未 Configure 文件 sink 前文件写入为 no-op。
/// 文件按自然日分片 <c>app-yyyyMMdd.log</c>,首开当天顺带清理超过 <see cref="KeepDays"/> 天的旧片。
/// </summary>
internal static class Log
{
    /// <summary>日志片保留天数(超过即清理)。</summary>
    public const int KeepDays = 30;

    private static readonly object Gate = new();
    private static LogLevel _level = LogLevel.Info;
    private static string? _dir;          // null = 不开文件 sink
    private static bool _console;
    private static StreamWriter? _writer;
    private static string? _day;          // 已开文件对应的 yyyy-MM-dd

    /// <summary>当前配置快照。</summary>
    public static LogConfig Config
    {
        get { lock (Gate) return new LogConfig(_level, _dir, _console); }
    }

    /// <summary>重配日志:关旧会话、按新级别/目录/控制台开关惰性重开(首次写才建文件)。</summary>
    public static void Configure(LogLevel level, string? dir = null, bool console = false)
    {
        lock (Gate)
        {
            CloseWriterLocked();
            _level = level;
            _dir = dir;
            _console = console;
        }
    }

    /// <summary>把字符串级别解析为 <see cref="LogLevel"/>(未知/缺省 → Info)。</summary>
    public static LogLevel ParseLevel(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "debug" => LogLevel.Debug,
        "info" => LogLevel.Info,
        "warn" or "warning" => LogLevel.Warn,
        "error" => LogLevel.Error,
        _ => LogLevel.Info
    };

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Warn(string msg) => Write(LogLevel.Warn, msg);
    public static void Error(string msg) => Write(LogLevel.Error, msg);

    /// <summary>记异常:首行含上下文/类型/消息,随后缩进的堆栈逐行。</summary>
    public static void Error(Exception ex, string? context = null)
    {
        var prefix = context is null ? "" : context + ": ";
        Write(LogLevel.Error, prefix + ex.GetType().Name + ": " + ex.Message);
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            foreach (var line in ex.StackTrace.Replace("\r\n", "\n").Split('\n'))
                Write(LogLevel.Error, "    " + line);
        }
    }

    /// <summary>
    /// 清空日志目录下所有当日分片。先关掉当前打开的 writer 再删(否则 Windows 上文件被占用删不掉);
    /// 之后首次写日志会自动重建当天文件。未配置文件 sink 或删除失败时静默。
    /// </summary>
    public static void Clear()
    {
        lock (Gate)
        {
            var dir = _dir;
            if (dir is null)
                return;
            CloseWriterLocked();
            try
            {
                foreach (var f in Directory.GetFiles(dir, "app-*.log"))
                {
                    try { File.Delete(f); }
                    catch { /* 单文件删不掉不阻断整体 */ }
                }
            }
            catch { /* 目录不可枚举时忽略 */ }
        }
    }

    private static void Write(LogLevel level, string line)
    {
        string text;
        lock (Gate)
        {
            if (level < _level)
                return;
            text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Tag(level)}] {line}";
            if (_console)
            {
                try { Console.WriteLine(text); }
                catch { /* 控制台不可用时忽略 */ }
            }
            if (_dir is null)
                return;
            try
            {
                EnsureOpenLocked();
                _writer!.WriteLine(text);
                _writer.Flush();
            }
            catch { /* 落盘失败不影响业务 */ }
        }
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        _ => "ERR"
    };

    /// <summary>调用方已持锁:按需建目录/开当日文件/跨天轮转/清理旧片。</summary>
    private static void EnsureOpenLocked()
    {
        if (_dir is null)
            return;
        var now = DateTime.Now;
        var day = now.ToString("yyyy-MM-dd");
        if (_writer is not null && _day == day)
            return;
        CloseWriterLocked();
        try { Directory.CreateDirectory(_dir); }
        catch { return; }
        var path = Path.Combine(_dir, "app-" + now.ToString("yyyyMMdd") + ".log");
        try
        {
            // FileShare.ReadWrite:进程运行时也允许外部读取/临时读取日志(默认 Share.Read 在 Windows 上会挡别的读句柄)
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(false));
            _day = day;
            PruneLocked();
        }
        catch { _writer = null; _day = null; }
    }

    /// <summary>调用方已持锁:关闭当前 writer。</summary>
    private static void CloseWriterLocked()
    {
        if (_writer is null)
            return;
        try { _writer.Dispose(); }
        catch { /* 忽略 */ }
        _writer = null;
        _day = null;
    }

    /// <summary>调用方已持锁:删除超过保留期的当日日志片。</summary>
    private static void PruneLocked()
    {
        if (_dir is null)
            return;
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-KeepDays);
            foreach (var f in Directory.GetFiles(_dir, "app-*.log"))
            {
                var info = new FileInfo(f);
                if (info.LastWriteTime < cutoff)
                {
                    try { info.Delete(); }
                    catch { /* 删不掉也由它去 */ }
                }
            }
        }
        catch { /* 清理失败忽略 */ }
    }
}
