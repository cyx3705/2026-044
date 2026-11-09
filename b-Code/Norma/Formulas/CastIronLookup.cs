using System.Globalization;
using System.Text.RegularExpressions;

namespace Norma.Formulas;

// REQ-IRON-004: scalar lookup, explicit one-dimensional expansion, contextual missing inputs.
internal static class CastIronLookup
{
    internal sealed record Fact(string Tool, string Family, string Grade, string Property, string Condition, string Value, string Source, string RowId);
    private static readonly Lazy<Fact[]> Data = new(Build);
    internal static IReadOnlyList<Fact> Facts => Data.Value;
    private static string Norm(string? s) => Regex.Replace(s ?? "", @"\s|\*", "").ToUpperInvariant();
    private static string N(double x) => x.ToString("G", CultureInfo.InvariantCulture);
    internal static string Value(CastIron.Cell c)
    {
        var suffix = c.Unit.Length == 0 || c.Unit == "1" ? "" : " " + c.Unit;
        if (c.Undefined is not null) return c.Text.Length == 0 || c.Text == "—" ? "未给出" : c.Text + "（" + c.Undefined + "）";
        if (c.Parts is not null) return string.Join("；", c.Parts.Select(p => p.Key + ":" + Value(p.Value))) + (c.Text.Contains("加入量") ? "（加入量）" : "");
        var text = c.Value is not null ? N(c.Value.Value) : c.Options is not null ? string.Join("或", c.Options.Select(N))
            : c.Min is not null && c.Max is not null ? (c.MinInclusive == false ? ">" : "") + N(c.Min.Value) + "～" + N(c.Max.Value)
            : c.Min is not null ? (c.MinInclusive == false ? ">" : "≥") + N(c.Min.Value)
            : c.Max is not null ? (c.MaxInclusive == false ? "<" : "≤") + N(c.Max.Value) : c.Text;
        return (c.Parenthesized ? "(" + text + ")" : text) + suffix;
    }
    private static Fact[] Build()
    {
        var facts = new List<Fact>();
        foreach (var t in CastIron.Tables)
        for (var ri = 0; ri < t.Rows.Length; ri++)
        {
            var row = t.Rows[ri];
            var grade = t.GradeColumn < 0 ? "灰铸铁" : row[t.GradeColumn];
            for (var ci = 0; ci < row.Length; ci++)
            {
                var f = t.Fields[ci];
                if (ci == t.GradeColumn || f.Role == "condition" || (f.Role == "identity" && t.Kind == "strength")) continue;
                var property = f.Name;
                var conditions = new List<string>();
                if (t.Kind is "composition" or "strength")
                {
                    foreach (var (rf, rc) in t.Fields.Select((rf, rc) => (rf, rc)).Where(x => x.rf.Role == "condition"))
                    {
                        if (t.Entry == 295 && t.Id.EndsWith(":1", StringComparison.Ordinal) && ci == 1) continue;
                        conditions.Add(rf.Name + "=" + row[rc] + " " + rf.Unit);
                    }
                }
                if (t.Entry == 295 && t.Id.EndsWith(":1", StringComparison.Ordinal))
                { property = "抗拉强度"; conditions.Insert(0, ci == 1 ? "单铸试棒" : "铸件预计"); }
                if (t.Entry == 295 && t.Id.EndsWith(":2", StringComparison.Ordinal))
                { property = "抗拉强度"; conditions.Insert(0, f.Name.Replace("抗拉强度", "")); }
                if (t.Entry == 574) conditions.Insert(0, t.Id.EndsWith(":1", StringComparison.Ordinal) ? "单铸试块" : "附铸试块");
                if (t.Entry == 575 && ci >= 2) conditions.Insert(0, "参考值");
                if (t.Entry == 345) { property = "硬度" + f.Unit; conditions.Add(f.Headers[1]); }
                if (t.Entry == 490)
                { property = ci == 2 ? "硬度" : "抗拉强度"; conditions.Add(ci < 3 ? "室温" : f.Headers[1] + "高温短时"); }
                if (t.Entry == 346 && ci == 4) property = "伸长率(L0=3d)";
                if (t.Entry == 470 && ci is 2 or 3 or 7 or 8)
                {
                    property = ci is 2 or 3 ? "抗弯强度" : "挠度";
                    conditions.Add(ci is 2 or 7 ? "砂型" : "金属型");
                    conditions.Add(ci is 2 or 3 ? "试样直径=" + (ci == 2 ? "30" : "50") + " mm" : "支距=" + (ci == 7 ? "300" : "500") + " mm");
                }
                if (t.Entry == 614) conditions.Insert(0, "单铸试块");
                if (t.Entry is 551 or 550 or 299 or 298 or 402 or 403 && t.Rows.Count(r => Norm(r[t.GradeColumn]) == Norm(grade)) > 1)
                {
                    var index = t.Rows.Take(ri + 1).Count(r => Norm(r[t.GradeColumn]) == Norm(grade));
                    conditions.Add("同名配方" + index);
                }
                var cell = f.Role == "identity" ? new CastIron.Cell(row[ci], "", "text") : CastIron.Parse(row[ci], f.Unit, f.Semantics);
                var condition = conditions.Count == 0 ? "原表条件" : string.Join("；", conditions);
                var source = t.SourceId + " " + Regex.Match(t.Notes, @"(?:GB/T|JB/T|JB/ZQ|JB/GQ)\s*[\d.]+-\d+").Value;
                if (cell.Kind == "conditional" && cell.Parts is not null)
                {
                    foreach (var p in cell.Parts)
                        facts.Add(new("strength", t.Family, grade, property, condition == "原表条件" ? p.Key : condition + "；" + p.Key, Value(p.Value), source.Trim(), t.Id + ":" + (ri + 1)));
                    continue;
                }
                if (property != "其他" || cell.Parts is null)
                    facts.Add(new(t.Kind == "strength" ? "strength" : "grade", t.Family, grade, property, condition, Value(cell), source.Trim(), t.Id + ":" + (ri + 1)));
                if (property == "其他" && cell.Parts is not null)
                    foreach (var p in cell.Parts)
                        facts.Add(new("grade", t.Family, grade, p.Key, condition, Value(p.Value) + (cell.Text.Contains("加入量") ? "（加入量）" : ""), source.Trim(), t.Id + ":" + (ri + 1)));
            }
        }
        foreach (var t in CastIron.Tables.Where(t => t.Kind == "strength" && t.GradeColumn >= 0))
        foreach (var grade in t.Rows.Select(r => r[t.GradeColumn]).Distinct())
        {
            facts.Add(new("grade", t.Family, grade, "牌号", "原表条件", grade, t.SourceId, t.Id));
            if (!CastIron.Tables.Any(c => c.Kind == "composition" && c.Family == t.Family && c.Rows.Any(r => Norm(r[c.GradeColumn]) == Norm(grade))))
                facts.Add(new("grade", t.Family, grade, "化学成分", "原表条件", "原表未给出", t.SourceId, t.Id));
        }
        return facts.Distinct().ToArray();
    }
    internal static object Lookup(string tool, string? family = null, string? grade = null, string? property = null,
        string? condition = null, int offset = 0, int limit = 20, IReadOnlyList<Fact>? facts = null)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0，limit为1～100");
        string[] keys = ["family", "grade", "property", "condition"];
        string?[] inputs = [family, grade, property, condition];
        if (inputs.Count(x => x == "*") > 1) throw new ArgumentException("一次只允许一个参数为*；其余参数须固定");
        var wildcard = Array.FindIndex(inputs, x => x == "*");
        string Get(Fact f, int i) => i switch { 0 => f.Family, 1 => f.Grade, 2 => f.Property, _ => f.Condition };
        var rows = (facts ?? Facts).Where(f => f.Tool == tool).ToList();
        for (var i = 0; i < keys.Length; i++)
            if (!string.IsNullOrWhiteSpace(inputs[i]) && inputs[i] != "*")
            {
                var index = i;
                rows = rows.Where(f => Norm(Get(f, index)) == Norm(inputs[index])).ToList();
            }
        if (rows.Count == 0) return new { error = "没有匹配值；请省略待确认的参数查看可选项" };
        foreach (var i in new[] { 1, 2, 0, 3 })
        {
            if (inputs[i] == "*" || !string.IsNullOrWhiteSpace(inputs[i])) continue;
            var options = rows.Select(f => Get(f, i)).Distinct().ToArray();
            if (i is 0 or 3 && options.Length == 1) continue;
            return new { missing = keys[i], @default = (string?)null, options = options.Skip(offset).Take(limit).ToArray(),
                total = options.Length, nextOffset = offset + limit < options.Length ? (int?)(offset + limit) : null };
        }
        if (wildcard < 0)
        {
            var values = rows.Select(f => f.Value).Distinct().ToArray();
            if (values.Length == 1) return values[0];
            return new { error = "原表存在不同值，无法唯一确定", options = rows.Select(f => new { source = f.Source, value = f.Value }).Distinct().ToArray() };
        }
        var groups = rows.GroupBy(f => Get(f, wildcard)).ToArray();
        if (groups.Any(g => g.Select(f => f.Value).Distinct().Count() != 1)) return new { error = "展开项仍有多个值，请补齐条件" };
        var result = groups.Skip(offset).Take(limit).ToDictionary(g => g.Key, g => g.First().Value);
        if (offset + limit >= groups.Length && offset == 0) return result;
        return new { values = result, total = groups.Length, nextOffset = offset + limit < groups.Length ? (int?)(offset + limit) : null };
    }
}
