using System;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>一个已打开的账本(文件路径 + 账本名 + 打开的连接)。</summary>
internal sealed class LedgerSession : IDisposable
{
    public LedgerSession(string path, string name, SqliteConnection connection)
    {
        Path = path;
        Name = name;
        Connection = connection;
    }

    public string Path { get; }
    public string Name { get; }
    public SqliteConnection Connection { get; }

    /// <summary>读 meta;键不存在返回 null。</summary>
    public string? GetMeta(string key)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($k, $v);";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => Connection.Dispose();
}
