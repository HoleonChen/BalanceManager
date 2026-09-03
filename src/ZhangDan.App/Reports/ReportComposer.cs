using System;
using System.Collections.Generic;
using System.Linq;
using ZhangDan;
using Reports = ZhangDan.Reports;

namespace ZhangDan.App.Reporting;

/// <summary>一张报表表格(供 PDF/xlsx 共用同一份内容)。</summary>
internal sealed record ReportSheet(string Title, IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows);

/// <summary>组合好的整份报表内容(各块表格 + 可选堆叠面积图 PNG)。</summary>
internal sealed class ReportContent
{
    public List<ReportSheet> Sheets { get; } = new();
    public byte[]? TrendPng { get; set; }
    public byte[]? SharePng { get; set; }

    /// <summary>趋势图横轴各列对应周期名(与图内 P0…Pn 对齐)。</summary>
    public List<string> TrendPeriodMap { get; } = new();

    /// <summary>趋势图各彩色带 = 支出分类(名称 + 色,供 PDF 图例色块表)。</summary>
    public List<(string Name, string Hex)> TrendCats { get; } = new();
}

/// <summary>
/// 把「请求 + Core 聚合」组合成报表内容(与渲染分离;PDF/xlsx/图都从这里取数)。
/// </summary>
internal static class ReportComposer
{
    public static ReportContent Compose(LedgerSession s, ReportRequest req)
    {
        var content = new ReportContent();
        var scope = BuildScope(req);

        // 统一「范围窗口」(日期)供账户块/日均;Periods 内部会取实际日期
        var (from, to) = ScopeWindowText(s, req, scope);

        if (req.BlockOverview)
            content.Sheets.Add(OverviewSheet(s, req, scope));
        if (req.BlockShare)
            AddShare(content, s, scope);
        if (req.BlockTrend)
            AddTrend(content, s, req, scope);
        if (req.BlockAccounts)
            content.Sheets.Add(AccountsSheet(s, from, to));
        if (req.BlockTop)
        {
            content.Sheets.Add(TopSheet(s, scope, income: false, "大额支出 TOP"));
            content.Sheets.Add(TopSheet(s, scope, income: true, "大额收入 TOP"));
        }
        if (req.BlockDaily)
            content.Sheets.Add(DailySheet(s, scope));
        if (req.BlockTransfer)
            content.Sheets.Add(TransferSheet(s, scope));
        if (req.BlockPool && req.Kind == ReportRangeKind.Period && req.PeriodIds is { Length: 1 })
        {
            if (PoolSheet(s, req.PeriodIds[0]) is { } poolSheet)
                content.Sheets.Add(poolSheet);
        }
        if (req.Kind == ReportRangeKind.Compare)
            content.Sheets.Insert(0, CompareSheet(s, req, scope));

        content.Sheets.Add(NoteSheet(req));
        return content;
    }

    public static ZhangDan.Reports.Scope BuildScope(ReportRequest req)
    {
        if (req.Kind == ReportRangeKind.Custom)
            return new ZhangDan.Reports.Scope { Kind = ZhangDan.Reports.ScopeKind.Range, Start = req.Start, End = req.End };
        return new ZhangDan.Reports.Scope { Kind = ZhangDan.Reports.ScopeKind.Periods, PeriodIds = req.PeriodIds! };
    }

    private static (string From, string To) ScopeWindowText(LedgerSession s, ReportRequest req, ZhangDan.Reports.Scope scope)
    {
        if (req.Kind == ReportRangeKind.Custom)
            return (req.Start!, req.End!);
        long[] ids = req.PeriodIds!;
        var periods = ids.Select(id => Periods.Get(s, id)).Where(p => p is not null).Cast<PeriodRow>().ToList();
        string f = periods.Min(p => p.StartDate) ?? DateTime.Today.ToString("yyyy-MM-dd");
        string t = periods.Max(p => p.EndDate ?? DateTime.Today.ToString("yyyy-MM-dd"))
                    ?? DateTime.Today.ToString("yyyy-MM-dd");
        return (f, t);
    }

    // ---------- 各块 ----------

