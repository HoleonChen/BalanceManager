# 账本数据规范(明文 JSON)· Zd Book Spec

> 目标:让 **agent(AI)能把早期数据整理成这份明文 JSON**,再由密封器加密建成 `.lbook`,被主程序直接打开。
> 格式原则:**只写人/agent 好写的**(金额用「元」、引用用「名称」),难的部分(元→分、查 ID、周期自动归属、防透支、加密)全部由工具做。

配套:
- 密封器(建加密库):`tools/ZhangDan.Sealer`
- 便捷调用:`tools/zd-import/zd_book.py lint|import|sample`
- 最小示例:`tools/zd-import/example/示例账本.json`

---

## 1. 工作流

```
源数据(账单.xlsx / 微信 / 支付宝 / 建行 .xls)
   │  agent 按本规范整理(见 §6 规则速记)
   ▼
spec.json(明文)
   │  zd_book.py import spec.json --out 账本.lbook
   ▼
SQLCipher 加密 .lbook  →  主程序「打开账本」即可使用
```

## 2. 顶层结构

```jsonc
{
  "ledgerName": "账本名(可选;缺省用 --name 或输出文件名)",

  "accounts":    [ /* §3.1 账户 */ ],
  "categories":  [ /* §3.2 分类 */ ],
  "periods":     [ /* §3.3 周期 */ ],
  "fundPools":   [ /* §3.4 资金池 */ ],
  "transactions":[ /* §3.5 流水 */ ],

  "_notes": "任意说明,工具忽略"
}
```
- 数组顺序即插入顺序;分类 `sortOrder`、账户/账户平台排序由工具按出现序自动排。
- 所有 **name 引用按名称**;引用了但未定义、或同组重名 → 工具报错并指到第几条。
- 顶层字段都可缺省(空则建一个只有该结构的库),但账户/分类/周期常为导入必需。

## 3. 各实体字段

### 3.1 账户 `accounts[]`
| 字段 | 必填 | 取值/说明 |
|---|---|---|
| `name` | ✔ | 唯一 |
| `type` | 默认 `wallet` | `wallet`(钱包/零钱) `money_fund`(货币基金/零钱通·余额宝) `bank` `cash` `fixed_deposit` `fund`(基金) `prepaid`(储值卡/水卡) |
| `platform` | 可选 | 建议 `微信/支付宝/银行/投资/现金/储值卡`,可自定义 |
| `balanceBase` | 可选 | 账户**基准余额(元)**,decimal |
| `balanceDate` | 可选 | 基准日 `yyyy-MM-dd`;**建议给出**(见 §5「余额两种口径」) |
| `enabled` | 默认 `true` | `false` = 停用(不计净资产、不进记账下拉;历史流水保留) |

### 3.2 分类 `categories[]`
| 字段 | 必填 | 说明 |
|---|---|---|
| `name` | ✔ | 同一 kind 下唯一 |
| `kind` | ✔ | `expense`(支出)/ `income`(收入) |
| `color` | 可选 | `#RRGGBB` 主题色(图表/标签同色;income 可留空自动) |
| `keyword` | 可选 | 导入归类关键词(空格/逗号分隔) |

> 系统保留子分类「差额调整」(校准自动建、挂「其他」下),**不用出现在规范里**。

### 3.3 周期 `periods[]`(生活费周期)
| 字段 | 必填 | 说明 |
|---|---|---|
| `name` | ✔ | 唯一 |
| `startDate` | ✔ | `yyyy-MM-dd` |
| `endDate` | 可选 | `yyyy-MM-dd`;空 = 长期 |
| `note` | 可选 | 备注(现版本不落库,忽略) |

不可与其他周期重叠(工具会校验)。流水会按日期**自动归属**到覆盖它的进行中周期;落在所有周期之外的流水保持「未归属」。

### 3.4 资金池 `fundPools[]`(单周期至多一个)
| 字段 | 必填 | 说明 |
|---|---|---|
| `period` | ✔ | 引用周期 name |
| `account` | ✔ | 引用账户 name |
| `name` | 默认 `生活费` | |
| `budget` | 默认 0 | 预算(元) |
| `reserve` | 默认 0 | 预计保留(元) |

### 3.5 流水 `transactions[]`
| 字段 | 必填 | 说明 |
|---|---|---|
| `date` | ✔ | `yyyy-MM-dd` |
| `direction` | ✔ | `in`(收入)/ `out`(支出)/ `transfer`(转账) |
| `name` | ✔ | 名称(转账时通常就是转账类别文字) |
| `account` | ✔ | 账户名 |
| `category` | in/out 时可选 | 分类名;**支出→expense 分类,收入→income 分类** |
| `amount` | ✔ | **元**;**in/out 为正数**;transfer 为本金正数 |
| `toAccount` | transfer 必填 | 转入账户名 |
| `delta` | transfer 可选 | 浮动(元):收益 `+` / 手续费 `-`(如提现手续费) |
| `transferKind` | transfer 可选 | `互转`/`充值`/`提现`/`理财结算`/`存取` |
| `time` | 可选 | `HH:mm` 展示用;缺省 `12:00`(历史数据没有准确时间时给个中性值) |
| `channel` | 可选 | `实体`/`网购`/… |
| `note` | 可选 | 备注 |
| `inPool` | 默认 | `out` 默认 `true`(计入池);`in`/`transfer` 默认 `false` |
| `status` | 默认 `normal` | `normal`/`cancelled`(已作废)/`refunded`(已退款) |
| `source` | 默认 `manual` | `manual`/`wx`/`zfb`/`legacy`(微信/支付宝/银行或手工账迁移统一 legacy) |
| `refId` | 可选 | 平台交易单号(对账/去重备查,主程序暂不消费) |

