<div align="center">

# 账单管理 · ZhangDan

**全离线 · 单文件加密 · 个人账本桌面应用(Windows)**

记日常收支、管理记账周期与资金池、月度复盘 —— 数据只在你手里。

`.NET 8 · WPF · SQLCipher`  |  PDF / Excel 报表一键导出  |  浅色 / 深色 / 跟随系统

</div>

---

## ✨ 特性

- 🧾 **记账**:支出 / 收入 / 转账(含提现手续费、理财收益等浮动 Δ),凌晨宽限自动记昨天,支持「保存并记下一笔」批量录入;双击编辑、右键作废。
- 🗓️ **记账周期 + 资金池**:按生活费周期组织账目;资金池预算/已花/剩余/可自由支配;到期推荐、封存只读(可解除)。
- 🏦 **账户管理**:支持 钱包/货基/银行卡/现金/定存/基金/储值卡 与 **信用额度·负债**(花呗/白条/信用卡,余额可为负、计入净资产);余额校准(记调整流水 / 补记真实明细 / 仅更新基准)+ 审计历史。
- 🏷️ **分类**:支出 / 收入两大类;颜色、关键词、排序;合并 / 删除;流水页支持 范围/方向/账户/分类/关键词 筛选。
- 📅 **总览**:当日流水 + 自然月月历热力,点日联动,收支红绿一眼看清。
- ⚡ **批量操作**:周期、账户、分类三表多选,一次 封存/解除、启用/停用、删除。
- 📊 **报表**:单周期 / 自定义日期 / 多周期对比;支出分类饼图、跨周期趋势图、账户与净资产(按期末重建)、大额 TOP、每日收支、转账汇总、资金池;输出 **PDF + Excel**,历史可重生成。
- 🎨 **主题**:浅色 / 深色 / 跟随系统 + 强调色自选。
- 🔒 **隐私**:全离线无上报;「账户」页进入需口令二次验证(防窥屏);总览不显示净资产。

## 🖼️ 截图

| | |
|---|---|
| *(总览)* | *(流水)* |
| *(报表 PDF)* | *(设置 / 关于)* |

> 截图占位:发布后替换。

---

## 🚀 使用

Windows 运行:

```bash
dotnet build ZhangDan.sln
dotnet run --project src/ZhangDan.App
```

发布 / 打包(自带 .NET,免安装):

```bash
dotnet publish src/ZhangDan.App -c Release -r win-x64 --self-contained true -o publish
```

首次运行会自动注册 `.lbook` 双击关联;账本默认存放于 `文档\账单管理`。

## 🔐 数据与安全

- **全离线**:无任何网络上报。
- 账本 = 单个 **SQLCipher 加密** 文件(`.lbook`);**口令即密钥,遗忘无法找回**。
- 明文偏好(主题、上次账本路径等)存于 `%APPDATA%\账单管理\app.json`,**不含口令**。
- 报表导出在 `文档\账单管理\报表`;报表历史仅记在账本内。
- 应用启动/自检内置完整数据自检(设置页可运行)。

## 🛠 技术栈

- .NET 8 / WPF · [WPF-UI](https://github.com/lepoco/wpfui)(Fluent 观感)
- SQLite + **SQLCipher**(SQLitePCLRaw)
- 报表:[QuestPDF](https://github.com/QuestPDF/QuestPDF) · [ClosedXML](https://github.com/ClosedXML/ClosedXML) · [ScottPlot](https://github.com/ScottPlot/ScottPlot)

## 🤖 AI 协助声明

本项目在开发过程中使用了 **AI 编码助手(Claude)** 辅助编写/重构代码与文档;
所有代码与设计由作者 **HoleonChen** 负责审核、测试与发布。应用**全离线、无任何数据上报或训练采集**。

## 📄 许可与致谢

- 本仓库代码以 **MIT** 许可发布(见 [`LICENSE`](LICENSE))。
- 第三方组件各自保留其许可:
  - **QuestPDF**:使用其 Community 版生成 PDF,遵循 QuestPDF 自身许可(非商用 / 年收入 < $1M 的组织免费;产物页脚含水印即其 Community 标识)。是否满足该许可由使用方按自身情况确认。
  - ClosedXML / ScottPlot / WPF-UI —— MIT;SQLite(公有领域)+ SQLCipher(BSD 风格)。

> 设计与数据导入规范、Windows 构建细节、验收清单见仓库内 `设计文档.md`、`WINDOWS_BUILD.md`、`tools/zd-import/SPEC.md`。

---

<div align="center"><sub>记账周期 | 资金池 | 账户与负债 | 校准 | 报表 —— 把每笔钱记清楚。</sub></div>
