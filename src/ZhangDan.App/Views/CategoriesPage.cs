using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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
        public string Color => C.Color ?? "—";
        public string Keyword { get; init; } = "";
    }

    public CategoriesPage()
    {
        _outRadio.Checked += (_, _) => Reload();
        _inRadio.Checked += (_, _) => Reload();

        var create = new Button { Content = "＋ 新建分类…", MinWidth = 128, Height = 34 };
        create.Click += (_, _) => Create();
        var del = new Button { Content = "删除所选", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        del.Click += (_, _) => Delete();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 10) };
        top.Children.Add(_outRadio);
        top.Children.Add(_inRadio);
        top.Children.Add(create);
        top.Children.Add(del);

        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "名称", Width = 180, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new GridViewColumn { Header = "颜色", Width = 90, DisplayMemberBinding = Bind("Color") });
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
        var dlg = new CategoryCreateDialog(Income) { Owner = Window.GetWindow(this) };
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

/// <summary>新建分类小窗(名称/关键词/可选颜色)。</summary>
internal sealed class CategoryCreateDialog : Window
{
    private static readonly string[] Palette =
    {
        "#F06292", "#42A5F5", "#FFA726", "#8E24AA", "#29B6F6", "#66BB6A",
        "#5C6BC0", "#9E9E9E", "#EC407A", "#26A69A", "#EF6C00", "#7E57C2"
    };

    private readonly TextBox _name = new() { Width = 300 };
    private readonly TextBox _keyword = new() { Width = 300 };
    private readonly TextBox _color = new() { Width = 120 };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public string CategoryName => _name.Text.Trim();
    public string Keyword => _keyword.Text.Trim();
    public string? Color => _color.Text.Trim().Length == 0 ? null : _color.Text.Trim();

    public CategoryCreateDialog(bool income)
    {
        Title = income ? "新建收入分类" : "新建支出分类";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        _color.Text = Palette[0];

        var ok = new Button { Content = "创建", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Field("名称", _name));
        panel.Children.Add(Field("关键词(可空)", _keyword));
        panel.Children.Add(Field("颜色(#RRGGBB)", _color));
        panel.Children.Add(_error);
        panel.Children.Add(row);
        Content = panel;
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
            _error.Text = "颜色请填 #RRGGBB 格式,或留空。";
            return;
        }
        DialogResult = true;
    }
}
