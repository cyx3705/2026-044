using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-BASIC-017: IT grade owns selection, manufacture and size-dependent tolerance facts.
internal static class ToleranceSelection
{
    internal sealed record Cell(string Raw, int Row, int Column, int LogicalColumn, int Colspan, int Rowspan, string Background, string[] Images);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, string[] Notes, Cell[][] Table);
    internal sealed record Fact(string Grade, string Property, JsonElement Value, string Method, string Feature, int Page, int Row, int Column, int GradeColumn);
    private sealed record Source(Page[] Pages, Fact[] Facts);
    private static readonly string[] Grades = new[] { "IT01", "IT0" }.Concat(Enumerable.Range(1, 18).Select(g => "IT" + g)).ToArray();
    private static readonly string[] Properties = ["公差", "应用范围", "选用举例", "可达加工方法", "经济加工方法", "加工成本比"];
    private static readonly Lazy<Source> Loaded = new(() =>
    {
        using var stream = typeof(ToleranceSelection).Assembly.GetManifestResourceStream("Norma.Formulas.tolerance-selection.json")
            ?? throw new InvalidOperationException("缺少IT等级选用数据");
        var data = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (data.Pages.Length != 5 || data.Facts.Length != 264) throw new InvalidOperationException("IT等级选用来源不完整");
        var heights = new[] { 8, 20, 22, 8, 20 };
        var widths = new[] { 21, 2, 19, 3, 18 };
        var grids = new List<Dictionary<(int Row, int Column), Cell>>();
        for (var i = 0; i < data.Pages.Length; i++)
        {
            var p = data.Pages[i];
            if (p.Id != $"13-02:{i + 41}" || p.ByteOffset <= 0 || p.Table.Length != heights[i]) throw new InvalidOperationException("IT来源页定位错误");
            var grid = new Dictionary<(int, int), Cell>();
            foreach (var c in p.Table.SelectMany(r => r))
                for (var r = c.Row; r < c.Row + c.Rowspan; r++)
                for (var col = c.LogicalColumn; col < c.LogicalColumn + c.Colspan; col++)
                    if (r >= heights[i] || col >= widths[i] || !grid.TryAdd((r, col), c)) throw new InvalidOperationException("IT原表合并格错误");
            if (grid.Count != heights[i] * widths[i]) throw new InvalidOperationException("IT原表缺格");
            grids.Add(grid);
        }
        foreach (var f in data.Facts)
        {
            if (!Grades.Contains(f.Grade) || !Properties.Contains(f.Property) || f.Page is < 0 or > 4) throw new InvalidOperationException("IT选用事实主键错误");
            var p = data.Pages[f.Page]; var c = p.Table[f.Row][f.Column]; var grid = grids[f.Page];
            bool AtGrade() => grid[(1, f.GradeColumn)].Raw == f.Grade[2..] && grid[(f.Row, f.GradeColumn)] == c;
            var valid = f.Property switch
            {
                "应用范围" => AtGrade() && c.Background == "#cccccc" && f.Value.GetString() == Compact(grid[(f.Row, 0)].Raw),
                "可达加工方法" => AtGrade() && c.Background == "#cccccc" && f.Value.GetString() == Compact(grid[(f.Row, 0)].Raw) && f.Method == f.Value.GetString(),
                "选用举例" => Regex.Matches(p.Table[f.Row][0].Raw, @"IT(?:01|0|1[0-8]|[1-9])\b").Any(m => m.Value == f.Grade) && f.Value.GetString() == c.Raw,
                "经济加工方法" => f.Value.GetString() == c.Raw && f.Method == c.Raw && f.Feature == (f.GradeColumn == 0 ? "轴" : "孔") && InRange(grid[(f.Row, f.GradeColumn)].Raw, int.Parse(f.Grade[2..], CultureInfo.InvariantCulture)),
                "加工成本比" => AtGrade() && f.Value.GetDouble() == (c.Background switch { "#cdc3d2" => 1, "#cccccc" => 2.5, "#a5b4b4" => 5, _ => -1 }) && f.Feature == Compact(grid[(f.Row, 0)].Raw) && f.Method == Compact(grid[(f.Row, 1)].Raw),
                _ => false
            };
            if (!valid) throw new InvalidOperationException($"IT事实与原格不一致：{p.Id}/{f.Row}/{f.Column}");
        }
        if (data.Facts.DistinctBy(f => (f.Grade, f.Property, f.Method, f.Feature, f.Page, f.Row, f.Column)).Count() != data.Facts.Length)
            throw new InvalidOperationException("IT选用事实重复");
        return data;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    internal static IReadOnlyList<Fact> Facts => Loaded.Value.Facts;
    private static string Compact(string? s) => Regex.Replace(s ?? "", @"\s+", "");
    private static bool InRange(string raw, int grade)
    {
        var ns = Regex.Matches(raw, @"\d+").Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        return ns.Length is 1 or 2 && grade >= ns[0] && grade <= ns[^1];
    }
    private static bool StandardMatches(string source, string filter) => filter.Length == 0 || Compact(source).Contains(filter, StringComparison.OrdinalIgnoreCase);

    internal static object Query(string? size, string? grade, string? standard, int offset, int limit,
        string? property, string? process, string? feature)
    {
        size = Compact(size); grade = Compact(grade); standard = Compact(standard); property = Compact(property);
        process = Compact(process); feature = Compact(feature);
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0；limit为1～100");
        if (new[] { size, grade, property }.Count(x => x == "*") > 1 || new[] { size, grade, property }.Any(x => x.Contains('*') && x != "*")
            || new[] { standard, process, feature }.Any(x => x.Contains('*')))
            throw new ArgumentException("一次最多一个完整*，只用于grade、size或property；其余过滤不支持*");
        if (grade.Length > 0 && grade != "*")
        {
            grade = "IT" + Regex.Replace(grade, "^IT", "", RegexOptions.IgnoreCase);
            if (!Grades.Contains(grade)) throw new ArgumentException("等级为IT01、IT0、IT1～IT18；IT01/IT0仅有选用资料，无公差数值");
        }
        if (size.Length > 0 && size != "*" && (!double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d) || d <= 0))
            throw new ArgumentException("基本尺寸须为有限正数，单位mm");
        if (feature.Length > 0 && feature is not ("轴" or "孔" or "外径" or "内径" or "长度")) throw new ArgumentException("feature为轴、孔、外径、内径或长度");
        object Options(string key, string[] values) => new { missing = key, @default = (object?)null, options = values.Skip(offset).Take(limit).ToArray(), total = values.Length, nextOffset = offset + limit < values.Length ? (int?)(offset + limit) : null };
        object Needs(params object[] missing) => new { status = "needs_input", @default = (object?)null, name = "标准公差", missing };
        object Table(object[] rows) => new { rows = rows.Skip(offset).Take(limit).ToArray(), total = rows.Length, nextOffset = offset + limit < rows.Length ? (int?)(offset + limit) : null };
        object NoMatch() => new { status = "no_match", name = "标准公差", grade, size, property, process, feature, standard, value = (object?)null, reason = "收录来源无此条件组合，不放宽过滤条件" };
        bool FeatureMatches(Fact f) => feature.Length == 0 || f.Feature == feature || feature == "轴" && f.Feature == "外径" || feature == "孔" && f.Feature == "内径" || feature == "外径" && f.Feature == "轴" || feature == "内径" && f.Feature == "孔";
        var selected = Facts.Where(f => StandardMatches(Pages[f.Page].Standard, standard) && FeatureMatches(f)
            && (process.Length == 0 || Compact(f.Method).Contains(process, StringComparison.Ordinal))
            && (property.Length == 0 || property == "*" || f.Property == property)).ToArray();
        var numeric = process.Length == 0 && feature.Length == 0 && StandardMatches(BasicStandards.Pages[2].Standard, standard)
            && (property.Length == 0 || property is "*" or "公差");
        if (property.Length > 0 && property != "*" && !Properties.Concat(ConicalReferences.ToleranceProperties).Contains(property)) return Options("property", Properties.Concat(ConicalReferences.ToleranceProperties).ToArray());
        var conical = process.Length == 0 && feature.Length == 0 && ConicalReferences.Matches(standard)
            && (property.Length == 0 || property == "*" || ConicalReferences.ToleranceProperties.Contains(property));
        if (ConicalReferences.ToleranceProperties.Contains(property) || conical && !numeric && selected.Length == 0)
            return conical ? ConicalReferences.QueryTolerance(size, grade, property, standard, offset, limit) : NoMatch();
        var grades = Grades.Where(g => (grade.Length == 0 || grade == "*" || g == grade) && (selected.Any(f => f.Grade == g) || conical && ConicalReferences.HasTolerance(g, property) || numeric && (g is not ("IT0" or "IT01") || g == grade))).ToArray();
        // A size enumeration is the numeric relationship; never repeat dimension-free selection rows per interval.
        if (size == "*")
        {
            if (!numeric || grade is "IT0" or "IT01")
                return conical ? ConicalReferences.QueryTolerance(size, grade, property, standard, offset, limit) : NoMatch();
            if (grade.Length == 0) return Needs(Options("grade", grades));
            if (grade is "IT0" or "IT01") return NoMatch();
            var source = JsonSerializer.SerializeToElement(BasicStandards.QueryToleranceValue("*", grade, standard, offset, limit));
            if (!conical || grade.Length == 0) return source;
            var rows = source.GetProperty("rows").EnumerateArray().Select(row =>
            {
                var fields = row.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
                var data = row.GetProperty("data").EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
                var interval = row.GetProperty("spec").GetProperty("基本尺寸").GetString()!;
                var upper = double.Parse(Regex.Matches(interval, @"\d+(?:\.\d+)?").Last().Value, CultureInfo.InvariantCulture);
                foreach (var f in ConicalReferences.ResolveTolerance(grade, upper, standard)) data[f.Key] = f.Value;
                fields["data"] = data; return fields;
            }).ToArray();
            return new { rows, total = source.GetProperty("total").GetInt32(), nextOffset = source.GetProperty("nextOffset") };
        }
        if (size.Length > 0 && double.Parse(size, CultureInfo.InvariantCulture) > 3150 && numeric) return NoMatch();
        if (grades.Length == 0) return NoMatch();
        var requireSize = numeric && property == "公差" && grade is not ("IT0" or "IT01");
        var missing = new List<object>();
        if (grade.Length == 0) missing.Add(Options("grade", grades));
        if (requireSize && size.Length == 0) missing.Add(Options("size", SizeOptions()));
        if (missing.Count > 0) return Needs(missing.ToArray());
        object Card(string g)
        {
            var data = new Dictionary<string, object>();
            if (numeric)
            {
                if (g is "IT01" or "IT0") data["公差"] = new { value = (double?)null, reason = "原数值表未收录此等级，不回退公式估算" };
                else if (size.Length == 0) data["公差"] = Needs(Options("size", SizeOptions()));
                else data["公差"] = JsonSerializer.SerializeToElement(BasicStandards.QueryToleranceValue(size, g, standard, 0, 20)).GetProperty("data").GetProperty("公差").Clone();
            }
            foreach (var group in selected.Where(f => f.Grade == g).GroupBy(f => f.Property))
                data[group.Key] = group.Select(f =>
                {
                    var p = Pages[f.Page]; var c = p.Table[f.Row][f.Column];
                    return new { value = f.Value, method = f.Method.Length == 0 ? null : f.Method, feature = f.Feature.Length == 0 ? null : f.Feature,
                        unit = f.Property == "加工成本比" ? "1" : null,
                        source = new { id = p.Id, byteOffset = p.ByteOffset, standard = p.Standard, row = f.Row, column = f.Column }, notes = p.Notes };
                }).ToArray();
            if (conical)
                foreach (var prop in ConicalReferences.ToleranceProperties.Where(p => ConicalReferences.HasTolerance(g, p)))
                {
                    if (size.Length == 0) data[prop] = Needs(Options("size", ConicalReferences.Sizes));
                    else foreach (var f in ConicalReferences.ResolveTolerance(g, double.Parse(size, CultureInfo.InvariantCulture), standard, prop)) data[f.Key] = f.Value;
                }
            var spec = new Dictionary<string, string> { ["等级"] = g };
            if (size.Length > 0) spec["基本尺寸"] = size;
            return new { name = "标准公差", spec, data };
        }
        if (grade == "*") return Table(grades.Select(Card).ToArray());
        var card = Card(grade);
        if (property == "*")
        {
            var data = JsonSerializer.SerializeToElement(card).GetProperty("data");
            return Table(data.EnumerateObject().Select(p => (object)new { property = p.Name, data = p.Value.Clone() }).ToArray());
        }
        return card;
    }

    private static string[] SizeOptions()
    {
        var response = JsonSerializer.SerializeToElement(BasicStandards.QueryToleranceValue(grade: "IT7", limit: 100));
        return response.GetProperty("missing")[0].GetProperty("options").EnumerateArray().Select(x => x.GetString()!).ToArray();
    }
}
