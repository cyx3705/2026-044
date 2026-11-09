using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-PRODUCT-001: one query surface for model rows, valid size combinations and
// interval-dependent tolerances. The embedded source retains each native cell.
internal static class ProductSpecs
{
    internal sealed record Page(int Key, string Name, string Title, string Path, string Notes,
        string[][][] Tables, string SourceId, long? ByteOffset);
    internal sealed record Cell(string Property, string Unit, string Value, int Page, int Table, int Row, int Column, string Prefix = "", string Note = "");
    internal sealed record Axis(string Text, string Unit = "", bool Numeric = false);
    internal sealed record Entry(string Name, Dictionary<string, Axis> Spec, Cell[] Values);
    internal sealed record Coverage(int Page, string Name, int Tables, int[] Imported, string Status);
    internal sealed record Source(Page[] Pages, Entry[] Entries, Dictionary<string, string[]> Aliases, Coverage[] Coverage);
    private static readonly Lazy<Source> Loaded = new(() =>
    {
        using var stream = typeof(ProductSpecs).Assembly.GetManifestResourceStream("Norma.Formulas.product-specs.json")
            ?? throw new InvalidOperationException("缺少产品规格数据");
        var source = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var pages = source.Pages.ToDictionary(p => p.Key);
        foreach (var c in source.Entries.SelectMany(e => e.Values))
        {
            if (!pages.TryGetValue(c.Page, out var p) || c.Table < 0 || c.Table >= p.Tables.Length
                || c.Row < 0 || c.Row >= p.Tables[c.Table].Length || c.Column < 0 || c.Column >= p.Tables[c.Table][c.Row].Length
                || p.Tables[c.Table][c.Row][c.Column].Trim() != c.Value)
                throw new InvalidOperationException("产品规格值与原表不一致");
        }
        if (source.Pages.Any(p => p.ByteOffset is null || !p.SourceId.StartsWith("jxclsj:", StringComparison.Ordinal)))
            throw new InvalidOperationException("产品规格缺少来源定位");
        var imported = source.Entries.SelectMany(e => e.Values).Select(c => (c.Page, c.Table)).ToHashSet();
        if (source.Coverage.Select(c => c.Page).Distinct().Count() != source.Coverage.Length
            || !source.Coverage.SelectMany(c => c.Imported.Select(t => (c.Page, t))).ToHashSet().SetEquals(imported)
            || source.Coverage.Any(c => c.Imported.Distinct().Count() != c.Imported.Length || c.Imported.Any(t => t < 0 || t >= c.Tables)))
            throw new InvalidOperationException("产品规格覆盖清单与入库表不一致");
        return source;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    internal static IReadOnlyList<Entry> Entries => Loaded.Value.Entries;
    internal static string InputGuide => string.Join("；", Entries.GroupBy(e => e.Name).Select(g =>
        g.Key + "(" + string.Join("/", g.SelectMany(e => e.Spec.Keys).Distinct()) + ")"));
    // Latin dimensions are case-sensitive: B and b, H and h can be different axes.
    private static string Norm(string? value) => Regex.Replace(value ?? "", @"\s+", "").Replace('×', 'x').Replace('＊', '*');
    private static string Show(Axis a) => a.Text + (a.Unit.Length == 0 ? "" : " " + a.Unit);
    private static string Show(Cell c) => c.Prefix + c.Value + (c.Unit.Length == 0 ? "" : " " + c.Unit);
    private static object Options(string key, IEnumerable<string> values, int offset, int limit)
    {
        var all = values.Distinct().ToArray();
        return new { missing = key, @default = (string?)null, options = all.Skip(offset).Take(limit).ToArray(), total = all.Length,
            nextOffset = offset + limit < all.Length ? (int?)(offset + limit) : null };
    }

    internal static object Query(string? name = null, string? spec = null, string? property = null,
        string? standard = null, int offset = 0, int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0；limit为1～100");
        var filters = ParseSpec(spec);
        var wildcards = filters.Where(p => p.Value == "*").Select(p => p.Key)
            .Concat(property == "*" ? new[] { "property" } : []).Concat(name == "*" ? new[] { "name" } : []).ToArray();
        if (wildcards.Length > 1 || standard == "*") throw new ArgumentException("一次最多一个*，用于name、property或spec中的一个字段；standard不支持*");
        var entries = Entries.ToArray();
        if (!string.IsNullOrWhiteSpace(standard))
        {
            var pages = Pages.Where(p => Norm(p.Title).Contains(Norm(standard), StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToHashSet();
            entries = entries.Select(e => e with { Values = e.Values.Where(c => pages.Contains(c.Page)).ToArray() }).Where(e => e.Values.Length > 0).ToArray();
        }
        var names = entries.Select(e => e.Name).Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(name)) return Options("name", names, offset, limit);
        if (name == "*")
        {
            if (filters.Count > 0 || !string.IsNullOrWhiteSpace(property)) throw new ArgumentException("name=*只展开产品目录，可用standard筛选");
            return new { rows = names.Skip(offset).Take(limit).ToArray(), total = names.Length,
                nextOffset = offset + limit < names.Length ? (int?)(offset + limit) : null };
        }
        var matchedNames = names.Where(n => Norm(n) == Norm(name)).ToArray();
        if (matchedNames.Length == 0 && Loaded.Value.Aliases.TryGetValue(Norm(name), out var aliases))
            matchedNames = names.Where(n => aliases.Contains(n)).ToArray();
        if (matchedNames.Length != 1)
            return Options("name", matchedNames.Length > 0 ? matchedNames : names.Where(n => Norm(n).Contains(Norm(name))), offset, limit);
        entries = entries.Where(e => e.Name == matchedNames[0]).ToArray();
        var allProperties = entries.SelectMany(e => e.Values).Select(c => c.Property).Distinct().ToArray();
        if (!string.IsNullOrWhiteSpace(property) && property != "*")
        {
            var exact = allProperties.Where(p => Norm(p) == Norm(property)).ToArray();
            var properties = exact.Length > 0 ? exact : allProperties.Where(p => Norm(p).Contains(Norm(property))).ToArray();
            if (properties.Length == 0) return Options("property", allProperties, offset, limit);
            entries = entries.Select(e => e with { Values = e.Values.Where(c => properties.Contains(c.Property)).ToArray() }).Where(e => e.Values.Length > 0).ToArray();
        }
        foreach (var filter in filters.Where(p => p.Value != "*"))
        {
            var candidates = entries.Where(e => e.Spec.TryGetValue(filter.Key, out var a) && Matches(a, filter.Value)).ToArray();
            if (candidates.Length == 0)
            {
                var values = entries.Where(e => e.Spec.ContainsKey(filter.Key)).Select(e => Show(e.Spec[filter.Key])).ToArray();
                return values.Length == 0 ? Options("spec字段", entries.SelectMany(e => e.Spec.Keys), offset, limit)
                    : Options("spec." + filter.Key, values, offset, limit);
            }
            entries = candidates;
        }
        var variable = wildcards.SingleOrDefault();
        if (variable is not null && variable != "property" && !entries.Any(e => e.Spec.ContainsKey(variable)))
            return Options("spec字段", entries.SelectMany(e => e.Spec.Keys), offset, limit);
        // An omitted dimension is never an implicit wildcard. One unique coordinate is
        // usable without another round trip; differing coordinates need an explicit choice.
        foreach (var axis in entries.SelectMany(e => e.Spec.Keys).Distinct())
        {
            if (filters.ContainsKey(axis)) continue;
            var values = entries.Where(e => e.Spec.ContainsKey(axis)).Select(e => Show(e.Spec[axis])).Distinct().ToArray();
            if (values.Length > 1) return Options("spec." + axis, values, offset, limit);
        }
        var cells = entries.SelectMany(e => e.Values).DistinctBy(c => (c.Property, c.Value, c.Unit, c.Prefix, c.Page)).ToArray();
        var notes = cells.Select(c => c.Page).Distinct().Select(k => Pages.Single(p => p.Key == k).Notes)
            .Concat(cells.Select(c => c.Note)).Where(n => n.Length > 0).Distinct().ToArray();
        if (variable == "property")
        {
            var rows = cells.Select(c => new Dictionary<string, object> { ["property"] = c.Property, ["value"] = Show(c) }).ToArray();
            return Table(rows, offset, limit, notes);
        }
        if (variable is not null)
        {
            var rows = entries.Where(e => e.Spec.ContainsKey(variable)).GroupBy(e => Show(e.Spec[variable])).Select(g =>
                new Dictionary<string, object> { [variable] = g.Key, ["value"] = string.IsNullOrWhiteSpace(property)
                    ? ValueMap(g.SelectMany(e => e.Values).ToArray()) : Values(g.SelectMany(e => e.Values).ToArray()) }).ToArray();
            return Table(rows, offset, limit, notes);
        }
        if (!string.IsNullOrWhiteSpace(property) && cells.Select(c => (c.Property, c.Unit, c.Value)).Distinct().Count() == 1)
            return notes.Length == 0 ? Show(cells[0]) : new Dictionary<string, object> { ["value"] = Show(cells[0]), ["原注"] = notes };
        var card = new Dictionary<string, object>
        {
            ["产品"] = matchedNames[0],
            ["规格"] = entries.SelectMany(e => e.Spec).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => string.Join("；", g.Select(p => Show(p.Value)).Distinct())),
            ["数据"] = ValueMap(cells),
            ["来源"] = cells.Select(c => c.Page).Distinct().Select(k => Pages.Single(p => p.Key == k).Title).Distinct().ToArray()
        };
        if (notes.Length > 0) card["原注"] = notes;
        return card;
    }

    private static object Table(Dictionary<string, object>[] rows, int offset, int limit, string[] notes)
    {
        var result = new Dictionary<string, object?>
        {
            ["rows"] = rows.Skip(offset).Take(limit).ToArray(), ["total"] = rows.Length,
            ["nextOffset"] = offset + limit < rows.Length ? (int?)(offset + limit) : null
        };
        if (notes.Length > 0) result["原注"] = notes;
        return result;
    }
    private static object Values(Cell[] cells)
    {
        var values = ValueMap(cells);
        return values.Count == 1 ? values.First().Value : values;
    }
    private static Dictionary<string, object> ValueMap(Cell[] cells)
    {
        return cells.GroupBy(c => c.Property).ToDictionary(g => g.Key, g =>
        {
            var distinct = g.Select(Show).Distinct().ToArray();
            return distinct.Length == 1 ? (object)distinct[0] : distinct;
        });
    }
    private static Dictionary<string, string> ParseSpec(string? spec)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(spec)) return result;
        if (!spec.TrimStart().StartsWith('{')) { result["型号"] = spec.Trim(); return result; }
        try
        {
            using var doc = JsonDocument.Parse(spec);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || string.IsNullOrWhiteSpace(p.Name))
                    throw new ArgumentException("spec字段只能是字符串或数字，尺寸默认mm；不支持null、布尔或嵌套对象");
                if (!result.TryAdd(p.Name.Trim(), p.Value.ToString().Trim())) throw new ArgumentException("spec字段重复");
            }
            return result;
        }
        catch (JsonException) { throw new ArgumentException("spec应为JSON对象字符串，例如{\"外径\":32,\"壁厚\":3}，或直接填写型号20a"); }
    }

    internal static bool Matches(Axis axis, string filter)
    {
        var input = filter.Trim();
        if (axis.Unit.Length > 0 && input.EndsWith(axis.Unit, StringComparison.Ordinal)) input = input[..^axis.Unit.Length].Trim();
        if (Norm(axis.Text) == Norm(input)) return true;
        if (!axis.Numeric || !double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x)) return false;
        var text = Norm(axis.Text).Replace('～', '~').Replace('＞', '>').Replace('＜', '<').Replace('－', '-');
        // Only explicit series and numerical inequalities/ranges are supported. Expressions,
        // symbols, negotiable sizes and dimensions with a tolerance are never guessed.
        if (Regex.IsMatch(text, @"^[0-9.,，、;；]+$"))
            return Regex.Split(text, @"[,，、;；]").Any(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == x);
        var m = Regex.Match(text, @"^(?<lo>[>≥]?)\s*(?<a>\d+(?:\.\d+)?)~(?<hi>[<≤]?)\s*(?<b>\d+(?:\.\d+)?)$");
        if (m.Success)
        {
            var a = double.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
            var b = double.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture);
            return a <= b && (m.Groups["lo"].Value == ">" ? x > a : x >= a) && (m.Groups["hi"].Value == "<" ? x < b : x <= b);
        }
        m = Regex.Match(text, @"^(?<op>[<>≤≥])(?<n>\d+(?:\.\d+)?)$");
        if (!m.Success) return false;
        var bound = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        return m.Groups["op"].Value switch { ">" => x > bound, "≥" => x >= bound, "<" => x < bound, "≤" => x <= bound, _ => false };
    }
}