    private static ReportSheet OverviewSheet(LedgerSession s, ReportRequest req, ZhangDan.Reports.Scope scope)
    {
        var o = Reports.Overview(s, scope);
        string Avg(long outCents, int days) => days <= 0 ? "-" : Fmt(outCents / (long)days);
        var rows = new List<string[]>
        {
            new[] { "区间支出", Fmt(o.OutCents) },
            new[] { "区间收入", Fmt(o.InCents) },
            new[] { "结余", Fmt(o.InCents - o.OutCents) },
            new[] { "日均支出", Avg(o.OutCents, o.Days) },
            new[] { "校准/差额调整合计(不含用途占比)", $"{Fmt(o.AdjOutCents)}(支) / {Fmt(o.AdjInCents)}(收)" }
        };
        return new ReportSheet("总览", new[] { "指标", "金额(元)" }, rows);
    }

    /// <summary>占比表 + 同步生成饼图 PNG;类别按金额降序、未归类垫底,表/饼同序。</summary>
    private static void AddShare(ReportContent content, LedgerSession s, ZhangDan.Reports.Scope scope)
    {
        var share = Reports.ExpenseShare(s, scope)
            .OrderBy(r => r.IsUnassigned ? 1 : 0)
            .ThenByDescending(r => r.Cents)
            .ToList();
        long total = share.Sum(r => r.Cents);
        var rows = share.Select(r => new[]
        {
            r.Name,
            r.IsUnassigned ? "(未归类)" : "",
            Fmt(r.Cents),
            total > 0 ? (r.Cents * 100.0 / total).ToString("0.0") + "%" : "-"
        }).ToList();
        content.Sheets.Add(new ReportSheet("支出分类占比", new[] { "分类", "说明", "金额(元)", "占支出比" }, rows));

        var slices = share.Where(r => r.Cents > 0)
                          .Select(r => (r.Name, r.Color, (double)r.Cents)).ToList();
        if (slices.Count > 0)
            content.SharePng = ReportCharts.Pie(slices);
    }

    private static void AddTrend(ReportContent content, LedgerSession s, ReportRequest req, ZhangDan.Reports.Scope scope)
    {
        long[] ids = req.Kind == ReportRangeKind.Compare
            ? req.PeriodIds!
            : TrendingIds(s, req);
        var cols = Reports.StackColumns(s, ids);

        // 表格:每周期一行
        var rows = cols.Select(c => new[]
        {
            c.Name,
            Fmt(c.InCents),
            Fmt(c.OutCents),
            Fmt(c.InCents - c.OutCents),
            Fmt(c.AdjOutCents)
        }).ToList();
        content.Sheets.Add(new ReportSheet("跨周期趋势", new[] { "周期", "收入", "支出", "结余", "校准(支)" }, rows));

        // 图:堆叠面积;类别统一序 = 合计金额降序(未归类垫底),表/饼/堆叠/图例同序。图内无中文。
        var union = UnionAxis(s, cols);
        content.TrendPeriodMap.Clear();
        foreach (var c in cols) content.TrendPeriodMap.Add(c.Name);
        content.TrendCats.Clear();
        if (union.Count > 0 && cols.Count > 0)
        {
            var cents = new double[union.Count, cols.Count];
            for (int j = 0; j < cols.Count; j++)
            {
                var byName = cols[j].Bands.ToDictionary(b => b.Name, b => (double)b.Cents);
                for (int i = 0; i < union.Count; i++)
                    cents[i, j] = byName.GetValueOrDefault(union[i].Name);
            }
            // 统一序:合计金额降序,未归类垫底
            double Grand(int i)
            {
                double s = 0;
                for (int j = 0; j < cols.Count; j++) s += cents[i, j];
                return s;
            }
            var orderIdx = Enumerable.Range(0, union.Count).ToList();
            orderIdx.Sort((a, b) => CompareCat(union[a].Name, Grand(a), union[b].Name, Grand(b)));
            var orderedUnion = orderIdx.Select(i => union[i]).ToList();
            var orderedCents = new double[orderIdx.Count, cols.Count];
            for (int k = 0; k < orderIdx.Count; k++)
                for (int j = 0; j < cols.Count; j++)
                    orderedCents[k, j] = cents[orderIdx[k], j];

            content.TrendCats.AddRange(orderedUnion);
            content.TrendPng = ReportCharts.StackedArea(cols.Count, orderedUnion.Select(u => u.Hex).ToList(), orderedCents, req.PercentMode);
        }
        else
        {
            content.TrendCats.AddRange(union);
        }
    }

