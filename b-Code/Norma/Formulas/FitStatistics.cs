using System.Text.Json;

namespace Norma.Formulas;

// REQ-BASIC-022: statistical fit limits are additional facts on the existing fit key.
internal static class FitStatistics
{
    internal sealed record Cell(string Raw, int Row, int Column, int LogicalColumn, int Rowspan, int Colspan, bool Gray);
    internal sealed record Header(int Row, int Column);
    internal sealed record Entry(string Fit, double Lower, double Upper, int Row, int Column, string Raw,
        int Section, int Usage, Header[] Headers, double? High, double? Low);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, double TToWcRatio,
        double PcMinPercent, Cell[][] Table, string[] Notes);
    private sealed record Data(Page Page, Entry[] Entries);
    internal static readonly string[] Properties = ["统计最大间隙或最小过盈", "统计最小间隙或最大过盈"];
    private static readonly Lazy<Data> Loaded = new(() =>
    {
        using var stream = typeof(FitStatistics).Assembly.GetManifestResourceStream("Norma.Formulas.fit-statistics.json")
            ?? throw new InvalidOperationException("缺少统计配合来源");
        var d = JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (d.Page.Id != "13-02:52" || d.Page.ByteOffset != 4508232 || d.Page.Table.Length != 197
            || d.Entries.Length != 2450 || d.Entries.Select(e => e.Fit).Distinct().Count() != 98
            || d.Page.TToWcRatio != 2 || d.Page.PcMinPercent != 86)
            throw new InvalidOperationException("统计配合来源不完整");
        foreach (var e in d.Entries)
        {
            if (e.Lower >= e.Upper || e.Raw != d.Page.Table[e.Row][e.Column].Raw || e.High < e.Low
                || (e.High is null) != (e.Low is null) || e.Headers.Length == 0
                || e.Headers.Any(h => d.Page.Table[h.Row][h.Column].Raw != e.Fit))
                throw new InvalidOperationException("统计配合原格或表头定位错误");
        }
        return d;
    });
    internal static Page Source => Loaded.Value.Page;
    internal static IReadOnlyList<Entry> Entries => Loaded.Value.Entries;
    internal static bool Matches(string standard) => BandLimits.Matches(Source.Standard, standard);
    internal static bool Has(string fit) => Entries.Any(e => e.Fit == fit);
    internal static Dictionary<string, object> Resolve(string fit, double lower, double upper)
    {
        var entry = Entries.SingleOrDefault(e => e.Fit == fit && e.Lower <= lower && e.Upper >= upper);
        if (entry is null) return new();
        object Fact(double? value) => new { value, unit = "μm", raw = entry.Raw,
            reason = value is null ? "统计原表为横线，无数值；不从极限间隙或孔轴偏差反算补值" : null,
            conditions = new { tToWc = "2:1", pcMin = "86%" },
            source = new { id = Source.Id, byteOffset = Source.ByteOffset, standard = Source.Standard,
                row = entry.Row, column = entry.Column, headers = entry.Headers }, notes = Source.Notes };
        return new() { [Properties[0]] = Fact(entry.High), [Properties[1]] = Fact(entry.Low) };
    }
}
