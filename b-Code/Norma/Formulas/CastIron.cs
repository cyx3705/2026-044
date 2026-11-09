using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-IRON-001/002: reviewed native manual cells; no dependency on the manual at runtime.
internal static class CastIron
{
    internal sealed record Field(string Key, string Name, string Unit, string[] Headers, string Semantics, string Role);
    internal sealed record Table(string Id, int Entry, string Title, string Family, string Kind, int GradeColumn,
        string SourceId, string SourceFile, long ByteOffset, string DirectoryPath, string Notes, Field[] Fields, string[][] Rows);
    internal sealed record Cell(string Text, string Unit, string Kind, double? Value = null, double? Min = null,
        double? Max = null, bool? MinInclusive = null, bool? MaxInclusive = null, double[]? Options = null,
        string? Undefined = null, bool Parenthesized = false, IReadOnlyDictionary<string, Cell>? Parts = null);
    internal sealed record Row(string Id, string TableId, string Family, string Grade, string Kind,
        IReadOnlyDictionary<string, Cell> Properties, string? Note, string? RelatedId);
    internal sealed record Found(int Total, int Offset, int? NextOffset, IReadOnlyList<Row> Items, IReadOnlyList<Table> Tables);
    private static readonly Lazy<Table[]> Data = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Norma.Formulas.cast-iron.json")
            ?? throw new InvalidOperationException("缺少铸铁数据");
        var tables = JsonSerializer.Deserialize<Table[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (tables.Length != 30 || tables.Select(t => t.Entry).Distinct().Count() != 28 ||
            tables.Any(t => t.Rows.Any(r => r.Length != t.Fields.Length))) throw new InvalidOperationException("铸铁数据覆盖/列数异常");
        return tables;
    });
    internal static IReadOnlyList<Table> Tables => Data.Value;
    internal const string Scope = "机械工程师手册铸铁整节28页；标准年份以原页为准。按原牌号/品种及试样、壁厚、铸型、热处理、温度分别取用；不插值、不外推、不取区间中值。value仅为原表单值，min/max为边界（包含性另列）；最小/最大值按表头解释，参考值仍是参考值。空白/—不填0，异常照录；表头、脚注和疑点见tables。";
    private static string Norm(string? text) => Regex.Replace(text ?? "", @"\s|\*", "").ToUpperInvariant();

    internal static Found Search(string tool, string? q = null, string? family = null, string? grade = null,
        string? table = null, string? section = null, int offset = 0, int limit = 20)
    {
        if (tool is not ("grade" or "strength")) throw new ArgumentException("工具为grade或strength");
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0，limit为1～100");
        section = string.IsNullOrWhiteSpace(section) ? "all" : section;
        if (section is not ("all" or "composition" or "designation" or "heat_state" or "catalog")) throw new ArgumentException("section为all/composition/designation/heat_state/catalog");
        if (tool == "strength" && section != "all") throw new ArgumentException("section仅牌号工具使用");
        if (!string.IsNullOrWhiteSpace(table) && !Tables.Any(t => t.Id == table)) throw new ArgumentException("未知table；省略table查看表目录");
        var rows = new List<Row>();
        foreach (var t in Tables.Where(t => (string.IsNullOrWhiteSpace(table) || t.Id == table) &&
                     (string.IsNullOrWhiteSpace(family) || Norm(t.Family).Contains(Norm(family)))))
        {
            var catalog = tool == "grade" && t.Kind == "strength";
            if (tool == "strength" && t.Kind != "strength") continue;
            if (catalog && t.GradeColumn < 0) continue;
            var kind = catalog ? "catalog" : t.Kind;
            if (section != "all" && kind != section) continue;
            for (var i = 0; i < t.Rows.Length; i++)
            {
                var r = t.Rows[i];
                var identity = t.GradeColumn < 0 ? "" : r[t.GradeColumn];
                if (!string.IsNullOrWhiteSpace(grade) && Norm(identity) != Norm(grade)) continue;
                var id = t.Id + ":" + (i + 1);
                var searchable = t.Family + " " + t.Title + " " + string.Join(" ", t.Fields.Select(f => f.Name)) + " " + string.Join(" ", catalog ? r.Take(t.GradeColumn + 1) : r) + " " + id;
                if (!(q ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .All(term => Norm(searchable).Contains(Norm(term)))) continue;
                var fields = catalog ? t.Fields.Where(f => f.Role == "identity") : t.Fields;
                var properties = fields.ToDictionary(f => f.Key, f => f.Role == "identity"
                    ? new Cell(r[Array.IndexOf(t.Fields, f)], f.Unit, "text")
                    : Parse(r[Array.IndexOf(t.Fields, f)], f.Unit, f.Semantics));
                string? related = null;
                // These three paired tables have repeated names: row order, not name, identifies the recipe.
                var paired = t.Entry switch { 551 => 550, 550 => 551, 299 => 298, 298 => 299, 402 => 403, 403 => 402, _ => 0 };
                if (paired != 0) related = $"iron:{paired}:1:{i + 1}";
                rows.Add(new(id, t.Id, t.Family, identity, kind, properties,
                    catalog ? "牌号目录投影，化学成分未在此表给出；力学性能请调用material.strength并使用同一table/grade。" : null, related));
            }
        }
        // Exact row/grade query never discards other conditions of the same grade.
        if (!string.IsNullOrWhiteSpace(q))
        {
            var exact = rows.Where(r => Norm(r.Grade) == Norm(q) || r.Id.Equals(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) rows = exact;
        }
        var items = rows.Skip(offset).Take(limit).ToArray();
        var refs = Tables.Where(t => items.Any(r => r.TableId == t.Id)).ToArray();
        return new(rows.Count, offset, offset + items.Length < rows.Count ? offset + items.Length : null, items, refs);
    }

    internal static Cell Parse(string text, string unit, string semantics = "")
    {
        if (string.IsNullOrWhiteSpace(text) || text is "—" or "-" or "－")
            return new(text, unit, "undefined", Undefined: "原表未给出");
        var s = Regex.Replace(text, @"\s", "").Replace('～', '~').Replace('（', '(').Replace('）', ')');
        if (s.StartsWith("HB", StringComparison.Ordinal)) { unit = "HB"; s = s[2..]; }
        var parens = s.Contains('(');
        s = s.Replace("(", "").Replace(")", "");
        if (unit == "HBS(HBC)") unit = parens ? "HBC" : "HBS";
        double N(string v) => double.Parse(v, CultureInfo.InvariantCulture);
        const string number = @"\d+(?:\.\d+)?";
        if (Regex.IsMatch(s, "^" + number + "或" + number + "$"))
            return new(text, unit, "options", Options: s.Split('或').Select(N).ToArray());
        var interval = Regex.Match(s, "^(?<op>>|≥)?(?<a>" + number + ")~(?<b>" + number + ")$");
        if (interval.Success)
        {
            var a = N(interval.Groups["a"].Value); var b = N(interval.Groups["b"].Value);
            return a > b ? new(text, unit, "undefined", Undefined: "原表区间倒置，待核")
                : new(text, unit, "range", Min: a, Max: b, MinInclusive: interval.Groups["op"].Value != ">", MaxInclusive: true, Parenthesized: parens);
        }
        var expression = Regex.Match(s, "^(?<a>" + number + ")<e≤(?<b>" + number + ")$");
        if (expression.Success) return new(text, unit, "range", Min: N(expression.Groups["a"].Value), Max: N(expression.Groups["b"].Value), MinInclusive: false, MaxInclusive: true);
        var value = Regex.Match(s, "^(?<op>≥|≤|>|<)?(?<n>" + number + ")$");
        if (value.Success)
        {
            var n = N(value.Groups["n"].Value); var op = value.Groups["op"].Value;
            if (op.Length == 0) op = semantics == "min" ? "≥" : semantics == "max" ? "≤" : "";
            return op switch
            {
                "≥" or ">" => new(text, unit, "lower_bound", Min: n, MinInclusive: op == "≥", Parenthesized: parens),
                "≤" or "<" => new(text, unit, "upper_bound", Max: n, MaxInclusive: op == "≤", Parenthesized: parens),
                _ => new(text, unit, "value", Value: n, Min: n, Max: n, MinInclusive: true, MaxInclusive: true, Parenthesized: parens)
            };
        }
        if (text == "铸态53～56 600℃回火后50～55") return new(text, unit, "conditional", Parts: new Dictionary<string, Cell>
        { ["铸态"] = Parse("53～56", unit), ["600℃回火后"] = Parse("50～55", unit) });
        // Other-element cells retain addition/residual qualifiers on their original text.
        var parts = Regex.Matches(text, @"(?<element>[A-Za-z]+|稀土硅铁合金):?\s*(?<v>[<>≥≤]?\d+(?:\.\d+)?(?:[~～]\d+(?:\.\d+)?)?)");
        if (parts.Count > 0) return new(text, unit, "components", Parts: parts.ToDictionary(m => m.Groups["element"].Value, m => Parse(m.Groups["v"].Value, unit)));
        return new(text, unit, "text", Undefined: "复合原文，须结合表头取用");
    }
}
