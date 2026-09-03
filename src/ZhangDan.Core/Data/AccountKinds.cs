using System;

namespace ZhangDan;

/// <summary>
/// 账户类型清单(单一真源)。type 字段为自由串(自 schema v5 起 DB 不再 CHECK),
/// 合法取值由这里统一定义,Core 守卫 / UI 下拉 / 导入 Sealer 同引用,避免各自漂移。
/// 资产类:wallet/money_fund/bank/cash/fixed_deposit/fund/prepaid(见 <see cref="AccountKinds.Asset"/>)。
/// 负债类(信用额度/待还):允许余额为负、计入净资产时按负值扣减。
/// </summary>
internal static class AccountKinds
{
    // —— 资产 ——
    public const string Wallet = "wallet";            // 钱包/零钱
    public const string MoneyFund = "money_fund";     // 货币基金(零钱通/余额宝)
    public const string Bank = "bank";                // 银行卡
    public const string Cash = "cash";                // 现金
    public const string FixedDeposit = "fixed_deposit"; // 定存(整存整取)
    public const string Fund = "fund";                // 基金
    public const string Prepaid = "prepaid";          // 储值卡(水卡等)

    /// <summary>资产类(不得透支)。</summary>
    public static readonly string[] Asset =
        { Wallet, MoneyFund, Bank, Cash, FixedDeposit, Fund, Prepaid };

    // —— 负债(信用额度 / 待还,允许负余额)——
    public const string CreditCard = "credit_card";   // 信用卡
    public const string HuaBei = "hua_bei";           // 花呗(信用额度消费)
    public const string BaiTiao = "bai_tiao";         // 白条(信用额度消费)
    public const string JinTiao = "jin_tiao";         // 京东金条(现金借贷)
    public const string Credit = "credit";            // 其他信用额度/负债

    /// <summary>负债型 token:允许负余额;净资产按当前(负)账面计入。</summary>
    public static readonly string[] Liability =
        { CreditCard, HuaBei, BaiTiao, JinTiao, Credit };

    /// <summary>是否负债型账户类型(可透支 / 余额为负)。</summary>
    public static bool IsLiability(string? type) =>
        type is not null && Array.IndexOf(Liability, type) >= 0;
}
