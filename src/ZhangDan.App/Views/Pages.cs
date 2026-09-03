using System.Windows;
using System.Windows.Controls;

namespace ZhangDan.App.Views;

/// <summary>各导航页基类:页在成为当前内容时收到 <see cref="OnShown"/>。</summary>
internal abstract class PageBase : UserControl
{
    protected PageBase() { }

    /// <summary>该页被切入前台时刷新数据(此时 App.Ledger 必非空)。</summary>
    public virtual void OnShown() { }
}