    private static int CompareCat(string nameA, double grandA, string nameB, double grandB)
    {
        int ua = nameA == "未归类" ? 1 : 0;
        int ub = nameB == "未归类" ? 1 : 0;
        if (ua != ub) return ua - ub;          // 未归类垫底
        return grandB.CompareTo(grandA);        // 其余金额降序
    }

    /// <summary>单周期:自动带上相邻几个周期作上下文(前后各 1);对比/多选直接用所选。</summary>
    private static long[] TrendingIds(LedgerSession s, ReportRequest req)
    {
        var all = Periods.ListAll(s).OrderBy(p => p.StartDate).ToList();
        if (req.Kind == ReportRangeKind.Period && req.PeriodIds is { Length: 1 })
        {
            int idx = all.FindIndex(p => p.Id == req.PeriodIds![0]);
            var list = new List<long>();
            if (idx > 0) list.Add(all[idx - 1].Id);
            list.Add(req.PeriodIds[0]);
            if (idx >= 0 && idx + 1 < all.Count) list.Add(all[idx + 1].Id);
            return list.ToArray();
        }
        return req.PeriodIds ?? Array.Empty<long>();
    }

    private static List<(string Name, string Hex)> UnionAxis(LedgerSession s, IReadOnlyList<ZhangDan.Reports.PeriodColumn> cols)
    {
        var order = new List<(string, string)>();
        var seen = new HashSet<string>();
        foreach (var c in Categories.ListManual(s, income: false))
        {
            var hex = c.Color ?? "#9E9E9E";
            if (seen.Add(c.Name))
                order.Add((c.Name, hex));
        }
        foreach (var col in cols)
        {
            foreach (var b in col.Bands)
            {
                if (seen.Add(b.Name))
                    order.Add((b.Name, b.Color));
            }
        }
        return order;
    }

    private static ReportSheet AccountsSheet(LedgerSession s, string from, string to)
    {
        var rows = new List<string[]>();
        long net = 0;
        foreach (var a in Reports.AccountsBlock(s, from, to))
        {
            net += a.Enabled ? a.BookCents : 0;
            rows.Add(new[]
            {
                a.Name,
                TypeLabelShort(a.TypeKey),
                a.Enabled ? "" : "(停用)",
                Fmt(a.BookCents),
                Fmt(a.NetInWindowCents)
            });
        }
        rows.Add(new[] { "净资产合计(启用账户)", "", "", Fmt(net), "" });
        return new ReportSheet("账户与净资产", new[] { "账户", "类型", "状态", "期末账面(元)", "窗口净变动(元)" }, rows);
    }

    private static string TypeLabelShort(string key) => key switch
    {
        "wallet" => "钱包", "money_fund" => "货基", "bank" => "银行卡", "cash" => "现金",
        "fixed_deposit" => "定存", "fund" => "基金", "prepaid" => "储值卡",
        "credit_card" => "信用卡(负债)", "hua_bei" => "花呗(负债)", "bai_tiao" => "白条(负债)",
        "jin_tiao" => "金条(负债)", "credit" => "其他信用/负债", _ => key
    };

    private static ReportSheet TopSheet(LedgerSession s, ZhangDan.Reports.Scope scope, bool income, string title)
    {
        var rows = Reports.Top(s, scope, income, 20)
            .Select(t => new[] { t.Name, Fmt(t.AmountCents), t.Date, t.Category }).ToList();
        return new ReportSheet(title, new[] { "名称", "金额(元)", "日期", "分类" }, rows);
    }

    private static ReportSheet DailySheet(LedgerSession s, ZhangDan.Reports.Scope scope)
    {
        var rows = Reports.Daily(s, scope)
            .Select(d => new[] { d.Date, Fmt(d.OutCents), Fmt(d.InCents), Fmt(d.CumNetCents) }).ToList();
        return new ReportSheet("每日收支", new[] { "日期", "支出(元)", "收入(元)", "累计净额(元)" }, rows);
    }

    private static ReportSheet TransferSheet(LedgerSession s, ZhangDan.Reports.Scope scope)
    {
        var rows = Reports.TransferSummary(s, scope)
            .Select(t => new[] { t.Kind.Length == 0 ? "(未分类)" : t.Kind, t.Count.ToString(), Fmt(t.PrincipalCents), Fmt(t.DeltaNetCents) })
            .ToList();
        return new ReportSheet("转账汇总", new[] { "类别", "笔数", "本金合计(元)", "浮动净额(元)" }, rows);
    }

