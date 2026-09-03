using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ZhangDan;

namespace ZhangDan.Sealer;

/// <summary>
/// 账本密封器 CLI:读「数据规范」明文 JSON(见 tools/zd-import/SPEC.md),用与主程序同款
/// SQLCipher 建 .lbook 并做一次性重开验证。
/// 用法:ZhangDan.Sealer import &lt;spec.json&gt; --out &lt;out.lbook&gt; [--name 账本名] [--password …]
/// </summary>
internal static class Program
{
    private static int Main(string[] argv)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // 控制台输出保持原样(给 zd_book.py/人看);额外镜像一份文件日志供审计排障
        Log.Configure(LogLevel.Info, AppPaths.LogDir, console: false);
        try
        {
            if (argv.Length == 0 || argv[0] is "-h" or "--help" or "help")
            {
                PrintHelp();
                return argv.Length == 0 ? 2 : 0;
            }
            if (argv[0] != "import")
            {
                Console.Error.WriteLine($"未知子命令:{argv[0]}");
                PrintHelp();
                return 2;
            }

            string? specPath = null, outPath = null, name = null, password = null;
            for (int i = 1; i < argv.Length; i++)
            {
                var a = argv[i];
                if (a == "--")
                    continue;
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    string key = a, val = "";
                    int eq = a.IndexOf('=');
                    if (eq > 0)
                    {
                        key = a[..eq];
                        val = a[(eq + 1)..];
                    }
                    (string, string?) opt = key switch
                    {
                        "--out" => ("out", null),
                        "--name" => ("name", null),
                        "--password" => ("password", null),
                        _ => (key[2..], null)
                    };
                    if (opt.Item2 is null && eq <= 0)
                    {
                        if (i + 1 >= argv.Length)
                        {
                            Console.Error.WriteLine($"选项 {key} 缺值。");
                            return 2;
                        }
                        val = argv[++i];
                    }
                    switch (key)
                    {
                        case "--out": outPath = val; break;
                        case "--name": name = val; break;
                        case "--password": password = val; break;
                        default:
                            Console.Error.WriteLine($"未知选项:{key}");
                            return 2;
                    }
                }
                else if (specPath is null)
                    specPath = a;
                else
                {
                    Console.Error.WriteLine($"多余的参数:{a}");
                    return 2;
                }
            }

            if (specPath is null)
            {
                Console.Error.WriteLine("缺少 spec.json 路径。");
                return 2;
            }
            if (outPath is null)
            {
                Console.Error.WriteLine("缺少 --out <账本.lbook>。");
                return 2;
            }
            if (password is null)
            {
                password = Environment.GetEnvironmentVariable("ZD_BOOK_PASSWORD");
                if (string.IsNullOrEmpty(password))
                {
                    Console.Write("账本口令(输入后回车):");
                    password = Console.ReadLine() ?? "";
                }
            }
            if (password.Length == 0)
                throw new InvalidOperationException("口令为空,无法建加密账本。请用 --password 或环境变量 ZD_BOOK_PASSWORD 提供。");

            var specText = File.ReadAllText(specPath);
            using var doc = JsonDocument.Parse(specText, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var root = doc.RootElement;

            var ledgerName = name ?? RootStr(root, "ledgerName", null)
                ?? Path.GetFileNameWithoutExtension(outPath);

            LedgerStore.Init();

            // 建库(内部会自动建表、写账本名、升 user_version=4;失败自删半成品)
            LedgerSession? ses = null;
            try
            {
                ses = LedgerStore.Create(outPath, ledgerName, password);
                var report = Importer.Build(ses, root);
                ses.Dispose();
                ses = null;
                Log.Info($"已生成:{outPath}  账本名「{ledgerName}」");
                Console.WriteLine($"已生成:{outPath}  账本名「{ledgerName}」");
                Console.WriteLine(report);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "导入失败");
                Console.Error.WriteLine($"导入失败:{ex.Message}");
                TryDelete(outPath);
                return 1;
            }
            finally
            {
                ses?.Dispose();
            }

            // 用同口令重开一遍:既是字节兼容自检,也顺便输出汇总结论
            try
            {
                using var verify = LedgerStore.Open(outPath, password);
                Log.Info($"重开验证:通过  {outPath}");
                Console.WriteLine("重开验证:通过(口令正确、可读)。");
                Console.WriteLine(VerifySummary(verify));
            }
            catch (LedgerPasswordException)
            {
                Log.Error("重开验证失败:口令不符(文件可能已损坏)。");
                Console.Error.WriteLine("重开验证失败:口令不符(文件可能已损坏)。");
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "sealer 顶层失败");
            Console.Error.WriteLine($"失败:{ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            "账本密封器:读数据规范 JSON,用主程序同款 SQLCipher 建 .lbook\n\n" +
            "用法:\n" +
            "  ZhangDan.Sealer import <spec.json> --out <账本.lbook> [--name 账本名] [--password 口令]\n\n" +
            "口令优先级:--password > 环境变量 ZD_BOOK_PASSWORD > 交互输入\n" +
            "建库后自动用同口令重开一次验证。数据规范见 tools/zd-import/SPEC.md。");
    }

    private static string RootStr(JsonElement root, string key, string? def)
        => root.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString() ?? def ?? ""
            : def ?? "";

    private static string VerifySummary(LedgerSession s)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
