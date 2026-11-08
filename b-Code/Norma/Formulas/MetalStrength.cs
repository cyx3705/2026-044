using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Norma.Formulas;

/// <summary>一种硬度制：键、显示名。硬度制本身无量纲。</summary>
public sealed record MetalScale(string Key, string Title);

/// <summary>强度口径：手册表里的钢种列。</summary>
public sealed record MetalSteel(string Key, string Title);

/// <summary>
/// 一次硬度—强度换算：已定义的各种硬度制、无定义项及原因、选定钢种与不分钢种的抗拉强度。
/// </summary>
public sealed record MetalResult(
    string Scale,
    double Value,
    string Steel,
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, string> Undefined,
    double? SigmaB,
    double? SigmaBGeneric,
    string? SigmaBUndefined);

/// <summary>
/// 黑色金属硬度—强度换算：手册「黑色金属硬度及强度换算」两张表的 PCHIP 保形插值。
/// 数据是嵌入资源 metal-high.tsv（HRC 段）与 metal-low.tsv（HRB 段），节点即原表；
/// 每种硬度制只在表里出现过的范围内有定义，超出范围不猜、不外推。
/// </summary>
public static class MetalStrength
{
    public const string GenericSteel = "generic";

    public const string Source = "机械工程师手册 sjzl 章「黑色金属硬度及强度换算」（GB/T 1172 体系）";

