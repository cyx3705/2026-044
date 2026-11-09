using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-MATERIAL-006: one grade, one compact passport. Original tables remain authoritative.
internal static class MaterialLibrary
{
    private static string Norm(string? s) => Regex.Replace(s ?? "", @"\s|\*", "").ToUpperInvariant();
    private static readonly HashSet<string> Labels = ["牌号", "附铸牌号", "类别", "名称", "类型"];
    // REQ-MATERIAL-007: verified manual JB/GQ0033-1980 family names to their
    // explicit grade series. A family name may intentionally map to several grades.
    private static readonly IReadOnlyDictionary<string, string[]> GradeAliases = new Dictionary<string, string[]>
    {
        ["磷铸铁"] = ["MTP15", "MTP20", "MTP25", "MTP30"],
        ["磷铜钛铸铁"] = ["MTPCuTi15", "MTPCuTi20", "MTPCuTi25", "MTPCuTi30"],
        ["钒钛铸铁"] = ["MTVTi20", "MTVTi25", "MTVTi30"],
        ["铬铜铸铁"] = ["MTCrCu25", "MTCrCu30", "MTCrCu35"],
        ["铬钼铜铸铁"] = ["MTCrMoCu25", "MTCrMoCu30", "MTCrMoCu35"],
        ["Cu-Cr-Mo合金铸铁"] = ["MTCrMoCu25", "MTCrMoCu30", "MTCrMoCu35"],
    };
    internal static IEnumerable<CastIronLookup.Fact> Data => CastIronLookup.Facts.Where(f =>
        f.Family != "通用" && !Labels.Contains(f.Property));

    internal static object Query(string tool, string? family = null, string? grade = null, string? property = null,
        string? condition = null, int offset = 0, int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0，limit为1～100");
        // Keep the explicit scalar/variable interface, including naming and heat-state dictionaries.
        if (new[] { family, grade, property, condition }.Contains("*"))
            return CastIronLookup.Lookup(tool, family, grade, property, condition, offset, limit);
        var rows = Data.Where(f => tool != "strength" || f.Tool == "strength");
        if (!string.IsNullOrWhiteSpace(family)) rows = rows.Where(f => Norm(f.Family) == Norm(family));
        if (string.IsNullOrWhiteSpace(family) && string.IsNullOrWhiteSpace(property) && string.IsNullOrWhiteSpace(condition)
            && !string.IsNullOrWhiteSpace(grade) && GradeAliases.TryGetValue(grade.Trim(), out var aliases))
        {
            return new { 材料名称 = grade, 牌号映射 = aliases,
                依据 = "同合金体系的机床导轨牌号系列，非跨用途配方等同；可指定family查询原用途配方" };
        }
        if (!string.IsNullOrWhiteSpace(grade)) rows = rows.Where(f => Norm(f.Grade) == Norm(grade));
        if (!string.IsNullOrWhiteSpace(property)) rows = rows.Where(f => Norm(f.Property) == Norm(property));
        if (!string.IsNullOrWhiteSpace(condition)) rows = rows.Where(f =>
            Norm(f.Condition) == Norm(condition) || Norm(Condition(f.Condition)) == Norm(condition));
        var facts = rows.ToArray();
        if (string.IsNullOrWhiteSpace(grade))
        {
            var options = facts.Select(f => f.Grade.Trim().TrimEnd('*')).Distinct().ToArray();
            return new { missing = "grade", @default = (string?)null,
                options = options.Skip(offset).Take(limit).ToArray(), total = options.Length,
                nextOffset = offset + limit < options.Length ? (int?)(offset + limit) : null };
        }
        if (facts.Length == 0)
            return CastIronLookup.Lookup(tool, family, grade, property, condition, offset, limit);
        var families = facts.Select(f => f.Family).Distinct().ToArray();
        if (families.Length > 1) return new { missing = "family", @default = (string?)null, options = families };
        if (!string.IsNullOrWhiteSpace(property) && facts.Select(f => f.Condition).Distinct().Count() == 1
            && facts.Select(f => f.Value).Distinct().Count() == 1) return facts[0].Value;
        return Card(facts, tool != "strength", string.IsNullOrWhiteSpace(property) && string.IsNullOrWhiteSpace(condition));
    }