SELECT
 (SELECT COUNT(*) FROM accounts),
 (SELECT COUNT(*) FROM categories),
 (SELECT COUNT(*) FROM periods),
 (SELECT COUNT(*) FROM fund_pools),
 (SELECT COUNT(*) FROM transactions),
 (SELECT COALESCE(SUM(CASE WHEN direction = 'in' AND status <> 'cancelled' THEN amount_cents ELSE 0 END), 0)
       - COALESCE(SUM(CASE WHEN direction = 'out' AND status <> 'cancelled' THEN amount_cents ELSE 0 END), 0)
  FROM transactions),
 (SELECT COUNT(*) FROM transactions WHERE direction = 'transfer' AND status <> 'cancelled');";
        using var r = cmd.ExecuteReader();
        r.Read();
        var acct = r.GetInt64(0);
        var cat = r.GetInt64(1);
        var period = r.GetInt64(2);
        var pool = r.GetInt64(3);
        var txn = r.GetInt64(4);
        var net = r.GetInt64(5);
        var tr = r.GetInt64(6);
        return $"  账户 {acct} · 分类 {cat} · 周期 {period} · 资金池 {pool}\n" +
               $"  流水 {txn} 笔(其中转账 {tr})· 收支净额(不含转账) {Money.Yuan(net)}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 忽略:失败文件留给用户自查
        }
    }
}

/// <summary>把规范 JSON 依序写进已建库(账户→分类→周期→资金池→流水,按日期升序)。</summary>
internal static class Importer
{
    private static readonly Dictionary<string, long> AcctId = new();
    private static readonly Dictionary<string, long> IncomeCatId = new();
    private static readonly Dictionary<string, long> ExpenseCatId = new();
    private static readonly Dictionary<string, long> PeriodId = new();

    public static string Build(LedgerSession s, JsonElement root)
    {
        AcctId.Clear();
        IncomeCatId.Clear();
        ExpenseCatId.Clear();
        PeriodId.Clear();
        var lines = new List<string>();

        lines.Add(InsertAccounts(s, root));
        lines.Add(InsertCategories(s, root));
        lines.Add(InsertPeriods(s, root));
        lines.Add(InsertPools(s, root));
        lines.Add(InsertTransactions(s, root));
        return string.Join("\n", lines);
    }

    private static string InsertAccounts(LedgerSession s, JsonElement root)
    {
        var list = Arr(root, "accounts");
        var kinds = AccountKinds.Asset.Concat(AccountKinds.Liability);
        for (int i = 0; i < list.Length; i++)
        {
            var o = list[i];
            var name = ReqStr(o, "name", i, "accounts");
            if (AcctId.ContainsKey(name))
                throw Err(i, "accounts", $"账户名重复:{name}");
            var type = Str(o, "type", "wallet");
            if (!kinds.Contains(type))
                throw Err(i, "accounts", $"账户 type 非法:{type}(合法:{string.Join('/', kinds)})");
            var platform = Str(o, "platform", "");
            var baseCents = ToCents(OptMoney(o, "balanceBase"), 0);
            var id = Accounts.Insert(s, name, type, platform, baseCents);
            if (TryStr(o, "balanceDate", out var bd) && bd is { Length: > 0 })
            {
                CheckDate(bd, i, "accounts.balanceDate");
                Exec(s, "UPDATE accounts SET balance_date = $v WHERE id = $id", bd, id);
            }
            if (o.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False)
                Accounts.Disable(s, id);
            AcctId[name] = id;
        }
        return $"账户 {list.Length} 个";
    }

    private static string InsertCategories(LedgerSession s, JsonElement root)
    {
        var list = Arr(root, "categories");
        for (int i = 0; i < list.Length; i++)
        {
            var o = list[i];
            var name = ReqStr(o, "name", i, "categories");
            var kind = ReqStr(o, "kind", i, "categories");
            if (kind is not ("expense" or "income"))
                throw Err(i, "categories", $"kind 非法:{kind}(expense/income)");
            var income = kind == "income";
            var color = OptStr(o, "color");
            var keyword = OptStr(o, "keyword");
            var id = Categories.Insert(s, name, income, color, keyword);
            var map = income ? IncomeCatId : ExpenseCatId;
            if (map.ContainsKey(name))
                throw Err(i, "categories", $"同 kind 分类名重复:{name}");
            map[name] = id;
        }
        return $"分类 {list.Length} 个";
    }