工具会自动把 `created_at` 写为 `date + time`,保证列表时间列合理;同一天内部按 id 倒序,无需你指定顺序。

## 4. 工具会做的处理(规范作者不必重复实现)

1. **建库加密**:口令即密钥;支持 `--password` / 环境变量 `ZD_BOOK_PASSWORD` / 交互输入。
2. **元→分**:所有金额字段经 `元 × 100` 四舍五入成整数分。
3. **顺序插入 + 周期归属**:账户→分类→周期→资金池→流水;流水**按日期升序**插入(防透支判定用「已插流水后的账面」),日期落入哪个进行中周期自动归属。
4. **写后可选字段**:`source`、`status`(作废/退款)、`ref_id`、`time` 在插入后落库。
5. **不变量守卫**:周期重叠、分类/账户重名、引用缺失、direction/kind/type/source/status 非法、金额 ≤ 0、非负债账户余额为负(out 透支)、日期格式,都会给出带「数组[第N条]」的中文报错;失败即删半成品文件,不留坏账本。

## 5. 余额两种口径(重要)

`账面余额 = balanceBase + (balanceDate 之后、状态正常流水的净变动)`。
- **口径 A「快照式」**:`balanceBase` = 某账户**当前实际余额**,`balanceDate` 给今天/最近日 → 历史流水只展示不重复计 → 账面≈当前实际。
- **口径 B「重建式」**:`balanceBase` = 该账户**最早一笔之前的期初余额**,`balanceDate` = 期初日 → 流水一路累加,账面自洽地推到今天。

早期手工账用哪种都行,**但要一致**(同账户别混),建议先在「其他账户统计/零钱通/余额宝」末行余额核对一遍。

## 6. 把源数据整理成 spec 的规则速记(给 agent)

源文件细节与逐列对照见 `/Users/apple/Documents/个人账单分析/账本程序设计构思.md` 与 `设计文档.md §7/§11`。要点:
- **手工账 账单.xlsx**:月度 sheet 一行一天、分类是列 → **逆透视**成逐笔;Excel **日期序列号归一化**(`46174`→`2026-06-01`)或中文日期(`2025年2月23日11点00分`)解析;公式文本需白名单求值后再写金额;备注列并进 `note`。2024.9 旧 sheet 按渠道列展开。账户 sheet(零钱通/余额宝/理财/其他账户统计)含**转账/理财结算**:有滚动余额、带符号流水 → 依「来源/去向」归纳成 `transfer`(充值/提现/存取/理财结算)。
- **微信 xlsx**:表头在第 17 行;`金额(元)` 无符号、方向看 `收/支`(`/`=中性);`交易单号`→`refId`;`支付方式` 决定 account;中性类型(零钱通转出/转入、零钱提现、理财通等)→ `transfer`,`source=wx`。
- **支付宝 CSV**:GB18030、表头在说明行之后(约第 24 行);`收/支`(`收入/支出/不计收支`);平台 `交易分类` 映射到本地分类(映射表建议做成 spec 的 categories.keyword / 单独映射文档);`不计收支`→`transfer`,`source=zfb`;`交易关闭`剔除、`退款成功`→该笔 `status=refunded`(退款不作为收入)。
- **建行 .xls**:BIFF8(旧 .xls);`交易金额`带符号(支出负/收入正);按 `摘要` 归类:消费→out、利息存入→in、转账/ATM 存取/支付机构提现→按账务本质转 transfer;`source=legacy`。
- 建行储蓄卡流水与账户初始余额现已齐(2024-08→2026-09),「账户初始余额来源待定」可解除。

## 7. 校验与构建命令

```bash
python3 tools/zd-import/zd_book.py lint <spec.json>          # 只校验+汇总(迭代用)
python3 tools/zd-import/zd_book.py sample > /tmp/spec.json   # 出示例
ZD_BOOK_PASSWORD=你的口令 python3 tools/zd-import/zd_book.py import <spec.json> --out 账本.lbook
```
Windows 下 `py tools\zd-import\zd_book.py …` 同理。首次用若找不到密封器,脚本会自动 `dotnet build -c Release` 生成;需要装有 .NET SDK(建议 8.0+)。密封器单独运行见其 `--help`。