    internal static string Condition(string text)
    {
        text = Regex.Replace(text, @"铸件壁厚=>\s*([\d.]+)\s*[~～]\s*([\d.]+)\s*mm", "$1<壁厚≤$2 mm");
        return text.Trim();
    }

    private static object Block(IEnumerable<CastIronLookup.Fact> source)
    {
        var rows = source.ToArray();
        if (rows.Length == 0) return "原表未给出";
        var result = new Dictionary<string, object>();
        foreach (var group in rows.GroupBy(f => Condition(f.Condition)))
        {
            var values = new Dictionary<string, object>();
            foreach (var p in group.GroupBy(f => f.Property))
            {
                var distinct = p.Select(f => f.Value).Distinct().ToArray();
                // Preserve evidence if a future import introduces conflicting values.
                values[p.Key] = distinct.Length == 1 ? distinct[0] :
                    p.Select(f => new { value = f.Value, source = f.Source }).Distinct().ToArray();
            }
            result[group.Key] = values;
        }
        if (result.Count == 1 && result.ContainsKey("原表条件")) return result["原表条件"];
        // HT grades have many conditions but only tensile strength: don't repeat the property 25 times.
        var properties = rows.Select(f => f.Property).Distinct().ToArray();
        if (properties.Length == 1)
            return new Dictionary<string, object> { [properties[0]] = result.ToDictionary(p => p.Key,
                p => ((Dictionary<string, object>)p.Value)[properties[0]]) };
        return result;
    }

    private static object Card(CastIronLookup.Fact[] facts, bool chemistry, bool references)
    {
        var first = facts[0];
        var name = first.Grade.Trim().TrimEnd('*');
        var ungraded = Regex.IsMatch(name, @"[\p{IsCJKUnifiedIdeographs}]");
        var result = new Dictionary<string, object> { [ungraded ? "材料名称" : "牌号"] = name, ["类别"] = first.Family };
        var aliases = GradeAliases.Where(p => p.Value.Any(a => Norm(a) == Norm(name))).Select(p => p.Key).ToArray();
        if (aliases.Length > 0) result["材料名称"] = aliases;
        if (ungraded)
        {
            result["牌号状态"] = GradeAliases.TryGetValue(name, out var series) ? "已关联同族牌号，非配方等同" : "原表未指定，尚未确认对应牌号";
            if (series is not null) result["关联牌号"] = series;
        }
        if (chemistry)
        {
            var chemical = facts.Where(f => f.Tool == "grade").ToArray();
            result["化学成分"] = chemical.All(f => f.Property == "化学成分") ? "原表未给出" : Block(chemical);
        }
        var mechanical = facts.Where(f => f.Tool == "strength").ToArray();
        if (mechanical.Length > 0) result["力学性能"] = Block(mechanical);
        result["来源"] = facts.Where(f => f.Property != "化学成分").Select(f => f.Source)
            .GroupBy(s => s.Split(' ')[0]).Select(g => g.OrderByDescending(s => s.Length).First()).ToArray();
        if (aliases.Length > 0 && chemistry && references)
        {
            var reference = Data.Where(f => aliases.Contains(f.Grade)).ToArray();
            if (reference.Length > 0)
                result["同族参考配方（非本牌号标准指标）"] = reference.GroupBy(f => (f.Family, f.Grade))
                    .ToDictionary(g => g.Key.Family + "/" + g.Key.Grade, g => new Dictionary<string, object>
                    {
                        ["化学成分"] = Block(g.Where(f => f.Tool == "grade")),
                        ["力学性能"] = Block(g.Where(f => f.Tool == "strength")),
                        ["来源"] = g.Select(f => f.Source).Distinct().ToArray()
                    });
        }
        return result;
    }
}