    private static ReportSheet? PoolSheet(LedgerSession s, long periodId)
    {
        var pool = Pools.Get(s, periodId);
        if (pool is null)
            return null;
        var st = Pools.State(s, pool);
        var rows = new List<string[]>
        {
            new[] { "资金池", pool.Name },
            new[] { "预算(池大小)", Fmt(st.BudgetCents) },
            new[] { "已花", Fmt(st.SpentCents) },
            new[] { "剩余", Fmt(st.RemainingCents) },
            new[] { "预计保留", Fmt(st.ReserveCents) },
            new[] { "可自由支配", Fmt(st.DisposableCents) }
        };
        foreach (var ri in Pools.ReserveItems(s, pool.Id))
            rows.Add(new[] { $"预留:{ri.Item}" + (ri.Due is null ? "" : $"({ri.Due})"), Fmt(ri.AmountCents) });
        return new ReportSheet("资金池", new[] { "项", "金额(元)" }, rows);
    }

    /// <summary>对比块:指标×周期 + 结余/日均 环比 + 各期末净资产重建。</summary>
    private static ReportSheet CompareSheet(LedgerSession s, ReportRequest req, ZhangDan.Reports.Scope scope)
    {
        long[] ids = req.PeriodIds!;
        var periods = ids.Select(id => Periods.Get(s, id)).Where(p => p is not null).Cast<PeriodRow>().ToList();
        var cols = Reports.StackColumns(s, ids);
        var scopes = ids.Select(id => new ZhangDan.Reports.Scope { Kind = ZhangDan.Reports.ScopeKind.Periods, PeriodIds = new[] { id } }).ToList();
        var overviews = scopes.Select(sc => Reports.Overview(s, sc)).ToList();

        var metricRows = new List<string[]>();
        AddCompareRow(metricRows, "收入(元)", overviews.Select(o => (double)o.InCents).ToList());
        AddCompareRow(metricRows, "支出(元)", overviews.Select(o => (double)o.OutCents).ToList());
        AddCompareRow(metricRows, "结余(元)", overviews.Select((o, i) => (double)(o.InCents - o.OutCents)).ToList());
        AddCompareRow(metricRows, "日均支出(元)", overviews.Select((o, i) => o.Days > 0 ? o.OutCents / (double)o.Days : 0).ToList());

        var headers = new List<string> { "指标" };
        headers.AddRange(periods.Select(p => p.Name));
        headers.Add("环比结余");
        // 净资产:各期末
        var netRow = new List<string> { "期末净资产(元)" };
        foreach (var p in periods)
        {
            var end = p.EndDate ?? DateTime.Today.ToString("yyyy-MM-dd");
            netRow.Add(Fmt(Reports.NetAssetsAt(s, end)));
        }
        netRow.Add("");
        metricRows.Add(netRow.ToArray());
        return new ReportSheet("周期对比", headers, metricRows);
    }

    private static void AddCompareRow(List<string[]> rows, string label, List<double> values)
    {
        var r = new List<string> { label };
        double? prev = null;
        string? delta = null;
        foreach (var v in values)
        {
            r.Add(v.ToString("N2"));
            if (prev is not null && prev != 0)
                delta = ((v - prev.Value) / prev.Value * 100).ToString("0.0") + "%";
            prev = v;
        }
        while (r.Count < 2) r.Add("");
        delta ??= "";
        r.Add(delta);
        rows.Add(r.ToArray());
    }

    private static ReportSheet NoteSheet(ReportRequest req)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("范围:").Append(req.ScopeLabel);
        sb.Append("\n生成时间:").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.Append("\n口径:仅计正常流水(作废/退款不计);转账不计收支;差额调整/校准不计入用途占比、总览单列;未归类分类单列。");
        sb.Append("\n周期报表不含未归属空档账;自定义日期范围含空档。");
        sb.Append("\n账户与净资产:账面/净资产按报表期末重建(基准+截至期末净变动),非当前快照;停用账户不计净资产。");
        return new ReportSheet("说明", new[] { "说明" },
            sb.ToString().Split('\n').Select(x => new[] { x }).ToList());
    }

    public static string Fmt(long cents) => (cents / 100m).ToString("N2");
}
