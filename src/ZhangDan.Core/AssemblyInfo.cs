using System.Runtime.CompilerServices;

// Core 的业务类型保持 internal;仅对两个 UI 壳可见:
//  ZhangDan.App = 新的 WPF 主程序;账单管理 = 迁移期的旧 WinForms(过渡用)
//  ZhangDan.Sealer = 账本导入密封器(独立 CLI,复用 Core 建库/插入,建 SQLCipher .lbook)
[assembly: InternalsVisibleTo("ZhangDan.App")]
[assembly: InternalsVisibleTo("账单管理")]
[assembly: InternalsVisibleTo("ZhangDan.Sealer")]
