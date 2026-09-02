# Windows 编译指引 · 账单管理

> 目标:在 Windows 上构建并运行本账本(Windows 桌面 GUI)。
> 开发流程:macOS 写代码 → `git push` 到 GitHub → Windows `git clone` + 编译运行。
> 面向 Windows 10/11 x64。

---

## 0. 最小路径(不看细节版)

```powershell
# 1. 装好 .NET 8 SDK 与 git(见 §1)
# 2. 拿代码
git clone https://github.com/<你的用户名>/<仓库名>.git
cd <仓库名>
# 3. 构建 + 运行(调试)
dotnet build
dotnet run
# 4. 出正式 exe(发给最终使用)
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

产物在 `publish\` 里,`publish\账单管理.exe` 双击即可运行,**目标机无需安装 .NET**。

---

## 1. 环境准备

### 1.1 git
```powershell
winget install Git.Git
# 或 https://git-scm.com 手动安装
git --version   # 验证
```

### 1.2 .NET 8 SDK
```powershell
winget install Microsoft.DotNet.SDK.8
# 或 https://dotnet.microsoft.com/download/dotnet/8.0 下载 "SDK 8.0.x"
dotnet --version        # 应显示 8.0.x
dotnet --list-runtimes  # 应包含 "Microsoft.WindowsDesktop.App 8.0.x"(WinForms 需要)
```

> WinForms 需要 **Windows Desktop SDK**。上面装的完整 SDK 已自带,`--list-runtimes` 能看到 `Microsoft.WindowsDesktop.App` 即说明没问题。

---

## 2. 获取代码

```powershell
git clone https://github.com/<你的用户名>/<仓库名>.git
cd <仓库名>
git checkout main        # 或约定的分支
```

首次执行任何 `dotnet` 命令会联网还原 NuGet 包(SQLCipher / ScottPlot / QuestPDF / ClosedXML),需联网一次,之后走缓存。

---

## 3. 常规构建与运行(开发调试)

```powershell
dotnet build             # 还原依赖 + 编译
dotnet run               # 运行(等同调试)
```

- 改了代码后重跑 `dotnet build` 即可增量编译。
- `dotnet run` 从仓库根启动;工程在 `src/` 子目录时先 `cd src/<工程>` 再 run。

---

## 4. 发布正式版本

### 4.1 推荐:文件夹发布(自包含)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

- 产物在 `publish\`:**`账单管理.exe` + 若干运行库文件**。
- 整个文件夹拷到任意 Windows x64 机器直接运行,**无需装 .NET**。
- 数据库、报表等用户数据默认写入用户的「文档」目录(见 §7),不在 publish 文件夹内,重装升级不影响数据。

### 4.2 可选:单文件发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

- 产物是一个 exe。
- **注意**:SQLCipher、SkiaSharp(ScottPlot 用)的原生库会在首次运行时自解压到临时目录;个别杀毒软件/Windows Defender 可能误报或拦截。**若遇到问题,退回 4.1 的文件夹发布**,功能完全一致。

---

## 5. SQLCipher 全库加密(SQLite)

设计:整个数据库单文件、打开需密码,采用 SQLCipher。

**依赖(项目 csproj 已配好,发布自动带上):**
- `Microsoft.Data.Sqlite.Core`
- `SQLitePCLRaw.bundle_e_sqlcipher` ← 原生 `e_sqlcipher` 按 RID(win-x64)由 NuGet 提供,**不需要手动拷贝任何 dll**;自包含发布时原生库会自动进入产物。

**代码侧约定(实现时遵循):**
```csharp
// 程序启动、打开账本前调用一次
SQLitePCL.Batteries_V2.Init();

// 连接串带密码即加密
var conn = new SqliteConnection($"Data Source={dbPath};Password={口令}");
```
- 数据库文件本身就是加密后的单文件,直接用文本编辑器打开是乱码。
- **口令不要硬编码进代码**:首次启动弹窗让用户设定;之后可选择「记住口令(存 Windows 凭据管理器 Credential Manager)」或「每次打开输入」(对应 设置→密码与加密)。

---

## 6. 常见问题(FAQ)

| 现象 | 处理 |
|---|---|
| `error NETSDK...Windows Desktop SDK` / 找不到 WinForms | 确认装的 SDK 包含 WindowsDesktop.Runtime(见 §1.2 的 `--list-runtimes`)|
| 双击 exe 提示「未知发布者 / SmartScreen」 | 本地自用、未签名 exe 属正常:属性 → 勾「解除锁定」,或点「更多信息 → 仍要运行」|
| 杀软误报单文件 exe | 改用 §4.1 文件夹发布 |
| 要在 ARM64 的 Windows 上跑 | RID 改 `win-arm64` 重新发布 |
| 首次 `dotnet run` 慢 | 正在还原 NuGet 包,属正常 |
| 打开已建账本提示口令错误 | 输入的是建账本时设定的口令;口令即密钥,遗忘无法找回(设计如此)|
| 中文乱码/方块 | 用 UTF-8 源码文件;WinForms 字体设置成微软雅黑(实现时处理)|

---

## 7. 数据与文件位置(约定)

- 用户数据目录:`%USERPROFILE%\Documents\账单管理\`
  - 账本文件:`.lbook`(SQLCipher 加密,单个文件;后缀只是标识)
  - 报表导出:`报表\` 子目录(PDF / xlsx / CSV)
- **升级流程**:先备份(拷贝 .lbook),再覆盖新 exe;数据目录不随程序删除。
- **git 忽略**:`.lbook`、`publish/`、`bin/`、`obj/` 不入库(仓库 .gitignore 已含),避免把加密库或构建产物推到 GitHub。

---

## 8. 从 mac 更新到 Windows 重新构建

```powershell
git pull
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

mac 上写完改完 push;Windows 上一键拉取重发,覆盖旧 publish 即可。

---

## 附:工程关键配置(供核对)

`<工程>.csproj` 关键行:
```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
<Nullable>enable</Nullable>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```
- 仓库结构(计划):仓库根放 `.sln`,代码在 `src/账单管理/`。
- 版本基线:Windows 10/11 x64,`dotnet publish` 目标 `win-x64`。
