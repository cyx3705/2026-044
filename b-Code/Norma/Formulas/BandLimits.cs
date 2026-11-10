using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-BASIC-012/013: original hole/shaft limits are facts on a band, never another public lookup.
internal static class BandLimits
{
    internal sealed record Cell(string Text, int[] Origin, int Rowspan, int Colspan);
    internal sealed record Entry(string Code, int Grade, double Lower, double Upper, string Raw, int Row, int Column, string Unit, string? Image = null);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, string[] Notes, Cell[][] Table, Entry[] Records,
        string SourceKind = "native-dom", string[]? ImageReferences = null);
    private sealed record Data(Page[] Pages);
    private static readonly Lazy<Data> Loaded = new(() =>
    {
        var pages = new List<Page>();
        foreach (var resource in new[] { "hole-limit-deviations", "shaft-limit-deviations" })
        {
            using var stream = typeof(BandLimits).Assembly.GetManifestResourceStream($"Norma.Formulas.{resource}.json")
                ?? throw new InvalidOperationException("缺少极限偏差原表：" + resource);
            pages.AddRange(JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Pages);
        }
        var data = new Data(pages.ToArray());
        if (data.Pages.Length != 31) throw new InvalidOperationException("孔轴极限偏差页数不完整");
        foreach (var (p, i) in data.Pages.Select((p, i) => (p, i)))
        {
            if (p.Id != $"13-02:{i + 5}" || p.ByteOffset <= 0 || p.Records.Length == 0
                || p.Records.Any(r => r.Lower >= r.Upper || r.Grade is < 1 or > 18
                    || p.Table[r.Row][r.Column].Text != r.Raw
                    || !p.Table[r.Row][r.Column].Origin.SequenceEqual(new[] { r.Row, r.Column })))
                throw new InvalidOperationException("孔轴极限偏差来源定位或投影不一致：" + p.Id);
        }
        return data;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    internal static IEnumerable<Page> HolePages => Pages.Take(15);
    internal static IEnumerable<Page> ShaftPages => Pages.Skip(15);
    private static readonly Lazy<Dictionary<string, Page>> ByCode = new(() => Pages
        .SelectMany(p => p.Records.Select(r => r.Code).Distinct().Select(c => (Code: c, Page: p)))
        .ToDictionary(x => x.Code, x => x.Page, StringComparer.Ordinal));
    internal static Page? ForCode(string code) => ByCode.Value.GetValueOrDefault(code);
    internal static bool Matches(string source, string filter) => filter.Length == 0
        || Regex.Replace(source, @"\s+", "").Contains(filter, StringComparison.OrdinalIgnoreCase);

    internal static Dictionary<string, object> Resolve(string code, int grade, double lower, double upper)
    {
        var page = ForCode(code) ?? throw new ArgumentException("无此公差带代号");
        var shaft = code == code.ToLowerInvariant();
        var reference = code == "J" && grade >= 9;
        var valuePage = reference ? ForCode("JS")! : page;
        var entry = valuePage.Records.SingleOrDefault(r => r.Code == (reference ? "JS" : code)
            && r.Grade == grade && lower >= r.Lower && upper <= r.Upper);
        var raw = entry?.Raw ?? "";
        string? reason = entry is null ? "原表未列此尺寸或等级组合，不外推" : raw.Length == 0 ? "原表此组合为空，不补0、不自动代用其他公差带" : null;
        if (upper <= 1 && (code is "A" or "B" or "a" or "b" || (code is "H" or "JS" or "h" or "js" || reference) && grade >= 14 || code == "N" && grade >= 9))
            reason = "基本尺寸≤1mm时此组合不采用（原表脚注）";
        double? es = null, ei = null;
        if (raw.StartsWith('±'))
        {
            if (double.TryParse(raw[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var half)) { es = half; ei = -half; }
        }
        else
        {
            // Spaces within thousands are not pair separators. Match exactly two numbers.
            var match = Regex.Match(raw, @"^([+\-]?\d+(?: \d{3})*(?:\.\d+)?)(?: |(?=[+\-]))([+\-]?\d+(?: \d{3})*(?:\.\d+)?)$");
            if (match.Success)
            {
                es = double.Parse(match.Groups[1].Value.Replace(" ", ""), CultureInfo.InvariantCulture);
                ei = double.Parse(match.Groups[2].Value.Replace(" ", ""), CultureInfo.InvariantCulture);
            }
        }
        if (reason is null && (es is null || ei is null)) reason = "原文数值格式异常，未猜测修正";
        if (reason is null && es < ei) reason = "原表上偏差小于下偏差，数值待核，不交换或改符号";
        var notes = valuePage.Notes.ToList();
        if (reference) notes.Add("按孔J原表脚注1明确引用JS9、JS10等；不是公式补值。");
        if (lower >= 500 && grade <= 5 && code is "H" or "JS" or "h" or "js") notes.Add("大于500至3150mm的IT1至IT5为试用值。");
        object Fact(bool high) => new
        {
            value = reason is null ? high ? es : ei : null,
            unit = entry?.Unit ?? (code is "H" or "JS" or "h" or "js" && grade >= 12 ? "mm" : "μm"), raw, reason,
            symbol = high ? shaft ? "es" : "ES" : shaft ? "ei" : "EI", expression = reference ? "原孔J表脚注1引用JS" : null,
            source = new { id = valuePage.Id, byteOffset = valuePage.ByteOffset, standard = valuePage.Standard,
                row = entry?.Row, column = entry?.Column, kind = valuePage.SourceKind, image = entry?.Image },
            notes = notes.ToArray(),
            dependencies = reference ? new object[] { new { id = page.Id, byteOffset = page.ByteOffset, standard = page.Standard, note = 1 } } : Array.Empty<object>()
        };
        return new() { ["上偏差"] = Fact(true), ["下偏差"] = Fact(false) };
    }
}
