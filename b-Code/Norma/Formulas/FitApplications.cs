using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-BASIC-018: application evidence belongs to the complete fit key, independently of size.
internal static class FitApplications
{
    internal sealed record Cell(string Raw, int Row, int Column, int LogicalColumn, int Rowspan, int Colspan, string[] Images);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, Cell[][] Table, string[] Notes);
    internal sealed record Reference(int Row, int Column);
    internal sealed record KeyReference(int Row, int KeyRow, int KeyColumn, string Image);
    internal sealed record Fact(string Fit, string Property, string Raw, Reference[] Cells, KeyReference[] Keys);
    private sealed record Data(Page Page, Fact[] Facts);
    internal static readonly string[] Properties = ["装配方法", "配合特性及使用条件", "应用举例", "过盈概率"];
    private static readonly Lazy<Data> Loaded = new(() =>
    {
        using var stream = typeof(FitApplications).Assembly.GetManifestResourceStream("Norma.Formulas.fit-applications.json")
            ?? throw new InvalidOperationException("缺少配合应用数据");
        var data = JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var page = data.Page;
        if (page.Id != "13-02:47" || page.ByteOffset != 4425145 || page.Table.Length != 48 || data.Facts.Length != 265
            || data.Facts.Select(f => f.Fit).Distinct().Count() != 78) throw new InvalidOperationException("配合应用来源不完整");
        var grid = new Dictionary<(int, int), Cell>();
        foreach (var c in page.Table.SelectMany(r => r))
            for (var row = c.Row; row < c.Row + c.Rowspan; row++)
            for (var col = c.LogicalColumn; col < c.LogicalColumn + c.Colspan; col++)
                if (row >= 48 || col >= 7 || !grid.TryAdd((row, col), c)) throw new InvalidOperationException("配合应用合并格错误");
        if (grid.Count != 48 * 7) throw new InvalidOperationException("配合应用原表缺格");
        foreach (var f in data.Facts)
        {
            if (!Properties.Contains(f.Property) || f.Cells.Length == 0 || f.Keys.Length == 0
                || f.Raw != string.Join("；", f.Cells.Select(c => page.Table[c.Row][c.Column].Raw)))
                throw new InvalidOperationException("配合应用文本与权威格不一致");
            foreach (var k in f.Keys)
            {
                var key = page.Table[k.KeyRow][k.KeyColumn];
                var imageIndex = Array.IndexOf(key.Images, k.Image);
                if (imageIndex < 0 || key.Raw.Split(' ')[imageIndex] != f.Fit || k.Row < key.Row || k.Row >= key.Row + key.Rowspan)
                    throw new InvalidOperationException("配合应用代号图片映射不一致");
                foreach (var c in f.Cells.Select(c => page.Table[c.Row][c.Column]))
                    if (k.Row < c.Row || k.Row >= c.Row + c.Rowspan || c.LogicalColumn < 2)
                        throw new InvalidOperationException("配合应用越过合并格范围");
                var expected = f.Property switch
                {
                    "装配方法" => new[] { grid[(k.Row, 2)] },
                    "应用举例" => new[] { grid[(k.Row, 6)] },
                    "过盈概率" when k.Row is >= 15 and <= 27 => new[] { grid[(k.Row, 3)] },
                    "配合特性及使用条件" => (k.Row is >= 15 and <= 27 ? new[] { grid[(k.Row, 5)] }
                        : new[] { grid[(k.Row, 3)], grid[(k.Row, 4)], grid[(k.Row, 5)] }).Distinct().ToArray(),
                    _ => []
                };
                if (!expected.SequenceEqual(f.Cells.Select(c => page.Table[c.Row][c.Column]))) throw new InvalidOperationException("配合应用属性投影错误");
            }
        }
        return data;
    });
    internal static Page SourcePage => Loaded.Value.Page;
    internal static IReadOnlyList<Fact> Facts => Loaded.Value.Facts;
    internal static bool Matches(string standard) => BandLimits.Matches(SourcePage.Standard, standard);
    internal static bool Has(string fit, string? property = null) => Facts.Any(f => f.Fit == fit && (property is null || f.Property == property));
    internal static Dictionary<string, object> Resolve(string fit)
    {
        var page = SourcePage;
        return Facts.Where(f => f.Fit == fit).GroupBy(f => f.Property).ToDictionary(g => g.Key,
            g => (object)g.Select(f =>
            {
                double? value = null, min = null, max = null;
                string? reason = null;
                if (f.Property == "过盈概率")
                {
                    var ns = Regex.Matches(f.Raw, @"\d+(?:\.\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                    if (ns.Length == 1) value = min = max = ns[0];
                    else if (ns.Length == 2) { min = ns[0]; max = ns[1]; }
                    else reason = "原表该格仅标过盈概率，未给出概率数值";
                }
                return new { value = f.Property == "过盈概率" ? (object?)value : f.Raw, min, max,
                    unit = f.Property == "过盈概率" ? "%" : "", raw = f.Raw, reason,
                    source = new { id = page.Id, byteOffset = page.ByteOffset, standard = page.Standard,
                        cells = f.Cells.Select(c => new { row = c.Row, column = c.Column }).ToArray(),
                        keys = f.Keys.Select(k => new { row = k.KeyRow, column = k.KeyColumn, image = k.Image }).ToArray() },
                    notes = f.Property == "过盈概率" ? new[] { "手册经验概率；区间保留上下限，不取中点，不是由极限间隙反算的概率。" } : page.Notes };
            }).ToArray());
    }
}
