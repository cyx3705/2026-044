using System.Text.Json;

namespace Norma.Formulas;

// REQ-MATERIAL-011: volume reconciliation found useful grade tables omitted by
// earlier name-based extraction. Each fact is checked against the native grid.
internal static class MaterialSupplement
{
    internal sealed record Item(string Grade, string Family, string Tool, string Property, string Condition,
        string Unit, string Prefix, int Page, int Table, int Row, int Column);
    internal sealed record Source(ProductSpecs.Page[] Pages, Item[] Facts);
    private static readonly Lazy<Source> Loaded = new(() =>
    {
        using var stream = typeof(MaterialSupplement).Assembly.GetManifestResourceStream("Norma.Formulas.material-supplement.json")
            ?? throw new InvalidOperationException("缺少材料卷补充数据");
        var source = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var f in source.Facts)
        {
            var p = source.Pages.Single(p => p.Key == f.Page);
            if (p.ByteOffset is null || !p.SourceId.StartsWith("jxclsj:", StringComparison.Ordinal)
                || f.Table < 0 || f.Table >= p.Tables.Length || f.Row < 0 || f.Row >= p.Tables[f.Table].Length
                || f.Column < 0 || f.Column >= p.Tables[f.Table][f.Row].Length
                || string.IsNullOrWhiteSpace(p.Tables[f.Table][f.Row][f.Column])
                || string.IsNullOrWhiteSpace(f.Grade) || string.IsNullOrWhiteSpace(f.Condition))
                throw new InvalidOperationException("材料补充数据缺少来源或原单元格");
        }
        return source;
    });
    internal static IReadOnlyList<ProductSpecs.Page> Pages => Loaded.Value.Pages;
    internal static IReadOnlyList<Item> Items => Loaded.Value.Facts;
    private static readonly Lazy<HashSet<string>> RowIds = new(() => Items.Select(i =>
        (i.Family == "钢" ? "steel:" : "supplement:") + i.Page + ":" + i.Table + ":" + i.Row).ToHashSet());
    internal static bool IsSupplement(CastIronLookup.Fact fact) => RowIds.Value.Contains(fact.RowId);
    internal static IReadOnlyList<CastIronLookup.Fact> Facts => Loaded.Value.Facts.Select(f =>
    {
        var p = Pages.Single(p => p.Key == f.Page);
        var raw = p.Tables[f.Table][f.Row][f.Column];
        return new CastIronLookup.Fact(f.Tool, f.Family, f.Grade, f.Property, f.Condition + " [" + p.SourceId + "]",
            f.Prefix + raw + (f.Unit.Length == 0 ? "" : " " + f.Unit), p.SourceId + " " + p.Title,
            (f.Family == "钢" ? "steel:" : "supplement:") + f.Page + ":" + f.Table + ":" + f.Row);
    }).ToArray();
    internal static string[] Notes(IEnumerable<CastIronLookup.Fact> facts)
    {
        var ids = facts.Select(f => f.RowId).ToHashSet();
        return Pages.Where(p => ids.Any(id => id.StartsWith((p.Path.Contains("/钢/") ? "steel:" : "supplement:") + p.Key + ":", StringComparison.Ordinal)))
            .Select(p => p.Notes).Where(n => n.Length > 0).Distinct().ToArray();
    }
}
