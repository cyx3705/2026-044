using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Norma.Formulas;

/// <summary>一种硬度制：键、显示名。硬度制本身无量纲。</summary>
public sealed record MetalScale(string Key, string Title);

/// <summary>材料族：钢、变形铝合金、铜合金。</summary>
public sealed record MetalFamily(string Key, string Title);

/// <summary>一条强度输出：键、显示名、节点表中的列名。</summary>
public sealed record StrengthSpec(string Key, string Title, string Column);

/// <summary>强度口径：钢种列、铝合金牌号或铜合金板材/棒材列。</summary>
public sealed record MetalGrade(string Key, string Title, string Family, IReadOnlyList<StrengthSpec> Strengths);

/// <summary>
/// 一次硬度—强度换算：已定义的各种硬度制、无定义项及原因、选定牌号的强度、以及数据缺口说明。
/// </summary>
public sealed record MetalResult(
    string Family,
    string Scale,
    double Value,
    string Grade,
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, string> Undefined,
    IReadOnlyDictionary<string, double> Strengths,
    IReadOnlyDictionary<string, string> StrengthUndefined,
    IReadOnlyList<string> Notes);

/// <summary>
/// 黑色金属与有色合金的硬度—强度换算：手册「黑色金属硬度及强度换算」「钢的肖氏与洛氏硬度换算」
/// 「变形铝合金」「黄铜/铍青铜」几张表的 PCHIP 保形插值。节点表是嵌入资源；
/// 每种硬度制、每个牌号只在原表出现过的范围内有定义，缺口不插值、不外推。
/// </summary>
public static class MetalStrength
{
    public const string GenericSteel = "generic";

    public const string Source =
        "机械工程师手册 sjzl 章「黑色金属硬度及强度换算」「钢的肖氏与洛氏硬度换算」「变形铝合金」「黄铜、铍青铜」";

    public const string Scope =
        "钢、变形铝合金、铜合金。各硬度制按原表定义域取值，超出定义域返回「无定义」；" +
        "铜表 HB 222–286、铝表 HB 165–175 的硬度列在原表中缺行或重复，按无定义处理，不做插值或外推。";

    public static readonly IReadOnlyList<MetalFamily> Families =
    [
        new("steel", "钢"),
        new("aluminum", "变形铝合金"),
        new("copper", "铜合金"),
    ];

    /// <summary>全部硬度制，页面与参数顺序一致。</summary>
    public static readonly IReadOnlyList<MetalScale> Scales =
    [
        new("hrc", "HRC"),
        new("hra", "HRA"),
        new("hr15n", "HR15N"),
        new("hr30n", "HR30N"),
        new("hr45n", "HR45N"),
        new("hv", "HV"),
        new("hb", "HB"),
        new("hrb", "HRB"),
        new("hrf", "HRF"),
        new("hr15t", "HR15T"),
        new("hr30t", "HR30T"),
        new("hr45t", "HR45T"),
        new("hbs", "HBS"),
        new("hs", "HS"),
    ];

