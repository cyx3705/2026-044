using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-STEEL-001: native document cells, explicit grade membership, no interpolation.
internal static class SteelMaterials
{
    internal sealed record Field(string Name, string Unit, string Semantics, string Context, string[] Headers, string Role, string Kind);
    internal sealed record Table(string Id, int Key, string Title, string Family, string[] Standard, string Path,
        string Notes, int GradeColumn, Field[] Fields, string[][] Rows, string[][] HeaderRows,
        string SourceId, string SourceFile, long? ByteOffset, string Scenario);
    internal sealed record Source(Table[] Tables);
    private static readonly Lazy<Source> SourceData = new(() =>
    {
        using var stream = typeof(SteelMaterials).Assembly.GetManifestResourceStream("Norma.Formulas.steel.json")
            ?? throw new InvalidOperationException("缺少钢材牌号数据");
        var source = JsonSerializer.Deserialize<Source>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (source.Tables.Any(t => t.GradeColumn < 0 || t.GradeColumn >= t.Fields.Length || t.Rows.Any(r => r.Length != t.Fields.Length)))
            throw new InvalidOperationException("钢材牌号表列数异常");
        return source;
    });
    internal static IReadOnlyList<Table> Tables => SourceData.Value.Tables;
    private static readonly Lazy<CastIronLookup.Fact[]> Data = new(() => Build(Tables));
    internal static IReadOnlyList<CastIronLookup.Fact> Facts => Data.Value;
    private static string Clean(string value) => Regex.Replace(value, @"\s+", "").Replace("\u00ad", "");

    internal static IEnumerable<string> Grades(string text, int page = 0)
    {
        // Parentheses are original old names / shorthand, not extra compositions.
        var primary = Regex.Replace(text, @"[（(][^）)]*[）)]", "");
        // Only these source rows use spaces to separate several full grades.
        // Other spaces are typography inside one grade (ML 40, 9Cr2 W, etc.).
        var separator = page is 24 or 103 or 106 || page >= 1000 ? @"[、，,；;\s]+" : @"[、，,；;]+";
        foreach (var token in Regex.Split(primary.Trim(), separator))
        {
            var grade = Clean(token).TrimEnd('①', '②', '③', '④', '⑤', '*');
            if ((Regex.IsMatch(grade, page >= 1000 ? @"^[A-Za-z0-9][A-Za-z0-9.\-]*$" : @"^[A-Za-z0-9][A-Za-z0-9-]*$") && grade.Any(char.IsLetter)) || Regex.IsMatch(grade, page >= 1000 ? @"^\d{2,4}$" : @"^\d{2,3}$"))
                yield return grade;
        }
    }

