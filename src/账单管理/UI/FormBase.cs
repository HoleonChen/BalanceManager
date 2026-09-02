using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 全部窗口基类:显式以 96-DPI 为 AutoScaleDimensions 基准 + AutoScaleMode.Dpi。
/// 代码直排的布局(固定像素坐标)在高分屏下会随 DPI 整体放大,避免按钮偏小、文字截断;
/// 100% 缩放下系数为 1,不受影响。
/// </summary>
internal class FormBase : Form
{
    protected FormBase()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }
}
