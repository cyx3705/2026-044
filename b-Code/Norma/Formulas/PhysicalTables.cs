using System.Globalization;
using System.Reflection;

namespace Norma.Formulas;

// REQ-MATERIAL-003：固化手册物理性能表，运行时不依赖本地手册。
internal static class PhysicalTables
{
    internal sealed record Field(string Key, string Title, string Unit);
    internal sealed record Table(string Key, string Title, int Entry, long ByteOffset, Field[] Fields);
    internal sealed record Cell(string Text, string Unit, double? Value, double? Min, double? Max, string? Undefined);
    internal sealed record Row(string Id, string Material, IReadOnlyDictionary<string, Cell> Properties, string? Note);
    internal sealed record Found(string Table, string Title, string Source, string Scope, int Total, int Offset, int? NextOffset, IReadOnlyList<Row> Items);
    internal static readonly string[] Periods = ["20", "20~100", "20~200", "20~300", "20~400", "20~600", "20~700", "20~900", "70~1000"];
    internal static readonly Table[] Tables =
    [
        new("elastic", "常用材料弹性模量及泊松比", 5, 89444,
            [new("e", "弹性模量 E", "GPa"), new("g", "切变模量 G", "GPa"), new("nu", "泊松比 μ", "")]),
        new("density", "常用材料的密度", 6, 117475, [new("rho", "密度 ρ", "g/cm³（=t/m³）")]),
        new("thermal", "常用材料熔点、热导率及比热容", 8, 157833,
            [new("melting", "熔点", "℃"), new("conductivity", "热导率 λ", "W/(m·K)"), new("specific_heat", "比热容 c", "kJ/(kg·K)")]),
        new("expansion", "常用材料线膨胀系数", 9, 173037,
            Periods.Select(p => new Field(p, "温度范围 " + p + " ℃ 的线膨胀系数 α", "10⁻⁶/℃")).ToArray()),
        new("bulk", "松散物料的堆密度和安息角", 30, 1703354,
            [new("rho_bulk", "堆密度", "t/m³"), new("angle_moving", "运动时安息角", "°"), new("angle_static", "静止时安息角", "°")]),
        new("friction", "材料的滑动摩擦系数", 4, 61704,
            [new("static_dry", "静摩擦/无润滑剂", "1"), new("static_lubricated", "静摩擦/有润滑剂", "1"),
             new("sliding_dry", "滑动摩擦/无润滑剂", "1"), new("sliding_lubricated", "滑动摩擦/有润滑剂", "1")]),
        new("object_friction", "物体的摩擦系数", 32, 2198427, [new("f", "摩擦系数 f", "1")]),
        new("rolling_arm", "滚动摩擦力臂(大约值)", 16, 532283, [new("k", "滚动摩擦力臂 k", "mm")]),
    ];
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<Row>>> Data = new(() =>
        Tables.ToDictionary(t => t.Key, t => Load(t)));

    private static IReadOnlyList<Row> Load(Table table)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Norma.Formulas.physical-" + table.Key + ".tsv")
            ?? throw new InvalidOperationException("缺少物理性能表 " + table.Key);
        using var reader = new StreamReader(stream);
        var rows = new List<Row>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split('\t');
            if (cells.Length != table.Fields.Length + 1) throw new InvalidOperationException("物理性能列数错误 " + table.Key);
            var properties = table.Fields.Select((f, i) => (f.Key, CellValue(cells[i + 1], f.Unit))).ToDictionary(p => p.Key, p => p.Item2);
            string? note = cells[0].StartsWith("未辨材料", StringComparison.Ordinal)
                ? "源页材料名缺损，保留可辨数值，不推定其材料身份。" : null;
            if (table.Key == "elastic" && cells[0] is "玻璃" or "有机玻璃" or "硬聚氟乙烯")
                note = "按原表保留名称与数值，存在疑似源表错字/异常值；未经其他证据确认不自动更正。";
            if (properties.Values.Any(v => v.Undefined == "原表区间倒置，待核"))
                note = "原窗口已核对该倒置区间，保留原文；未确认正确端点，不排序、不猜改，数值返回null。";
            rows.Add(new Row(table.Key + ":" + (rows.Count + 1), cells[0], properties, note));
        }
        return rows;
    }

    internal static Cell CellValue(string text, string unit)
    {
        if (text is "—" or "?")
            return new(text, unit, null, null, null, text == "—" ? "原表未给出" : "源页该值缺损，待核");
        var parts = text.Split('~');
        var min = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var max = parts.Length == 1 ? min : double.Parse(parts[1], CultureInfo.InvariantCulture);
        if (!double.IsFinite(min) || !double.IsFinite(max)) throw new InvalidOperationException("物理性能值错误 " + text);
        if (min > max) return new(text, unit, null, null, null, "原表区间倒置，待核");
        return new(text, unit, min == max ? min : null, min, max, null);
    }

    internal static Found Search(string key, string? query, string? period, int offset, int limit)
    {
        var table = Tables.Single(t => t.Key == key);
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset ≥0，limit 为1～100");
        if (period is not null && (key != "expansion" || !Periods.Contains(period))) throw new ArgumentException("period 必须为原表温度范围：" + string.Join("/", Periods));
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var rows = Data.Value[key].Where(r => terms.All(q => r.Material.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Id.Equals(q, StringComparison.OrdinalIgnoreCase))).ToList();
        var exact = rows.Where(r => r.Material.Equals(query?.Trim(), StringComparison.OrdinalIgnoreCase) || r.Id.Equals(query?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0) rows = exact;
        var items = rows.Skip(offset).Take(limit).Select(r => period is null ? r : r with
        { Properties = r.Properties.Where(p => p.Key == period).ToDictionary(p => p.Key, p => p.Value) }).ToList();
        return new(key, table.Title, $"机械工程师手册 sjzl:{table.Entry}，sjzl/sjzl.ysc，HTML字节偏移{table.ByteOffset}（条目号不是印刷页码）",
            Scope(key), rows.Count, offset,
            offset + items.Count < rows.Count ? offset + items.Count : null, items);
    }

    private static string Scope(string key)
    {
        const string common = "原表参考值；范围保留上下界，不取中值、不插值、不外推。已核对原窗口；—为原表未给出，数值null，不补猜。";
        return common + (key switch
        {
            "friction" => "四列分别为静摩擦/滑动摩擦、有/无润滑剂，系数无量纲（单位1）；不把不同条件混用。材料配对按原文查询。",
            "object_friction" => "名称保留类别/对象/载荷或摩擦状态，空格分词可组合查询；f无量纲（单位1）。制动带保留原表压力条件。离合器0.43~0.4原表倒置，保留text，数值null并说明。",
            "rolling_arm" => "k为滚动摩擦力臂大约值，单位mm，不是无量纲摩擦系数；木材与木材0.5~0.3原表倒置，保留text，数值null并说明。",
            _ => "不按牌号细分。线膨胀系数按原表温区选列，不当作瞬时值；70~1000为原文。单位按属性返回，GPa换为MPa乘1000。",
        });
    }
}
