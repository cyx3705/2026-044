using System.Globalization;
using System.Text.Json;

namespace Norma.Formulas;

// REQ-BASIC-021: two orthogonal coordinates return all three general-tolerance relations.
internal static class GeneralTolerances
{
    internal sealed record Cell(string Raw, int Row, int Column, int LogicalColumn, int Rowspan, int Colspan);
    internal sealed record Entry(string Grade, double Lower, double? Upper, bool LowerInclusive, double? Value, string Raw, int Row, int Column);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, string Unit, string SizeMeaning, Cell[][] Table, Entry[] Entries);
    private sealed record Source(Page[] Pages);
    private static readonly string[] Grades = ["f", "m", "c", "v"];
    private static readonly string[] Properties = ["线性尺寸", "倒圆半径和倒角高度", "角度尺寸"];
    private static readonly Lazy<Source> Loaded = new(() =>
    {
        using var stream = typeof(GeneralTolerances).Assembly.GetManifestResourceStream("Norma.Formulas.general-tolerances.json")
            ?? throw new InvalidOperationException("缺少一般公差原表");
        var source = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (source.Pages.Length != 3) throw new InvalidOperationException("一般公差页数错误");
        foreach (var (p, i) in source.Pages.Select((p, i) => (p, i)))
        {
            if (p.Id != $"13-02:{49 + i}" || p.ByteOffset <= 0 || p.Table.Length != 6
                || p.Entries.Length != new[] { 32, 16, 20 }[i]) throw new InvalidOperationException("一般公差来源不完整");
            foreach (var e in p.Entries)
                if (!Grades.Contains(e.Grade) || p.Table[e.Row][e.Column].Raw != e.Raw
                    || e.Value < 0 || e.Upper is { } upper && upper <= e.Lower)
                    throw new InvalidOperationException("一般公差来源坐标错误");
        }
        return source;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    private static string N(double n) => n.ToString(CultureInfo.InvariantCulture);
    private static string Range(double lower, double? upper, bool inclusive)
        => (inclusive ? "≥" : ">") + N(lower) + (upper is { } hi ? "～≤" + N(hi) : "");

    private sealed record Segment(double Lower, double? Upper, bool LowerInclusive, bool UpperInclusive)
    {
        internal string Label => Lower == 0 ? ">0～<0.5" : Range(Lower, Upper, LowerInclusive);
        internal double Sample => Upper ?? Lower + 1;
    }
    private static Segment[] Segments()
    {
        var boundaries = Pages.SelectMany(p => p.Entries.SelectMany(e => e.Upper is { } hi ? new[] { e.Lower, hi } : new[] { e.Lower }))
            .Distinct().Order().ToArray();
        return boundaries.Select((lo, i) => new Segment(lo, i + 1 < boundaries.Length ? boundaries[i + 1] : null,
            lo == .5, lo != 0)).ToArray();
    }

    internal static object Query(string? size = null, string? grade = null, string? property = null)
    {
        size = size?.Trim(); grade = grade?.Trim(); property = property?.Trim();
        if (new[] { size, grade, property }.Count(v => v == "*") > 1)
            throw new ArgumentException("一次最多一个显式*：size、grade或property");
        if (!string.IsNullOrEmpty(property) && property != "*" && !Properties.Contains(property))
            throw new ArgumentException("property为线性尺寸、倒圆半径和倒角高度或角度尺寸；省略返回全部三项");
        if (!string.IsNullOrEmpty(grade) && grade != "*" && !Grades.Contains(grade))
            throw new ArgumentException("grade严格取f/m/c/v（精密/中等/粗糙/最粗），不是IT等级");
        double number = 0;
        if (!string.IsNullOrEmpty(size) && size != "*" && (!double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            || !double.IsFinite(number) || number <= 0)) throw new ArgumentException("size为有限正数，单位mm；角度项按较短边长度查询，不填角度值");
        var segments = Segments();
        var missing = new List<object>();
        if (string.IsNullOrEmpty(size)) missing.Add(new { missing = "size", @default = (object?)null,
            unit = "mm", options = segments.Select(s => s.Label).ToArray() });
        if (string.IsNullOrEmpty(grade)) missing.Add(new { missing = "grade", @default = (object?)null, options = Grades });
        if (missing.Count > 0) return new { status = "needs_input", @default = (object?)null, missing };

        object Card(string g, double dimension, object coordinate)
        {
            var data = new Dictionary<string, object>();
            for (var i = 0; i < Pages.Count; i++)
            {
                if (!string.IsNullOrEmpty(property) && property != "*" && property != Properties[i]) continue;
                var p = Pages[i];
                var e = p.Entries.SingleOrDefault(e => e.Grade == g && (e.LowerInclusive ? dimension >= e.Lower : dimension > e.Lower)
                    && (e.Upper is null || dimension <= e.Upper));
                var value = e?.Value;
                data[Properties[i]] = new { value, upperDeviation = value, lowerDeviation = -value, unit = p.Unit,
                    raw = e?.Raw, sizeMeaning = p.SizeMeaning,
                    sizeRange = e is null ? null : Range(e.Lower, e.Upper, e.LowerInclusive),
                    reason = value is not null ? null : e is null ? "基本尺寸超出本项原表范围；不外推" : "原表为横线，未规定此组合；不估算补值",
                    source = new { id = p.Id, byteOffset = p.ByteOffset, standard = p.Standard, title = p.Title, row = e?.Row, column = e?.Column } };
            }
            return new { grade = g, size = coordinate, data };
        }
        if (grade == "*") return new { variable = "grade", rows = Grades.Select(g => Card(g, number, number)).ToArray(), total = Grades.Length };
        if (size == "*") return new { variable = "size", rows = segments.Select(s => Card(grade!, s.Lower == 0 ? .25 : s.Sample,
            new { lower = s.Lower, upper = s.Upper, lowerInclusive = s.LowerInclusive, upperInclusive = s.Upper is not null && s.UpperInclusive, unit = "mm", range = s.Label })).ToArray(), total = segments.Length };
        if (property == "*")
        {
            var card = JsonSerializer.SerializeToElement(Card(grade!, number, number));
            return new { variable = "property", rows = card.GetProperty("data").EnumerateObject()
                .Select(p => new { property = p.Name, data = p.Value.Clone() }).ToArray(), total = Properties.Length };
        }
        return Card(grade!, number, number);
    }
}
