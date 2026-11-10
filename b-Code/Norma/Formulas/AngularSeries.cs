using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-BASIC-023: geometric designation is the key; no source-table dispatcher.
internal static class AngularSeries
{
    internal sealed record Cell(string Raw, int Row, int Column, int Rowspan, int Colspan, string[] Images);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, Cell[][] Table, string[] Notes);
    internal sealed record Entry(string Key, string[] Aliases, string Shape, string Scope, string Page, int Row, Dictionary<string, JsonElement> Data);
    private sealed record Data(Page[] Pages, Entry[] Entries);
    private static readonly Lazy<Data> Loaded = new(() =>
    {
        using var stream = typeof(AngularSeries).Assembly.GetManifestResourceStream("Norma.Formulas.angular-series.json")
            ?? throw new InvalidOperationException("缺少角度与斜度系列来源");
        var d = JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (d.Pages.Length != 2 || d.Pages[0].Table.Length != 50 || d.Pages[1].Table.Length != 36
            || d.Entries.Length != 71 || d.Entries.Select(e => e.Key).Distinct().Count() != 71)
            throw new InvalidOperationException("角度与斜度系列来源不完整");
        foreach (var e in d.Entries)
        {
            var page = d.Pages.Single(p => p.Id == e.Page);
            if (e.Data.Count < 3 || e.Row >= page.Table.Length) throw new InvalidOperationException("角度系列卡定位错误");
            foreach (var f in e.Data.Values)
            {
                var src = f.GetProperty("source");
                if (src.GetProperty("id").GetString() != e.Page || src.GetProperty("row").GetInt32() >= page.Table.Length
                    || src.GetProperty("column").GetInt32() >= page.Table[src.GetProperty("row").GetInt32()].Length)
                    throw new InvalidOperationException("角度系列事实定位错误");
            }
        }
        return d;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    internal static IReadOnlyList<Entry> Entries => Loaded.Value.Entries;
    private static string Normalize(string? text) => Regex.Replace(text ?? "", @"\s+", "").Replace('∶', ':').Replace('：', ':').Replace("=", "");
    internal static object Query(string? code = null, string? property = null, string? series = null, string? standard = null, int offset = 0, int limit = 20)
    {
        code = Normalize(code); property = property?.Trim() ?? ""; series = series?.Trim() ?? ""; standard = standard?.Trim() ?? "";
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0，limit为1～100");
        if (series.Length > 0 && series is not ("1" or "2")) throw new ArgumentException("series仅支持1或2，精确筛选；基本斜度和特殊用途无系列号");
        if (code.Count(c => c == '*') + property.Count(c => c == '*') > 1 || standard.Contains('*')
            || property.Contains('*') && property != "*" || code.Contains('*') && code is not ("*" or "C*" or "α*" or "β*" or "S*"))
            throw new ArgumentException("designation/property合计最多一个显式*；designation可用*、C*、α*、β*、S*；standard不支持*");
        Dictionary<string, object> Resolve(Entry e)
        {
            var data = BandLimits.Matches(Pages.Single(p => p.Id == e.Page).Standard, standard)
                ? e.Data.ToDictionary(p => p.Key, p => (object)p.Value) : new Dictionary<string, object>();
            foreach (var f in ConicalReferences.ResolveFactor(e.Key, standard)) data[f.Key] = f.Value;
            return data;
        }
        var candidates = Entries.Where(e => Resolve(e).Count > 0
            && (series.Length == 0 || e.Data["系列"].GetProperty("value").ValueKind == JsonValueKind.Number
                && e.Data["系列"].GetProperty("value").GetInt32().ToString() == series)
            && (property.Length == 0 || property == "*" || Resolve(e).ContainsKey(property))).ToArray();
        object NoMatch() => new { status = "no_match", value = (object?)null, spec = new { designation = code, property, series, standard },
            reason = "未收录此基本值或筛选组合；须带C/α/β/S几何符号，不按近似角度、邻级或任意值换算" };
        object Table(object[] rows) => new { rows = rows.Skip(offset).Take(limit).ToArray(), total = rows.Length,
            nextOffset = offset + limit < rows.Length ? (int?)(offset + limit) : null };
        if (candidates.Length == 0) return NoMatch();
        if (code.Length == 0) return new { status = "needs_input", missing = "designation", @default = (object?)null,
            options = candidates.Skip(offset).Take(limit).Select(e => e.Key).ToArray(), total = candidates.Length,
            nextOffset = offset + limit < candidates.Length ? (int?)(offset + limit) : null };
        var rows = candidates.Where(e => code == "*" || code.EndsWith('*') && e.Key.StartsWith(code[..^1], StringComparison.Ordinal)
            || Normalize(e.Key) == code || e.Aliases.Any(a => Normalize(a) == code)).ToArray();
        if (rows.Length == 0) return NoMatch();
        object Card(Entry e) => new { name = "角度与斜度系列", spec = new { designation = e.Key, shape = e.Shape, scope = e.Scope }, aliases = e.Aliases,
            data = Resolve(e).Where(p => property.Length == 0 || property == "*" || p.Key == property).ToDictionary(),
            notes = Pages.Single(p => p.Id == e.Page).Notes };
        if (code.Contains('*')) return Table(rows.Select(Card).ToArray());
        if (property == "*") return Table(Resolve(rows[0]).Select(p => (object)new { property = p.Key, data = p.Value }).ToArray());
        return Card(rows[0]);
    }
}
