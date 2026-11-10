using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-BASIC-026: AT and cone length are independent coordinates, not IT and diameter.
internal static class ConeAngleTolerances
{
    internal sealed record Entry(string Grade, double Lower, double Upper, double Microradians, double Seconds,
        string AngleRaw, string DiameterRaw, int Row, int Column);
    private sealed record Data(string Id, long ByteOffset, ConicalReferences.Cell[][] Table, Entry[] Entries);
    private static readonly Lazy<Data> Loaded = new(() =>
    {
        using var stream = typeof(ConeAngleTolerances).Assembly.GetManifestResourceStream("Norma.Formulas.cone-angle-tolerances.json")!;
        var d = JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (d.Entries.Length != 120 || d.Table.Length != 56 || d.Entries.Any(e =>
            double.Parse(d.Table[e.Row][e.Column].Raw.Replace(" ", ""), CultureInfo.InvariantCulture) != e.Microradians
            || d.Table[e.Row][e.Column + 1].Raw != e.AngleRaw || d.Table[e.Row][e.Column + 2].Raw != e.DiameterRaw))
            throw new InvalidOperationException("圆锥角公差来源不完整");
        return d;
    });
    internal static IReadOnlyList<Entry> Entries => Loaded.Value.Entries;
    internal static object Query(string? grade = null, string? length = null)
    {
        grade = grade?.Trim(); length = length?.Trim();
        if (grade == "*" && length == "*") throw new ArgumentException("一次最多一个*：grade或length");
        if (!string.IsNullOrEmpty(grade) && grade != "*" && !Regex.IsMatch(grade, @"^AT(?:[1-9]|1[0-2])$"))
            throw new ArgumentException("grade为AT1～AT12，不能填IT等级或公差带");
        double l = 0;
        if (!string.IsNullOrEmpty(length) && length != "*" && (!double.TryParse(length, NumberStyles.Float, CultureInfo.InvariantCulture, out l)
            || !double.IsFinite(l) || l <= 0)) throw new ArgumentException("length为有限正数mm，指基本圆锥长度L，不是直径或母线长");
        var ranges = Entries.DistinctBy(e => e.Lower).ToArray();
        object Range(Entry e) => new { lower = e.Lower, upper = e.Upper, lowerInclusive = e.Lower == 6, upperInclusive = true, unit = "mm" };
        var missing = new List<object>();
        if (string.IsNullOrEmpty(grade)) missing.Add(new { missing = "grade", @default = (object?)null, options = Entries.Select(e => e.Grade).Distinct().ToArray() });
        if (string.IsNullOrEmpty(length)) missing.Add(new { missing = "length", @default = (object?)null, options = ranges.Select(Range).ToArray() });
        if (missing.Count > 0) return new { status = "needs_input", @default = (object?)null, missing };
        object Card(Entry e, double? actualLength)
        {
            var diameter = actualLength is { } value ? e.Microradians * value / 1000 : (double?)null;
            // Page55 note3: retain the significant digits of the printed ATD maximum (GB/T8170).
            var maximumRaw = e.DiameterRaw.Split('～')[^1];
            var digits = maximumRaw.Replace(".", "").TrimStart('0').Length;
            var rounded = diameter is > 0 ? Math.Round(diameter.Value, digits - 1 - (int)Math.Floor(Math.Log10(diameter.Value)), MidpointRounding.ToEven) : diameter;
            // Note3 fixes listed length endpoints to the corresponding printed ATD maximum.
            if (actualLength == e.Upper)
                rounded = double.Parse(maximumRaw, CultureInfo.InvariantCulture);
            return new { grade = e.Grade, length = actualLength, lengthRange = Range(e),
                data = new Dictionary<string, object>
                {
                    ["锥角公差"] = new { value = e.Microradians, unit = "μrad", seconds = e.Seconds, secondsUnit = "″", raw = e.AngleRaw },
                    ["直径公差"] = new { value = rounded, unit = "μm", rawRange = e.DiameterRaw, expression = "ATD=ATα×L×10⁻³；原表长度上端点取所列最大值", significantDigits = digits, precision = "中间长度按原范围最大值的有效位数修约；列出长度取原表最大值" }
                }, source = new { id = Loaded.Value.Id, byteOffset = Loaded.Value.ByteOffset, row = e.Row, column = e.Column,
                    title = "圆锥角公差数值", standard = "机械工程师手册（页面未标标准号）" },
                notes = new[] { "ATα的μrad和角秒均按原表各自舍入值返回；ATD在原表长度上端点取所列最大值，其余按实际L计算并按原最大值有效位数修约（GB/T8170）；不取原区间中点。length=*仅列原范围，不虚设L。" } };
        }
        var selected = Entries.Where(e => (grade == "*" || e.Grade == grade)
            && (length == "*" || (e.Lower == 6 ? l >= 6 : l > e.Lower) && l <= e.Upper)).ToArray();
        if (selected.Length == 0) return new { status = "no_match", value = (object?)null, reason = "圆锥长度仅6～630mm，不外推" };
        if (grade == "*" || length == "*") return new { rows = selected.Select(e => Card(e, length == "*" ? null : l)).ToArray(), total = selected.Length };
        return Card(selected.Single(), l);
    }
}
