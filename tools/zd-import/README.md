# zd-import · 账本导入工具(数据规范 + 加密)

把早期数据(手工账单.xlsx / 微信 / 支付宝 / 建行流水)整理成**明文 JSON 规范**(`SPEC.md`),
再用本工具加密建成主程序可打开的 `.lbook`。

```
源数据 ──agent 依 SPEC.md 整理──▶ spec.json ──zd_book.py import──▶ 账本.lbook ──主程序打开
```

## 组成
| 文件 | 作用 |
|---|---|
| `SPEC.md` | 数据规范(字段/枚举/不变式/源数据整理速记) |
| `example/示例账本.json` | 最小示例(账户/分类/周期/资金池/收支/转账全覆盖) |
| `zd_book.py` | Python 入口(lint / import / sample);零第三方依赖 |
| `zd-book.sh` / `zd-book.cmd` | mac/Linux / Windows 薄壳,随处调用 |
| `../ZhangDan.Sealer/` | .NET 密封器:真正建 SQLCipher 加密库(复用主程序同款库) |

## 快速开始(mac / Linux)
```bash
# 1) 出示例并看一眼
python3 tools/zd-import/zd_book.py sample

# 2) lint 迭代(agent 常用)
python3 tools/zd-import/zd_book.py lint 你的账本.json

# 3) 建加密账本(口令三选:--password / 环境变量 / 交互)
ZD_BOOK_PASSWORD=你的口令 \
  python3 tools/zd-import/zd_book.py import 你的账本.json --out 我的账本.lbook
```
首次若没编译过密封器,脚本会自动 `dotnet build -c Release`;要求装了 .NET SDK(8.0+,本机 10 也能跑)。

### Windows
```bat
py tools\zd-import\zd_book.py lint 你的账本.json
set ZD_BOOK_PASSWORD=你的口令
py tools\zd-import\zd_book.py import 你的账本.json --out 我的账本.lbook
```

### 随处调用
把 `tools/zd-import` 加入 PATH 后直接 `zd-book`(mac 需 `chmod +x zd-book.sh`):
```bash
zd-book.sh lint spec.json
zd-book.sh import spec.json --out a.lbook --password …
```

## 口令
优先级:`--password` > 环境变量 `ZD_BOOK_PASSWORD` > 终端交互(不回显)。
⚠️ 口令即密钥,遗失无法找回;加密后的 `.lbook` 只有正确口令可开。

## 密封器产物 / 自检
`import` 建库后会**用同口令重开一次**并打印计数(账户/分类/周期/资金池/流水/收支净额)——
这同时证明文件字节与主程序兼容。之后到主程序「打开账本」选它即可。

也可单独跑密封器:
```bash
dotnet build -c Release tools/ZhangDan.Sealer/ZhangDan.Sealer.csproj
ZD_BOOK_PASSWORD=… tools/ZhangDan.Sealer/bin/Release/net8.0/ZhangDan.Sealer \
  import spec.json --out a.lbook
```
`ZhangDan.Sealer import --help` 看参数。

## 常见问题
- **找不到密封器**:装 .NET SDK 后重跑(脚本会自动 build);或把产物路径设 `ZD_BOOK_SEALER`。
- **lint 报「分类不在 xx 分类里」**:看它是提示(可能只是该分类尚在对方 kind),真引用缺失会拦。
- **导入报透支**:非负债账户某时点余额为负 → 调大该账户 `balanceBase`(口径 B)或拆分导入起点。
- **历史行时间**:没给 `time` 的流水列表「时间」列显示中性 `12:00`,不影响金额/统计。

## 下一步(未在本步做)
- 真实历史数据(账单.xlsx 909 行 / 微信 / 支付宝 / 建行 .xls)由 agent 依 `SPEC.md §6` 整理成 spec。
- 应用侧「待归类队列 / 自动导入按钮 / 退款冲减 / 混合支付拆分」等仍按设计文档后续排期。
