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
    private static readonly Lazy<CastIronLookup.Fact[]> AllFacts = new(() => CastIronLookup.Facts.Concat(MaterialRepresentatives.AdditionalFacts).Concat(SteelMaterials.Facts).Concat(NonFerrousMaterials.Facts).ToArray());
    internal static IEnumerable<CastIronLookup.Fact> Data => AllFacts.Value.Where(f =>
        f.Family != "通用" && !Labels.Contains(f.Property));

    internal static object Query(string tool, string? family = null, string? grade = null, string? property = null,
        string? condition = null, int offset = 0, int limit = 20, string basis = "representative")
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0，limit为1～100");
        if (basis is not ("representative" or "source")) throw new ArgumentException("basis为representative或source");
        var requestedGrade = grade;
        if (!string.IsNullOrWhiteSpace(grade) && NonFerrousMaterials.Aliases.TryGetValue(Norm(grade), out var mapped))
        {
            // Conflicting source correspondences require an explicit target, never a guessed merge.
            if (mapped.Length > 1) return new { missing = "grade", @default = (string?)null, options = mapped };
            grade = mapped[0];
        }
        // Keep the explicit scalar/variable interface, including naming and heat-state dictionaries.
        if (new[] { family, grade, property, condition }.Contains("*"))
        {
            if (basis == "representative" && MaterialRepresentatives.Selections.Any(s => Norm(s.Material) == Norm(grade)))
                throw new ArgumentException("名称代表选型不支持*；请直接查询返回牌号，或用basis=source展开原配方");
            var expanded = AllFacts.Value.Select(f => tool == "grade" && f.RowId.StartsWith("nonferrous:", StringComparison.Ordinal) ? f with { Tool = "grade" } : f).ToArray();
            return CastIronLookup.Lookup(tool, family, grade, property, condition, offset, limit, expanded);
        }
        var selections = MaterialRepresentatives.Selections.Where(s => Norm(s.Material) == Norm(grade)
            && (string.IsNullOrWhiteSpace(family) || Norm(s.Family) == Norm(family))).ToArray();
        if (basis == "representative" && selections.Length > 0)
        {
            if (selections.Length > 1) return new { missing = "family", @default = (string?)null, options = selections.Select(s => s.Family).ToArray() };
            var selected = selections[0];
            var target = Data.Where(f => Norm(f.Grade) == Norm(selected.Grade)).ToArray();
            var targetFamily = target.Select(f => f.Family).Distinct().Single();
            var answer = Query(tool, targetFamily, selected.Grade, property, condition, offset, limit, "source");
            var result = answer as Dictionary<string, object> ?? new Dictionary<string, object> { ["牌号"] = selected.Grade, ["取值"] = answer };
            result["代表对象"] = selected.Family + "/" + selected.Material;
            result["选型关系"] = "用途基准，非等同或替代认证";
            result["选型说明"] = selected.Reason;
            result["选型依据"] = selected.Source;
            if (string.IsNullOrWhiteSpace(property) && string.IsNullOrWhiteSpace(condition))
            {
                var original = Data.Where(f => f.Family == selected.Family && f.Grade == selected.Material).ToArray();
                result["原配方（非代表牌号指标）"] = Card(original, tool != "strength", false);
            }
            return result;
        }
        var rows = Data.Where(f => tool != "strength" || f.Tool == "strength");
        if (!string.IsNullOrWhiteSpace(family)) rows = rows.Where(f => Norm(f.Family) == Norm(family) || family == "钢" && f.RowId.StartsWith("steel:", StringComparison.Ordinal));
        if (basis == "representative" && string.IsNullOrWhiteSpace(family) && string.IsNullOrWhiteSpace(property) && string.IsNullOrWhiteSpace(condition)
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
            return CastIronLookup.Lookup(tool, family, grade, property, condition, offset, limit, AllFacts.Value);
        var families = facts.Select(f => f.Family).Distinct().ToArray();
        if (families.Length > 1 && !facts.All(f => f.RowId.StartsWith("steel:", StringComparison.Ordinal))) return new { missing = "family", @default = (string?)null, options = families };
        if (!string.IsNullOrWhiteSpace(property) && facts.Select(f => f.Condition).Distinct().Count() == 1
            && facts.Select(f => f.Value).Distinct().Count() == 1) return facts[0].Value;
        var card = Card(facts, tool != "strength", string.IsNullOrWhiteSpace(property) && string.IsNullOrWhiteSpace(condition));
        if (Norm(requestedGrade) != Norm(grade)) card["查询牌号"] = requestedGrade!;
        return card;
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

    private static Dictionary<string, object> Card(CastIronLookup.Fact[] facts, bool chemistry, bool references)
    {
        var first = facts[0];
        var name = first.Grade.Trim().TrimEnd('*');
        var ungraded = Regex.IsMatch(name, @"[\p{IsCJKUnifiedIdeographs}]");
        var result = new Dictionary<string, object> { [ungraded ? "材料名称" : "牌号"] = name, ["类别"] = first.Family };
        if (facts.All(f => f.RowId.StartsWith("steel:", StringComparison.Ordinal)))
        {
            result["类别"] = "钢";
            result["说明"] = "手册旧版数据；原文疑点未擅改，未给出的项目省略";
            if (Regex.IsMatch(name, @"A1|Mi|Mnr|^45ML$|^Cr18Ni9$|^1Cr$|^W$|Mo[l]|Ni[l]|Ni1O|M0[23]"))
                result["牌号疑点"] = "原表写法，疑似拼写不一致；未自动映射其他牌号";
        }
        var aliases = GradeAliases.Where(p => p.Value.Any(a => Norm(a) == Norm(name))).Select(p => p.Key).ToArray();
        if (aliases.Length > 0) result["材料名称"] = aliases;
        if (ungraded)
        {
            result["牌号状态"] = GradeAliases.TryGetValue(name, out var series) ? "已关联同族牌号，非配方等同" : "原表未指定，尚未确认对应牌号";
            if (series is not null) result["关联牌号"] = series;
            var representative = MaterialRepresentatives.Selections.SingleOrDefault(s => s.Material == name && s.Family == first.Family);
            if (representative is not null)
            {
                result["牌号状态"] = "已选用途基准，非等同牌号";
                result["代表牌号"] = representative.Grade;
            }
        }
        if (chemistry)
        {
            var chemical = facts.Where(f => f.Tool == "grade").ToArray();
            result["化学成分"] = chemical.All(f => f.Property == "化学成分")
                ? chemical.Select(f => f.Value).Distinct().FirstOrDefault() ?? "原表未给出" : Block(chemical);
        }
        var mechanical = facts.Where(f => f.Tool == "strength").ToArray();
        if (mechanical.Length > 0) result["力学性能"] = Block(mechanical);
        var physical = facts.Where(f => f.Tool == "physical").ToArray();
        if (physical.Length > 0) result["物理及使用性能"] = Block(physical);
        if (facts.All(f => f.RowId.StartsWith("nonferrous:", StringComparison.Ordinal)))
        {
            result["说明"] = "手册旧版原值；多值原文与条件按原顺序对应，非连续区间；原表疑点未擅改";
            var notes = NonFerrousMaterials.Notes(facts);
            if (notes.Count > 0) result["原注"] = notes;
            if (name is "2A112017" or "5A01" or "5A05" or "FH2036" or "GH1040")
                result["原表疑点"] = "牌号、列位或数字存在疑点，保留原文，待标准原件复核";
        }
        result["来源"] = facts.Select(f => f.Source)
            .GroupBy(s => s.Split(' ')[0]).Select(g => g.OrderByDescending(s => s.Length).First()).ToArray();
        if (!ungraded && references)
        {
            var selectedFor = MaterialRepresentatives.Selections.Where(s => Norm(s.Grade) == Norm(name))
                .GroupBy(s => s.Family).ToDictionary(g => g.Key, g => g.Select(s => s.Material).ToArray());
            if (selectedFor.Count > 0) result["代表选型对象（非等同）"] = selectedFor;
            if (name == "DH33") result["牌号体系"] = "东亚企业牌号；ISO材料细类MC13；弹性模量原页未标单位，未录入";
        }
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
