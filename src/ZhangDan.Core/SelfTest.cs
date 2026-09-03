using System;
using System.Collections.Generic;
using System.IO;

namespace ZhangDan;

/// <summary>
/// 数据自检:临时库上跑完整链路——建库加密 → 记账 → 周期自动归属 →
/// 作废撤出 → 错口令拦截 → 重开读回(含封存/净资产/分类/CSV 等端到端断言)。
/// 与 UI 无关:返回「是否通过 + 步骤明细 + 失败原因」,由调用方决定如何展示。临时数据用完即删。
/// </summary>
internal static class SelfTest
{
    private const string TestPassword = "selftest-123";

    public static (bool Ok, IReadOnlyList<string> Steps, string? Error) Run()
    {
        var dir = Path.Combine(AppPaths.AppDataDir, "自检");
        var path = Path.Combine(dir, $"自检-{Guid.NewGuid():N}.lbook");
        Directory.CreateDirectory(dir);

        var steps = new List<string>();
        try
        {
            string token;
            using (var session = LedgerStore.Create(path, "自检账本", TestPassword))
            {
                token = Guid.NewGuid().ToString("N");
                session.SetMeta("selftest.token", token);
                var metaName = session.GetMeta("ledger.name");
                if (metaName != "自检账本")
                    throw new Exception("账本名写入后读回不符。");
                steps.Add("建库 + 写账本名/标记");

                SeedCanonicalCategories(session);   // 新库分类默认空;自检需标准分类支撑断言
                DataFlow(session, steps);
            }

            // 用错误口令打开:应当被拒绝
            bool wrongBlocked;
            try
            {
                using (LedgerStore.Open(path, "wrong-password"))
                    wrongBlocked = false; // 不应走到这:正确实现应抛 LedgerPasswordException
            }
            catch (LedgerPasswordException)
            {
                wrongBlocked = true;
            }
            steps.Add($"错口令被拦截:{(wrongBlocked ? "通过" : "失败(未拦截)!")}");

            // 正确口令重开:读回标记
            string? readBack;
            using (var session = LedgerStore.Open(path, TestPassword))
                readBack = session.GetMeta("selftest.token");
            if (readBack != token)
                throw new Exception("重开读回的标记与写入不一致。");
            steps.Add("正确口令重开读回");

            LogSteps(steps);

            return (true, steps, null);
        }
        catch (Exception ex)
        {
            steps.Add($"失败:{ex.Message}");
            return (false, steps, ex.ToString());
        }
        finally
        {
            TryDelete(path);
            try { Directory.Delete(dir, recursive: true); } catch { /* 忽略 */ }
        }
    }

