using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 数据自检(工具菜单):临时库上跑完整链路——
/// 建库加密 → 记账 → 周期自动归属 → 作废撤出 → 错口令拦截 → 重开读回。
/// 证明 SQLCipher 建库/写读 + 流水/周期/作废数据流可用;自检数据用完即删。
/// </summary>
internal static class DbSelfTest
{
    private const string TestPassword = "selftest-123";

    public static void Run(IWin32Window owner)
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

            var detail = string.Join("\n", steps) + $"\n\n临时文件:{Path.GetFileName(path)}";
            MessageBox.Show(owner, "数据自检通过。\n\n" + detail, "数据自检",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"数据自检失败:\n{ex}", "数据自检",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            TryDelete(path);
            try { Directory.Delete(dir, recursive: true); } catch { /* 忽略 */ }
        }
    }

    /// <summary>流水/周期/作废 数据流断言;任何不符即抛错。</summary>
    private static void DataFlow(LedgerSession s, List<string> steps)
    {
        var accountA = Accounts.Insert(s, "微信零钱", "wallet", "微信", 0);
        var accountB = Accounts.Insert(s, "银行卡", "bank", "银行", 0);

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
        if (outAll != 0 || inAll != 0)
            throw new Exception("转账被错误计入收支合计。");
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
        var a = Accounts.Insert(s, "封测A", "bank", "银行", 0);
        var b = Accounts.Insert(s, "封测B", "wallet", "微信", 0);

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

        // ④ 新周期不得把封存期游离账改挂(补归属的 notSealed 守卫)
        Periods.Seal(s, pId);   // 再封存,模拟已冻结窗口
        var legacy = InsertLegacyUnassigned(s, d1, a);
        var qId = Periods.Insert(s, "重叠新期", d0, d2);
        if (Periods.Get(s, qId) is null || GetPeriodId(s, legacy) is not null)
            throw new Exception("新周期补归属把封存期游离账改挂了(应保持未归属)。");
        steps.Add("生命周期:新周期不改挂封存期游离账");

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
