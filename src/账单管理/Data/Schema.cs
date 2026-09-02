using System;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>
/// 账本骨架 schema(版本由 PRAGMA user_version 控制;后续加列/建表在此递增 CurrentVersion)。
/// 字段均对应设计文档 §3 数据模型。
/// </summary>
internal static class Schema
{
    public const int CurrentVersion = 1;

    // 注意:Microsoft.Data.Sqlite 一条命令只执行首条语句,故统一按 ';' 切分逐条执行。
    private const string Ddl = @"
CREATE TABLE IF NOT EXISTS meta (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS periods (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  name       TEXT NOT NULL,
  note       TEXT,
  start_date TEXT NOT NULL,                 -- ISO yyyy-MM-dd
  end_date   TEXT,
  status     TEXT NOT NULL DEFAULT 'active'
             CHECK (status IN ('active', 'ended', 'sealed'))
);

CREATE TABLE IF NOT EXISTS accounts (
  id                 INTEGER PRIMARY KEY AUTOINCREMENT,
  name               TEXT NOT NULL,
  platform           TEXT,                  -- 微信/支付宝/银行/投资/现金/储值卡
  type               TEXT NOT NULL
                     CHECK (type IN ('wallet', 'money_fund', 'bank', 'cash',
                                     'fixed_deposit', 'fund', 'prepaid')),
  enabled            INTEGER NOT NULL DEFAULT 1,
  balance_base_cents INTEGER NOT NULL DEFAULT 0,
  balance_date       TEXT,
  sort_order         INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS categories (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  parent_id  INTEGER REFERENCES categories(id),
  name       TEXT NOT NULL,
  keyword    TEXT,                          -- 导入自动归类关键词
  color      TEXT,                          -- 主题色 #RRGGBB(图表统一引用)
  sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS transactions (
  id              INTEGER PRIMARY KEY AUTOINCREMENT,
  period_id       INTEGER REFERENCES periods(id),   -- 由日期自动归属;可空=未归属
  date            TEXT NOT NULL,
  account_id      INTEGER NOT NULL REFERENCES accounts(id),
  to_account_id   INTEGER REFERENCES accounts(id),  -- 仅 direction='transfer'
  category_id     INTEGER REFERENCES categories(id),
  channel         TEXT NOT NULL DEFAULT '',         -- 网购/实体/其他(渠道下拉)
  name            TEXT NOT NULL,                    -- 交易名,界面必填
  note            TEXT NOT NULL DEFAULT '',         -- 小类/细节
  amount_cents    INTEGER NOT NULL,                 -- 非负金额(分);方向由 direction 决定;转账看 principal/delta
  direction       TEXT NOT NULL CHECK (direction IN ('in', 'out', 'transfer')),
  counterparty    TEXT,
  source          TEXT NOT NULL DEFAULT 'manual',   -- manual/wx/zfb/legacy
  ref_id          TEXT,                             -- 平台交易单号
  status          TEXT NOT NULL DEFAULT 'normal'
                  CHECK (status IN ('normal', 'refunded', 'cancelled')),
  in_pool         INTEGER NOT NULL DEFAULT 1,       -- 计入资金池
  refund_of       INTEGER REFERENCES transactions(id),
  group_parent    INTEGER REFERENCES transactions(id),  -- 混合支付主笔
  principal_cents INTEGER,                          -- transfer 本金
  delta_cents     INTEGER NOT NULL DEFAULT 0,       -- transfer 浮动(+/-,默认0)
  created_at      TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_tx_period  ON transactions(period_id);
CREATE INDEX IF NOT EXISTS ix_tx_account ON transactions(account_id);
CREATE INDEX IF NOT EXISTS ix_tx_date    ON transactions(date);

CREATE TABLE IF NOT EXISTS fund_pools (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  period_id     INTEGER NOT NULL REFERENCES periods(id),
  name          TEXT NOT NULL,
  account_id    INTEGER NOT NULL REFERENCES accounts(id),
  budget_cents  INTEGER NOT NULL,
  reserve_cents INTEGER NOT NULL DEFAULT 0,
  created_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS reserve_items (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  pool_id      INTEGER NOT NULL REFERENCES fund_pools(id),
  due          TEXT,
  item         TEXT NOT NULL,
  amount_cents INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS calibration_log (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id   INTEGER NOT NULL REFERENCES accounts(id),
  recorded_at  TEXT NOT NULL,
  book_cents   INTEGER NOT NULL,
  actual_cents INTEGER NOT NULL,
  diff_cents   INTEGER NOT NULL,
  method       TEXT NOT NULL DEFAULT 'adjustment',  -- adjustment/real_details/base_only
  note         TEXT
);
";

    // 预设分类:8 个支出大类 + 「差额调整」(挂其他下) + 5 个收入类。
    // color 为固定调色板(图表/界面统一引用);收入类暂留 NULL 走自动分配。
    private const string Seed = @"
INSERT OR IGNORE INTO categories (id, parent_id, name, color, sort_order) VALUES
  (1,  NULL, '餐饮',   '#F06292', 0),
  (2,  NULL, '交通',   '#42A5F5', 1),
  (3,  NULL, '购物',   '#FFA726', 2),
  (4,  NULL, '教育',   '#8E24AA', 3),
  (5,  NULL, '娱乐',   '#29B6F6', 4),
  (6,  NULL, '医疗',   '#66BB6A', 5),
  (7,  NULL, '居住',   '#5C6BC0', 6),
  (8,  NULL, '其他',   '#9E9E9E', 7),
  (9,  8,    '差额调整','#B0BEC5', 0),
  (10, NULL, '生活费',  NULL, 0),
  (11, NULL, '家人转账', NULL, 1),
  (12, NULL, '红包',    NULL, 2),
  (13, NULL, '理财收益', NULL, 3),
  (14, NULL, '其他',    NULL, 4);
";

    /// <summary>在新库上建表 + 预设分类 + 写入账本名与创建时间;已建库(version&gt;0)则不动。</summary>
    public static void Ensure(SqliteConnection conn, string ledgerName)
    {
        long version = UserVersion(conn);
        if (version >= CurrentVersion)
            return;

        ExecEach(conn, Ddl);
        ExecEach(conn, Seed);
        SetMeta(conn, "ledger.name", ledgerName);
        SetMeta(conn, "created_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        SetUserVersion(conn, CurrentVersion);
    }

    public static long UserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private static void SetUserVersion(SqliteConnection conn, int version)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    private static void ExecEach(SqliteConnection conn, string sql)
    {
        foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(statement))
                continue;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            cmd.ExecuteNonQuery();
        }
    }

    private static void SetMeta(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO meta (key, value) VALUES ($k, $v);";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }
}