    private static string InsertPeriods(LedgerSession s, JsonElement root)
    {
        var list = Arr(root, "periods");
        for (int i = 0; i < list.Length; i++)
        {
            var o = list[i];
            var name = ReqStr(o, "name", i, "periods");
            if (PeriodId.ContainsKey(name))
                throw Err(i, "periods", $"周期名重复:{name}");
            var start = ReqStr(o, "startDate", i, "periods");
            CheckDate(start, i, "periods.startDate");
            string? end = null;
            if (TryStr(o, "endDate", out var e) && e is { Length: > 0 })
            {
                CheckDate(e, i, "periods.endDate");
                end = e;
            }
            var id = Periods.Insert(s, name, start, end);   // 重叠即抛(含定位补充)
            PeriodId[name] = id;
        }
        return $"周期 {list.Length} 个";
    }

    private static string InsertPools(LedgerSession s, JsonElement root)
    {
        var list = Arr(root, "fundPools");
        for (int i = 0; i < list.Length; i++)
        {
            var o = list[i];
            var pname = ReqStr(o, "period", i, "fundPools");
            if (!PeriodId.TryGetValue(pname, out var pid))
                throw Err(i, "fundPools", $"周期「{pname}」未定义");
            var acct = ReqStr(o, "account", i, "fundPools");
            if (!AcctId.TryGetValue(acct, out var aid))
                throw Err(i, "fundPools", $"账户「{acct}」未定义");
            var poolName = Str(o, "name", "生活费");
            var budget = ToCents(OptMoney(o, "budget"), 0);
            var reserve = ToCents(OptMoney(o, "reserve"), 0);
            Pools.Save(s, pid, poolName, aid, budget, reserve);
        }
        return $"资金池 {list.Length} 个";
    }

