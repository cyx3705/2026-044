using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-BASIC-001/011: independent business queries; omitted coordinates are options, never defaults.
internal static class BasicStandards
{
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, string[] Notes, string[][] Table);
    private sealed record Source(Page[] Pages);
    private sealed record Coordinate(Page Page, int Row, double Lower, double Upper, int Grade, string Code)
    {
        internal Dictionary<string, string> Spec => Code.Length == 0 ? new() { ["基本尺寸"] = Interval(Lower, Upper), ["等级"] = "IT" + Grade }
            : new() { ["代号"] = Code, ["基本尺寸"] = Interval(Lower, Upper), ["等级"] = "IT" + Grade };
    }
    private static readonly Lazy<Source> Loaded = new(() =>
    {
        using var stream = typeof(BasicStandards).Assembly.GetManifestResourceStream("Norma.Formulas.basic-standards.json")
            ?? throw new InvalidOperationException("缺少基础标准数据");
        var source = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var dimensions = new[] { (45, 33), (45, 42), (24, 20) };
        if (source.Pages.Length != 3 || source.Pages.Where((p, i) => p.Id != $"13-02:{i + 2}"
            || p.ByteOffset <= 0 || p.Table.Length != dimensions[i].Item1
            || p.Table.Any(r => r.Length != dimensions[i].Item2)).Any())
            throw new InvalidOperationException("基础标准来源页、定位或原表列宽不完整");
        return source;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    private const string Tolerance = "标准公差", Deviation = "基本偏差", Band = "公差带";
    private static readonly string[] Codes = "a b c cd d e ef f fg g h js j k m n p r s t u v x y z za zb zc".Split(' ');
    private static string Norm(string? s) => Regex.Replace(s ?? "", @"\s+", "");
    private static string Interval(double lo, double hi) => $">{lo.ToString(CultureInfo.InvariantCulture)}~{hi.ToString(CultureInfo.InvariantCulture)}";
    private static double Number(string s) => double.Parse(Norm(s), CultureInfo.InvariantCulture);

    private static IEnumerable<Coordinate> Coordinates(string name)
    {
        var pages = name == Tolerance ? new[] { Pages[2] } : new[] { Pages[0], Pages[1] };
        foreach (var page in pages)
        {
            var start = name == Tolerance ? 3 : 4;
            for (var row = start; row < page.Table.Length; row++)
            {
                var lo = row == start ? 0 : Number(page.Table[row][0]);
                var hi = Number(page.Table[row][1]);
                // Footnotes distinguish <=1 mm from the rest of the printed <=3 mm row.
                var ranges = lo == 0 ? new[] { (0d, 1d), (1d, hi) } : new[] { (lo, hi) };
                foreach (var (lower, upper) in ranges)
                foreach (var code in name == Tolerance ? new[] { "" } : Codes.Select(c => page.Id == "13-02:3" ? c.ToUpperInvariant() : c))
                for (var grade = 1; grade <= 18; grade++)
                    yield return new(page, row, lower, upper, grade, code);
            }
        }
    }

    internal static object QueryTolerance(string? size = null, string? grade = null, string? standard = null, int offset = 0, int limit = 20,
        string? property = null, string? process = null, string? feature = null)
        => ToleranceSelection.Query(size, grade, standard, offset, limit, property, process, feature);

    internal static object QueryToleranceValue(string? size = null, string? grade = null, string? standard = null, int offset = 0, int limit = 20)
        => Query(Tolerance, Filters(("基本尺寸", size), ("等级", grade)), null, standard, offset, limit);

    internal static object QueryBand(string? band = null, string? size = null, string? property = null,
        string? standard = null, int offset = 0, int limit = 20, string? usage = null)
    {
        usage = usage?.Trim();
        if (!string.IsNullOrEmpty(usage) && usage is not ("1" or "2" or "3" or "4"))
            throw new ArgumentException("usage为常用级别1～4，精确匹配；不是IT精度等级，不支持*");
        var filters = Filters(("基本尺寸", size));
        band = band?.Trim();
        if (!string.IsNullOrEmpty(band))
        {
            // One individual tolerance band only: H7/g6 is a fit, never a band key.
            var match = Regex.Match(band, @"^([A-Za-z]+|\*)([1-9][0-9]*|\*)?$");
            if (!match.Success || band == "**") throw new ArgumentException("band只接受单个公差带代号，如H7、g6；不接受H7/g6或多个代号。裸H返回缺失等级候选；*、H*、*7可展开一个变化维度");
            filters["代号"] = match.Groups[1].Value;
            if (match.Groups[2].Success) filters["等级"] = match.Groups[2].Value;
            else if (band == "*") filters["等级"] = "*";
        }
        return Query(Band, filters, property, standard, offset, limit, usage);
    }

    private static Dictionary<string, string> Filters(params (string Key, string? Value)[] fields)
        => fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToDictionary(f => f.Key, f => f.Value!.Trim());

    // Shared source/validation implementation, never a public table-name dispatcher.
    private static object Query(string name, Dictionary<string, string> filters, string? property, string? standard, int offset, int limit, string? usage = null)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0；limit为1～100");
        property = Norm(property); standard = Norm(standard);
        var stars = (name == Band ? (filters.Any(p => p.Value == "*" && p.Key != "基本尺寸") ? 1 : 0) + (filters.GetValueOrDefault("基本尺寸") == "*" ? 1 : 0) : filters.Count(p => p.Value == "*")) + (property == "*" ? 1 : 0);
        if (stars > 1 || standard.Contains('*') || filters.Any(p => p.Value.Contains('*') && p.Value != "*")
            || property.Contains('*') && property != "*")
            throw new ArgumentException("一次最多一个完整*，用于size、grade、band或property；standard不支持*");
        var all = Coordinates(name).ToArray();
        var axes = all[0].Spec.Keys.ToArray();
        foreach (var key in filters.Keys.ToArray())
        {
            if (filters[key] == "*") continue;
            if (key == "等级")
            {
                var value = Regex.Replace(filters[key], "^IT", "", RegexOptions.IgnoreCase);
                if (!Regex.IsMatch(value, @"^(?:[1-9]|1[0-8])$") || !int.TryParse(value, out var g)) throw new ArgumentException("等级为IT1～IT18；原表未提供IT01/IT0，不估算补值");
                filters[key] = "IT" + g;
            }
            if (key == "基本尺寸" && (!double.TryParse(filters[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || size <= 0))
                throw new ArgumentException("基本尺寸须为有限正数，单位mm；仅显式*可展开尺寸分段");
        }
        bool HasLimitSource(Coordinate c) => name == Band && BandLimits.ForCode(c.Code) is { } hp && BandLimits.Matches(hp.Standard, standard);
        bool HasUsageSource(Coordinate c) => name == Band && BandUsage.Matches(c.Upper, standard);
        bool HasAxialSource(Coordinate c) => name == Band && ConeAxialDeviations.Has(c.Code, c.Upper, standard);
        // Application facts have no size axis. Validate supplied size, but never invent one.
        if (name == Band && (property == BandApplications.Property || standard.Length > 0 && BandApplications.Matches(standard) && property != ConeAxialDeviations.Property && !filters.ContainsKey("基本尺寸")))
        {
            object NoApplication() => new { status = "no_match", name, spec = filters, property, standard, value = (object?)null,
                reason = "此条件无轴基本偏差应用资料；不向孔或相邻代号套用" };
            if (!BandApplications.Matches(standard) || filters.GetValueOrDefault("基本尺寸") == "*") return NoApplication();
            var apps = all.Where(c => BandApplications.Has(c.Code))
                .Where(c => filters.Where(f => f.Key != "基本尺寸").All(f => f.Value == "*" || Match(c, f.Key, f.Value)))
                .GroupBy(c => (c.Code, c.Grade)).Select(g => g.First()).ToArray();
            var hasSize = filters.TryGetValue("基本尺寸", out var suppliedSize);
            if (!string.IsNullOrEmpty(usage) && !hasSize)
                apps = apps.Where(c => BandUsage.Pages.Any(p => BandUsage.Level(c.Code + c.Grade, p.Upper)
                    .ToString(CultureInfo.InvariantCulture) == usage)).ToArray();
            if (!string.IsNullOrEmpty(usage) && hasSize)
            {
                var sizeValue = Number(suppliedSize!);
                if (sizeValue > 3150) return NoApplication();
                apps = apps.Where(c => BandUsage.Level(c.Code + c.Grade, sizeValue).ToString(CultureInfo.InvariantCulture) == usage).ToArray();
            }
            if (apps.Length == 0) return NoApplication();
            if (property.Length > 0 && property != "*" && property != BandApplications.Property)
                return Options("property", [BandApplications.Property], offset, limit);
            var required = new List<object>();
            if (!filters.ContainsKey("代号") || !filters.ContainsKey("等级"))
                required.Add(Options("band", apps.Select(c => c.Code + c.Grade).ToArray(), offset, limit));
            if (!string.IsNullOrEmpty(usage) && !hasSize)
                required.Add(Options("size", all.Select(c => c.Spec["基本尺寸"]).Distinct().ToArray(), offset, limit));
            if (required.Count > 0) return new { status = "needs_input", @default = (object?)null, name, missing = required };
            object ApplicationCard(Coordinate c) => new { name, spec = new Dictionary<string, string> { ["公差带"] = c.Code + c.Grade },
                data = new Dictionary<string, object> { [BandApplications.Property] = BandApplications.Resolve(c.Code) } };
            if (filters.Any(f => f.Key != "基本尺寸" && f.Value == "*"))
                return Table(apps.Select(ApplicationCard).ToArray(), offset, limit);
            if (property == "*") return Table([new { property = BandApplications.Property, data = BandApplications.Resolve(apps[0].Code) }], offset, limit);
            return ApplicationCard(apps[0]);
        }
        var candidates = all.Where(c => MatchesStandard(c.Page, standard) || HasLimitSource(c) || HasUsageSource(c) || HasAxialSource(c))
            .Where(c => property != ConeAxialDeviations.Property || HasAxialSource(c))
            .Where(c => filters.All(f => f.Value == "*" || Match(c, f.Key, f.Value)))
            .Where(c => string.IsNullOrEmpty(usage) || HasUsageSource(c) && BandUsage.Level(c.Code + c.Grade, c.Upper).ToString(CultureInfo.InvariantCulture) == usage).ToArray();
        if (candidates.Length == 0) return new { status = "no_match", name, spec = filters, usage, property, standard, value = (object?)null,
            reason = "原表无此条件组合或标准版本；不插值、不外推、不回退公式估算", standards = all.Select(c => c.Page.Standard).Concat(name == Band ? BandLimits.Pages.Select(p => p.Standard).Concat(BandUsage.Pages.Select(p => p.Standard)) : []).Distinct().ToArray() };
        string[] Properties(Coordinate c)
        {
            var available = new List<string>();
            if (MatchesStandard(c.Page, standard))
                available.AddRange(name == Tolerance ? ["公差"] : c.Code.Equals("js", StringComparison.OrdinalIgnoreCase)
                    ? ["公差", "上偏差", "下偏差"] : ["公差", "基本偏差"]);
            if (HasLimitSource(c)) available.AddRange(["上偏差", "下偏差"]);
            if (HasUsageSource(c)) available.Add("常用级别");
            if (HasAxialSource(c)) available.Add(ConeAxialDeviations.Property);
            return available.Distinct().ToArray();
        }
        if (property.Length > 0 && property != "*")
        {
            var offered = candidates.SelectMany(Properties).Distinct().ToArray();
            if (!offered.Contains(property)) return Options("property", offered, offset, limit);
            candidates = candidates.Where(c => Properties(c).Contains(property)).ToArray();
        }
        var missing = axes.Where(a => !filters.ContainsKey(a) && candidates.Select(c => c.Spec[a]).Distinct().Count() > 1).ToArray();
        if (missing.Length > 0)
        {
            var options = new List<object>();
            if (name == Band && missing.Any(a => a is "代号" or "等级"))
                options.Add(Options("band", candidates.Select(c => c.Code + c.Grade).Distinct().ToArray(), offset, limit));
            foreach (var axis in missing.Where(a => name != Band || a == "基本尺寸"))
                options.Add(Options(axis == "基本尺寸" ? "size" : "grade", candidates.Select(c => c.Spec[axis]).Distinct().ToArray(), offset, limit));
            return new { status = "needs_input", @default = (object?)null, name, missing = options };
        }
        var variable = filters.FirstOrDefault(p => p.Value == "*").Key;
        var facts = candidates.Select(c => new { coordinate = c, facts = Resolve(c, name, standard) }).ToArray();
        if (filters.GetValueOrDefault("基本尺寸") == "*")
            foreach (var fact in facts) fact.facts.Remove(BandApplications.Property);
        var properties = facts.SelectMany(f => f.facts.Keys).Distinct().ToArray();
        if (property.Length > 0 && property != "*" && !properties.Contains(property)) return Options("property", properties, offset, limit);
        object Card(Coordinate c, Dictionary<string, object> values) => new { name, spec = name == Band ? new Dictionary<string, string> { ["公差带"] = c.Code + c.Grade, ["基本尺寸"] = c.Spec["基本尺寸"] } : c.Spec,
            data = property.Length > 0 && property != "*" ? values.Where(p => p.Key == property).ToDictionary() : values };
        if (variable is not null)
            return Table(facts.Select(f => Card(f.coordinate, f.facts)).ToArray(), offset, limit);
        if (property == "*") return Table(facts.SelectMany(f => f.facts.Select(v => (object)new { property = v.Key, data = v.Value })).ToArray(), offset, limit);
        return Card(facts[0].coordinate, facts[0].facts);
    }

    private static bool MatchesStandard(Page p, string s) => s.Length == 0 || Norm(p.Standard).Contains(s, StringComparison.OrdinalIgnoreCase);
    private static bool Match(Coordinate c, string key, string value) => key == "基本尺寸"
        ? Number(value) > c.Lower && Number(value) <= c.Upper : c.Spec[key] == value;
    private static object Options(string key, string[] options, int offset, int limit) => new { missing = key, @default = (object?)null,
        options = options.Skip(offset).Take(limit).ToArray(), total = options.Length, nextOffset = offset + limit < options.Length ? (int?)(offset + limit) : null };
    private static object Table(object[] rows, int offset, int limit) => new { rows = rows.Skip(offset).Take(limit).ToArray(), total = rows.Length,
        nextOffset = offset + limit < rows.Length ? (int?)(offset + limit) : null };

    private static Dictionary<string, object> Resolve(Coordinate c, string name, string standard = "")
    {
        if (name == Band)
        {
            var facts = Resolve(c, Deviation);
            var page = Pages[2];
            var row = Enumerable.Range(3, page.Table.Length - 3).First(r => c.Upper <= Number(page.Table[r][1]));
            var toleranceCoordinate = c with { Page = page, Row = row, Code = "" };
            facts["公差"] = Resolve(toleranceCoordinate, Tolerance)["公差"];
            if (BandLimits.ForCode(c.Code) is { } limitPage && BandLimits.Matches(limitPage.Standard, standard))
            {
                foreach (var fact in BandLimits.Resolve(c.Code, c.Grade, c.Lower, c.Upper)) facts[fact.Key] = fact.Value;
                // Preserve discrepancies between printed versions as evidence, never reconcile by arithmetic.
                var toleranceFact = JsonSerializer.SerializeToElement(facts["公差"]);
                var high = JsonSerializer.SerializeToElement(facts["上偏差"]);
                var low = JsonSerializer.SerializeToElement(facts["下偏差"]);
                if (toleranceFact.GetProperty("value").ValueKind == JsonValueKind.Number
                    && high.GetProperty("value").ValueKind == JsonValueKind.Number && low.GetProperty("value").ValueKind == JsonValueKind.Number)
                {
                    var tv = toleranceFact.GetProperty("value").GetDouble();
                    var hv = high.GetProperty("value").GetDouble();
                    var lv = low.GetProperty("value").GetDouble();
                    var width = (hv - lv) * (high.GetProperty("unit").GetString() == "mm" ? 1000 : 1);
                    var expected = tv * (toleranceFact.GetProperty("unit").GetString() == "mm" ? 1000 : 1);
                    if (Math.Abs(width - expected) > 1e-8)
                        foreach (var key in new[] { "上偏差", "下偏差" })
                        {
                            var item = JsonSerializer.SerializeToElement(facts[key]).EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
                            item["notes"] = ((JsonElement)item["notes"]).EnumerateArray().Select(n => n.GetString()!).Append("此原表极限偏差宽度与1998标准公差表不一致；分别保留各自来源，不自动修正。").ToArray();
                            facts[key] = item;
                        }
                }
            }
            if (ConeAxialDeviations.Has(c.Code, c.Upper, standard)) facts[ConeAxialDeviations.Property] = ConeAxialDeviations.Resolve(c.Code, c.Grade, c.Upper);
            if (BandUsage.Matches(c.Upper, standard)) facts["常用级别"] = BandUsage.Resolve(c.Code + c.Grade, c.Upper);
            if (BandApplications.Has(c.Code) && BandApplications.Matches(standard))
                facts[BandApplications.Property] = BandApplications.Resolve(c.Code);
            if (standard.Length > 0)
                facts = facts.Where(f => BandLimits.Matches(JsonSerializer.SerializeToElement(f.Value).GetProperty("source").GetProperty("standard").GetString()!, standard)).ToDictionary();
            return facts;
        }
        var p = c.Page;
        object Value(int col, string unit, string? reason = null, double? computed = null, string? expression = null, string[]? notes = null, object[]? dependencies = null, string? symbol = null)
        {
            var raw = p.Table[c.Row][col];
            double? value = reason is not null ? null : computed ?? (double.TryParse(Norm(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null);
            if (value is null && reason is null) reason = raw.Length == 0 ? "原表未给出此组合，不补0" : "原文异常或非数值，未推定修正";
            return new { value, unit, raw, reason, expression, symbol,
                source = new { id = p.Id, byteOffset = p.ByteOffset, standard = p.Standard, row = c.Row, column = col },
                notes = notes ?? Array.Empty<string>(), dependencies = dependencies ?? Array.Empty<object>() };
        }
        if (name == Tolerance)
        {
            var reason = c.Upper <= 1 && c.Grade >= 14 ? "基本尺寸≤1mm时无IT14～IT18（原表脚注）" : null;
            return new() { ["公差"] = Value(c.Grade + 1, c.Grade <= 11 ? "μm" : "mm", reason,
                notes: c.Lower >= 500 && c.Grade <= 5 ? [p.Notes[0]] : []) };
        }
        var hole = c.Code == c.Code.ToUpperInvariant();
        var code = c.Code.ToLowerInvariant();
        var restricted = c.Upper <= 1 && (code is "a" or "b" || hole && code == "n" && c.Grade > 8);
        if (code == "js")
        {
            var it = Pages[2];
            var itRow = Enumerable.Range(3, it.Table.Length - 3).First(r => c.Upper <= Number(it.Table[r][1]));
            var itRaw = it.Table[itRow][c.Grade + 1];
            var tolerance = Number(itRaw) * (c.Grade <= 11 ? 1 : 1000);
            var half = c.Grade is >= 7 and <= 11 && tolerance % 2 == 1 ? (tolerance - 1) / 2 : tolerance / 2;
            var why = c.Upper <= 1 && c.Grade >= 14 ? "基本尺寸≤1mm时无IT14～IT18；不能据表头公式补值" : null;
            var deps = new object[] { new { id = it.Id, it.ByteOffset, standard = it.Standard, row = itRow, column = c.Grade + 1, raw = itRaw } };
            var jsNotes = c.Lower >= 500 && c.Grade <= 5 ? new[] { p.Notes[1], it.Notes[0] } : new[] { p.Notes[1] };
            return new() { ["上偏差"] = Value(13, "μm", why, half, "+ITn/2（IT7～IT11奇数时取+(ITn−1)/2）", jsNotes, deps, hole ? "ES" : "es"),
                ["下偏差"] = Value(13, "μm", why, -half, "−ITn/2（IT7～IT11奇数时取−(ITn−1)/2）", jsNotes, deps, hole ? "EI" : "ei") };
        }
        int col;
        if (code == "j") col = hole ? c.Grade is >= 6 and <= 8 ? c.Grade + 8 : -1 : c.Grade is 5 or 6 ? 14 : c.Grade == 7 ? 15 : c.Grade == 8 ? 16 : -1;
        else if (code == "k") col = hole ? c.Grade <= 8 ? 17 : 18 : c.Grade is >= 4 and <= 7 ? 17 : 18;
        else if (hole && code == "m") col = c.Grade <= 8 ? 19 : 20;
        else if (hole && code == "n") col = c.Grade <= 8 ? 21 : 22;
        else
        {
            col = Array.FindIndex(p.Table[3], h => Norm(h).Equals(code, StringComparison.OrdinalIgnoreCase));
        }
        if (col < 0) return new() { ["基本偏差"] = Value(14, "μm", "原表未列此代号与等级组合") };
        var symbol = !hole ? col < 14 ? "es" : "ei" : col < 14 ? "EI" : "ES";
        var whyRestricted = restricted ? "基本尺寸≤1mm时此基本偏差不采用（原表脚注）" : null;
        var raw = Norm(p.Table[c.Row][col]);
        var notes = new List<string>();
        double? computed = null;
        string? expression = null;
        object[] dependencies = [];
        if (hole && c.Lower >= 250 && c.Upper <= 315 && code == "m" && c.Grade == 6)
        {
            computed = -9; expression = "原表特殊情况ES=−9μm"; notes.Add(p.Notes[3]);
        }
        else if (raw.Contains('Δ') || hole && c.Grade <= 7 && Array.IndexOf(Codes, code) >= Array.IndexOf(Codes, "p"))
        {
            var deltaCol = 33 + c.Grade;
            if (c.Grade < 3) whyRestricted ??= "所需Δ值未在原表IT3～IT8列中定义，不补值";
            else if (double.TryParse(raw.Replace("+Δ", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var basis)
                && double.TryParse(Norm(p.Table[c.Row][deltaCol]), NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
            {
                computed = basis + delta; expression = raw + (raw.Contains('Δ') ? "" : "+Δ") + "; Δ=" + delta.ToString(CultureInfo.InvariantCulture);
                dependencies = [new { id = p.Id, p.ByteOffset, standard = p.Standard, row = c.Row, column = deltaCol, raw = p.Table[c.Row][deltaCol] }];
                notes.Add(p.Notes[2]);
            }
            else whyRestricted ??= "所需Δ或基础数值未在原表定义，不补值";
        }
        return new() { ["基本偏差"] = Value(col, "μm", whyRestricted, computed, expression, notes.ToArray(), dependencies, symbol) };
    }
}
