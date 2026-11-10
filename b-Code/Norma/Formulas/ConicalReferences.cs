using System.Globalization;
using System.Text.Json;

namespace Norma.Formulas;

// REQ-BASIC-024: diameter/IT facts and geometric-key factors extend existing cards.
internal static class ConicalReferences
{
    internal sealed record Cell(string Raw, int Row, int Column, int Rowspan, int Colspan, string[] Images);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, Cell[][] Table);
    internal sealed record Fact(string Key, string Property, string Page, int Row, int Column, string Raw, double Value,
        double Lower, double Upper, int KeyRow, int KeyColumn);
    private sealed record Data(Page[] Pages, Fact[] Facts);
    internal static readonly string[] ToleranceProperties = ["圆锥最大角误差", "圆锥轴向公差"];
    internal const string FactorProperty = "圆锥换算系数";
    private static readonly Lazy<Data> Loaded = new(() =>
    {
        using var stream = typeof(ConicalReferences).Assembly.GetManifestResourceStream("Norma.Formulas.conical-references.json")
            ?? throw new InvalidOperationException("缺少圆锥参考表");
        var d = JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (d.Pages.Length != 4 || d.Facts.Length != 426 || d.Facts.Count(f => f.Property == FactorProperty) != 36
            || d.Pages.Select(p => p.Id).SequenceEqual(new[] { "13-02:56", "13-02:59", "13-02:60", "13-02:61" }) == false)
            throw new InvalidOperationException("圆锥参考表不完整");
        foreach (var f in d.Facts)
        {
            var p = d.Pages.Single(p => p.Id == f.Page);
            if (p.Table[f.Row][f.Column].Raw != f.Raw || double.Parse(f.Raw.Replace(" ", ""), CultureInfo.InvariantCulture) != f.Value
                || f.Property != FactorProperty && (p.Table[f.KeyRow][f.KeyColumn].Raw != f.Key || f.Lower >= f.Upper))
                throw new InvalidOperationException("圆锥参考事实与原格不一致");
        }
        return d;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    internal static IReadOnlyList<Fact> Facts => Loaded.Value.Facts;
    internal static bool Matches(string standard) => BandLimits.Matches(Pages[0].Standard, standard);
    internal static bool HasTolerance(string grade, string property = "") => Facts.Any(f => f.Key == grade
        && ToleranceProperties.Contains(f.Property) && (property.Length == 0 || property == "*" || f.Property == property));
    private static object Value(Fact f)
    {
        var p = Pages.Single(p => p.Id == f.Page);
        return new { value = f.Value, unit = f.Property == "圆锥最大角误差" ? "μrad" : f.Property == "圆锥轴向公差" ? "mm" : "1", raw = f.Raw,
            conditions = f.Property == "圆锥最大角误差" ? new Dictionary<string, object> { ["圆锥长度mm"] = 100 }
                : new Dictionary<string, object> { ["基准锥度"] = "1:10" },
            source = new { id = p.Id, byteOffset = p.ByteOffset, standard = p.Standard, title = p.Title,
                row = f.Row, column = f.Column, keyRow = f.KeyRow, keyColumn = f.KeyColumn },
            notes = f.Property switch
            {
                "圆锥最大角误差" => new[] { "size为圆锥基本直径，不是圆锥长度。本值对应L=100mm；原注其他长度乘100/L（L单位mm），当前接口不接收L，不自动换算。" },
                "圆锥轴向公差" => new[] { "size为基本直径；本值仅对应锥度C=1:10的轴向公差Tz，不是径向公差，也不是带符号的轴向极限偏差。" },
                _ => new[] { "用于由C=1:10基准轴向公差/基本偏差向此圆锥换算。系数为原表约数，保留原精度，不当作精确几何比值或自动代算。" }
            } };
    }
    internal static Dictionary<string, object> ResolveTolerance(string grade, double size, string standard = "", string property = "")
        => !Matches(standard) ? new() : Facts.Where(f => f.Key == grade && ToleranceProperties.Contains(f.Property)
            && size > f.Lower && size <= f.Upper && (property.Length == 0 || property == "*" || f.Property == property)).ToDictionary(f => f.Property, Value);
    internal static Dictionary<string, object> ResolveFactor(string designation, string standard = "")
        => !Matches(standard) ? new() : Facts.Where(f => f.Key == designation && f.Property == FactorProperty).ToDictionary(f => f.Property, Value);
    internal static string[] Grades(string property = "") => Facts.Where(f => ToleranceProperties.Contains(f.Property)
        && (property.Length == 0 || property == "*" || f.Property == property)).Select(f => f.Key).Distinct().ToArray();
    private static string Interval(double lower, double upper) => $">{lower.ToString(CultureInfo.InvariantCulture)}~{upper.ToString(CultureInfo.InvariantCulture)}";
    internal static string[] Sizes => Facts.Where(f => f.Property == ToleranceProperties[0]).Select(f => Interval(f.Lower, f.Upper)).Distinct().ToArray();
    internal static object QueryTolerance(string size, string grade, string property, string standard, int offset, int limit)
    {
        var grades = Grades(property).Where(g => grade.Length == 0 || grade == "*" || grade == g).ToArray();
        object Options(string key, string[] values) => new { missing = key, @default = (object?)null, options = values.Skip(offset).Take(limit).ToArray(), total = values.Length,
            nextOffset = offset + limit < values.Length ? (int?)(offset + limit) : null };
        object NoMatch() => new { status = "no_match", value = (object?)null, reason = "圆锥参考表无此等级/直径/来源组合，不外推，不放宽过滤" };
        object Table(object[] rows) => new { rows = rows.Skip(offset).Take(limit).ToArray(), total = rows.Length,
            nextOffset = offset + limit < rows.Length ? (int?)(offset + limit) : null };
        if (!Matches(standard) || grades.Length == 0 || size.Length > 0 && size != "*" && double.Parse(size, CultureInfo.InvariantCulture) > 500) return NoMatch();
        var missing = new List<object>();
        if (grade.Length == 0) missing.Add(Options("grade", grades));
        if (size.Length == 0) missing.Add(Options("size", Sizes));
        if (missing.Count > 0) return new { status = "needs_input", @default = (object?)null, missing };
        object Card(string g, double diameter, string display) => new { name = "标准公差", spec = new Dictionary<string, string> { ["等级"] = g, ["基本尺寸"] = display },
            data = ResolveTolerance(g, diameter, standard, property) };
        if (size == "*") return Table(Facts.Where(f => f.Key == grades[0] && ToleranceProperties.Contains(f.Property) && (property.Length == 0 || property == "*" || f.Property == property))
            .DistinctBy(f => (f.Lower, f.Upper)).Select(f => Card(grades[0], f.Upper, Interval(f.Lower, f.Upper))).ToArray());
        var number = double.Parse(size, CultureInfo.InvariantCulture);
        if (grade == "*") return Table(grades.Select(g => Card(g, number, size)).ToArray());
        if (property == "*") return Table(ResolveTolerance(grade, number, standard).Select(f => (object)new { property = f.Key, data = f.Value }).ToArray());
        return Card(grade, number, size);
    }
}
