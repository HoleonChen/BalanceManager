using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZhangDan.App.Views;

/// <summary>周期:列表 + 新建/封存/解除封存(查看流水等后续)。</summary>
internal sealed class PeriodsPage : PageBase
{
    private LedgerSession S => App.Ledger!;
    private readonly ListView _list = new();

    private sealed class Row
    {
        public required PeriodRow P { get; init; }
        public string Name => P.Name;
        public string Range => P.EndDate is null ? $"{Short(P.StartDate)} ~ 长期" : $"{Short(P.StartDate)} ~ {Short(P.EndDate)}";
        public string Status => P.Status == "sealed" ? "已封存(只读)" : "进行中";

        private static string Short(string iso)
        {
            var p = iso.Split('-');
            return $"{p[0]}/{int.Parse(p[1])}/{int.Parse(p[2])}";
        }
    }

    public PeriodsPage()
    {
        var create = new Button { Content = "＋ 新建周期…", MinWidth = 128, Height = 34 };
        create.Click += (_, _) => Create();
        var seal = new Button { Content = "封存所选", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        seal.Click += (_, _) => Seal();
        var unseal = new Button { Content = "解除封存", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        unseal.Click += (_, _) => Unseal();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 10) };
        top.Children.Add(create);
        top.Children.Add(seal);
        top.Children.Add(unseal);

        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "名称", Width = 180, DisplayMemberBinding = Bind("Name") });
        gv.Columns.Add(new GridViewColumn { Header = "起止", Width = 230, DisplayMemberBinding = Bind("Range") });
        gv.Columns.Add(new GridViewColumn { Header = "状态", Width = 130, DisplayMemberBinding = Bind("Status") });
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

    public override void OnShown() => Reload();

    private Row? Selected() => _list.SelectedItem as Row;

    private void Reload()
    {
        var rows = new List<Row>();
        foreach (var p in Periods.ListAll(S))
            rows.Add(new Row { P = p });
        _list.ItemsSource = rows;
    }

    private void Create()
    {
        var dlg = new PeriodCreateDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Periods.Insert(S, dlg.PeriodName, dlg.StartDate, dlg.EndDate);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"建立周期失败:\n{ex.Message}", "周期", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Seal()
    {
        var r = Selected();
        if (r is null || r.P.Status == "sealed")
            return;
        if (r.P.EndDate is null)
        {
            MessageBox.Show("该周期还没有结束日。请先补结束日(或解除后再编辑)再封存,否则会冻结未来日期。",
                "封存周期", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"封存周期「{r.P.Name}」?\n\n封存后该周期内流水只读(可在本页解除)。",
                "封存周期", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        Periods.Seal(S, r.P.Id);
        Reload();
    }

    private void Unseal()
    {
        var r = Selected();
        if (r is null || r.P.Status != "sealed")
            return;
        Periods.Unseal(S, r.P.Id);
        Reload();
    }
}

/// <summary>新建周期小窗。</summary>
internal sealed class PeriodCreateDialog : Window
{
    private readonly TextBox _name = new() { Text = "生活费", Width = 300 };
    private readonly DatePicker _start = new() { Width = 300, SelectedDate = DateTime.Today };
    private readonly CheckBox _endCheck = new() { Content = "计划结束日期", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly DatePicker _end = new() { Width = 180, SelectedDate = DateTime.Today.AddDays(30) };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public string PeriodName => _name.Text.Trim();
    public string StartDate => _start.SelectedDate!.Value.ToString("yyyy-MM-dd");
    public string? EndDate => _endCheck.IsChecked == true ? _end.SelectedDate?.ToString("yyyy-MM-dd") : null;

    public PeriodCreateDialog()
    {
        Title = "新建记账周期";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        _endCheck.Checked += (_, _) => _end.IsEnabled = true;
        _endCheck.Unchecked += (_, _) => _end.IsEnabled = false;

        var endRow = new DockPanel();
        DockPanel.SetDock(_endCheck, Dock.Left);
        endRow.Children.Add(_endCheck);
        endRow.Children.Add(_end);

        var ok = new Button { Content = "建立", Width = 96, Height = 34, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Width = 96, Height = 34, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Field("名称", _name));
        panel.Children.Add(Field("开始日期", _start));
        panel.Children.Add(Field("结束", endRow));
        panel.Children.Add(_error);
        panel.Children.Add(row);
        Content = panel;
    }

    private static UIElement Field(string label, UIElement input)
    {
        var text = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(text, Dock.Left);
        d.Children.Add(text);
        d.Children.Add(input);
        return d;
    }

    private void Accept()
    {
        if (PeriodName.Length == 0)
        {
            _error.Text = "请填写周期名称。";
            return;
        }
        if (_start.SelectedDate is null || (_endCheck.IsChecked == true && _end.SelectedDate is null))
        {
            _error.Text = "请选择完整起止日期。";
            return;
        }
        if (_endCheck.IsChecked == true && _end.SelectedDate!.Value.Date < _start.SelectedDate!.Value.Date)
        {
            _error.Text = "结束日期不能早于开始日期。";
            return;
        }
        DialogResult = true;
    }
}
