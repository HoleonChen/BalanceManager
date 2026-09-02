using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ZhangDan.App.Views;

/// <summary>分类管理:支出/收入切换;新建、删除(用中禁止删)。</summary>
internal sealed class CategoriesPage : PageBase
{
    private LedgerSession S => App.Ledger!;
    private readonly RadioButton _outRadio = new() { Content = "支出分类", IsChecked = true };
    private readonly RadioButton _inRadio = new() { Content = "收入分类", Margin = new Thickness(14, 0, 0, 0) };
    private readonly ListView _list = new();

    private sealed class Row
    {
        public required CategoryRow C { get; init; }
        public string Name => C.Name;
        public string Keyword { get; init; } = "";
        public Brush Vivid => ParseHex(C.Color) ?? Brushes.Transparent;
        public Brush Light => Vivid is SolidColorBrush b ? new SolidColorBrush(Lighten(b.Color)) : Brushes.Transparent;

        private static Brush? ParseHex(string? hex)
        {
            if (hex is null || hex.Length != 7 || hex[0] != '#')
                return null;
            try
            {
                return new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16)));
            }
            catch
            {
                return null;
            }
        }

        private static System.Windows.Media.Color Lighten(System.Windows.Media.Color c)
        {
            byte Mix(byte v) => (byte)(v + (255 - v) * 0.6);
            return System.Windows.Media.Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
        }
    }

    public CategoriesPage()
    {
        _outRadio.Checked += (_, _) => Reload();
        _inRadio.Checked += (_, _) => Reload();

        var create = new Button { Content = "＋ 新建分类…", MinWidth = 128, Height = 34 };
        create.Click += (_, _) => Create();
        var edit = new Button { Content = "编辑…", MinWidth = 76, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        edit.Click += (_, _) => EditSelected();
        var del = new Button { Content = "删除所选", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        del.Click += (_, _) => Delete();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 10) };
        top.Children.Add(_outRadio);
        top.Children.Add(_inRadio);
        top.Children.Add(create);
        top.Children.Add(edit);
        top.Children.Add(del);

        var menu = new ContextMenu();
        var mEdit = new MenuItem { Header = "编辑…" }; mEdit.Click += (_, _) => EditSelected();
        var mUp = new MenuItem { Header = "上移" }; mUp.Click += (_, _) => MoveSelected(true);
        var mDown = new MenuItem { Header = "下移" }; mDown.Click += (_, _) => MoveSelected(false);
        var mDel = new MenuItem { Header = "删除" }; mDel.Click += (_, _) => Delete();
        menu.Items.Add(mEdit);
        menu.Items.Add(new Separator());
        menu.Items.Add(mUp);
        menu.Items.Add(mDown);
        menu.Items.Add(new Separator());
        menu.Items.Add(mDel);
        _list.ContextMenu = menu;

        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "名称", Width = 180, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new GridViewColumn { Header = "颜色", Width = 96, CellTemplate = SwatchTemplate() });
        gv.Columns.Add(new GridViewColumn { Header = "关键词(导入归类)", Width = 240, DisplayMemberBinding = Bind("Keyword") });
        _list.View = gv;
        _list.Margin = new Thickness(20, 0, 20, 12);
        _list.SelectionMode = SelectionMode.Single;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(top, 0);
        Grid.SetRow(_list, 1);
        grid.Children.Add(top);
        grid.Children.Add(_list);
        Content = grid;
    }

    private static System.Windows.Data.Binding Bind(string p) => new(p) { Mode = System.Windows.Data.BindingMode.OneWay };

    /// <summary>颜色列:两个小色块 = 鲜艳 + 自动淡色,不看 hex 值。</summary>
    private static DataTemplate SwatchTemplate()
    {
        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        sp.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
        var vivid = new FrameworkElementFactory(typeof(Border));
        vivid.SetValue(Border.WidthProperty, 26.0);
        vivid.SetValue(Border.HeightProperty, 14.0);
        vivid.SetValue(Border.MarginProperty, new Thickness(0, 0, 4, 0));
        vivid.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        vivid.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Vivid"));
        sp.AppendChild(vivid);
        var light = new FrameworkElementFactory(typeof(Border));
        light.SetValue(Border.WidthProperty, 26.0);
        light.SetValue(Border.HeightProperty, 14.0);
        light.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        light.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Light"));
        sp.AppendChild(light);
        return new DataTemplate { VisualTree = sp };
    }
    private bool Income => _inRadio.IsChecked == true;
    private Row? Selected() => _list.SelectedItem as Row;

    public override void OnShown() => Reload();

    private void Reload()
    {
        var rows = new List<Row>();
        foreach (var c in Categories.ListManual(S, Income))
            rows.Add(new Row { C = c, Keyword = KeywordOf(c) });
        _list.ItemsSource = rows;
    }

    private string KeywordOf(CategoryRow c)
    {
        using var cmd = S.Connection.CreateCommand();
        cmd.CommandText = "SELECT keyword FROM categories WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", c.Id);
        return cmd.ExecuteScalar() as string ?? "";
    }

    private void Create()
    {
        var dlg = new CategoryCreateDialog(income: Income) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Categories.Insert(S, dlg.CategoryName, Income, dlg.Color, dlg.Keyword);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"新建分类失败:\n{ex.Message}", "分类", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditSelected()
    {
        var r = Selected();
        if (r is null)
            return;
        var dlg = new CategoryCreateDialog(income: Income, name: r.C.Name, keyword: r.Keyword, color: r.C.Color)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Categories.Rename(S, r.C.Id, dlg.CategoryName);
            Categories.SetKeyword(S, r.C.Id, dlg.Keyword);
            Categories.SetColor(S, r.C.Id, dlg.Color);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败:\n{ex.Message}", "分类", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MoveSelected(bool up)
    {
        var r = Selected();
        if (r is null)
            return;
        Categories.Move(S, r.C.Id, up);
        Reload();
    }

    private void Delete()
    {
        var r = Selected();
        if (r is null)
            return;
        var used = Categories.UsedCount(S, r.C.Id);
        if (used > 0)
        {
            MessageBox.Show($"「{r.C.Name}」仍被 {used} 笔流水使用,不能直接删除。\n请先把这些流水合并/改到别的分类。",
                "删除分类", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"删除分类「{r.C.Name}」?", "删除分类",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            Categories.Delete(S, r.C.Id);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败:\n{ex.Message}", "分类", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

/// <summary>新建/编辑分类小窗(名称/关键词/颜色)。颜色=预设鲜艳色组(点选)+ 预览;淡色由系统自动派生。</summary>
internal sealed class CategoryCreateDialog : Window
{
    private static readonly string[] Palette =
    {
        "#F06292", "#EC407A", "#D81B60", "#42A5F5", "#29B6F6", "#26C6DA",
        "#26A69A", "#66BB6A", "#9CCC65", "#FFA726", "#EF6C00", "#FFB300",
        "#8E24AA", "#7E57C2", "#5C6BC0", "#8D6E63", "#9E9E9E", "#26A69A"
    };

    private readonly TextBox _name = new() { Width = 300 };
    private readonly TextBox _keyword = new() { Width = 300 };
    private readonly TextBox _color = new() { Width = 110 };
    private readonly Border _vividPreview = NewPreview();
    private readonly Border _lightPreview = NewPreview();
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public string CategoryName => _name.Text.Trim();
    public string Keyword => _keyword.Text.Trim();
    public string? Color => _color.Text.Trim().Length == 0 ? null : _color.Text.Trim();

    public CategoryCreateDialog(bool income, string? name = null, string? keyword = null, string? color = null)
    {
        Title = income ? (name is null ? "新建收入分类" : "编辑收入分类") : (name is null ? "新建支出分类" : "编辑支出分类");
        Width = 540;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _name.Text = name ?? "";
        _keyword.Text = keyword ?? "";
        _color.Text = color ?? Palette[0];
        _color.TextChanged += (_, _) => RefreshPreview();
        RefreshPreview();

        // 预设色块:点选即设色
        var swatches = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var hex in Palette)
        {
            var b = new Button
            {
                Content = "",
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                Background = Parse(hex),
                BorderBrush = Brushes.Transparent,
                ToolTip = hex
            };
            b.Click += (_, _) => { _color.Text = hex; };
            swatches.Children.Add(b);
        }

        var colorRow = new StackPanel();
        colorRow.Children.Add(swatches);
        var editRow = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var pair = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pair.Children.Add(_vividPreview);
        pair.Children.Add(new TextBlock { Text = " + ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        pair.Children.Add(_lightPreview);
        pair.Children.Add(new TextBlock { Text = "(鲜艳+淡)", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        DockPanel.SetDock(pair, Dock.Right);
        DockPanel.SetDock(_color, Dock.Left);
        editRow.Children.Add(_color);
        editRow.Children.Add(pair);
        colorRow.Children.Add(editRow);

        var ok = new Button { Content = name is null ? "创建" : "保存", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Field("名称", _name));
        panel.Children.Add(Field("关键词(可空)", _keyword));
        panel.Children.Add(new TextBlock { Text = "颜色(预设点选 / 可手填 #RRGGBB)", Margin = new Thickness(0, 4, 0, 2) });
        panel.Children.Add(colorRow);
        panel.Children.Add(_error);
        panel.Children.Add(row);
        Content = panel;
    }

    private static Border NewPreview() => new()
    {
        Width = 56,
        Height = 22,
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void RefreshPreview()
    {
        var hex = _color.Text.Trim();
        _vividPreview.Background = Parse(hex);
        _lightPreview.Background = Parse(hex) is SolidColorBrush b ? Lighten(b.Color) : null;
    }

    /// <summary>淡色 = 鲜艳色与白色按 60% 混合(自动派生,不另存)。</summary>
    private static Brush Lighten(Color c)
    {
        byte Mix(byte v) => (byte)(v + (255 - v) * 0.6);
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)));
    }

    private static Brush? Parse(string hex)
    {
        try
        {
            return hex.Length == 7 && hex[0] == '#'
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16)))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static UIElement Field(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(text, Dock.Left);
        d.Children.Add(text);
        d.Children.Add(input);
        return d;
    }

    private void Accept()
    {
        if (CategoryName.Length == 0)
        {
            _error.Text = "请填写名称。";
            return;
        }
        var c = Color;
        if (c is not null && (c.Length != 7 || c[0] != '#'))
        {
            _error.Text = "颜色请填 #RRGGBB 格式,或留空(留空则以后自动配色)。";
            return;
        }
        DialogResult = true;
    }
}
