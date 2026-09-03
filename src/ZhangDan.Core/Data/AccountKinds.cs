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

    // —— 负债(信用额度/待还,允许负余额)——
    // 用户拍板:花呗/白条/信用卡等统一归一大类「信用额度/负债」(credit),平台名在 platform 里填;
    // 不再按平台细分类型。下方旧 token 仅作历史兼容(老数据里的 credit_card/hua_bei/bai_tiao 仍判负债),新账户用 credit。
    public const string CreditCard = "credit_card";   // 旧:信用卡(兼容)
    public const string HuaBei = "hua_bei";           // 旧:花呗(兼容)
    public const string BaiTiao = "bai_tiao";         // 旧:白条(兼容)
    public const string Credit = "credit";            // 信用额度/负债(花呗/白条/信用卡…)

    /// <summary>负债型 token:允许负余额;净资产按当前(负)账面计入。新值只用 <see cref="Credit"/>。</summary>
    public static readonly string[] Liability =
        { Credit, CreditCard, HuaBei, BaiTiao };

    /// <summary>是否负债型账户类型(可透支 / 余额为负)。</summary>
    public static bool IsLiability(string? type) =>
        type is not null && Array.IndexOf(Liability, type) >= 0;
}
