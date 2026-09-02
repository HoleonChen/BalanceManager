using System;

namespace ZhangDan;

/// <summary>分(整数)↔ 元(Decimal) 换算与显示。</summary>
internal static class Money
{
    /// <summary>分 → 显示串,如 12345 → "¥123.45"。</summary>
    public static string Yuan(long cents) => "¥" + (cents / 100m).ToString("0.00");

    /// <summary>元(界面 Decimal 输入)→ 分,四舍五入。</summary>
    public static long ToCents(decimal yuan)
        => decimal.ToInt64(decimal.Round(yuan * 100m, 0, MidpointRounding.AwayFromZero));
}
