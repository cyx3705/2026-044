using System.Text.Json;

namespace Norma.Formulas;

// REQ-BASIC-014: graphical selection rank, independent of IT precision grade.
internal static class BandUsage
{
    internal sealed record Entry(string Band, string Raw, int Level, string Kind, int Row, string Column);
    internal sealed record Page(string Id, long ByteOffset, string Standard, double Lower, double Upper, string Title, Entry[] Entries);
    private sealed record Source(Page[] Pages);
    private static readonly Lazy<Source> Loaded = new(() =>
    {
        using var stream = typeof(BandUsage).Assembly.GetManifestResourceStream("Norma.Formulas.band-usage.json")
            ?? throw new InvalidOperationException("缺少公差带常用级别来源");
        var source = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (source.Pages.Length != 2 || source.Pages.Where((p, i) => p.Id != $"13-02:{36 + i}"
            || p.ByteOffset <= 0 || p.Entries.Length != (i == 0 ? 221 : 72)
            || p.Entries.Select(e => e.Band).Distinct().Count() != p.Entries.Length
            || p.Entries.Any(e => e.Level is < 2 or > 4)).Any())
            throw new InvalidOperationException("公差带选用图来源不完整");
        return source;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    private static Page ForSize(double upper) => Pages.Single(p => upper > p.Lower && upper <= p.Upper);
    internal static bool Matches(double upper, string standard) => BandLimits.Matches(ForSize(upper).Standard, standard);
    internal static int Level(string band, double upper) => ForSize(upper).Entries.SingleOrDefault(e => e.Band == band)?.Level ?? 1;
    internal static object Resolve(string band, double upper)
    {
        var p = ForSize(upper);
        var entry = p.Entries.SingleOrDefault(e => e.Band == band);
        var level = entry?.Level ?? 1;
        var label = level switch { 1 => "适用尺寸图未列出", 2 => "表中列出、常用框外", 3 => "常用框内、未圈出", _ => "常用框内且圈出" };
        return new { value = level, unit = "级", raw = entry?.Raw ?? "", reason = (string?)null,
            expression = (string?)null, symbol = (string?)null,
            source = new { id = p.Id, byteOffset = p.ByteOffset, standard = p.Standard, title = p.Title,
                kind = "image-transcription", image = p.Id == "13-02:36" ? (band == band.ToUpperInvariant() ? "./13.2-36.files/image002.jpg" : "./13.2-36.files/image004.jpg") : null,
                row = entry?.Row, column = entry?.Column, rowMeaning = "IT等级行", sizeRange = $">{p.Lower}~{p.Upper}",
                evidence = label, classification = "用户约定：图外1级、列出2级、框内3级、框内且圈出4级（取最高级）" },
            notes = new[] { label, "常用级别不是IT精度等级；未列出不等于禁用，不改变数值事实的适用性。" }
                .Concat(p.Lower == 500 ? ["大尺寸页虚线框按常用框处理；图中无圈选。"] : Array.Empty<string>()).ToArray(),
            dependencies = Array.Empty<object>() };
    }
}
