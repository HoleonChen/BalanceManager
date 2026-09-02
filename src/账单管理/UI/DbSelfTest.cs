using System;
using System.IO;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 数据自检(工具菜单):建临时账本 → 写入标记 → 错口令应被拦 → 正确口令重开读回。
/// 证明 SQLCipher 建库/加密/写读/口令校验整条链路可用;自检数据用完即删。
/// </summary>
internal static class DbSelfTest
{
    private const string TestPassword = "selftest-123";

    public static void Run(IWin32Window owner)
    {
        var dir = Path.Combine(AppPaths.AppDataDir, "自检");
        var path = Path.Combine(dir, $"自检-{Guid.NewGuid():N}.lbook");
        Directory.CreateDirectory(dir);

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
            }

            // 用错误口令打开:应当被拒绝
            bool wrongBlocked;
            try
            {
                LedgerStore.Open(path, "wrong-password");
                wrongBlocked = false;
            }
            catch (LedgerPasswordException)
            {
                wrongBlocked = true;
            }

            // 正确口令重开:读回标记
            string? readBack;
            using (var session = LedgerStore.Open(path, TestPassword))
                readBack = session.GetMeta("selftest.token");

            if (readBack != token)
                throw new Exception("重开读回的标记与写入不一致。");

            var detail =
                $"建库 + 写标记:通过\n错口令被拦截:{(wrongBlocked ? "通过" : "失败(未拦截)!")}\n" +
                $"正确口令重开读回:通过\n\n临时文件:{Path.GetFileName(path)}";

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

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 忽略 */ }
    }
}