    public const string Scope =
        "适用于黑色金属（钢）。各硬度制按手册表的定义域取值，超出定义域返回「该硬度无定义」；" +
        "抗拉强度按钢种列取值，不分钢种列覆盖最广。PCHIP 只在节点之间保形插值，不做外推。";

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
        new("hr15t", "HR15T"),
        new("hr30t", "HR30T"),
        new("hr45t", "HR45T"),
        new("hbs", "HBS"),
    ];

    /// <summary>强度口径；generic 是「不分钢种」。</summary>
    public static readonly IReadOnlyList<MetalSteel> Steels =
    [
        new(GenericSteel, "不分钢种"),
        new("carbon", "碳钢"),
        new("chrome", "铬钢"),
        new("chrome_ba", "铬钡钢"),
        new("chrome_ni", "铬镍钢"),
        new("chrome_mo", "铬钼钢"),
        new("chrome_mo_ni", "铬钼镍钢"),
        new("chrome_si_mn", "铬硅锰钢"),
        new("ultra", "超高强度钢"),
        new("stainless", "不锈钢"),
    ];

    private static readonly string[] HighScaleKeys = ["hrc", "hra", "hr15n", "hr30n", "hr45n", "hv", "hb"];

    private static readonly string[] LowScaleKeys = ["hrb", "hr15t", "hr30t", "hr45t", "hv", "hbs"];

    private static readonly Lazy<MetalTable> High =
        new(() => MetalTable.Load("Norma.Formulas.metal-high.tsv", "hrc"));

    private static readonly Lazy<MetalTable> Low =
        new(() => MetalTable.Load("Norma.Formulas.metal-low.tsv", "hrb"));

    public static string ScaleTitle(string key)
        => Scales.FirstOrDefault(scale => scale.Key == key)?.Title ?? key;

    public static string SteelTitle(string key)
        => Steels.FirstOrDefault(steel => steel.Key == key)?.Title ?? key;

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

    /// <summary>
    /// 一次换算。硬度制或钢种不认识、或硬度值超出所有分支的定义域时抛 <see cref="ArgumentException"/>；
    /// 单个输出硬度制超出它自己的定义域时，记入 <see cref="MetalResult.Undefined"/>，不算失败。
    /// </summary>
    public static MetalResult Convert(string scaleKey, double value, string steelKey)
    {
        var scale = Scales.FirstOrDefault(item => string.Equals(item.Key, scaleKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"未知硬度制 {scaleKey}；可选 {string.Join("/", Scales.Select(item => item.Key))}");
        var steel = Steels.FirstOrDefault(item => string.Equals(item.Key, steelKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"未知钢种 {steelKey}；可选 {string.Join("/", Steels.Select(item => item.Key))}");
        if (!double.IsFinite(value))
            throw new ArgumentException("硬度值必须是有限数");

        var high = High.Value;
        var low = Low.Value;
        var key = scale.Key;
        var highCovers = high.Has(key) && high.Covers(key, value);
        var lowCovers = low.Has(key) && low.Covers(key, value);
        if (!highCovers && !lowCovers)
            throw new ArgumentException($"{scale.Title}={Num.Format(value)} 超出定义域：{DomainText(key)}");

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        var undefined = new Dictionary<string, string>(StringComparer.Ordinal);
        double? hrc = null;
        double? hrb = null;

        if (highCovers)
        {
            hrc = key == "hrc" ? value : high.Curve(key).Invert(value);
            AddBranch(high, hrc.Value, HighScaleKeys, values, undefined);
        }

        if (lowCovers)
        {
            hrb = key == "hrb" ? value : low.Curve(key).Invert(value);
            AddBranch(low, hrb.Value, LowScaleKeys, values, undefined);
        }

        // 两段在 HV 上重叠：用 HV 把另一段的硬度制补齐，不覆盖已有值。
        if (highCovers && values.TryGetValue("hv", out var hvHigh) && low.Covers("hv", hvHigh))
        {
            hrb ??= low.Curve("hv").Invert(hvHigh);
            AddBranch(low, hrb.Value, LowScaleKeys.Where(item => item != "hv").ToArray(), values, undefined);
        }

        if (lowCovers && values.TryGetValue("hv", out var hvLow) && high.Covers("hv", hvLow))
        {
            hrc ??= high.Curve("hv").Invert(hvLow);
            AddBranch(high, hrc.Value, HighScaleKeys.Where(item => item != "hv").ToArray(), values, undefined);
        }

        // 每种硬度制都要有交代：要么给出值，要么写明该硬度无定义。
        foreach (var item in Scales)
            if (!values.ContainsKey(item.Key))
                undefined.TryAdd(item.Key, DomainText(item.Key));

        var sigma = Sigma(high, low, hrc, hrb, steel.Key, lowCovers && !highCovers, out var sigmaWhy);
        var sigmaGeneric = steel.Key == GenericSteel ? sigma : GenericSigma(high, low, hrc, hrb);

        var rounded = values.ToDictionary(
            pair => pair.Key,
            pair => Round(pair.Key, pair.Value),
            StringComparer.Ordinal);
        return new MetalResult(
            scale.Key,
            value,
            steel.Key,
            rounded,
            undefined,
            sigma is null ? null : Math.Round(sigma.Value, 0, MidpointRounding.AwayFromZero),
            sigmaGeneric is null ? null : Math.Round(sigmaGeneric.Value, 0, MidpointRounding.AwayFromZero),
            sigma is null ? sigmaWhy : null);
    }

    private static void AddBranch(
        MetalTable table,
        double coordinate,
        IReadOnlyList<string> keys,
        Dictionary<string, double> values,
        Dictionary<string, string> undefined)
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
                undefined.TryAdd(key, DomainText(key));
                continue;
            }

            var curve = table.Curve(key);
            if (curve.CoversX(coordinate))
                values[key] = curve.Evaluate(coordinate);
            else
                undefined.TryAdd(key, DomainText(key));
        }
    }

    private static double? Sigma(
        MetalTable high,
        MetalTable low,
        double? hrc,
        double? hrb,
        string steelKey,
        bool lowPrimary,
        out string? why)
    {
        why = null;

        // 输入本来就是低硬度段：不分钢种直接用低硬度表的 σb，保持行内一致。
        if (steelKey == GenericSteel && lowPrimary && hrb is not null)
        {
            var lowCurve = low.Curve("sigma_b");
            if (lowCurve.CoversX(hrb.Value))
                return lowCurve.Evaluate(hrb.Value);
        }

        if (hrc is not null && high.Has(steelKey))
        {
            var curve = high.Curve(steelKey);
            if (curve.CoversX(hrc.Value))
                return curve.Evaluate(hrc.Value);
            why = $"{SteelTitle(steelKey)} 仅在 {SteelDomainText(steelKey)} 有定义";
            return null;
        }

        // HRB 段经 HV 换算到 HRC 后仍能落到高硬度表的不分钢种列。
        if (steelKey == GenericSteel && hrb is not null && low.Curve("sigma_b").CoversX(hrb.Value))
            return low.Curve("sigma_b").Evaluate(hrb.Value);

        why = hrc is null
            ? steelKey == GenericSteel
                ? "该硬度落在 HRB 段，HRC 段无定义"
                : $"{SteelTitle(steelKey)} 只在 HRC 段有数据"
            : $"手册表没有「{SteelTitle(steelKey)}」列";
        return null;
    }

    private static double? GenericSigma(MetalTable high, MetalTable low, double? hrc, double? hrb)
    {
        if (hrc is not null && high.Has(GenericSteel) && high.Curve(GenericSteel).CoversX(hrc.Value))
            return high.Curve(GenericSteel).Evaluate(hrc.Value);
        if (hrb is not null && low.Curve("sigma_b").CoversX(hrb.Value))
            return low.Curve("sigma_b").Evaluate(hrb.Value);
        return null;
    }

    /// <summary>某硬度制的定义域说明；同一种硬度制可能只在其中一段表里有。</summary>
    private static string DomainText(string key)
    {
        var title = ScaleTitle(key);
        var high = High.Value;
        var low = Low.Value;
        if (key == "hv")
        {
            var (lowMin, lowMax) = low.Domain("hv");
            var (highMin, highMax) = high.Domain("hv");
            return $"HV 仅在 {Num.Format(lowMin)}–{Num.Format(lowMax)}（HRB 段）或 {Num.Format(highMin)}–{Num.Format(highMax)}（HRC 段）有定义";
        }

        if (high.Has(key))
        {
            var (min, max) = high.Domain(key);
            return $"{title} 仅在 {Num.Format(min)}–{Num.Format(max)} 有定义";
        }

        if (low.Has(key))
        {
            var (min, max) = low.Domain(key);
            return $"{title} 仅在 {Num.Format(min)}–{Num.Format(max)} 有定义";
        }

        return $"{title} 在手册表中无定义";
    }

    private static string SteelDomainText(string key)
    {
        var high = High.Value;
        if (!high.Has(key))
            return "该表无此列";
        var curve = high.Curve(key);
        return $"HRC {Num.Format(curve.MinX)}–{Num.Format(curve.MaxX)}";
    }

    private static double Round(string key, double value)
        => key is "hv" or "hb" or "hbs"
            ? Math.Round(value, 0, MidpointRounding.AwayFromZero)
            : Math.Round(value, 1, MidpointRounding.AwayFromZero);
}

