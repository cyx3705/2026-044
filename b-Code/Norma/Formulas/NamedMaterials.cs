using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-MATERIAL-010: named materials are separate from the metal grade passports.
// Values are native document cells; printed units, direction and fraction bases stay intact.
internal static class NamedMaterials
{
    internal sealed record Page(int Key, string Name, string Title, string Path, string Notes,
        string[][][] Tables, string SourceId, string SourceFile, long? ByteOffset);
    internal sealed record Fact(string Name, string Family, string Property, string Condition, string Value,
        int Page, int Table, int Row, int Column);
    internal sealed record Source(Page[] Pages, Fact[] Facts, Dictionary<string, string[]> Aliases);
    private static readonly Lazy<Source> Loaded = new(() =>
    {
        using var stream = typeof(NamedMaterials).Assembly.GetManifestResourceStream("Norma.Formulas.nonmetal.json")
            ?? throw new InvalidOperationException("缺少非金属及复合材料库数据");
        var data = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var pages = data.Pages.ToDictionary(p => p.Key);
        foreach (var f in data.Facts)
        {
            if (!pages.TryGetValue(f.Page, out var p) || f.Table < 0 || f.Table >= p.Tables.Length
                || f.Row < 0 || f.Row >= p.Tables[f.Table].Length || f.Column < 0 || f.Column >= p.Tables[f.Table][f.Row].Length
                || p.Tables[f.Table][f.Row][f.Column].Trim() != f.Value
                || string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrWhiteSpace(f.Property) || string.IsNullOrWhiteSpace(f.Condition))
                throw new InvalidOperationException("材料库条目与原表不一致");
        }
        if (data.Pages.Any(p => p.ByteOffset is null || !p.SourceId.StartsWith("jxclsj:", StringComparison.Ordinal)))
            throw new InvalidOperationException("材料库缺少原手册定位信息");
        return data;
    });
    internal static IReadOnlyList<Page> Pages => Loaded.Value.Pages;
    internal static IReadOnlyList<Fact> Facts => Loaded.Value.Facts;
    internal static IEnumerable<Fact> AllFacts => Facts.Concat(ReferenceMaterials.All.Facts);
    private static string Norm(string? text) => Regex.Replace(text ?? "", @"\s|\^?[①②③④⑤⑥]", "").ToUpperInvariant();
    private static bool Match(string value, string? filter) => string.IsNullOrWhiteSpace(filter) || filter == "*" || Norm(value) == Norm(filter);
    private static object Options(string key, IEnumerable<string> source, int offset, int limit)
    {
        var values = source.Distinct().ToArray();
        return new { missing = key, @default = (string?)null, options = values.Skip(offset).Take(limit).ToArray(), total = values.Length,
            nextOffset = offset + limit < values.Length ? (int?)(offset + limit) : null };
    }

