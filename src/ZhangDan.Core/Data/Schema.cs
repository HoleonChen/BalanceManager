using System;
using Microsoft.Data.Sqlite;

namespace ZhangDan;

/// <summary>
/// 账本骨架 schema(版本由 PRAGMA user_version 控制;后续加列/建表在此递增 CurrentVersion)。
/// 字段均对应设计文档 §3 数据模型。
/// </summary>
internal static class Schema
{
    // v3:资金池落地——每周期至多一个池(单池,fund_pools.period_id 唯一)。
    // v4:分类显式 kind(income/expense)——分类管理(新建/合并/删除)后不再能靠 id 区间 10–14 判收支。
    public const int CurrentVersion = 4;

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
  period_id       INTEGER REFERENCES periods(id),   -- 由日期自动归属,可空=未归属
  date            TEXT NOT NULL,
  account_id      INTEGER NOT NULL REFERENCES accounts(id),
  to_account_id   INTEGER REFERENCES accounts(id),  -- 仅 direction='transfer'
  category_id     INTEGER REFERENCES categories(id),
  channel         TEXT NOT NULL DEFAULT '',         -- 网购/实体/其他(渠道下拉)
  name            TEXT NOT NULL,                    -- 交易名,界面必填
  note            TEXT NOT NULL DEFAULT '',         -- 小类/细节
  amount_cents    INTEGER NOT NULL,                 -- 非负金额(分),方向由 direction 决定,转账看 principal/delta
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

-- 单池:每周期至多一个资金池,upsert 依赖此唯一约束
CREATE UNIQUE INDEX IF NOT EXISTS ux_fund_pools_period ON fund_pools(period_id);

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

    /// <summary>
    /// 建表/写账本名(仅新库 v0)。分类与账户都默认空,由用户在界面里自建
    /// (设计改定:新账本从零开始;旧库已有的预设分类作为既有数据原样保留)。
    /// </summary>
    public static void Ensure(SqliteConnection conn, string ledgerName)
    {
        long version = UserVersion(conn);

        if (version < 1)
        {
            ExecEach(conn, Ddl);
            SetMeta(conn, "ledger.name", ledgerName);
            SetMeta(conn, "created_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        else if (version < CurrentVersion)
        {
            // 老库升级:早期版本建表时还没有 fund_pools / reserve_items / calibration_log 等表,
            // 补跑一遍幂等 DDL(全部 IF NOT EXISTS)把这些表补上,再走各自增量迁移。
            ExecEach(conn, Ddl);
        }

        if (version < 2)
        {
            // v2:转账类别(设计 §3.5 五类:互转/充值/提现/理财结算/存取),
            // 与收支分类分开存,避免污染手动录入的收支分类列表
            ExecEach(conn, "ALTER TABLE transactions ADD COLUMN transfer_kind TEXT;");
        }

        if (version < 3)
        {
            // v3:资金池单池约束(每周期至多一个池);新库建表已含,老库补加
            ExecEach(conn,
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_fund_pools_period ON fund_pools(period_id);");
        }

        if (version < 4)
        {
            // v4:分类 kind 显式化。seed 收支靠固定 id(1–9 支出、10–14 收入)回填;
            // 此后用户自建分类写入 kind,不再依赖 id 区间。
            ExecEach(conn, "ALTER TABLE categories ADD COLUMN kind TEXT;");
            ExecEach(conn, "UPDATE categories SET kind = 'expense' WHERE id BETWEEN 1 AND 9;");
            ExecEach(conn, "UPDATE categories SET kind = 'income'  WHERE id BETWEEN 10 AND 14;");
        }

        if (version < CurrentVersion)
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
        // 先整段执行前去掉「整行注释」:注释里的 ';' 若参与切分会把残句当 SQL,出现
        // 「near '...': syntax error」(如历史 bug:注释里写 ';upsert ...')。
        var cleaned = new System.Text.StringBuilder();
        foreach (var line in sql.Split('\n'))
        {
            if (line.TrimStart().StartsWith("--", StringComparison.Ordinal))
                continue;
            cleaned.AppendLine(line);
        }

        foreach (var statement in cleaned.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries))
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
