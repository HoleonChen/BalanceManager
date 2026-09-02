using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>
/// 账本文件入口:初始化 SQLCipher、新建(加密)账本、按口令打开。
/// 账本 = 单个 .lbook 文件,本质 SQLCipher 加密的 SQLite;口令即密钥。
/// </summary>
internal static class LedgerStore
{
    private static readonly object Lock = new();
    private static bool _initialized;

    /// <summary>进程内初始化一次 SQLCipher 原生 provider(main 开头调用)。</summary>
    public static void Init()
    {
        if (_initialized)
            return;
        lock (Lock)
        {
            if (_initialized)
                return;
            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
        }
    }

    private static string ConnectionString(string path, string password)
        => $"Data Source={path};Password={password};Foreign Keys=True;";

    /// <summary>新建账本文件并写入骨架 schema(向导已保证文件不存在)。</summary>
    public static LedgerSession Create(string path, string ledgerName, string password)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connection = new SqliteConnection(ConnectionString(path, password));
        try
        {
            connection.Open();
            Schema.Ensure(connection, ledgerName);
            return new LedgerSession(path, ledgerName, connection);
        }
        catch
        {
            connection.Dispose();
            // 建库失败时把半成品文件清掉,避免留下打不开的空文件
            TryDelete(path);
            throw;
        }
    }

    /// <summary>打开既有账本并校验口令;口令错误抛 <see cref="LedgerPasswordException"/>。</summary>
    public static LedgerSession Open(string path, string password)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("账本文件不存在。", path);

        var connection = new SqliteConnection(ConnectionString(path, password));
        try
        {
            connection.Open();
            // 先做一次读取,把口令校验提前到「不是数据库/口令错」立刻暴露
            var name = ReadMeta(connection, "ledger.name") ?? Path.GetFileNameWithoutExtension(path);
            Schema.Ensure(connection, name);
            return new LedgerSession(path, name, connection);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26)
        {
            // SQLITE_NOTADB:对 SQLCipher 加密文件而言 = 口令错误(或非本应用加密文件)
            connection.Dispose();
            throw new LedgerPasswordException(path);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 清理失败不阻断主流程 */ }
    }
}

/// <summary>口令错误(或文件非本应用加密账本)。</summary>
internal sealed class LedgerPasswordException : Exception
{
    public LedgerPasswordException(string path)
        : base("口令错误,无法打开账本。")
    {
        Path = path;
    }

    public string Path { get; }
}