    private static string InsertTransactions(LedgerSession s, JsonElement root)
    {
        var list = Arr(root, "transactions");
        var sorted = new List<(int Idx, JsonElement El, string Date)>();
        for (int i = 0; i < list.Length; i++)
        {
            var d = ReqStr(list[i], "date", i, "transactions");
            CheckDate(d, i, "transactions.date");
            sorted.Add((i, list[i], d));
        }
        sorted.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));

        long inSum = 0, outSum = 0, transfers = 0;
        foreach (var (idx, o, date) in sorted)
        {
            var dir = ReqStr(o, "direction", idx, "transactions");
            var name = ReqStr(o, "name", idx, "transactions");
            var account = ReqStr(o, "account", idx, "transactions");
            if (!AcctId.TryGetValue(account, out var acct))
                throw Err(idx, "transactions", $"账户「{account}」未定义");

            var note = Str(o, "note", "");
            var channel = Str(o, "channel", "");
            var inPool = Bool(o, "inPool", dir == "transfer" ? false : true);
            long id;

            switch (dir)
            {
                case "in":
                case "out":
                {
                    var amount = ReqMoney(o, "amount", idx, "transactions");
                    if (amount <= 0)
                        throw Err(idx, "transactions", $"金额需 > 0,当前 {amount:0.##} 元");
                    var cat = FindCategory(o, idx, dir == "in");
                    id = Transactions.Add(s, new TxnDraft
                    {
                        Date = date, Direction = dir, AccountId = acct, CategoryId = cat,
                        AmountCents = Money.ToCents(amount), Name = name,
                        Note = note, Channel = channel, InPool = inPool
                    });
                    if (dir == "in") inSum += Money.ToCents(amount);
                    else outSum += Money.ToCents(amount);
                    break;
                }
                case "transfer":
                {
                    var toAcctName = ReqStr(o, "toAccount", idx, "transactions");
                    if (!AcctId.TryGetValue(toAcctName, out var toAcct))
                        throw Err(idx, "transactions", $"转入账户「{toAcctName}」未定义");
                    var principal = ReqMoney(o, "amount", idx, "transactions");
                    if (principal <= 0)
                        throw Err(idx, "transactions", $"转账本金需 > 0");
                    var delta = ToCents(OptMoney(o, "delta"), 0);
                    var kind = Str(o, "transferKind", "互转");
                    id = Transactions.Transfer(s, new TransferDraft
                    {
                        Date = date, FromAccountId = acct, ToAccountId = toAcct,
                        PrincipalCents = Money.ToCents(principal), DeltaCents = delta,
                        Kind = kind, Note = note, InPool = inPool
                    });
                    transfers++;
                    break;
                }
                default:
                    throw Err(idx, "transactions", $"direction 非法:{dir}(in/out/transfer)");
            }

            // 可选落库字段:时间(影响列表「时间」列)、来源、状态(作废/退款)、平台单号
            var status = Str(o, "status", "normal");
            if (status is not ("normal" or "refunded" or "cancelled"))
                throw Err(idx, "transactions", $"status 非法:{status}");
            var time = Str(o, "time", "12:00");
            if (time.Length != 5 || time[2] != ':')
                throw Err(idx, "transactions", $"time 需 HH:mm 形如 08:30,当前:{time}");
            var source = Str(o, "source", "manual");
            if (source is not ("manual" or "wx" or "zfb" or "legacy"))
                throw Err(idx, "transactions", $"source 非法:{source}(manual/wx/zfb/legacy)");
            var refId = OptStr(o, "refId");
            ApplyMeta(s, id, date, time, status, source, refId);
        }
        return $"流水 {list.Length} 笔(收支 {inSum / 100m:0.00}/{outSum / 100m:0.00} 元,转账 {transfers})";
    }

    /// <summary>按方向找分类(名称引用;同名跨 kind 按方向取),未命中报错。</summary>
    private static long? FindCategory(JsonElement o, int idx, bool income)
    {
        if (!TryStr(o, "category", out var cat) || cat is not { Length: > 0 })
            return null;
        var map = income ? IncomeCatId : ExpenseCatId;
        if (map.TryGetValue(cat, out var id))
            return id;
        var opposite = income ? ExpenseCatId : IncomeCatId;
        throw Err(idx, "transactions", $"分类「{cat}」未定义或方向不匹配(收入用 income 分类/支出用 expense 分类)");
    }

    private static void ApplyMeta(LedgerSession s, long id, string date, string time,
        string status, string source, string? refId)
    {
        var set = new List<string> { "created_at = $ct" };
        if (status != "normal")
            set.Add("status = $status");
        if (source != "manual")
            set.Add("source = $source");
        if (refId is { Length: > 0 })
            set.Add("ref_id = $refId");
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = $"UPDATE transactions SET {string.Join(", ", set)} WHERE id = $id;";
        cmd.Parameters.AddWithValue("$ct", $"{date} {time}:00");
        cmd.Parameters.AddWithValue("$id", id);
        if (status != "normal") cmd.Parameters.AddWithValue("$status", status);
        if (source != "manual") cmd.Parameters.AddWithValue("$source", source);
        if (refId is { Length: > 0 }) cmd.Parameters.AddWithValue("$refId", refId);
        cmd.ExecuteNonQuery();
    }

    private static void Exec(LedgerSession s, string sql, string val, long id)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$v", val);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ---------- JsonElement helpers ----------

    private static JsonElement[] Arr(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return Array.Empty<JsonElement>();
        if (v.ValueKind != JsonValueKind.Array)
            throw Err(-1, key, "应为数组");
        var buf = new List<JsonElement>();
        foreach (var e in v.EnumerateArray())
            buf.Add(e);
        return buf.ToArray();
    }

    private static string ReqStr(JsonElement o, string key, int idx, string where)
    {
        if (o.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null)
        {
            var t = v.GetString()?.Trim();
            if (!string.IsNullOrEmpty(t))
                return t;
        }
        throw Err(idx, where, $"缺少字段「{key}」");
    }

    private static string Str(JsonElement o, string key, string def)
        => o.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null && v.GetString() is { } s
            ? s
            : def;

    private static bool TryStr(JsonElement o, string key, out string? val)
    {
        if (o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        {
            val = v.GetString();
            return true;
        }
        val = null;
        return false;
    }

    private static string? OptStr(JsonElement o, string key)
        => TryStr(o, key, out var v) && v is { Length: > 0 } ? v : null;

    private static decimal ReqMoney(JsonElement o, string key, int idx, string where)
    {
        if (o.TryGetProperty(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number)
                return v.GetDecimal();
            if (v.ValueKind == JsonValueKind.String
                && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                return d;
        }
        throw Err(idx, where, $"金额字段「{key}」需为数字(元)");
    }

    private static decimal? OptMoney(JsonElement o, string key)
    {
        if (!o.TryGetProperty(key, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number)
            return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    private static long ToCents(decimal? yuan, long def)
        => yuan is null ? def : Money.ToCents(yuan.Value);

    private static bool Bool(JsonElement o, string key, bool def)
        => o.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : def;

    private static void CheckDate(string iso, int idx, string where)
    {
        if (!DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw Err(idx, where, $"日期需 yyyy-MM-dd,当前:{iso}");
    }

    private static Exception Err(int idx, string where, string msg)
        => new InvalidOperationException(idx < 0
            ? $"{where}:{msg}"
            : $"{where}[{idx + 1}]:{msg}");
}
