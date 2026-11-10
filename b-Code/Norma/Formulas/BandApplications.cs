using System.Text.Json;

namespace Norma.Formulas;

// REQ-BASIC-019: code-level shaft guidance belongs to the individual band card.
internal static class BandApplications
{
    internal const string Property = "基本偏差应用";
    internal sealed record Entry(string Code, string Text, string ReferenceCategory, int Row, int Column, int KeyRow, int KeyColumn);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, JsonElement[][] Rows, Entry[] Entries);
    private static readonly Lazy<Page> Loaded = new(() =>
    {
        using var stream = typeof(BandApplications).Assembly.GetManifestResourceStream("Norma.Formulas.band-applications.json")
            ?? throw new InvalidOperationException("缺少轴基本偏差应用来源");
        var page = JsonSerializer.Deserialize<Page>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (page.Id != "13-02:46" || page.ByteOffset != 4412692 || page.Rows.Length != 12
            || page.Entries.Length != 21 || page.Entries.Select(e => e.Code).Distinct().Count() != 21
            || page.Entries.Any(e => e.Code != e.Code.ToLowerInvariant() || string.IsNullOrWhiteSpace(e.Text)))
            throw new InvalidOperationException("轴基本偏差应用来源不完整");
        return page;
    });
    internal static Page Source => Loaded.Value;
    internal static bool Has(string code) => Source.Entries.Any(e => e.Code == code);
    internal static bool Matches(string standard) => BandLimits.Matches(Source.Standard, standard);
    internal static object Resolve(string code)
    {
        var e = Source.Entries.Single(e => e.Code == code);
        return new { value = e.Text, code, scope = "轴基本偏差", referenceCategory = e.ReferenceCategory,
            source = new { id = Source.Id, byteOffset = Source.ByteOffset, standard = Source.Standard, title = Source.Title,
                row = e.Row, column = e.Column, keyRow = e.KeyRow, keyColumn = e.KeyColumn },
            notes = new[] { "代号层面的选用参考；文中IT等级是推荐范围，不是查询适用等级限制。",
                "参考分类按基孔制语境；实际配合类型须结合孔轴完整代号及基本尺寸。" } };
    }
}
