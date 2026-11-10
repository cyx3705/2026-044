using System.Globalization;
using System.Text.Json;

namespace Norma.Formulas;

// REQ-BASIC-027: the fixed C=1:10 reference belongs to the existing individual band key.
internal static class ConeAxialDeviations
{
    internal const string Property = "圆锥轴向基本偏差";
    internal sealed record Row(double Lower, double Upper, string[] Values);
    private sealed record Data(string Id, long ByteOffset, string Title, string Standard, string Unit, double Taper,
        string SourceKind, string[] Images, string[] Columns, Row[] Rows);
    private static readonly Lazy<Data> Loaded = new(() =>
    {
        using var stream = typeof(ConeAxialDeviations).Assembly.GetManifestResourceStream("Norma.Formulas.cone-axial-deviations.json")!;
        var d = JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (d.Rows.Length != 25 || d.Columns.Length != 31 || d.Rows.Any(r => r.Values.Length != 31 || r.Lower >= r.Upper))
            throw new InvalidOperationException("圆锥轴向基本偏差图表不完整");
        return d;
    });
    internal static IReadOnlyList<Row> Rows => Loaded.Value.Rows;
    internal static bool Has(string designation, double size, string standard) => size <= 500 && designation == designation.ToLowerInvariant()
        && BandLimits.Matches(Loaded.Value.Standard, standard);
    internal static object Resolve(string designation, int grade, double size)
    {
        var d = Loaded.Value;
        var row = Array.FindIndex(d.Rows, r => size > r.Lower && size <= r.Upper);
        var key = designation == "j" ? grade is 5 or 6 ? "j56" : grade == 7 ? "j7" : grade == 8 ? "j8" : ""
            : designation == "k" ? grade is >= 4 and <= 7 ? "k47" : "kother" : designation;
        var col = Array.IndexOf(d.Columns, key);
        var raw = row >= 0 && col >= 0 ? d.Rows[row].Values[col] : "—";
        double? value = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
        object[] dependencies = [];
        if (designation == "js")
        {
            var it = BasicStandards.Pages[2];
            var ri = Enumerable.Range(3, it.Table.Length - 3).First(i => size <= double.Parse(it.Table[i][1], CultureInfo.InvariantCulture));
            var text = it.Table[ri][grade + 1];
            value = size <= 1 && grade >= 14 ? null : double.Parse(text.Replace(" ", ""), CultureInfo.InvariantCulture) * (grade <= 11 ? 1 : 1000) / 200;
            dependencies = [new { id = it.Id, row = ri, column = grade + 1, raw = text }];
        }
        return new { value, unit = d.Unit, raw, symbol = "ez", conditions = new { taper = d.Taper },
            expression = designation == "js" ? "+Tze/2；Tze=IT/C（直径IT由μm换算mm）" : null,
            reason = value is null ? "原表未列此代号/等级组合或所需IT值未定义；不外推、不补零" : null,
            source = new { id = d.Id, byteOffset = d.ByteOffset, title = d.Title, standard = d.Standard, kind = d.SourceKind,
                panel = col <= 14 ? 0 : 1, row, column = col, images = d.Images }, dependencies,
            notes = new[] { "仅外圆锥C=1:10、基本直径≤500mm的轴向基本偏差；不是圆柱直径偏差。任意锥度由cone.fundamental换算。js按本页+Tze/2，不能套用直径js奇数减1规则。" } };
    }
}
