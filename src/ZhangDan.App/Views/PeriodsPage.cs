using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZhangDan.App.Dialogs;

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
        var edit = new Button { Content = "编辑…", MinWidth = 76, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        edit.Click += (_, _) => EditSelected();
        var seal = new Button { Content = "封存所选", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        seal.Click += (_, _) => Seal();
        var unseal = new Button { Content = "解除封存", MinWidth = 92, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
        unseal.Click += (_, _) => Unseal();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 10) };
        top.Children.Add(create);
        top.Children.Add(edit);
        top.Children.Add(seal);
        top.Children.Add(unseal);

        var menu = new ContextMenu();
        var mEdit = new MenuItem { Header = "编辑周期…" }; mEdit.Click += (_, _) => EditSelected();
        var mSeal = new MenuItem { Header = "封存" }; mSeal.Click += (_, _) => Seal();
        var mUnseal = new MenuItem { Header = "解除封存" }; mUnseal.Click += (_, _) => Unseal();
        menu.Items.Add(mEdit);
        menu.Items.Add(mSeal);
        menu.Items.Add(mUnseal);
        _list.ContextMenu = menu;

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
        var dlg = new PeriodCreateDialog(S, existing: null) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            var pid = Periods.Insert(S, dlg.PeriodName, dlg.StartDate, dlg.EndDate);

            if (dlg.UseInitialIncome)
            {
                Transactions.Add(S, new TxnDraft
                {
                    Date = dlg.StartDate,
                    Direction = "in",
                    AccountId = dlg.IncomeAccountId,
                    CategoryId = dlg.IncomeCategoryId,
                    AmountCents = dlg.IncomeCents,
                    Name = "初始收入",
                    Note = "",
                    Channel = "",
                    InPool = false
                });
            }

            if (dlg.UsePool)
                Pools.Save(S, pid, "生活费", dlg.PoolAccountId, dlg.PoolBudgetCents, dlg.PoolReserveCents);

            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"建立周期失败:\n{ex.Message}", "周期", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditSelected()
    {
        var r = Selected();
        if (r is null)
            return;
        if (r.P.Status == "sealed")
        {
            MessageBox.Show("已封存周期只读,请先解除封存再编辑。", "周期", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new PeriodCreateDialog(S, r.P) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            Periods.Update(S, r.P.Id, dlg.PeriodName, dlg.StartDate, dlg.EndDate);
            // 编辑态资金池:已有池 → 改;无池勾选 → 补建;保存走 upsert
            if (dlg.UsePool)
                Pools.Save(S, r.P.Id, "生活费", dlg.PoolAccountId, dlg.PoolBudgetCents, dlg.PoolReserveCents);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败:\n{ex.Message}", "周期", MessageBoxButton.OK, MessageBoxImage.Error);
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
