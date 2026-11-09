using System.Reflection;

namespace Norma.Formulas;

// REQ-DRIVE-001: reference table, not an efficiency model or a design guarantee.
internal static class DriveEfficiency
{
    internal const string Title = "机械传动效率的概略数值";
    internal const string Source = "机械工程师手册 sjzl:22，sjzl/sjzl.ysc，HTML字节偏移1074586（条目号不是印刷页码）";
    internal const string Scope = "η为无量纲效率（单位1），仅供概略估算；按传动型式、精度、润滑和角度等原表条件查询。范围保留上下界，不取中值、不插值、不外推；不自动相乘或重复计入轴承损耗。原表倒置区间保留text，value/min/max为null并说明原因；疑似错字照录并注明。";
    internal const string ReducerNote = "原表脚注①：滚动轴承的损耗考虑在内。";
    internal sealed record Row(string Id, string Category, string Form, PhysicalTables.Cell Efficiency, string? Note);
    internal sealed record Found(string Source, string Scope, int Total, int Offset, int? NextOffset, IReadOnlyList<Row> Items);
    private static readonly Lazy<IReadOnlyList<Row>> Data = new(Load);

    private static IReadOnlyList<Row> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Norma.Formulas.drive-efficiency.tsv")
            ?? throw new InvalidOperationException("缺少传动效率表");
        using var reader = new StreamReader(stream);
        var rows = new List<Row>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split('\t');
            if (cells.Length is < 3 or > 4) throw new InvalidOperationException("传动效率表列数错误");
            var efficiency = PhysicalTables.CellValue(cells[2], "1");
            if (efficiency.Undefined is null && (efficiency.Min <= 0 || efficiency.Max > 1))
                throw new InvalidOperationException("传动效率必须在(0,1]内");
            var notes = new List<string>();
            if (cells.Length == 4) notes.Add(cells[3]);
            if (efficiency.Undefined is not null) notes.Add("原窗口确认该区间倒置；正确端点未确认，不排序、不猜改。");
            if (cells[0] == "减(变)速器") notes.Add(ReducerNote);
            rows.Add(new("efficiency:" + (rows.Count + 1), cells[0], cells[1], efficiency,
                notes.Count == 0 ? null : string.Join(" ", notes)));
        }
        return rows;
    }

    internal static Found Search(string? query, int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0，limit为1～100");
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var rows = Data.Value.Where(row => terms.All(term =>
            (row.Category + "/" + row.Form).Contains(term, StringComparison.OrdinalIgnoreCase) || row.Id.Equals(term, StringComparison.OrdinalIgnoreCase))).ToList();
        var exact = rows.Where(row => row.Id.Equals(query?.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.Category.Equals(query?.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.Form.Equals(query?.Trim(), StringComparison.OrdinalIgnoreCase)
            || (row.Category + "/" + row.Form).Equals(query?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0) rows = exact;
        var items = rows.Skip(offset).Take(limit).ToList();
        return new(Source, Scope, rows.Count, offset, offset + items.Count < rows.Count ? offset + items.Count : null, items);
    }
}
