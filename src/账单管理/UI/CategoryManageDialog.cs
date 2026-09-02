using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhangDan;

/// <summary>
/// 分类管理(设计 §9):支出/收入顶层大类——新建 / 重命名 / 改色 / 关键词 / 上移下移(叠放序)/
/// 合并(流水改挂 + 关键词并入)/ 删除(须先清流水:先合并)。子类(「差额调整」)不在此列、不可删。
/// </summary>
internal sealed class CategoryManageDialog : Form
{
    // 固定高区分度调色板(与 seed 配色一致,色盲友好);新建自动按序取色,可手动改。
    private static readonly string[] Palette =
    {
        "#F06292", "#42A5F5", "#FFA726", "#8E24AA", "#29B6F6", "#66BB6A",
        "#5C6BC0", "#9E9E9E", "#EC407A", "#26A69A", "#EF6C00", "#7E57C2",
        "#26C6DA", "#D81B60", "#8D6E63", "#FFB300"
    };

    private readonly LedgerSession _ledger;
    private readonly RadioButton _expRadio = new() { Text = "支出分类", Checked = true };
    private readonly RadioButton _incRadio = new() { Text = "收入分类", Margin = new Padding(12, 0, 0, 0) };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.FixedSingle,
        HideSelection = false
    };

    public CategoryManageDialog(LedgerSession ledger)
    {
        _ledger = ledger;
        Text = "分类管理";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 520);
        MinimumSize = new Size(680, 400);

        _list.Columns.Add("名称", 210);
        _list.Columns.Add("主题色", 90);
        _list.Columns.Add("关键词(自动归类)", 220);
        _list.Columns.Add("使用", 70, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => RenameSelected();

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(10, 6, 10, 0)
        };
        _expRadio.CheckedChanged += (_, _) => RefreshList();
        _incRadio.CheckedChanged += (_, _) => RefreshList();
        top.Controls.AddRange(new Control[] { _expRadio, _incRadio });

        void AddButton(FlowLayoutPanel host, string text, int width, EventHandler onClick)
        {
            var b = new Button { Text = text, Width = width, Height = 30, Margin = new Padding(8, 0, 0, 0) };
            b.Click += onClick;
            host.Controls.Add(b);
        }

        var row1 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        AddButton(row1, "＋ 新建…", 92, (_, _) => CreateCategory());
        AddButton(row1, "重命名…", 88, (_, _) => RenameSelected());
        AddButton(row1, "改色…", 80, (_, _) => ColorSelected());
        AddButton(row1, "关键词…", 90, (_, _) => KeywordSelected());
        AddButton(row1, "▲ 上移", 76, (_, _) => MoveSelected(true));
        AddButton(row1, "▼ 下移", 76, (_, _) => MoveSelected(false));
        AddButton(row1, "合并到…", 88, (_, _) => MergeSelected());
        AddButton(row1, "删除", 64, (_, _) => DeleteSelected());
        top.Controls.Add(row1);

        var hint = new Label
        {
            Text = "删除须先处理其下流水(先合并到其他分类);子分类(「差额调整」)不在此列。上移/下移决定图表堆叠顺序。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(4, 10, 0, 0)
        };
        top.Controls.Add(hint);
        top.SetFlowBreak(hint, true);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 12, 6)
        };
        var close = new Button { Text = "关闭", Width = 84, Height = 30, DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(close);

        Controls.Add(_list);
        Controls.Add(bottom);
        Controls.Add(top);

        RefreshList();
    }

    private bool Income => _incRadio.Checked;

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var c in Categories.ListManual(_ledger, Income))
        {
            var li = new ListViewItem(c.Name);
            li.SubItems.Add(c.Color ?? "—");
            li.SubItems.Add(KeywordOf(c));
            li.SubItems.Add(Categories.UsedCount(_ledger, c.Id).ToString());
            li.Tag = c;
            _list.Items.Add(li);
        }
        _list.EndUpdate();
    }

    private string KeywordOf(CategoryRow c)
    {
        using var cmd = _ledger.Connection.CreateCommand();
        cmd.CommandText = "SELECT keyword FROM categories WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", c.Id);
        return cmd.ExecuteScalar() as string ?? "";
    }

    private CategoryRow? Selected()
        => _list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not CategoryRow c
            ? null
            : c;

    private void CreateCategory()
    {
        using var dlg = new CategoryInputDialog(
            Income ? "新建收入分类" : "新建支出分类", "名称", "", withKeyword: true, "创建", "关键词(可空)");
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        var name = dlg.Value;
        if (name.Length == 0)
            return;
        try
        {
            var color = NextFreeColor();
            Categories.Insert(_ledger, name, Income, color, dlg.Keyword);
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"新建分类失败:\n{ex.Message}", "分类管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenameSelected()
    {
        var c = Selected();
        if (c is null)
            return;
        using var dlg = new CategoryInputDialog("重命名分类", "名称", c.Name, withKeyword: false, "保存");
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0 || dlg.Value == c.Name)
            return;
        Categories.Rename(_ledger, c.Id, dlg.Value);
        RefreshList();
    }

    private void ColorSelected()
    {
        var c = Selected();
        if (c is null)
            return;
        using var pick = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = ParseHex(c.Color) ?? Color.SteelBlue
        };
        if (pick.ShowDialog(this) != DialogResult.OK)
            return;
        Categories.SetColor(_ledger, c.Id, "#" + pick.Color.R.ToString("X2") + pick.Color.G.ToString("X2") + pick.Color.B.ToString("X2"));
        RefreshList();
    }

    private void KeywordSelected()
    {
        var c = Selected();
        if (c is null)
            return;
        using var dlg = new CategoryInputDialog($"关键词 · {c.Name}", "关键词(空格分隔多个)", KeywordOf(c), withKeyword: false, "保存");
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        Categories.SetKeyword(_ledger, c.Id, dlg.Value);
        RefreshList();
    }

    private void MoveSelected(bool up)
    {
        var c = Selected();
        if (c is null)
            return;
        Categories.Move(_ledger, c.Id, up);
        RefreshList();
        SelectId(c.Id);
    }

    private void MergeSelected()
    {
        var c = Selected();
        if (c is null)
            return;
        if (Categories.ChildCount(_ledger, c.Id) > 0)
        {
            MessageBox.Show(this,
                $"「{c.Name}」下还有子分类(如「差额调整」),不能作为被合并方。\n请先把子分类处理掉,或保留本类。",
                "分类管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var targets = new List<CategoryRow>();
        foreach (var t in Categories.ListManual(_ledger, Income))
        {
            if (t.Id != c.Id && Categories.ChildCount(_ledger, t.Id) == 0)
                targets.Add(t);
        }
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "没有可合并到的其他同收支分类。", "分类管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new MergeTargetDialog(targets);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        if (MessageBox.Show(this,
                $"把「{c.Name}」合并到「{dlg.Target.Name}」?\n\n其下 {Categories.UsedCount(_ledger, c.Id)} 笔流水改挂到「{dlg.Target.Name}」,"
                + "关键词并入、颜色以目标为准;原分类删除。",
                "合并分类", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        try
        {
            Categories.Merge(_ledger, c.Id, dlg.Target.Id);
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"合并失败:\n{ex.Message}", "分类管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelected()
    {
        var c = Selected();
        if (c is null)
            return;
        var used = Categories.UsedCount(_ledger, c.Id);
        var children = Categories.ChildCount(_ledger, c.Id);
        if (used > 0 || children > 0)
        {
            MessageBox.Show(this,
                $"「{c.Name}」仍被 {used} 笔流水使用{(children > 0 ? $",且下含 {children} 个子分类" : "")},不能直接删除。\n"
                + "请先「合并到…」把流水改挂到其他分类(如「其他」),再回来删除。",
                "删除分类", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"删除分类「{c.Name}」?\n\n(确认无流水引用后)分类删除后不可恢复。",
                "删除分类", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;
        try
        {
            Categories.Delete(_ledger, c.Id);
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"删除失败:\n{ex.Message}", "分类管理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SelectId(long id)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is CategoryRow c && c.Id == id)
            {
                item.Selected = true;
                item.Focused = true;
                return;
            }
        }
    }

    /// <summary>同 kind 内第一个未被占用的调色板色;全占则按数轮转。</summary>
    private string NextFreeColor()
    {
        var used = new HashSet<string>();
        foreach (var c in Categories.ListManual(_ledger, Income))
        {
            if (c.Color is not null)
                used.Add(c.Color.ToUpperInvariant());
        }
        foreach (var p in Palette)
        {
            if (!used.Contains(p))
                return p;
        }
        return Palette[used.Count % Palette.Length];
    }

    private static Color? ParseHex(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#')
            return null;
        try
        {
            return Color.FromArgb(
                Convert.ToInt32(hex.Substring(1, 2), 16),
                Convert.ToInt32(hex.Substring(3, 2), 16),
                Convert.ToInt32(hex.Substring(5, 2), 16));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>新建/重命名/关键词 共用的单字段(或双字段)小表单。</summary>
    private sealed class CategoryInputDialog : Form
    {
        private readonly TextBox _name = new();
        private readonly TextBox? _kw;
        private readonly Label _error = new() { ForeColor = Color.Firebrick, AutoSize = true };
        private readonly bool _withKeyword;

        public string Value => _name.Text.Trim();
        public string? Keyword => _withKeyword ? _kw!.Text.Trim() : null;

        public CategoryInputDialog(string title, string mainLabel, string initialValue,
            bool withKeyword, string okText, string? kwLabel = null)
        {
            _withKeyword = withKeyword;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(380, withKeyword ? 160 : 128);

            const int xl = 18, xf = 96, wf = 270;
            _name.Location = new Point(xf, 18);
            _name.Width = wf;
            _name.Text = initialValue;

            var nameLabel = new Label { Text = mainLabel, Location = new Point(xl, 21), AutoSize = true };
            Controls.Add(nameLabel);
            Controls.Add(_name);

            if (withKeyword)
            {
                _kw = new TextBox { Location = new Point(xf, 58), Width = wf };
                var kwLabelCtrl = new Label { Text = kwLabel ?? "关键词", Location = new Point(xl, 61), AutoSize = true };
                Controls.Add(kwLabelCtrl);
                Controls.Add(_kw);
            }

            var ok = new Button { Text = okText, Width = 84, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel };
            ok.Location = new Point(ClientSize.Width - 18 - 84 * 2 - 8, withKeyword ? 106 : 84);
            cancel.Location = new Point(ClientSize.Width - 18 - 84, withKeyword ? 106 : 84);
            ok.Click += (_, _) => TryAccept();
            AcceptButton = ok;
            CancelButton = cancel;

            _error.Location = new Point(xl, withKeyword ? 130 : 96);
            Controls.Add(ok);
            Controls.Add(cancel);
            Controls.Add(_error);

            _name.TextChanged += (_, _) => _error.Text = string.Empty;
        }

        private void TryAccept()
        {
            if (_name.Text.Trim().Length == 0)
            {
                _error.Text = "请填写名称。";
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>合并目标选择:列出同收支、排除自身与带子分类的顶层分类。</summary>
    private sealed class MergeTargetDialog : Form
    {
        private readonly ComboBox _box = new() { DropDownStyle = ComboBoxStyle.DropDownList };

        public CategoryRow Target { get; private set; } = null!;

        public MergeTargetDialog(List<CategoryRow> targets)
        {
            Text = "合并到…";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(360, 130);

            _box.DataSource = targets;
            _box.DisplayMember = nameof(CategoryRow.Name);
            _box.Location = new Point(20, 22);
            _box.Width = 320;

            var ok = new Button { Text = "确定", Width = 84, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "取消", Width = 84, DialogResult = DialogResult.Cancel };
            ok.Location = new Point(ClientSize.Width - 18 - 84 * 2 - 8, 70);
            cancel.Location = new Point(ClientSize.Width - 18 - 84, 70);
            ok.Click += (_, _) => TryAccept();
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(_box);
            Controls.Add(ok);
            Controls.Add(cancel);
        }

        private void TryAccept()
        {
            if (_box.SelectedItem is not CategoryRow c)
                return;
            Target = c;
            DialogResult = DialogResult.OK;
        }
    }
}