    /// <summary>为自检临时库种一套标准分类(旧设计里是建库预设;新库改为空,仅自检内部自建)。</summary>
    private static void SeedCanonicalCategories(LedgerSession s)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT OR IGNORE INTO categories (id, parent_id, name, color, sort_order, kind) VALUES
  (1,  NULL, '餐饮',   '#F06292', 0, 'expense'),
  (2,  NULL, '交通',   '#42A5F5', 1, 'expense'),
  (3,  NULL, '购物',   '#FFA726', 2, 'expense'),
  (4,  NULL, '教育',   '#8E24AA', 3, 'expense'),
  (5,  NULL, '娱乐',   '#29B6F6', 4, 'expense'),
  (6,  NULL, '医疗',   '#66BB6A', 5, 'expense'),
  (7,  NULL, '居住',   '#5C6BC0', 6, 'expense'),
  (8,  NULL, '其他',   '#9E9E9E', 7, 'expense'),
  (9,  8,    '差额调整','#B0BEC5', 0, 'expense'),
  (10, NULL, '生活费',  NULL, 0, 'income'),
  (11, NULL, '家人转账', NULL, 1, 'income'),
  (12, NULL, '红包',    NULL, 2, 'income'),
  (13, NULL, '理财收益', NULL, 3, 'income'),
  (14, NULL, '其他',    NULL, 4, 'income');";
        cmd.ExecuteNonQuery();
    }

    /// <summary>日志断言:临时目录开文件,验级别标记/异常文本/上下文落盘与级别过滤,随后还原配置。</summary>
    private static void LogSteps(List<string> steps)
    {
        var dir = Path.Combine(AppPaths.AppDataDir, "自检");
        string TodayLog() => Path.Combine(dir, "app-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        var prev = Log.Config;
        try
        {
            Log.Configure(LogLevel.Debug, dir, console: false);
            Log.Debug("自检 debug 行");
            Log.Info("自检 info 行");
            Log.Warn("自检 warn 行");
            Log.Error(new InvalidOperationException("自检异常"), "自检 ctx");

            var txt = File.ReadAllText(TodayLog());
            if (!txt.Contains("[INF]") || !txt.Contains("[WRN]") || !txt.Contains("[ERR]")
                || !txt.Contains("自检异常") || !txt.Contains("自检 ctx"))
                throw new Exception("日志行缺失(级别标记/异常文本/上下文未落盘)。");

            Log.Configure(LogLevel.Error, dir, console: false);   // 切到 Error 级
            Log.Info("不应落盘 info");
            if (File.ReadAllText(TodayLog()).Contains("不应落盘 info"))
                throw new Exception("级别过滤失效:Error 级别下 Info 仍被写入。");

            Log.Clear();
            if (File.Exists(TodayLog()))
                throw new Exception("日志清空失败:Clear 后当天文件仍存在。");
            Log.Error("清空后重建探测");
            if (!File.Exists(TodayLog()))
                throw new Exception("日志清空后重建失败:再写未自动创建当天文件。");

            steps.Add("日志:级别落盘/异常上下文/过滤/清空重建正确");
        }
        finally
        {
            Log.Configure(prev.Level, prev.Dir, prev.Console); // 先关 writer,免得外层删自检目录撞上打开句柄
        }
    }

    /// <summary>流水/周期/作废 数据流断言;任何不符即抛错。</summary>
    private static void DataFlow(LedgerSession s, List<string> steps)
    {
        var accountA = Accounts.Insert(s, "微信零钱", "wallet", "微信", 10_000_000);
        var accountB = Accounts.Insert(s, "银行卡", "bank", "银行", 10_000_000);

        var today = DateTime.Today;
        var date = today.ToString("yyyy-MM-dd");

        // ① 未建周期前记账 → 保持未归属
        var early = today.AddDays(-4).ToString("yyyy-MM-dd");
        var id0 = Transactions.Add(s, new TxnDraft
        {
            Date = early,
            Direction = "out",
            AccountId = accountA,
            CategoryId = 1,          // 餐饮
            AmountCents = 3000,
            Name = "预记",
            Note = "",
            Channel = "",
            InPool = true
        });
        if (GetPeriodId(s, id0) is not null)
            throw new Exception("未建周期时记账应保持未归属。");
        steps.Add("无周期先记账 → 未归属");

        // ② 补建覆盖期(含早前流水)的周期 → 回填期内未归属流水
        var periodStart = today.AddDays(-5).ToString("yyyy-MM-dd");
        var periodEnd = today.AddDays(30).ToString("yyyy-MM-dd");
        var periodId = Periods.Insert(s, "生活费", periodStart, periodEnd);
        if (GetPeriodId(s, id0) != periodId)
            throw new Exception("补建周期后未回填期内流水。");
        steps.Add("补建周期 → 回填期内未归属流水");

        var id = Transactions.Add(s, new TxnDraft
        {
            Date = date,
            Direction = "out",
            AccountId = accountA,
            CategoryId = 1,          // 餐饮
            AmountCents = 12000,
            Name = "早餐",
            Note = "",
            Channel = "实体",
            InPool = true
        });

        if (GetPeriodId(s, id) != periodId)
            throw new Exception("周期内的记账未自动归属到该周期。");
        steps.Add("记账 → 自动归属进行中周期");

        var rows = Transactions.ListByDate(s, date);
        if (rows.Count != 1 || rows[0].Id != id)
            throw new Exception("当日流水列表不符。");
        var (outCents, _) = Transactions.DayTotals(s, date);
        if (outCents != 12000)
            throw new Exception("当日支出合计不符。");
        steps.Add("当日流水 + 合计正确");

        // 周期外日期(40 天后)不应归属任何进行中周期
        var outside = today.AddDays(40).ToString("yyyy-MM-dd");
        var id2 = Transactions.Add(s, new TxnDraft
        {
            Date = outside,
            Direction = "in",
            AccountId = accountB,
            CategoryId = 10,         // 生活费(收入)
            AmountCents = 5000,
            Name = "跨期",
            Note = "",
            Channel = "",
            InPool = false
        });
        if (GetPeriodId(s, id2) is not null)
            throw new Exception("周期外流水被错误归属。");
        steps.Add("周期外流水保持未归属");

        // 转账:本金 500 转出,实收 500.5(理财结算 Δ+0.5)——
        // 转入 B,kind/本金/Δ 应如写存,且不计入收支合计
        var tx = Transactions.Transfer(s, new TransferDraft
        {
            Date = date,
            FromAccountId = accountB,
            ToAccountId = accountA,
            PrincipalCents = 50000,
            DeltaCents = 50,
            Kind = "理财结算",
            Note = "",
            InPool = false
        });
        var shown = Transactions.ListByDate(s, date);
        var txRow = shown.FirstOrDefault(x => x.Direction == "transfer");
        if (txRow is null || txRow.Id != tx || txRow.AccountTo == ""
            || txRow.DeltaCents != 50 || txRow.Kind != "理财结算")
            throw new Exception("转账写入/展示字段不符。");
        var (outAll, inAll) = Transactions.DayTotals(s, date);
        // 当天除转账外还有一笔 ¥120 早餐支出(尚未作废):转账不应并入收支合计,
        // 故合计应仍为 (12000, 0) —— 与转账前一致,而非凭空变 0。
        if (outAll != 12000 || inAll != 0)
            throw new Exception($"转账后当日收支合计异常(期望 12000/0 且转账不计入,实际 {outAll}/{inAll})。");
        steps.Add("转账:本金/Δ/类别存库,收支合计不受影响");

        // 作废:支出一笔撤出列表与合计(转账仍在,但不影响收支合计)
        Transactions.Cancel(s, id);
        if (Transactions.ListByDate(s, date).FirstOrDefault(x => x.Id == id) is not null)
            throw new Exception("作废后仍出现在流水列表。");
        var (outAfter, _) = Transactions.DayTotals(s, date);
        if (outAfter != 0)
            throw new Exception("作废后合计未撤出。");
        steps.Add("作废一笔 → 撤出列表与合计");

        // 就地编辑:改早前那笔(仍正常)的金额/名称,合计与读回应更新
        var e0 = Transactions.GetEditable(s, id0)
            ?? throw new Exception("读不到待编辑流水。");
        Transactions.Update(s, e0 with { AmountCents = 5500, Name = "预记改" });
        var e1 = Transactions.GetEditable(s, id0);
        if (e1 is null || e1.AmountCents != 5500 || e1.Name != "预记改")
            throw new Exception("就地编辑未生效。");
        var (outEarly, _) = Transactions.DayTotals(s, early);
        if (outEarly != 5500)
            throw new Exception("编辑后合计未更新。");
        steps.Add("就地编辑 → 金额/名称更新,合计刷新");

        // 范围合计:期内 = 已编辑的早前支出 5500(当日那笔已作废、转账不计入);期外收入不混入
        var (outRange, inRange) = Transactions.RangeTotals(s, periodStart, periodEnd);
        if (outRange != 5500 || inRange != 0)
            throw new Exception("范围合计错误。");
        steps.Add("范围合计 → 作废/转账/期外均正确处理");

        // 月历逐日合计(DayTotalsMap):隔离的一个未来月——同日 收支各一 + 转账一笔 + 次日支出后作废
        var fmo = DateTime.Today.AddMonths(6);
        var f1 = new DateTime(fmo.Year, fmo.Month, 1);
        var mStart = f1.ToString("yyyy-MM-dd");
        var mEnd = f1.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var md1 = f1.ToString("yyyy-MM-dd");
        var md2 = f1.AddDays(1).ToString("yyyy-MM-dd");
        Transactions.Add(s, new TxnDraft
        {
            Date = md1, Direction = "out", AccountId = accountB,
            CategoryId = 1, AmountCents = 2500, Name = "月历支出", Note = "", Channel = "", InPool = false
        });
        Transactions.Add(s, new TxnDraft
        {
            Date = md1, Direction = "in", AccountId = accountB,
            CategoryId = 10, AmountCents = 1500, Name = "月历收入", Note = "", Channel = "", InPool = false
        });
        Transactions.Transfer(s, new TransferDraft
        {
            Date = md1, FromAccountId = accountB, ToAccountId = accountA,
            PrincipalCents = 120000, DeltaCents = 0, Kind = "互转", Note = "", InPool = false
        });
        var toCancel = Transactions.Add(s, new TxnDraft
        {
            Date = md2, Direction = "out", AccountId = accountB,
            CategoryId = 1, AmountCents = 4000, Name = "月历待作废", Note = "", Channel = "", InPool = false
        });
        Transactions.Cancel(s, toCancel);
        var mmap = Transactions.DayTotalsMap(s, mStart, mEnd);
        if (!mmap.TryGetValue(md1, out var mv1) || mv1.OutCents != 2500 || mv1.InCents != 1500)
            throw new Exception("月历逐日合计不符(转账不应计入同日收支)。");
        if (mmap.ContainsKey(md2))
            throw new Exception("月历逐日合计:作废日不应出现。");
        steps.Add("月历逐日合计 → 同日收支 + 转账不计入 + 作废日缺席");

        // 就地编辑转账:改转出账户/本金与 Δ
        var txEdit = Transactions.GetTransfer(s, tx)
            ?? throw new Exception("读不到待编辑转账。");
        Transactions.UpdateTransfer(s, txEdit with
        {
            FromAccountId = accountA,
            ToAccountId = accountB,
            PrincipalCents = 80000,
            DeltaCents = -100,       // 改为手续费(提现 Δ-1.00)
            Kind = "提现"
        });
        var tx2 = Transactions.GetTransfer(s, tx);
        if (tx2 is null || tx2.FromAccountId != accountA || tx2.ToAccountId != accountB
            || tx2.PrincipalCents != 80000 || tx2.DeltaCents != -100 || tx2.Kind != "提现")
            throw new Exception("转账就地编辑未生效。");
        steps.Add("就地编辑转账 → 账户/本金/Δ/类别更新");

        // 账户停用后应移出下拉(启用列表);重新启用应恢复
        Accounts.Disable(s, accountB);
        if (Accounts.ListEnabled(s).Any(a => a.Id == accountB))
            throw new Exception("停用账户仍出现在启用列表。");
        Accounts.Enable(s, accountB);
        if (!Accounts.ListEnabled(s).Any(a => a.Id == accountB))
            throw new Exception("重新启用后账户未恢复。");
        steps.Add("账户停用/启用 → 下拉可见性正确");

        // 资金池(单池):已花 = 池账户本期 in_pool 直支出 + 勾入池转出;收入/不入池/他账户不计
        PoolFlow(s, accountA, accountB, periodId, date, steps);

        // 校准余额:账面 = 基准 + 基准日后净变动;三种处理 + 审计历史
        CalibrationFlow(s, accountA, date, steps);

        // 周期生命周期:封存 → 只读(增/改/作废拦截)→ 解除恢复;到期推荐;新周期不改挂封存期游离账
        PeriodLifecycleFlow(s, steps);

        // 账户派生视图:账面=基准+净变动;收支转构成拆分;净资产合计(停用不计)
        AccountViewFlow(s, steps);

        // 分类管理:收支隔离 / 改名改色关键词 / 排序上移 / 合并改挂 / 删除先清交易
        CategoryFlow(s, steps);

        // CSV 导出:转义正确 + 全量行数与库内流水一致
        CsvFlow(s, steps);
    }

    /// <summary>校准余额断言:账面派生 / 记调整流水 / 仅改基准 / 补记明细(仅审计) / 审计历史。</summary>
    private static void CalibrationFlow(LedgerSession s, long sourceAccount, string date, List<string> steps)
    {
        var card = Accounts.Insert(s, "校准卡", "bank", "银行", 200000);
        if (AccountCalibration.BookCents(s, card) != 200000)
            throw new Exception("建户初始余额的账面不符。");

        Transactions.Add(s, new TxnDraft
        {
            Date = date, Direction = "in", AccountId = card,
            CategoryId = 10, AmountCents = 5000, Name = "利息", Note = "", Channel = "", InPool = false
        });
        Transactions.Transfer(s, new TransferDraft
        {
            Date = date, FromAccountId = sourceAccount, ToAccountId = card,
            PrincipalCents = 30000, DeltaCents = 0, Kind = "互转", Note = "", InPool = false
        });
        Transactions.Add(s, new TxnDraft
        {
            Date = date, Direction = "out", AccountId = card,
            CategoryId = 8, AmountCents = 2000, Name = "杂费", Note = "", Channel = "", InPool = false
        });
        if (AccountCalibration.BookCents(s, card) != 233000)
            throw new Exception("账面派生错误(基准+收支+转入)。");
        steps.Add("校准:账面 = 基准 + 基准日后收支转");

        // 记调整流水(实际 228000 < 账面 → 支出 差额调整 5000,不入池)
        var diff = AccountCalibration.Apply(s, card, 228000, CalibMethod.Adjustment, "对账");
        if (diff != -5000 || AccountCalibration.BookCents(s, card) != 228000)
            throw new Exception("记调整流水后账面未对齐实际。");
        bool adjFound;
        using (var cmd = s.Connection.CreateCommand())
        {
            cmd.CommandText = @"
SELECT COUNT(*) FROM transactions
WHERE account_id = $a AND name = '差额调整' AND direction = 'out'
  AND amount_cents = 5000 AND in_pool = 0 AND status = 'normal';";
            cmd.Parameters.AddWithValue("$a", card);
            adjFound = Convert.ToInt64(cmd.ExecuteScalar()) == 1;
        }
        if (!adjFound)
            throw new Exception("调整流水(差额调整,不入池)未正确落库。");
        steps.Add("校准:记调整流水 → 差额分类不入池,账面对齐");

        var hist = AccountCalibration.History(s, card);
        if (hist.Count != 1 || hist[0].BookCents != 233000 || hist[0].ActualCents != 228000
            || hist[0].DiffCents != -5000 || hist[0].Method != CalibMethod.Adjustment)
            throw new Exception("校准审计历史不符。");
        steps.Add("校准:审计历史记录(时间/账面/实际/差额/方式)");

        // 仅更新基准:账面 50000 → 实际 47000,不动流水
        var baseCard = Accounts.Insert(s, "基准卡", "cash", "现金", 50000);
        AccountCalibration.Apply(s, baseCard, 47000, CalibMethod.BaseOnly, "改基准");
        if (AccountCalibration.BookCents(s, baseCard) != 47000)
            throw new Exception("仅更新基准后账面未对齐。");
        long baseCardTxns;
        using (var cmd = s.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM transactions WHERE account_id = $a;";
            cmd.Parameters.AddWithValue("$a", baseCard);
            baseCardTxns = Convert.ToInt64(cmd.ExecuteScalar());
        }
        if (baseCardTxns != 0)
            throw new Exception("仅更新基准不应产生流水。");
        var bh = AccountCalibration.History(s, baseCard);
        if (bh.Count != 1 || bh[0].Method != CalibMethod.BaseOnly || bh[0].DiffCents != -3000)
            throw new Exception("仅更新基准的审计记录不符。");
        steps.Add("校准:仅更新基准(不动流水) + 审计");

        // 补记真实明细:不自动改账,仅留审计(账面已一致差额 0)
        var diff0 = AccountCalibration.Apply(s, baseCard, 47000, CalibMethod.RealDetails, "补记后归零");
        if (diff0 != 0 || AccountCalibration.History(s, baseCard).Count != 2
            || AccountCalibration.BookCents(s, baseCard) != 47000)
            throw new Exception("补记真实明细处理不符。");
        steps.Add("校准:补记真实明细(差额 0 仅审计)");
    }

    /// <summary>资金池单池派生断言(追加在其余断言之后,状态已知:accountA 池上有 预记 5500 支出 in_pool)。</summary>
    private static void PoolFlow(LedgerSession s, long accountA, long accountB, long periodId,
        string date, List<string> steps)
    {
        Pools.Save(s, periodId, "生活费池", accountA, 100000, 20000);
        var p = Pools.Get(s, periodId)
            ?? throw new Exception("池保存后读不回。");
        if (p.BudgetCents != 100000 || p.ReserveCents != 20000 || p.AccountId != accountA)
            throw new Exception("池字段存错。");

        var st = Pools.State(s, p);
        if (st.SpentCents != 5500 || st.RemainingCents != 94500 || st.DisposableCents != 74500)
            throw new Exception("池已花派生错误(应只含早前那笔正常 in_pool 支出)。");
        steps.Add("资金池:池保存 + 已花/剩余/可支配派生正确");

        // 记一笔 in_pool 支出(池账户)→ 已花增加
        Transactions.Add(s, new TxnDraft
        {
            Date = date, Direction = "out", AccountId = accountA,
            CategoryId = 1, AmountCents = 7000, Name = "池测", Note = "", Channel = "", InPool = true
        });
        if (Pools.State(s, Pools.Get(s, periodId)!).SpentCents != 12500)
            throw new Exception("池账户 in_pool 支出未计入已花。");

        // 非池账户支出(即使 in_pool)、池账户不入池支出 → 均不计已花
        Transactions.Add(s, new TxnDraft
        {
            Date = date, Direction = "out", AccountId = accountB,
            CategoryId = 1, AmountCents = 1000, Name = "他账户", Note = "", Channel = "", InPool = true
        });
        Transactions.Add(s, new TxnDraft
        {
            Date = date, Direction = "out", AccountId = accountA,
            CategoryId = 8, AmountCents = 3000, Name = "不入池", Note = "", Channel = "", InPool = false
        });
        if (Pools.State(s, Pools.Get(s, periodId)!).SpentCents != 12500)
            throw new Exception("他账户/不入池支出被错误计入已花。");
        steps.Add("资金池:他账户与不入池支出不计已花");

        // 勾「计入池」的转出(池账户→他账户)→ 本金计入已花
        Transactions.Transfer(s, new TransferDraft
        {
            Date = date, FromAccountId = accountA, ToAccountId = accountB,
            PrincipalCents = 20000, DeltaCents = 0, Kind = "互转", Note = "", InPool = true
        });
        if (Pools.State(s, Pools.Get(s, periodId)!).SpentCents != 32500)
            throw new Exception("勾入池转出的本金未计入已花。");
        steps.Add("资金池:勾「计入池」转出本金计入已花");

        // 作废一笔池内支出 → 撤出已花(退款/作废恢复池)
        var extra = Transactions.ListByDate(s, date).First(x => x.Name == "池测");
        Transactions.Cancel(s, extra.Id);
        if (Pools.State(s, Pools.Get(s, periodId)!).SpentCents != 25500)
            throw new Exception("作废池内支出后已花未恢复。");
        steps.Add("资金池:作废撤出已花");

        // 再次保存(改预算)→ 仍单池、不新增行
        Pools.Save(s, periodId, "生活费池", accountA, 90000, 20000);
        long count;
        using (var cmd = s.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM fund_pools WHERE period_id = $pid;";
            cmd.Parameters.AddWithValue("$pid", periodId);
            count = Convert.ToInt64(cmd.ExecuteScalar());
        }
        var p2 = Pools.Get(s, periodId)!;
        if (count != 1 || p2.BudgetCents != 90000)
            throw new Exception("单池 upsert 失败(行数/预算不符)。");
        if (Pools.State(s, p2).RemainingCents != 64500)
            throw new Exception("改预算后剩余派生不符。");
        steps.Add("资金池:同周期二次保存仍是单池(upsert)");
    }

    /// <summary>
    /// 周期生命周期断言:封存 → 该期日期只读(增/改/作废全部拦截)→ 解除恢复可写;
    /// 读(流水/合计)不受封存影响;到期未封存周期能被「推荐新建」查到,封存后不再推荐;
    /// 新周期补归属不得改挂封存期内的游离(未归属)账。
    /// 场景日期选在主周期(+30 天)之外的未来窗口与远古窗口,避免扰动其余断言。
    /// </summary>
    private static void PeriodLifecycleFlow(LedgerSession s, List<string> steps)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var a = Accounts.Insert(s, "封测A", "bank", "银行", 1_000_000);
        var b = Accounts.Insert(s, "封测B", "wallet", "微信", 1_000_000);

        // ① 未来窗口建周期并记账 → 自动归属
        var d0 = DateTime.Today.AddDays(41).ToString("yyyy-MM-dd");
        var d1 = DateTime.Today.AddDays(43).ToString("yyyy-MM-dd");
        var d2 = DateTime.Today.AddDays(45).ToString("yyyy-MM-dd");
        var pId = Periods.Insert(s, "封测期", d0, d2);
        var outId = Transactions.Add(s, new TxnDraft
        {
            Date = d1, Direction = "out", AccountId = a,
            CategoryId = 8, AmountCents = 4000, Name = "封存前支出", Note = "", Channel = "", InPool = false
        });
        var trId = Transactions.Transfer(s, new TransferDraft
        {
            Date = d1, FromAccountId = a, ToAccountId = b,
            PrincipalCents = 2000, DeltaCents = 0, Kind = "互转", Note = "", InPool = false
        });
        if (Periods.Get(s, pId)!.Status != "active"
            || GetPeriodId(s, outId) != pId || GetPeriodId(s, trId) != pId)
            throw new Exception("封存前周期归属不符。");
        steps.Add("生命周期:未来窗口建期 → 期记账自动归属");

        // ② 封存 → 期内日期只读
        Periods.Seal(s, pId);
        if (Periods.Get(s, pId)!.Status != "sealed"
            || !Periods.HasSealedCovering(s, d1) || !Periods.HasSealedCovering(s, d0)
            || Periods.HasSealedCovering(s, today))
            throw new Exception("封存后状态/覆盖判定不符。");

        ExpectReadonly(() => Transactions.Add(s, new TxnDraft
        {
            Date = d1, Direction = "out", AccountId = a,
            CategoryId = 8, AmountCents = 100, Name = "应被拦", Note = "", Channel = "", InPool = false
        }));
        ExpectReadonly(() => Transactions.Transfer(s, new TransferDraft
        {
            Date = d1, FromAccountId = a, ToAccountId = b,
            PrincipalCents = 100, DeltaCents = 0, Kind = "互转", Note = "", InPool = false
        }));
        ExpectReadonly(() => Transactions.Cancel(s, outId));
        ExpectReadonly(() => Transactions.Update(s,
            (Transactions.GetEditable(s, outId) ?? throw new Exception("读不到封存前支出。"))
            with { AmountCents = 1 }));
        ExpectReadonly(() => Transactions.UpdateTransfer(s,
            (Transactions.GetTransfer(s, trId) ?? throw new Exception("读不到封存前转账。"))
            with { PrincipalCents = 1 }));
        if (Transactions.ListByDate(s, d1).Count != 2
            || Transactions.RangeTotals(s, d0, d2).OutCents != 4000)
            throw new Exception("封存后流水/合计读不出来(应照常可见、只是只读)。");
        steps.Add("生命周期:封存 → 期内只读(增/改/作废拦截),读照常");

        // ③ 解除封存 → 恢复可写
        Periods.Unseal(s, pId);
        if (Periods.Get(s, pId)!.Status != "active")
            throw new Exception("解除封存后状态未回 active。");
        var okId = Transactions.Add(s, new TxnDraft
        {
            Date = d1, Direction = "out", AccountId = a,
            CategoryId = 8, AmountCents = 500, Name = "解封后", Note = "", Channel = "", InPool = false
        });
        Transactions.Cancel(s, okId);   // 能写能作废 = 已恢复
        steps.Add("生命周期:解除封存 → 恢复可写(记/作废均可)");

        // ④ 周期不允许重叠(与封存期重叠也要拒);封存期游离账不因新建被改挂
        Periods.Seal(s, pId);   // 再封存,模拟已冻结窗口
        var legacy = InsertLegacyUnassigned(s, d1, a);
        bool overlapBlocked = false;
        try
        {
            Periods.Insert(s, "重叠新期", d0, d2);
        }
        catch (InvalidOperationException)
        {
            overlapBlocked = true;
        }
        if (!overlapBlocked || GetPeriodId(s, legacy) is not null)
            throw new Exception("重叠周期未被拦截 / 封存期游离账被改挂。");
        steps.Add("生命周期:重叠周期被拒,封存期游离账不改挂");

        // ⑤ 到期推荐:已到期未封存周期可被查出,封存后不再推荐
        var oldStart = DateTime.Today.AddDays(-40).ToString("yyyy-MM-dd");
        var oldEnd = DateTime.Today.AddDays(-36).ToString("yyyy-MM-dd");
        var oldId = Periods.Insert(s, "旧到期期", oldStart, oldEnd);
        var latest = Periods.GetLatestExpiredActive(s, today);
        if (latest is null || latest.Id != oldId)
            throw new Exception("到期未封存周期未被「推荐新建」查到。");
        Periods.Seal(s, oldId);
        if (Periods.GetLatestExpiredActive(s, today) is not null)
            throw new Exception("封存后仍被推荐新建(应已归档)。");
        steps.Add("生命周期:到期未封存 → 推荐新建;封存后不再推荐");
    }

    /// <summary>
    /// 账户派生视图断言:账面 = 基准 + 净变动(收支转拆分各自正确);
    /// 净资产合计只计启用账户(停用/启用前后总额随之增减)。
    /// </summary>
    private static void AccountViewFlow(LedgerSession s, List<string> steps)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var c1 = Accounts.Insert(s, "派生甲", "bank", "银行", 10000);
        var c2 = Accounts.Insert(s, "派生乙", "wallet", "微信", 0);

        Transactions.Add(s, new TxnDraft
        {
            Date = today, Direction = "in", AccountId = c1,
            CategoryId = 10, AmountCents = 5000, Name = "利息", Note = "", Channel = "", InPool = false
        });
        Transactions.Transfer(s, new TransferDraft
        {
            Date = today, FromAccountId = c1, ToAccountId = c2,
            PrincipalCents = 2000, DeltaCents = 100, Kind = "互转", Note = "", InPool = false
        });
        Transactions.Add(s, new TxnDraft
        {
            Date = today, Direction = "out", AccountId = c2,
            CategoryId = 8, AmountCents = 300, Name = "杂费", Note = "", Channel = "", InPool = false
        });

        var m1 = Accounts.MovementBetween(s, c1, today, today);
        if (m1.InCents != 5000 || m1.OutCents != 0
            || m1.TransferInCents != 0 || m1.TransferOutCents != 2000 || m1.NetCents != 3000)
            throw new Exception("甲账户收支转拆分不符(应 +5000 收入 −2000 转出 → 净 +3000)。");
        var m2 = Accounts.MovementBetween(s, c2, today, today);
        if (m2.InCents != 0 || m2.OutCents != 300
            || m2.TransferInCents != 2100 || m2.TransferOutCents != 0 || m2.NetCents != 1800)
            throw new Exception("乙账户收支转拆分不符(应 +2100 转入 −300 支出 → 净 +1800)。");
        if (AccountCalibration.BookCents(s, c1) != 13000
            || AccountCalibration.BookCents(s, c2) != 1800)
            throw new Exception("派生账面不符(基准+净变动)。");
        var (baseOf, baseDate) = Accounts.BaseOf(s, c1);
        if (baseOf != 10000 || baseDate is null)
            throw new Exception("基准余额/基准日读取不符。");
        steps.Add("账户派生:收支转拆分正确,账面=基准+净变动");

        // 净资产合计只计启用账户:新增(有余额)→ 计入;停用 → 退出合计;启用 → 重新计入
        var before = Accounts.NetAssets(s);
        var z = Accounts.Insert(s, "净资产测试", "cash", "现金", 12345);
        if (Accounts.NetAssets(s) != before + 12345)
            throw new Exception("新增启用账户未计入净资产合计。");
        Accounts.Disable(s, z);
        if (Accounts.NetAssets(s) != before)
            throw new Exception("停用账户仍计入净资产合计(应排除)。");
        Accounts.Enable(s, z);
        if (Accounts.NetAssets(s) != before + 12345)
            throw new Exception("重新启用后净资产未恢复计入。");
        steps.Add("净资产:启用计入、停用不计、再启用恢复");
    }

    /// <summary>
    /// 分类管理断言:收支隔离 / 改名改色关键词 / 引用计数 / 排序上移;
    /// 合并 → 流水改挂、原分类删除;删除须先清交易(未用可删、用中被拦、合并后删)。
    /// </summary>
    private static void CategoryFlow(LedgerSession s, List<string> steps)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var catAcct = Accounts.Insert(s, "分类账户", "wallet", "微信", 1_000_000);

        var x = Categories.Insert(s, "临时支出", income: false, "#F0F0F0", "盒饭 外卖");
        var y = Categories.Insert(s, "临时收入", income: true, null, null);
        var expense = Categories.ListManual(s, income: false);
        var incomeList = Categories.ListManual(s, income: true);
        if (expense.Any(c => c.Id == y) || !expense.Any(c => c.Id == x) || !incomeList.Any(c => c.Id == y))
            throw new Exception("新建分类收支隔离不符(收入类进收入列表,支出类进支出列表)。");

        Categories.Rename(s, x, "改名支出");
        Categories.SetColor(s, x, "#112233");
        Categories.SetKeyword(s, x, "外卖 盒饭");
        var xr = Categories.ListManual(s, false).First(c => c.Id == x);
        if (xr.Name != "改名支出" || xr.Color != "#112233")
            throw new Exception("分类改名/改色未生效。");
        if (Categories.UsedCount(s, x) != 0)
            throw new Exception("新分类引用计数应为 0。");

        var tx = Transactions.Add(s, new TxnDraft
        {
            Date = today, Direction = "out", AccountId = catAcct, CategoryId = x,
            AmountCents = 900, Name = "分类测", Note = "", Channel = "", InPool = false
        });
        if (Categories.UsedCount(s, x) != 1)
            throw new Exception("记一笔后分类引用计数应 +1。");

        // 上移一位:应排到「其他」(id 8)前面
        Categories.Move(s, x, up: true);
        var after = Categories.ListManual(s, false);
        int xi = IndexOfId(after, x);
        int oi = IndexOfId(after, 8);
        if (xi < 0 || xi > oi)
            throw new Exception("分类上移未改变叠放序(应到「其他」之前)。");
        steps.Add("分类:收支隔离/改名改色关键词/引用计数/排序上移");

        // 合并到「餐饮」(1):流水改挂、原分类删除
        Categories.Merge(s, x, 1);
        if (Categories.ListManual(s, false).Any(c => c.Id == x) || GetCategoryId(s, tx) != 1)
            throw new Exception("合并后原分类未删 / 流水未改挂。");
        steps.Add("分类:合并 → 流水改挂、原分类删除");

        // 未使用分类可删
        var z = Categories.Insert(s, "待删", income: false, "#010101", null);
        Categories.Delete(s, z);
        if (Categories.ListManual(s, false).Any(c => c.Id == z))
            throw new Exception("未使用分类删除未生效。");
        steps.Add("分类:未使用删除成功");

        // 使用中删除被拦;合并清空后再删可成
        var z2 = Categories.Insert(s, "用过再删", income: false, "#020202", null);
        Transactions.Add(s, new TxnDraft
        {
            Date = today, Direction = "out", AccountId = catAcct, CategoryId = z2,
            AmountCents = 500, Name = "待删测", Note = "", Channel = "", InPool = false
        });
        bool blocked = false;
        try
        {
            Categories.Delete(s, z2);
        }
        catch (InvalidOperationException)
        {
            blocked = true;
        }
        if (!blocked)
            throw new Exception("有流水的分类删除应被拦截。");
        Categories.Merge(s, z2, 1);
        Categories.Delete(s, z2);
        if (Categories.ListManual(s, false).Any(c => c.Id == z2))
            throw new Exception("清空流水后删除未生效。");
        steps.Add("分类:使用中删除被拦 → 合并清空后可删");
    }

    /// <summary>CSV 导出断言:含逗号/引号的字段正确加引号转义;转账/作废等中文标签输出;全量行数=库内流水数。</summary>
    private static void CsvFlow(LedgerSession s, List<string> steps)
    {
        var samples = new List<Transactions.TxnExportRow>
        {
            new(1, "2026-01-02", "08:30", "in", "利息,含\"引号\"", "生活费", "微信", "",
                12345, 0, "", "", "入账", true, "normal", "2026春·1月", "2026-01-02 08:30:00"),
            new(2, "2026-01-03", "09:00", "transfer", "互转", "", "银行卡", "零钱",
                10000, -50, "提现", "", "", false, "cancelled", "2026春·1月", "2026-01-03 09:00:00")
        };
        var csv = CsvExporter.Build(samples);
        if (!csv.StartsWith("ID,日期,时间,方向,名称,分类,账户,转入账户,金额(元),浮动(元),转账类别,渠道,备注,计入池,状态,周期,创建时间", StringComparison.Ordinal)
            || !csv.Contains("\"利息,含\"\"引号\"\"\"", StringComparison.Ordinal)
            || !csv.Contains("转账", StringComparison.Ordinal)
            || !csv.Contains("已作废", StringComparison.Ordinal)
            || !csv.Contains("-100.00", StringComparison.Ordinal)
            || !csv.Contains("-0.50", StringComparison.Ordinal))
            throw new Exception("CSV 转义/内容不符(含引号逗号字段、转账负号、作废标记)。");
        steps.Add("CSV:转义(引号/逗号)、金额符号、状态标签正确");

        // 全量行数应等于库内流水总数(含作废/退款)
        long dbCount;
        using (var cmd = s.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM transactions;";
            dbCount = Convert.ToInt64(cmd.ExecuteScalar());
        }
        if (Transactions.ExportAll(s).Count != dbCount)
            throw new Exception("全量导出行数与库内流水数不一致(应含作废/退款)。");
        steps.Add("CSV:全量导出行数 = 库内流水数");
    }

    private static int IndexOfId(IReadOnlyList<CategoryRow> rows, long id)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == id)
                return i;
        }
        return -1;
    }

    private static long? GetCategoryId(LedgerSession s, long txId)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT category_id FROM transactions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", txId);
        return cmd.ExecuteScalar() as long?;
    }

    /// <summary>写操作应被只读保护拦截(LedgerReadonlyException);未拦截即断言失败。</summary>
    private static void ExpectReadonly(Action action)
    {
        try
        {
            action();
        }
        catch (LedgerReadonlyException)
        {
            return;
        }
        throw new Exception("封存期内写入未被拦截(应抛 LedgerReadonlyException)。");
    }

    /// <summary>直插一笔封存期内的游离(未归属)账,模拟历史遗留/导入产生的 period_id=NULL 行。</summary>
    private static long InsertLegacyUnassigned(LedgerSession s, string date, long accountId)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO transactions (period_id, date, account_id, category_id, channel, name, note,
                          amount_cents, direction, source, status, in_pool, created_at)
VALUES (NULL, $date, $acct, 8, '', '遗留游离账', '', 1200, 'out', 'legacy', 'normal', 0, $created);";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$acct", accountId);
        cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long? GetPeriodId(LedgerSession s, long txId)
    {
        using var cmd = s.Connection.CreateCommand();
        cmd.CommandText = "SELECT period_id FROM transactions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", txId);
        return cmd.ExecuteScalar() as long?;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 忽略 */ }
    }
}