    /// <summary>全部强度口径；每族只取自己那部分。</summary>
    public static readonly IReadOnlyList<MetalGrade> Grades =
    [
        new("generic", "不分钢种", "steel", [new("sigma_b", "σb", "generic")]),
        new("carbon", "碳钢", "steel", [new("sigma_b", "σb", "carbon")]),
        new("chrome", "铬钢", "steel", [new("sigma_b", "σb", "chrome")]),
        new("chrome_ba", "铬钡钢", "steel", [new("sigma_b", "σb", "chrome_ba")]),
        new("chrome_ni", "铬镍钢", "steel", [new("sigma_b", "σb", "chrome_ni")]),
        new("chrome_mo", "铬钼钢", "steel", [new("sigma_b", "σb", "chrome_mo")]),
        new("chrome_mo_ni", "铬钼镍钢", "steel", [new("sigma_b", "σb", "chrome_mo_ni")]),
        new("chrome_si_mn", "铬硅锰钢", "steel", [new("sigma_b", "σb", "chrome_si_mn")]),
        new("ultra", "超高强度钢", "steel", [new("sigma_b", "σb", "ultra")]),
        new("stainless", "不锈钢", "steel", [new("sigma_b", "σb", "stainless")]),

        new("a211_212_annealed", "2A11/2A12 退火、淬火人工时效", "aluminum",
            [new("sigma_b", "σb", "a211_212_annealed")]),
        new("a7a04", "7A04", "aluminum", [new("sigma_b", "σb", "a7a04")]),
        new("a2a50", "2A50", "aluminum", [new("sigma_b", "σb", "a2a50")]),
        new("a2a14", "2A14", "aluminum", [new("sigma_b", "σb", "a2a14")]),
        new("a211_212_natural", "2A11/2A12 淬火自然时效", "aluminum",
            [new("sigma_b", "σb", "a211_212_natural")]),
        new("a2a50_a2a14_natural", "2A50/2A14 淬火自然时效", "aluminum",
            [new("sigma_b", "σb", "a2a50_a2a14_natural")]),
        new("aluminum_generic", "变形铝合金", "aluminum", [new("sigma_b", "σb", "aluminum_generic")]),

        new("brass_plate", "黄铜板材", "copper", [new("sigma_b", "σb", "brass_plate_sb")]),
        new("brass_bar", "黄铜棒材", "copper", [new("sigma_b", "σb", "brass_bar_sb")]),
        new("beryllium_plate", "铍青铜板材", "copper",
        [
            new("sigma_b", "σb", "be_plate_sb"),
            new("sigma_02", "σ0.2", "be_plate_s02"),
            new("sigma_001", "σ0.01", "be_plate_s001"),
        ]),
        new("beryllium_bar", "铍青铜棒材", "copper",
        [
            new("sigma_b", "σb", "be_bar_sb"),
            new("sigma_02", "σ0.2", "be_bar_s02"),
            new("sigma_001", "σ0.01", "be_bar_s001"),
        ]),
    ];

    /// <summary>钢种口径（兼容既有调用）。</summary>
    public static IReadOnlyList<MetalGrade> Steels { get; } =
        Grades.Where(grade => grade.Family == "steel").ToList();

    private static readonly string[] SteelScaleKeys =
        ["hrc", "hra", "hr15n", "hr30n", "hr45n", "hv", "hb", "hrb", "hr15t", "hr30t", "hr45t", "hbs", "hs"];

    private static readonly string[] HighScaleKeys = ["hrc", "hra", "hr15n", "hr30n", "hr45n", "hv", "hb"];

    private static readonly string[] LowScaleKeys = ["hrb", "hr15t", "hr30t", "hr45t", "hv", "hbs"];

    private static readonly string[] AluminumScaleKeys = ["hb", "hv", "hrb", "hrf", "hr15t", "hr30t", "hr45t"];

    private static readonly string[] CopperScaleKeys =
        ["hb", "hv", "hrb", "hrf", "hr15t", "hr30t", "hr45t", "hrc", "hra", "hr15n", "hr30n", "hr45n"];

    private static readonly Lazy<MetalTable> High =
        new(() => MetalTable.Load("Norma.Formulas.metal-high.tsv", "hrc"));

    private static readonly Lazy<MetalTable> Low =
        new(() => MetalTable.Load("Norma.Formulas.metal-low.tsv", "hrb"));

    private static readonly Lazy<MetalTable> Shore =
        new(() => MetalTable.Load("Norma.Formulas.metal-shore.tsv", "hrc"));

    private static readonly Lazy<MetalTable> Aluminum =
        new(() => MetalTable.Load("Norma.Formulas.metal-aluminum.tsv", "hb"));

    private static readonly Lazy<MetalTable> CopperLow =
        new(() => MetalTable.Load("Norma.Formulas.metal-copper-low.tsv", "hb"));

    private static readonly Lazy<MetalTable> CopperHigh =
        new(() => MetalTable.Load("Norma.Formulas.metal-copper-high.tsv", "hb"));

    public static string ScaleTitle(string key)
        => Scales.FirstOrDefault(scale => scale.Key == key)?.Title ?? key;

    public static string GradeTitle(string key)
        => Grades.FirstOrDefault(grade => grade.Key == key)?.Title ?? key;

    public static string FamilyTitle(string key)
        => Families.FirstOrDefault(family => family.Key == key)?.Title ?? key;

    public static string StrengthTitle(string key) => key switch
    {
        "sigma_b" => "σb",
        "sigma_02" => "σ0.2",
        "sigma_001" => "σ0.01",
        "sigma_b_generic" => "σb(不分钢种)",
        _ => key,
    };

    public static IReadOnlyList<string> FamilyScaleKeys(string familyKey) => familyKey switch
    {
        "steel" => SteelScaleKeys,
        "aluminum" => AluminumScaleKeys,
        "copper" => CopperScaleKeys,
        _ => [],
    };

