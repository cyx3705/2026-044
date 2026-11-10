using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-BASIC-015: a fit pair is its own business key; table names are never query parameters.
internal static class FitStandards
{
    internal sealed record Cell(string Raw, int Row, int Column, int Rowspan, int Colspan, string[] Images, bool Gray);
    internal sealed record Page(string Id, long ByteOffset, string Title, string Standard, Cell[][][] Tables, string[] Notes);
    internal sealed record Selection(string Fit, string System, int Usage, string Kind, string Page, int Table, int Row, int Column, string RawBase, string Image, string[] Notes);
    internal sealed record Limit(string Fit, string System, int Usage, double Lower, double Upper, string Page, int Table, int Row, int Column, int LogicalColumn, int HeaderRow, int HeaderColumn, string Raw, string[] Notes);
    private sealed record Data(Page[] Pages, Selection[] Selections, Limit[] Limits);
    private static readonly Lazy<Data> Loaded = new(() =>
    {
        using var stream = typeof(FitStandards).Assembly.GetManifestResourceStream("Norma.Formulas.fit-standards.json")
            ?? throw new InvalidOperationException("缺少配合原表");
        var d = JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (d.Pages.Length != 3 || d.Selections.Length != 110 || d.Limits.Length != 2650
            || d.Pages.Where((p, i) => p.Id != $"13-02:{38 + i}" || p.ByteOffset <= 0 || p.Tables.Length != (i == 2 ? 5 : 1)).Any())
            throw new InvalidOperationException("配合来源页不完整");
        foreach (var s in d.Selections)
        {
            var cell = d.Pages.Single(p => p.Id == s.Page).Tables[s.Table][s.Row][s.Column];
            if (!cell.Images.Contains(s.Image) || s.Usage != (cell.Gray ? 3 : 2))
                throw new InvalidOperationException("配合选用图定位不一致");
        }
        foreach (var l in d.Limits)
        {
            var t = d.Pages.Single(p => p.Id == l.Page).Tables[l.Table];
            if (l.Lower >= l.Upper || t[l.Row][l.Column].Raw != l.Raw
                || t[l.HeaderRow][l.HeaderColumn].Images.Length == 0
                || l.Usage != (t[l.HeaderRow][l.HeaderColumn].Gray ? 3 : 2))
                throw new InvalidOperationException("配合极限格或图形表头定位不一致");
        }
        return d;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    internal static IReadOnlyList<Selection> Selections => Loaded.Value.Selections;
    internal static IReadOnlyList<Limit> Limits => Loaded.Value.Limits;
    private static readonly Lazy<string[]> Keys = new(() => Selections.Select(s => s.Fit).Distinct().ToArray());
    private static readonly Lazy<(double Lower, double Upper)[]> Ranges = new(() => Limits.Select(l => (l.Lower, l.Upper)).Distinct().OrderBy(r => r.Lower).ToArray());
    private static readonly string[] Properties = ["选用级别", "配合制", "配合类型", "最大间隙或最小过盈", "最小间隙或最大过盈"];
    private const string Codes = "a b c cd d e ef f fg g h js j k m n p r s t u v x y z za zb zc";
    private static bool ValidBand(string band, bool hole)
    {
        var m = Regex.Match(band, @"^([A-Za-z]+)([1-9]|1[0-8])$");
        return m.Success && Codes.Split(' ').Select(c => hole ? c.ToUpperInvariant() : c).Contains(m.Groups[1].Value);
    }
    private static string Interval(double lo, double hi) => $">{lo.ToString(CultureInfo.InvariantCulture)}~{hi.ToString(CultureInfo.InvariantCulture)}";
    private static object Options(string key, string[] items, int offset, int limit) => new { missing = key, @default = (object?)null,
        options = items.Skip(offset).Take(limit).ToArray(), total = items.Length, nextOffset = offset + limit < items.Length ? (int?)(offset + limit) : null };

    internal static object Query(string? fit = null, string? size = null, string? property = null, string? usage = null,
        string? standard = null, int offset = 0, int limit = 20)
    {
        fit = fit?.Trim() ?? ""; size = size?.Trim() ?? ""; property = property?.Trim() ?? "";
        usage = usage?.Trim() ?? ""; standard = Regex.Replace(standard ?? "", @"\s+", "");
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0；limit为1～100");
        if (usage.Length > 0 && usage is not ("1" or "2" or "3")) throw new ArgumentException("usage为配合选用级别1～3：未列、常用非优先、优先；精确匹配，不是公差带的1～4级，不支持*");
        var parts = fit.Split('/');
        var fitStar = fit.Contains('*');
        if (fit.Length > 0 && fit != "*" && (parts.Length != 2 || !(ValidBand(parts[0], true) || parts[0] == "*") || !(ValidBand(parts[1], false) || parts[1] == "*")))
            throw new ArgumentException("fit为孔/轴的完整配合代号，如H7/g6、G7/h6；严格区分孔大写轴小写。*、H7/*、*/h6可展开已收录配合");
        if (fit.Count(c => c == '*') > 1 || (fitStar ? 1 : 0) + (size == "*" ? 1 : 0) + (property == "*" ? 1 : 0) > 1
            || standard.Contains('*') || size.Contains('*') && size != "*" || property.Contains('*') && property != "*")
            throw new ArgumentException("fit、size、property合计最多一个显式*；usage/standard不支持*");
        double number = 0;
        if (size.Length > 0 && size != "*" && (!double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || !double.IsFinite(number) || number <= 0))
            throw new ArgumentException("size须为有限正数，单位mm；省略给尺寸候选，不默认尺寸");
        object NoMatch() => new { status = "no_match", name = "公差配合", spec = new { fit, size, property, usage, standard }, value = (object?)null,
            reason = "已收录来源无此条件组合；数值仅覆盖>0～500mm，不放宽条件、不反算补空格", standards = Pages.Select(p => p.Standard).Append(FitApplications.SourcePage.Standard).Append(FitStatistics.Source.Standard).Distinct().ToArray() };
        var numericSource = Pages.Any(p => BandLimits.Matches(p.Standard, standard));
        var applicationSource = FitApplications.Matches(standard);
        var statisticalSource = FitStatistics.Matches(standard);
        var statisticalProperty = FitStatistics.Properties.Contains(property);
        var statisticalOnly = statisticalProperty || !numericSource && !applicationSource && statisticalSource;
        var applicationProperty = FitApplications.Properties.Contains(property);
        var applicationOnly = applicationProperty || !numericSource && applicationSource && !statisticalOnly;
        var properties = Properties.Concat(FitApplications.Properties).Concat(FitStatistics.Properties).ToArray();
        if (property.Length > 0 && property != "*" && !properties.Contains(property)) return Options("property", properties, offset, limit);
        if (!numericSource && !applicationSource && !statisticalSource || applicationProperty && !applicationSource || statisticalProperty && !statisticalSource
            || statisticalOnly && property.Length > 0 && property != "*" && !statisticalProperty
            || !applicationOnly && !statisticalOnly && !numericSource || !applicationOnly && number > 500) return NoMatch();
        string[] keys;
        if (fit.Length == 0 || fitStar)
            keys = Keys.Value.Where(k => fit.Length == 0 || fit == "*" || (parts[0] == "*" || k.Split('/')[0] == parts[0]) && (parts[1] == "*" || k.Split('/')[1] == parts[1])).ToArray();
        else keys = [fit];
        keys = keys.Where(k => usage.Length == 0 || Rank(k) == int.Parse(usage, CultureInfo.InvariantCulture)).ToArray();
        if (statisticalOnly) keys = keys.Where(FitStatistics.Has).ToArray();
        object Table(object[] rows) => new { rows = rows.Skip(offset).Take(limit).ToArray(), total = rows.Length, nextOffset = offset + limit < rows.Length ? (int?)(offset + limit) : null };
        if (applicationOnly)
        {
            // Application is a fit-key relationship: no implicit diameter and no repeated size rows.
            if (size == "*") return NoMatch();
            keys = keys.Where(k => FitApplications.Has(k, property.Length == 0 || property == "*" ? null : property)).ToArray();
            if (keys.Length == 0) return NoMatch();
            if (fit.Length == 0) return new { status = "needs_input", @default = (object?)null, name = "公差配合", missing = new[] { Options("fit", keys, offset, limit) } };
            var applicationCards = keys.Select(k => new { name = "公差配合", spec = new { fit = k },
                data = FitApplications.Resolve(k).Where(p => property.Length == 0 || property == "*" || p.Key == property).ToDictionary() }).ToArray();
            if (fitStar) return Table(applicationCards.Cast<object>().ToArray());
            if (property == "*") return Table(applicationCards[0].data.Select(p => (object)new { property = p.Key, data = p.Value }).ToArray());
            return applicationCards[0];
        }
        var ranges = Ranges.Value.Where(r => size.Length == 0 || size == "*" || number > r.Lower && number <= r.Upper).ToArray();
        if (keys.Length == 0 || ranges.Length == 0) return NoMatch();
        var missing = new List<object>();
        if (fit.Length == 0) missing.Add(Options("fit", keys, offset, limit));
        if (size.Length == 0) missing.Add(Options("size", ranges.Select(r => Interval(r.Lower, r.Upper)).ToArray(), offset, limit));
        if (missing.Count > 0) return new { status = "needs_input", @default = (object?)null, name = "公差配合", missing };
        var cards = keys.SelectMany(k => ranges.Select(r => new { name = "公差配合", spec = new { fit = k, size = Interval(r.Lower, r.Upper) },
            data = (statisticalOnly ? FitStatistics.Resolve(k, r.Lower, r.Upper) : Resolve(k, r.Lower, r.Upper, standard)).Where(p => (size != "*" || !FitApplications.Properties.Contains(p.Key))
                && (property.Length == 0 || property == "*" || p.Key == property)).ToDictionary() })).ToArray();
        if (fitStar || size == "*") return Table(cards.Cast<object>().ToArray());
        if (property == "*") return Table(cards[0].data.Select(p => (object)new { property = p.Key, data = p.Value }).ToArray());
        return cards[0];
    }

    private static int Rank(string fit) => Selections.Where(s => s.Fit == fit).Select(s => s.Usage)
        .Concat(Limits.Where(l => l.Fit == fit).Select(l => l.Usage)).DefaultIfEmpty(1).Max();
    private static object Location(string page, int table, int row, int column, string? image = null)
    {
        var p = Pages.Single(p => p.Id == page);
        return new { id = p.Id, byteOffset = p.ByteOffset, standard = p.Standard, table, row, column, image };
    }
    private static Dictionary<string, object> Resolve(string fit, double lower, double upper, string standard)
    {
        var selections = Selections.Where(s => s.Fit == fit).ToArray();
        var limits = Limits.Where(l => l.Fit == fit && l.Lower <= lower && l.Upper >= upper).ToArray();
        var p = Pages.First(p => BandLimits.Matches(p.Standard, standard));
        var evidence = selections.Select(s => (object)new { usage = s.Usage, source = Location(s.Page, s.Table, s.Row, s.Column, s.Image), notes = s.Notes })
            .Concat(limits.Select(l => (object)new { usage = l.Usage, source = Location(l.Page, l.Table, l.HeaderRow, l.HeaderColumn), notes = l.Notes })).ToArray();
        var notes = selections.SelectMany(s => s.Notes).Concat(limits.SelectMany(l => l.Notes)).Distinct().ToList();
        object Fact(object? value, string unit, string raw, string? reason, object? source = null, string[]? extra = null)
            => new { value, unit, raw, reason, expression = (string?)null, symbol = (string?)null,
                source = source ?? new { standard = p.Standard, kind = "source-aggregation", pages = Pages.Select(p => new { id = p.Id, byteOffset = p.ByteOffset }).ToArray(), evidence },
                notes = notes.Concat(extra ?? []).Distinct().ToArray(), dependencies = Array.Empty<object>() };
        var rank = Rank(fit);
        var system = fit.Split('/')[0].StartsWith('H') ? (fit.Split('/')[1].StartsWith('h') ? "基孔制/基轴制" : "基孔制")
            : fit.Split('/')[1].StartsWith('h') ? "基轴制" : "非基孔/基轴组合";
        var kinds = selections.Select(s => s.Kind).Distinct().ToArray();
        var kind = upper <= 3 && fit is "H6/n5" or "H7/p6" ? "过渡配合" : kinds.Length == 1 ? kinds[0] : null;
        var values = limits.Select(Parse).ToArray();
        var valid = values.Length > 0 && values.All(v => v.Reason is null) && values.Select(v => (v.High, v.Low)).Distinct().Count() == 1;
        string? reason = valid ? null : limits.Length == 0 ? "第40页未列出此配合的数值列，不从孔轴偏差反算补值"
            : values.Any(v => v.Reason is not null) ? string.Join("；", values.Select(v => v.Reason).Where(r => r is not null).Distinct())
            : throw new InvalidOperationException("配合权威值不唯一：" + fit);
        var entry = limits.FirstOrDefault();
        var numericSource = new { id = Pages[2].Id, byteOffset = Pages[2].ByteOffset, standard = Pages[2].Standard,
            table = entry?.Table, row = entry?.Row, column = entry?.Column };
        var raw = entry?.Raw ?? "";
        var high = valid ? values[0].High : null; var low = valid ? values[0].Low : null;
        var numericNotes = new List<string> { "正值为间隙，负值为过盈；返回带符号的上/下极限，不转为过盈绝对值。原表空白、横线或异常不反算补齐。" };
        if (valid && kind == "过盈配合" && high > 0) numericNotes.Add("原表此列归过盈配合，但本尺寸上极限为正，可出现间隙；保留原值及分类。");
        var result = new Dictionary<string, object>()
        {
            ["选用级别"] = Fact(rank, "级", rank switch { 1 => "所收录优先/常用配合表未列出", 2 => "常用（非优先）", _ => "优先" }, null,
                extra: ["此配合按1未列、2常用非优先、3优先分级，不是IT精度等级，也不是单个公差带的四级分类；1不表示禁用。"]),
            ["配合制"] = Fact(system, "", system, null),
            ["配合类型"] = Fact(kind, "", string.Join("/", kinds), kind is null ? "选用表未列出，不按字母或缺失数值猜测类型" : null,
                extra: upper <= 3 && fit is "H6/n5" or "H7/p6" ? ["第40页脚注：H6/n5、H7/p6在基本尺寸≤3mm时为过渡配合。"] : []),
            ["最大间隙或最小过盈"] = Fact(high, "μm", raw, reason, numericSource, numericNotes.ToArray()),
            ["最小间隙或最大过盈"] = Fact(low, "μm", raw, reason, numericSource, numericNotes.ToArray()),
        };
        if (FitApplications.Matches(standard))
            foreach (var item in FitApplications.Resolve(fit)) result[item.Key] = item.Value;
        if (FitStatistics.Matches(standard))
            foreach (var item in FitStatistics.Resolve(fit, lower, upper)) result[item.Key] = item.Value;
        return result;
    }

    internal static (double? High, double? Low, string? Reason) Parse(Limit l)
    {
        var raw = l.Raw.Replace('\n', ' ').Trim();
        if (raw.StartsWith('±') && double.TryParse(raw[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var half))
            return (half, -half, null);
        var m = Regex.Match(raw, @"^([+\-]?\d+(?:\.\d+)?)(?:\s+|(?=[+\-]))([+\-]?\d+(?:\.\d+)?)$");
        if (!m.Success) return (null, null, raw is "" or "—" ? "原表空白或横线，无数值" : "原格非双极限数值；不猜改");
        var high = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var low = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return high < low ? (null, null, "原表上极限小于下极限，保留原文不排序、不修正符号") : (high, low, null);
    }
}
