namespace Norma.Formulas;

// REQ-MATERIAL-013: named references share one query surface with nonmetals.
// These are category/product reference values, never attributes assigned to a grade.
internal static class ReferenceMaterials
{
    internal sealed record Data(NamedMaterials.Page[] Pages, NamedMaterials.Fact[] Facts,
        Dictionary<NamedMaterials.Fact, object> Values, Dictionary<int, string> Categories,
        Dictionary<NamedMaterials.Fact, string> Notes);
    private static readonly Lazy<Data> Loaded = new(Build);
    internal static Data All => Loaded.Value;
    internal static readonly string[] Categories = ["非金属", "复合材料", "弹性", "密度", "热学", "线膨胀", "堆积", "滑动摩擦", "物体摩擦", "滚动摩擦力臂", "产品性能"];

    private static Data Build()
    {
        var pages = new List<NamedMaterials.Page>();
        var facts = new List<NamedMaterials.Fact>();
        var values = new Dictionary<NamedMaterials.Fact, object>();
        var categories = new Dictionary<int, string>();
        var notes = new Dictionary<NamedMaterials.Fact, string>();
        var labels = new[] { "弹性", "密度", "热学", "线膨胀", "堆积", "滑动摩擦", "物体摩擦", "滚动摩擦力臂" };
        for (var i = 0; i < PhysicalTables.Tables.Length; i++)
        {
            var t = PhysicalTables.Tables[i];
            var found = PhysicalTables.Search(t.Key, null, null, 0, 100);
            if (found.NextOffset is not null) throw new InvalidOperationException("通用材料表超过读取上限");
            var key = 7000 + i;
            categories[key] = labels[i];
            var grid = found.Items.Select(r => new[] { r.Material }.Concat(t.Fields.Select(f => r.Properties[f.Key].Text)).ToArray()).ToArray();
            pages.Add(new(key, t.Title, t.Title, "常用设计资料/" + t.Title, "", [grid], "sjzl:" + t.Entry, "sjzl/sjzl.ysc", t.ByteOffset));
            for (var r = 0; r < found.Items.Count; r++)
            for (var c = 0; c < t.Fields.Length; c++)
            {
                var row = found.Items[r]; var field = t.Fields[c]; var cell = row.Properties[field.Key];
                var condition = t.Title + "（通用参考，非牌号指标）";
                if (t.Key == "expansion") condition += "；温区=" + field.Key + "℃";
                var property = t.Key == "expansion" ? "线膨胀系数" : field.Title;
                var fact = new NamedMaterials.Fact(row.Material, "通用参考", property, condition, cell.Text, key, 0, r, c + 1);
                facts.Add(fact);
                values[fact] = cell.Undefined is null ? cell.Text + (field.Unit.Length == 0 ? "" : " " + field.Unit)
                    : new { value = (double?)null, text = cell.Text, unit = field.Unit, reason = cell.Undefined };
                if (row.Note is { Length: > 0 }) notes[fact] = row.Note;
            }
        }
        foreach (var p in ProductSpecs.Pages)
        {
            var entries = ProductSpecs.Entries.Select(e => e with { Values = e.Values.Where(ProductSpecs.IsPerformance).ToArray() })
                .Where(e => e.Values.Any(c => c.Page == p.Key)).ToArray();
            if (entries.Length == 0) continue;
            var key = 10000 + p.Key;
            categories[key] = "产品性能";
            pages.Add(new(key, p.Name, p.Title, p.Path, p.Notes, p.Tables, p.SourceId, "jxclsj/jxclsj.ysc", p.ByteOffset));
            foreach (var e in entries)
            foreach (var c in e.Values.Where(c => c.Page == p.Key))
            {
                var condition = string.Join("；", e.Spec.Select(a => a.Key + "=" + a.Value.Text + a.Value.Unit));
                var f = new NamedMaterials.Fact(e.Name, "产品通用性能", c.Property, condition, c.Value, key, c.Table, c.Row, c.Column);
                facts.Add(f);
                values[f] = c.Prefix + c.Value + (c.Unit.Length == 0 ? "" : " " + c.Unit);
                var note = string.Join(" ", new[] { p.Notes, c.Note }.Where(s => s.Length > 0).Distinct());
                if (note.Length > 0) notes[f] = note;
            }
        }
        return new(pages.ToArray(), facts.ToArray(), values, categories, notes);
    }
}
