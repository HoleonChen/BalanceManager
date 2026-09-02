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
