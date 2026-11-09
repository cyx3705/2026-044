using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-MATERIAL-009: explicit source grades, conditions, old-name links and physical data.
internal static class NonFerrousMaterials
{
    internal sealed record Source(SteelMaterials.Table[] Tables, Dictionary<string, string[]> Aliases);
    private static readonly Lazy<Source> Loaded = new(() =>
    {
        using var stream = typeof(NonFerrousMaterials).Assembly.GetManifestResourceStream("Norma.Formulas.nonferrous.json")
            ?? throw new InvalidOperationException("缺少有色/高温/耐蚀/粉末冶金牌号数据");
        var source = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (source.Tables.Any(t => t.GradeColumn < 0 || t.GradeColumn >= t.Fields.Length || t.Rows.Any(r => r.Length != t.Fields.Length)))
            throw new InvalidOperationException("材料牌号表列数异常");
        return source;
    });
    internal static IReadOnlyList<SteelMaterials.Table> Tables => Loaded.Value.Tables;
    internal static IReadOnlyDictionary<string, string[]> Aliases => Loaded.Value.Aliases;
    private static readonly Lazy<CastIronLookup.Fact[]> Data = new(() => SteelMaterials.Build(Tables));
    internal static IReadOnlyList<CastIronLookup.Fact> Facts => Data.Value;
    internal static Dictionary<string, string> Notes(IEnumerable<CastIronLookup.Fact> facts)
    {
        var ids = facts.Where(f => f.RowId.StartsWith("nonferrous:", StringComparison.Ordinal))
            .Select(f => string.Join(':', f.RowId.Split(':').Take(3))).ToHashSet();
        return Tables.Where(t => ids.Contains(t.Id)).GroupBy(t => t.SourceId).Select(g => g.First())
            .Select(t => (t.SourceId, Note: Regex.Match(t.Notes, @"(?:注[：:]|[①②③④⑤⑥]).*", RegexOptions.Singleline).Value))
            .Where(x => x.Note.Length > 0).ToDictionary(x => x.SourceId, x => x.Note);
    }
}