    internal static object Query(string? name = null, string? property = null, string? condition = null,
        string? family = null, int offset = 0, int limit = 20, string? category = null)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0，limit为1～100");
        if (!string.IsNullOrWhiteSpace(category) && !ReferenceMaterials.Categories.Contains(category))
            throw new ArgumentException("category为" + string.Join("/", ReferenceMaterials.Categories));
        var variables = new[] { ("name", name), ("property", property), ("condition", condition), ("family", family) }.Where(p => p.Item2 == "*").ToArray();
        if (variables.Length > 1) throw new ArgumentException("一次最多一个参数为*");
        var rows = AllFacts.Where(f => string.IsNullOrWhiteSpace(family) || family == "*" || Norm(f.Family) == Norm(family)
            || f.Family.Split('/').Any(part => Norm(part) == Norm(family)))
            .Where(f => string.IsNullOrWhiteSpace(category) || Category(f) == category);
        if (!string.IsNullOrWhiteSpace(name) && name != "*")
        {
            var exact = rows.Where(f => Norm(f.Name) == Norm(name)).ToArray();
            if (exact.Length > 0) rows = exact;
            else if (Loaded.Value.Aliases.TryGetValue(Norm(name), out var aliases))
            {
                var matched = rows.Where(f => aliases.Any(a => Norm(a) == Norm(f.Name))).ToArray();
                var names = matched.Select(f => f.Name).Distinct().ToArray();
                if (names.Length != 1) return Options("name", names, offset, limit);
                rows = matched;
            }
            else
            {
                // A partial name returns choices even if it has only one match: never silently
                // promote a broad request such as '尼龙' to one formulation or product.
                var terms = Regex.Split(name.Trim(), @"\s+");
                return Options("name", rows.Select(f => f.Name).Where(n => terms.All(term => Norm(n).Contains(Norm(term)))), offset, limit);
            }
        }
        var available = rows.ToArray();
        if (!string.IsNullOrWhiteSpace(property) && property != "*")
        {
            var exact = rows.Where(f => Norm(f.Property) == Norm(property)).ToArray();
            rows = exact.Length > 0 ? exact : rows.Where(f => Norm(f.Property).Contains(Norm(property)));
        }
        if (!string.IsNullOrWhiteSpace(condition) && condition != "*") rows = rows.Where(f => Norm(f.Condition) == Norm(condition));
        var facts = rows.DistinctBy(f => (f.Name, f.Family, f.Property, f.Condition, f.Value)).ToArray();
        if (string.IsNullOrWhiteSpace(name)) return Options("name", facts.Select(f => f.Name), offset, limit);
        if (facts.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(property) && property != "*" && !available.Any(f => Norm(f.Property).Contains(Norm(property))))
                return Options("property", available.Select(f => f.Property), offset, limit);
            return Options("condition", available.Select(f => f.Condition), offset, limit);
        }
        if (variables.Length > 0)
        {
            var variable = variables[0].Item1;
            // An explicit variable table requires the other coordinates, like the grade tool.
            foreach (var key in new[] { "name", "property", "condition", "family" }.Where(k => k != variable))
            {
                var input = key == "name" ? name : key == "property" ? property : key == "condition" ? condition : family;
                var choices = facts.Select(f => Dimension(f, key)).Distinct().ToArray();
                if (string.IsNullOrWhiteSpace(input) && choices.Length > 1) return Options(key, choices, offset, limit);
            }
            var table = facts.Select(f => new Dictionary<string, object> { [variable] = Dimension(f, variable), ["value"] = Value(f) })
                .DistinctBy(r => JsonSerializer.Serialize(r)).ToArray();
            var result = new Dictionary<string, object?> { ["rows"] = table.Skip(offset).Take(limit).ToArray(), ["total"] = table.Length,
                ["nextOffset"] = offset + limit < table.Length ? offset + limit : null };
            var note = Notes(facts);
            if (note.Count > 0) result["原注"] = note;
            return result;
        }
        var families = facts.Select(f => f.Family).Distinct().ToArray();
        if (!string.IsNullOrWhiteSpace(property) && facts.Select(f => f.Property).Distinct().Count() == 1
            && facts.Select(f => f.Condition).Distinct().Count() == 1 && facts.Select(f => f.Value).Distinct().Count() == 1)
        {
            // Some scalar values are footnote references or letter ratings, not free numbers.
            var note = Notes(facts);
            var value = Value(facts[0]);
            return note.Count == 0 ? value : new Dictionary<string, object> { ["值"] = value, ["原注"] = note };
        }
        var card = new Dictionary<string, object> { ["材料名称"] = facts[0].Name, ["类别"] = families.Length == 1 ? families[0] : families };
        var blocks = new Dictionary<string, object>();
        foreach (var group in facts.GroupBy(f => f.Condition))
        {
            var values = new Dictionary<string, object>();
            foreach (var p in group.GroupBy(f => f.Property))
            {
                var distinct = p.Select(Value).DistinctBy(v => JsonSerializer.Serialize(v)).ToArray();
                values[p.Key] = distinct.Length == 1 ? distinct[0] : p.Select(f => new { value = Value(f),
                    source = PageFor(f).SourceId + ":表" + (f.Table + 1) + ":行" + (f.Row + 1) + ":列" + (f.Column + 1) }).ToArray();
            }
            blocks[group.Key] = values;
        }
        card["性能"] = blocks;
        card["来源"] = facts.Select(PageFor).DistinctBy(p => p.Key).Select(p => p.SourceId + " " + p.Title).ToArray();
        var notes = Notes(facts);
        if (notes.Count > 0) card["原注"] = notes;
        card["说明"] = "手册旧版原值；类别参考值与具体制品/配方按条件区分；未给出的项目省略，不插值或跨配方补值";
        return card;
    }

    private static string Dimension(Fact f, string key) => key switch
    {
        "name" => f.Name, "property" => f.Property, "condition" => f.Condition, "family" => f.Family,
        _ => throw new ArgumentException("未知维度")
    };
    private static string Category(Fact f) => ReferenceMaterials.All.Categories.TryGetValue(f.Page, out var category)
        ? category : f.Family.Contains("复合材料", StringComparison.Ordinal) ? "复合材料" : "非金属";
    private static Page PageFor(Fact f) => Pages.Concat(ReferenceMaterials.All.Pages).Single(p => p.Key == f.Page);
    private static object Value(Fact f)
    {
        if (ReferenceMaterials.All.Values.TryGetValue(f, out var reference)) return reference;
        var value = f.Value;
        // Bounds are explicit in this property's heading, never inferred from a grade name.
        if (Regex.IsMatch(value, @"^[+-]?\d+(?:\.\d+)?$"))
        {
            if (Regex.IsMatch(f.Property, @"最大|不高于|≤")) value = "≤" + value;
            else if (Regex.IsMatch(f.Property, @"最小|不低于|≥")) value = "≥" + value;
        }
        var unit = Regex.Match(f.Property, @"/\s*(g[·•]cm\^-3|g/cm\^3|kg[·•]m\^-3|MPa|GPa|kPa|℃|kV/mm|kV|mg|mm|N)(?![\p{L}\d·•^/])");
        if (unit.Success && Regex.IsMatch(value, @"\d") && !value.Contains(unit.Groups[1].Value, StringComparison.Ordinal)) value += " " + unit.Groups[1].Value;
        if (f.Property.Contains('%') && Regex.IsMatch(value, @"^[≥≤<>+-]?\d+(?:\.\d+)?(?:[~～±][+-]?\d+(?:\.\d+)?)?$")) value += " %";
        if (f.Page == 4002) value += f.Value switch { "—" => "（不推荐）", "×" => "（不可用）", "○" => "（可用，寿命较长）", "◎" => "（可用，寿命一般）", "V" => "（可代用，寿命较短）", _ => "" };
        return value;
    }
    private static Dictionary<string, string> Notes(IEnumerable<Fact> facts)
    {
        var result = new Dictionary<string, string>();
        var selected = facts.ToArray();
        foreach (var f in selected)
            if (ReferenceMaterials.All.Notes.TryGetValue(f, out var text)) result[PageFor(f).SourceId] = text;
        foreach (var page in selected.Select(PageFor).DistinctBy(p => p.Key))
        {
            var note = Regex.Match(page.Notes, @"(?:注[：:]|[①②③④⑤⑥]).*", RegexOptions.Singleline).Value;
            if (page.Key == 4058 && !selected.Any(f => f.Page == 4058 && Regex.IsMatch(f.Property + f.Value, @"①|②|硬度"))) note = "";
            if (note.Length > 0) result[page.SourceId] = note;
            var caution = page.Key switch
            {
                4001 => "原表导热系数数量级疑点，保留打印值与单位，未换算",
                4039 => "原表扯断伸长率单位印作MPa，保留疑点，不解释为应力",
                4085 => "PFCP6厚度1.8mm印作98.5；耐电压强度单位原文疑点，未修正",
                4164 => "反应烧结氧化增重印作7.5~0.04，区间倒置，未排序",
                4200 => "介电强度原表未标单位",
                4282 => "ABC、聚四氯乙烯等原文名称有疑点，未自动改名；玻纤百分比为体积分数",
                4288 => "增强纤维百分比为质量分数；纯树脂是原页对照材料",
                4302 => "抗拉强度公式为图像，当前仅保存原表符号说明，未实现计算式",
                4314 => "原表元素栏印作Zu，未擅自改成Zn",
                4315 => "原页牌号写法存在长短异写，未自动建立等同映射；参考值仍为参考值",
                4322 => "本页两张表分别为铜-石墨和石墨铝合金，已分别建卡",
                _ => ""
            };
            if (caution.Length > 0) result[page.SourceId + "疑点/口径"] = caution;
        }
        return result;
    }
}
