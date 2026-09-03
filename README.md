# 账单管理(个人离线账本)

一个 Windows 桌面个人记账应用:**全离线、单文件加密账本(.lbook)**,按"记账周期 + 资金池"管理日常收支,可记账、转账、校准、分类、流水筛选、总览月历,并一键生成 **PDF/Excel 报表**。

技术栈:.NET 8 · WPF(WPF-UI) · SQLite **SQLCipher** 全库加密;报表用 QuestPDF / ClosedXML / ScottPlot。设计与数据规范详见 [`设计文档.md`](设计文档.md) 与 [`tools/zd-import/SPEC.md`](tools/zd-import/SPEC.md)。

> macOS 上只做开发/编译与数据层自检;界面与报表请用 Windows 运行验证。

---

## 快速开始(Windows)

### 运行
```bash
dotnet build ZhangDan.sln -c Debug
dotnet run --project src/ZhangDan.App
```

### 打包(日常使用 / 分发)
```bash
dotnet publish src/ZhangDan.App -c Release -r win-x64 --self-contained true -o publish
```
把 `publish/` 整个目录拷到任意位置即可运行(自带 .NET 运行时)。首次运行会**自动注册 `.lbook` 双击关联**(免管理员);建议先把程序放到最终目录再双击运行一次,关联会指向该路径。

详见 [`WINDOWS_BUILD.md`](WINDOWS_BUILD.md)。

---

## 功能一览

- **记一笔**:支出 / 收入,凌晨宽限自动记到昨天;「保存并记下一笔」批量录入;双击就地编辑、右键作废。
- **转账**:转出 → 转入 + 浮动 Δ(提现费/理财收益),五类(互转/充值/提现/理财结算/存取),可计入资金池。
- **记账周期**:按月/假期建周期,到期推荐、封存只读(可解除);资金池(预算/已花/剩余/可支配/保留)。
- **账户**:钱包/货基/银行卡/现金/定存/基金/储值卡 + **信用额度/负债**(花呗/白条/信用卡…,可负余额、计入净资产),停用、校准余额(记调整流水/补记真实明细/仅更新基准 + 审计历史)。
- **分类**:支出/收入两大类,颜色/关键词/排序,合并、删除(在用拦截)。
- **流水页**:按日分组,范围/方向/账户/分类/含作废/关键词筛选,跨页联动"只看该账户"。
- **总览**:周期 pill + 资金池进度 + 当日流水 + 自然月月历热力(点日联动、收支红绿)。
- **批量**:周期/账户/分类三表多选 + 批量封存/解除、启用/停用、删除。
- **报表**:左侧「报表」Tab 一键生成 —— 单周期 / 自定义日期范围(含未归属)/ 周期对比,可选 8 个内容块(总览、支出分类占比饼图、跨周期趋势图、账户与期末净资产、大额 TOP、每日收支、资金池、转账汇总),输出 **PDF + Excel**,历史可打开/重新生成/删除。
- **主题**:浅色 / 深色 / 跟随系统 + 强调色自选。
- **隐私**:总览不显示净资产;「账户」页每次进入需再输账本口令(防窥屏)。
- **导入**(进阶):见 [`tools/zd-import/README.md`](tools/zd-import/README.md) —— 按 SPEC 把早期/平台流水整理成 JSON 后用密封器建加密账本。

---

## 数据与隐私

- **全离线**,无任何网络上报。
- 账本为单个 SQLCipher 加密文件,后缀 `.lbook`,默认放 `文档\账单管理`;**口令即密钥,遗忘无法找回**。
- 明文偏好(上次账本路径、主题、凌晨宽限等)在 `%APPDATA%\账单管理\app.json`,**不含口令**。
- 日志(供排障)在 `%APPDATA%\账单管理\logs`(设置页可打开目录/清空)。
- 报表导出在 `文档\账单管理\报表`,历史记录只存在账本内。

---

## 目录结构(仓库)

```
ZhangDan.sln
设计文档.md / WINDOWS_BUILD.md / 验收清单.md
src/
  ZhangDan.Core     业务/数据层(net8.0 纯库,含 SelfTest)
  ZhangDan.App      WPF 主程序(net8.0-windows)
 账单管理           旧 WinForms 过渡壳(暂留)
tools/
  ZhangDan.Sealer   .lbook 密封器(导入建库 CLI)
  zd-import         spec 规范 + Python 入口 + 示例
```

---

## 测试与质量

- **数据自检**:程序内 设置 → 运行数据自检(建库/记账/转账/作废/周期/校准/负债/报表口径/日志等端到端断言)。
- **macOS 数据层直跑**:
  ```bash
  cd /private/tmp/zscheck   # 独立临时 runner(引用 Core)
  dotnet build
  DOTNET_ROLL_FORWARD=LatestMajor dotnet bin/Debug/net8.0/ZhangDan.App.dll
  # 期望输出 SELFTEST OK
  ```

---

## 第三方与许可

- **QuestPDF** —— Community 许可(年收入 < $1M / 非商用免费,含水印即其标识,勿去除;PDF 由此生成)。详见「设置 → 关于」署名。
- ClosedXML · ScottPlot · WPF-UI —— MIT。
- SQLitePCLRaw(bundle_e_sqlcipher):SQLite(公有领域)+ SQLCipher(BSD 风格)。

---

## 已知边界(FAQ)

- **负债账户**:花呗/白条/信用卡统一归「信用额度/负债」一类,平台名在「平台」下拉选/填;老数据里的旧类型仅作兼容仍按负债处理。
- **净资产口径**:报表里账户与净资产按**报表期末重建**(基准 + 截至期末净变动),不是今日快照。若账户基准日(balance_date)在报表期末之后(快照口径),更早的历史期账面停在基准数——这是快照模型的固有边界。
- **图表文字**:ScottPlot 图内不嵌中文,趋势图的横轴/图例语义以 PDF 下方的中文说明与色块图例表给出。
- 报表图/负债/门禁/文件关联等整批功能属新交付,日常使用中如遇异常,把设置页日志(`logs/`)或弹窗文案反馈即可。