    public static IReadOnlyList<MetalGrade> FamilyGrades(string familyKey)
        => Grades.Where(grade => grade.Family == familyKey).ToList();

    public static string DefaultGrade(string familyKey) => familyKey switch
    {
        "steel" => GenericSteel,
        "aluminum" => "aluminum_generic",
        "copper" => "brass_plate",
        _ => throw new ArgumentException($"未知材料族 {familyKey}"),
    };

    /// <summary>
    /// 一次换算。材料族、硬度制、牌号不认识，或硬度值超出该族所有定义域时抛 <see cref="ArgumentException"/>；
    /// 单个输出硬度制或牌号超出自己的定义域时记入无定义字典，不算失败。
    /// </summary>
    public static MetalResult Convert(string familyKey, string scaleKey, double value, string? gradeKey)
    {
        var family = Families.FirstOrDefault(item => string.Equals(item.Key, familyKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"未知材料族 {familyKey}；可选 {string.Join("/", Families.Select(item => item.Key))}");
        var scale = Scales.FirstOrDefault(item => string.Equals(item.Key, scaleKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"未知硬度制 {scaleKey}；可选 {string.Join("/", Scales.Select(item => item.Key))}");
        if (!double.IsFinite(value))
            throw new ArgumentException("硬度值必须是有限数");

        var keys = FamilyScaleKeys(family.Key);
        if (!keys.Contains(scale.Key, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"{family.Title} 不支持 {scale.Title}；可用 {string.Join("/", keys)}");

        var grade = ResolveGrade(family.Key, gradeKey);
        return family.Key switch
        {
            "steel" => ConvertSteel(scale, value, grade),
            "aluminum" => ConvertSimple(family, [Aluminum.Value], scale, value, grade),
            "copper" => ConvertSimple(family, [CopperLow.Value, CopperHigh.Value], scale, value, grade),
            _ => throw new ArgumentException($"未知材料族 {family.Key}"),
        };
    }

    private static MetalGrade ResolveGrade(string familyKey, string? gradeKey)
    {
        var grades = FamilyGrades(familyKey);
        if (string.IsNullOrWhiteSpace(gradeKey))
            return grades.Single(grade => grade.Key == DefaultGrade(familyKey));

        var grade = Grades.FirstOrDefault(item => string.Equals(item.Key, gradeKey, StringComparison.OrdinalIgnoreCase));
        if (grade is null || grade.Family != familyKey)
            throw new ArgumentException(
                $"{FamilyTitle(familyKey)} 不支持牌号 {gradeKey}；可选 {string.Join("/", grades.Select(item => item.Key))}");
        return grade;
    }

    private static MetalResult ConvertSteel(MetalScale scale, double value, MetalGrade grade)
    {
        var high = High.Value;
        var low = Low.Value;
        var shore = Shore.Value;
        var key = scale.Key;
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        var undefined = new Dictionary<string, string>(StringComparer.Ordinal);
        double? hrc = null;
        double? hrb = null;
        bool highCovers;
        bool lowCovers;

        if (key == "hs")
        {
            if (!shore.Covers("hs", value))
                throw new ArgumentException($"{scale.Title}={Num.Format(value)} 超出定义域：{DomainText("steel", "hs")}");
            hrc = shore.Curve("hs").Invert(value);
            values["hs"] = value;
            if (high.Covers("hrc", hrc.Value))
                AddBranch(high, hrc.Value, HighScaleKeys, values, undefined, "steel");
            highCovers = false;
            lowCovers = false;
        }
        else
        {
            highCovers = high.Has(key) && high.Covers(key, value);
            lowCovers = low.Has(key) && low.Covers(key, value);
            if (!highCovers && !lowCovers)
                throw new ArgumentException($"{scale.Title}={Num.Format(value)} 超出定义域：{DomainText("steel", key)}");

            if (highCovers)
            {
                hrc = key == "hrc" ? value : high.Curve(key).Invert(value);
                AddBranch(high, hrc.Value, HighScaleKeys, values, undefined, "steel");
            }

            if (lowCovers)
            {
                hrb = key == "hrb" ? value : low.Curve(key).Invert(value);
                AddBranch(low, hrb.Value, LowScaleKeys, values, undefined, "steel");
            }

            // 两段在 HV 上重叠：用 HV 把另一段的硬度制补齐，不覆盖已有值。
            if (highCovers && values.TryGetValue("hv", out var hvHigh) && low.Covers("hv", hvHigh))
            {
                hrb ??= low.Curve("hv").Invert(hvHigh);
                AddBranch(low, hrb.Value, LowScaleKeys.Where(item => item != "hv").ToArray(), values, undefined, "steel");
            }

            if (lowCovers && values.TryGetValue("hv", out var hvLow) && high.Covers("hv", hvLow))
            {
                hrc ??= high.Curve("hv").Invert(hvLow);
                AddBranch(high, hrc.Value, HighScaleKeys.Where(item => item != "hv").ToArray(), values, undefined, "steel");
            }
        }

        if (hrc is not null && shore.Curve("hs").CoversX(hrc.Value))
            values["hs"] = shore.Curve("hs").Evaluate(hrc.Value);

        foreach (var item in Scales)
            if (!values.ContainsKey(item.Key))
                undefined.TryAdd(item.Key,
                    SteelScaleKeys.Contains(item.Key) ? DomainText("steel", item.Key) : "钢不使用该硬度制");

        var strengths = new Dictionary<string, double>(StringComparer.Ordinal);
        var strengthUndefined = new Dictionary<string, string>(StringComparer.Ordinal);
        var spec = grade.Strengths[0];
        var selected = SteelSigma(high, low, hrc, hrb, grade.Key, lowCovers && !highCovers, out var why);
        if (selected is { } sigma)
            strengths[spec.Key] = sigma;
        else
            strengthUndefined[spec.Key] = why ?? $"{grade.Title} 在该硬度下无定义";

        if (grade.Key != GenericSteel && hrc is not null
            && high.Has(GenericSteel) && high.Curve(GenericSteel).CoversX(hrc.Value))
            strengths["sigma_b_generic"] = high.Curve(GenericSteel).Evaluate(hrc.Value);

        var rounded = values.ToDictionary(
            pair => pair.Key,
            pair => Round(pair.Key, pair.Value),
            StringComparer.Ordinal);
        var roundedStrengths = strengths.ToDictionary(
            pair => pair.Key,
            pair => Math.Round(pair.Value, 0, MidpointRounding.AwayFromZero),
            StringComparer.Ordinal);
        return new MetalResult(
            "steel", scale.Key, value, grade.Key, rounded, undefined, roundedStrengths, strengthUndefined, []);
    }

    private static MetalResult ConvertSimple(
        MetalFamily family,
        IReadOnlyList<MetalTable> tables,
        MetalScale scale,
        double value,
        MetalGrade grade)
    {
        var covering = tables
            .Where(table => table.Has(scale.Key) && table.Covers(scale.Key, value))
            .ToList();
        if (covering.Count == 0)
            throw new ArgumentException($"{scale.Title}={Num.Format(value)} 超出定义域：{DomainText(family.Key, scale.Key)}");

        var table = covering[0];
        var coordinate = scale.Key == table.Coordinate
            ? value
            : table.Curve(scale.Key).Invert(value);
        if (!double.IsFinite(coordinate))
            throw new ArgumentException($"{scale.Title}={Num.Format(value)} 无法换算到 {table.Coordinate.ToUpperInvariant()}");

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        var undefined = new Dictionary<string, string>(StringComparer.Ordinal);
        AddBranch(table, coordinate, FamilyScaleKeys(family.Key), values, undefined, family.Key);
        values[scale.Key] = value;

        var supported = FamilyScaleKeys(family.Key);
        foreach (var item in Scales)
            if (!values.ContainsKey(item.Key))
                undefined.TryAdd(item.Key,
                    supported.Contains(item.Key) ? DomainText(family.Key, item.Key) : $"{family.Title}不使用该硬度制");

        var strengths = new Dictionary<string, double>(StringComparer.Ordinal);
        var strengthUndefined = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var spec in grade.Strengths)
        {
            if (table.Has(spec.Column) && table.Curve(spec.Column).CoversX(coordinate))
                strengths[spec.Key] = table.Curve(spec.Column).Evaluate(coordinate);
            else
                strengthUndefined[spec.Key] = $"{grade.Title} 仅在 {GradeDomainText(tables, grade, spec)} 有定义";
        }

        var notes = new List<string>();
        if (family.Key == "aluminum" && scale.Key == "hb" && value >= 165)
            notes.Add("原表 HB 165–175 的硬度列重复，硬度制不输出；σb 仍按 a211_212_annealed 列。");

        var rounded = values.ToDictionary(
            pair => pair.Key,
            pair => Round(pair.Key, pair.Value),
            StringComparer.Ordinal);
        var roundedStrengths = strengths.ToDictionary(
            pair => pair.Key,
            pair => Math.Round(pair.Value, 0, MidpointRounding.AwayFromZero),
            StringComparer.Ordinal);
        return new MetalResult(
            family.Key, scale.Key, value, grade.Key, rounded, undefined, roundedStrengths, strengthUndefined, notes);
    }

    private static void AddBranch(
        MetalTable table,
        double coordinate,
        IReadOnlyList<string> keys,
        Dictionary<string, double> values,
        Dictionary<string, string> undefined,
        string familyKey)
    {
        foreach (var key in keys)
        {
            if (values.ContainsKey(key))
                continue;
            if (key == table.Coordinate)
            {
                values[key] = coordinate;
                continue;
            }

            if (!table.Has(key))
            {
                undefined.TryAdd(key, DomainText(familyKey, key));
                continue;
            }

            var curve = table.Curve(key);
            if (curve.CoversX(coordinate))
                values[key] = curve.Evaluate(coordinate);
            else
                undefined.TryAdd(key, DomainText(familyKey, key));
        }
    }

    private static double? SteelSigma(
        MetalTable high,
        MetalTable low,
        double? hrc,
        double? hrb,
        string gradeKey,
        bool lowPrimary,
        out string? why)
    {
        why = null;

        // 输入本来就是低硬度段：不分钢种直接用低硬度表的 σb，保持行内一致。
        if (gradeKey == GenericSteel && lowPrimary && hrb is not null)
        {
            var lowCurve = low.Curve("sigma_b");
            if (lowCurve.CoversX(hrb.Value))
                return lowCurve.Evaluate(hrb.Value);
        }

        if (hrc is not null && high.Has(gradeKey))
        {
            var curve = high.Curve(gradeKey);
            if (curve.CoversX(hrc.Value))
                return curve.Evaluate(hrc.Value);
            why = $"{GradeTitle(gradeKey)} 仅在 {SteelGradeDomainText(gradeKey)} 有定义";
            return null;
        }

        // HRB 段经 HV 换算到 HRC 后仍能落到高硬度表的不分钢种列。
        if (gradeKey == GenericSteel && hrb is not null && low.Curve("sigma_b").CoversX(hrb.Value))
            return low.Curve("sigma_b").Evaluate(hrb.Value);

        why = hrc is null
            ? gradeKey == GenericSteel
                ? "该硬度落在 HRB 段，HRC 段无定义"
                : $"{GradeTitle(gradeKey)} 只在 HRC 段有数据"
            : $"手册表没有「{GradeTitle(gradeKey)}」列";
        return null;
    }

    private static string SteelGradeDomainText(string gradeKey)
    {
        var high = High.Value;
        if (!high.Has(gradeKey))
            return "该表无此列";
        var curve = high.Curve(gradeKey);
        return $"HRC {Num.Format(curve.MinX)}–{Num.Format(curve.MaxX)}";
    }

    private static string GradeDomainText(IReadOnlyList<MetalTable> tables, MetalGrade grade, StrengthSpec spec)
    {
        var ranges = tables
            .Where(table => table.Has(spec.Column))
            .Select(table =>
            {
                var curve = table.Curve(spec.Column);
                return $"{Num.Format(curve.MinX)}–{Num.Format(curve.MaxX)}";
            })
            .ToList();
        return ranges.Count == 0 ? "该表没有这一列" : $"HB {string.Join(" 或 ", ranges)}";
    }

    /// <summary>某硬度制在某一族里的定义域说明；同一种硬度制可能只在其中一段表里有。</summary>
    private static string DomainText(string familyKey, string scaleKey)
    {
        var title = ScaleTitle(scaleKey);

        if (familyKey == "steel")
        {
            var high = High.Value;
            var low = Low.Value;
            var shore = Shore.Value;
            if (scaleKey == "hs")
            {
                var curve = shore.Curve("hs");
                return $"HS 仅在 {Num.Format(curve.MinY)}–{Num.Format(curve.MaxY)} 有定义（HRC {Num.Format(curve.MinX)}–{Num.Format(curve.MaxX)}）";
            }

            if (scaleKey == "hv")
            {
                var (lowMin, lowMax) = low.Domain("hv");
                var (highMin, highMax) = high.Domain("hv");
                return $"HV 仅在 {Num.Format(lowMin)}–{Num.Format(lowMax)}（HRB 段）或 {Num.Format(highMin)}–{Num.Format(highMax)}（HRC 段）有定义";
            }

            if (high.Has(scaleKey))
            {
                var (min, max) = high.Domain(scaleKey);
                return $"{title} 仅在 {Num.Format(min)}–{Num.Format(max)} 有定义";
            }

            if (low.Has(scaleKey))
            {
                var (min, max) = low.Domain(scaleKey);
                return $"{title} 仅在 {Num.Format(min)}–{Num.Format(max)} 有定义";
            }

            return $"{title} 在手册表中无定义";
        }

        var tables = familyKey switch
        {
            "aluminum" => new[] { Aluminum.Value },
            "copper" => new[] { CopperLow.Value, CopperHigh.Value },
            _ => Array.Empty<MetalTable>(),
        };
        var ranges = tables
            .Where(table => table.Has(scaleKey))
            .Select(table => table.Domain(scaleKey))
            .ToList();
        if (ranges.Count == 0)
            return $"{title} 在{FamilyTitle(familyKey)}表中无定义";

        var text = string.Join(" 或 ", ranges.Select(range => $"{Num.Format(range.Min)}–{Num.Format(range.Max)}"));
        var gap = ranges.Count > 1 ? "（原表中间缺行）" : "";
        return $"{title} 仅在 {text} 有定义{gap}";
    }

    private static double Round(string key, double value)
        => key is "hv" or "hb" or "hbs"
            ? Math.Round(value, 0, MidpointRounding.AwayFromZero)
            : Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private sealed class MetalTable
    {
        private readonly List<Dictionary<string, double?>> _rows;
        private readonly ConcurrentDictionary<string, Pchip> _curves = new(StringComparer.Ordinal);

        private MetalTable(string coordinate, List<Dictionary<string, double?>> rows)
        {
            Coordinate = coordinate;
            _rows = rows;
            CoordinateMin = rows.Min(row => row[coordinate]!.Value);
            CoordinateMax = rows.Max(row => row[coordinate]!.Value);
        }

        public string Coordinate { get; }

        public double CoordinateMin { get; }

        public double CoordinateMax { get; }

        public bool Has(string column)
            => column == Coordinate || _rows.Any(row => row.TryGetValue(column, out var value) && value.HasValue);

        public Pchip Curve(string column)
        {
            if (column == Coordinate)
                throw new InvalidOperationException($"{column} 是坐标列，不是插值曲线");

            return _curves.GetOrAdd(column, key =>
            {
                var points = _rows
                    .Where(row => row.TryGetValue(key, out var value) && value.HasValue)
                    .Select(row => (row[Coordinate]!.Value, row[key]!.Value))
                    .ToList();
                return new Pchip(points);
            });
        }

        public bool Covers(string column, double value)
            => column == Coordinate
                ? value >= CoordinateMin && value <= CoordinateMax
                : Has(column) && Curve(column).CoversY(value);

        public (double Min, double Max) Domain(string column)
            => column == Coordinate
                ? (CoordinateMin, CoordinateMax)
                : (Curve(column).MinY, Curve(column).MaxY);

        public static MetalTable Load(string resource, string coordinate)
        {
            using var stream = typeof(MetalStrength).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"缺少嵌入资源 {resource}");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var header = reader.ReadLine()?.Split('\t')
                ?? throw new InvalidOperationException($"{resource} 没有表头");
            var rows = new List<Dictionary<string, double?>>();
            while (reader.ReadLine() is { } line)
            {
                if (line.Trim().Length == 0)
                    continue;
                var fields = line.Split('\t');
                if (fields.Length < header.Length)
                    throw new InvalidOperationException($"{resource} 行字段数 {fields.Length}，少于表头 {header.Length}");
                var row = new Dictionary<string, double?>(StringComparer.Ordinal);
                for (var i = 0; i < header.Length; i++)
                {
                    var text = fields[i].Trim();
                    row[header[i]] = text.Length == 0
                        ? null
                        : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                            ? value
                            : throw new InvalidOperationException($"{resource} 无法解析「{text}」");
                }

                if (row[coordinate] is null)
                    throw new InvalidOperationException($"{resource} 缺坐标 {coordinate}");
                rows.Add(row);
            }

            if (rows.Count < 2)
                throw new InvalidOperationException($"{resource} 节点不足");
            rows.Sort((a, b) => a[coordinate]!.Value.CompareTo(b[coordinate]!.Value));
            return new MetalTable(coordinate, rows);
        }
    }
}