    private static string Cell(string text, Field field)
    {
        var s = text.Trim();
        if (s.Length == 0 || s is "—" or "－" or "-") return "未给出";
        // Keep literal ranges, formula-dependent chemistry and original anomalous order.
        // Header bounds apply only to a bare number, never to a conditional expression.
        if (Regex.IsMatch(s, @"^\d+(?:\.\d+)?$"))
            s = (field.Semantics == "min" ? "≥" : field.Semantics == "max" ? "≤" : "") + s;
        if (field.Unit.Length > 0 && Regex.IsMatch(s, @"\d") && !s.Contains(field.Unit, StringComparison.OrdinalIgnoreCase)
            && !Regex.IsMatch(s, @"HRC|HBS|HRB|HV|HB|MPa|Mpa|℃|ºC")) s += " " + field.Unit;
        var range = Regex.Match(s, @"^\s*([0-9]+(?:\.[0-9]+)?)\s*[~～]\s*([0-9]+(?:\.[0-9]+)?)");
        if (range.Success && double.Parse(range.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            > double.Parse(range.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
            s += "（原表区间倒置，待核）";
        if (Regex.IsMatch(s, @"\d+\.\d+\.\d+")) s += "（原表数字格式异常，待核）";
        if (field.Unit.Length == 0 && field.Name.Contains("强度") && Regex.IsMatch(s, @"^\s*[≥≤<>]?\d") && !Regex.IsMatch(s, @"MPa|Mpa|HB|HRC"))
            s += "（原表未标单位）";
        if (field.Unit.Length == 0 && field.Name.StartsWith("伸长率") && Regex.IsMatch(s, @"^\s*[≥≤<>]?\d"))
            s += "（原表未标单位）";
        return s;
    }

    internal static CastIronLookup.Fact[] Build(IReadOnlyList<Table> tables)
    {
        var facts = new List<CastIronLookup.Fact>();
        var seen = new Dictionary<(string, string, string, string, string), HashSet<string>>();
        var numericGrades = tables.Where(t => t.Fields.Any(f => f.Kind == "grade" && f.Role == "property"))
            .SelectMany(t => t.Rows.SelectMany(r => Grades(r[t.GradeColumn], t.Key)))
            .Where(g => int.TryParse(g, out _)).Distinct().ToArray();
        foreach (var table in tables)
        for (var ri = 0; ri < table.Rows.Length; ri++)
        {
            var row = table.Rows[ri];
            var rawGrade = row[table.GradeColumn];
            var grades = Grades(rawGrade, table.Key).Distinct().ToArray();
            var range = Regex.Match(Clean(rawGrade), @"^(\d+)[～~](\d+)$");
            if (range.Success) grades = numericGrades.Where(g => int.Parse(g) >= int.Parse(range.Groups[1].Value) && int.Parse(g) <= int.Parse(range.Groups[2].Value)).ToArray();
            foreach (var original in grades)
            {
                var state = table.Key == 170 ? Regex.Match(original, @"-([RQL])$") : Match.Empty;
                var grade = state.Success ? original[..^2] : original;
                var rowId = table.Id + ":" + (ri + 1);
                var source = table.SourceId + " " + string.Join("、", table.Standard);
                var conditions = new List<string> { table.Scenario + " [" + table.SourceId + "]" };
                if (table.Key == 1) conditions.Add("28mm厚标准试块");
                if (table.Key == 97) conditions.Add("钢材直径≤100mm；更大直径须按原页降低伸长率和收缩率");
                if (table.Title.Contains("参考数据")) conditions.Add("参考数据，非保证值");
                if (range.Success) conditions.Add("原表牌号范围=" + rawGrade);
                if (state.Success) conditions.Add("钢丝状态=" + state.Groups[1].Value);
                var annotation = Regex.Match(rawGrade, @"[（(]([^）)]+)[）)]");
                if (annotation.Success) conditions.Add("牌号原注=" + annotation.Groups[1].Value);
                foreach (var (f, ci) in table.Fields.Select((f, ci) => (f, ci)).Where(x => x.f.Role == "condition"))
                    if (!(table.Key == 59 && ci == 2))
                    if (!string.IsNullOrWhiteSpace(row[ci]) && row[ci] is not ("—" or "－" or "-")) conditions.Add(f.Name + "=" + row[ci].Trim());
                // Row identity prevents two differing source specimens with identical printed
                // conditions from being silently coalesced. Identical facts are removed below.
                var baseCondition = string.Join("；", conditions);
                var rowFacts = new List<CastIronLookup.Fact>();
                for (var ci = 0; ci < table.Fields.Length; ci++)
                {
                    var f = table.Fields[ci];
                    if (f.Role != "property") continue;
                    if (table.Key == 59 && ci == 3)
                    {
                        var project = Clean(row[2]);
                        var unit = project.Contains("MPa") ? "MPa" : project.Contains('%') ? "%" : project.Contains("HBS") ? "HBS" : project.Contains("/J") ? "J" : "";
                        var name = project.Contains("抗拉") ? "抗拉强度" : project.Contains("屈服") ? "屈服点" : project.Contains("伸长") ? "伸长率δ5" : project.Contains("收缩") ? "断面收缩率" : project.Contains("硬度") ? "硬度HBS" : "冲击吸收功Ak";
                        f = f with { Name = name, Unit = unit };
                    }
                    var printedHardness = Regex.Match(row[ci], @"HRC|HBS|HRB|HV|HB");
                    if (f.Name.StartsWith("硬度") && printedHardness.Success)
                        f = f with { Name = "硬度" + printedHardness.Value, Unit = printedHardness.Value };
                    var condition = baseCondition + (f.Context.Length > 0 ? "；" + f.Context : "");
                    if (table.Key >= 1000 && f.Semantics.Length > 0 && Regex.IsMatch(row[ci].Trim(), @"^\d+(?:\.\d+)?(?:\s+\d+(?:\.\d+)?)+$"))
                        condition += f.Semantics == "min" ? "；表头逐项下限≥" : "；表头逐项上限≤";
                    var value = Cell(row[ci], f);
                    // Missing cells stay in authoritative tables; compact cards omit them.
                    if (value == "未给出") continue;
                    rowFacts.Add(new(f.Kind, table.Family, grade, f.Name, condition, value, source.Trim(), rowId));
                }
                foreach (var f in rowFacts)
                {
                    var key = (f.Tool, f.Family, f.Grade, f.Property, f.Condition);
                    if (!seen.TryGetValue(key, out var values)) seen[key] = values = [];
                    var collision = values.Count > 0 && !values.Contains(f.Value);
                    values.Add(f.Value);
                    facts.Add(collision ? f with { Condition = f.Condition + "；原表行=" + rowId } : f);
                }
                if (rowFacts.Count == 0 && table.Key >= 1000)
                    foreach (var (field, column) in table.Fields.Select((field, column) => (field, column)).Where(x => x.field.Role == "condition" && x.field.Name.Contains("热处理")))
                        if (!string.IsNullOrWhiteSpace(row[column]))
                            facts.Add(new("strength", table.Family, grade, field.Name, table.Scenario + " [" + table.SourceId + "]", row[column], source.Trim(), rowId));
                if (rowFacts.Count == 0)
                    facts.Add(new("grade", table.Family, grade, "化学成分", baseCondition, "仅牌号目录，原页未给出成分", source.Trim(), rowId));
            }
        }
        var hasChemistry = facts.Where(f => f.Tool == "grade" && f.Property != "化学成分").Select(f => f.Grade).ToHashSet();
        return facts.Where(f => f.Property != "化学成分" || !hasChemistry.Contains(f.Grade))
            .DistinctBy(f => (f.Tool, f.Family, f.Grade, f.Property, f.Condition, f.Value)).ToArray();
    }
}
