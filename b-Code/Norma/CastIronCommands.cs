using HistoryVulcan.Core.Commands;
using Norma.Formulas;

namespace Norma;

internal static class CastIronCommands
{
    internal static void Register(ICommandRegistrar registry)
    {
        foreach (var key in new[] { "grade", "strength" })
        {
            var parameters = new List<ParameterSpec>
            {
                new() { Name = "q", Type = ParamType.String, Description = "名称/牌号/属性关键词或行ID；空格分词全部匹配，精确牌号优先并保留该牌号全部条件；省略分页浏览" },
                new() { Name = "family", Type = ParamType.String, Description = "铸铁分支名称包含匹配：灰铸铁/球墨铸铁/可锻铸铁/蠕墨铸铁/耐热铸铁/高硅耐蚀铸铁/耐磨铸铁/白口铸铁/机床导轨用耐磨铸铁/机床零件用耐磨铸铁/汽缸套用耐磨铸铁/活塞环用耐磨铸铁/冷硬铸铁；省略全部" },
                new() { Name = "grade", Type = ParamType.String, Description = "原牌号或品种名称精确匹配，忽略空格及推荐标记*，不猜改原表牌号拼写；同牌号多试样/壁厚全部返回" },
                new() { Name = "table", Type = ParamType.String, Description = "源表ID，如iron:295:2；省略跨表查询，完整表目录见data.catalog；不能用手册页号代替" },
                new() { Name = "offset", Type = ParamType.Int, Default = "0", Description = "匹配行偏移≥0，默认0；使用nextOffset续页" },
                new() { Name = "limit", Type = ParamType.Int, Default = "20", Description = "每页1～100行，默认20；不自动选单个试样条件" },
            };
            if (key == "grade") parameters.Add(new() { Name = "section", Type = ParamType.String, Default = "all",
                AllowedValues = ["all", "composition", "designation", "heat_state", "catalog"],
                Description = "all全部；composition化学成分（质量分数%）；designation命名/牌号；heat_state热处理状态；catalog力学表牌号目录（不杜撰化学成分）" });
            registry.Register(new CommandDescriptor
            {
                Name = "norma.material." + key, Domain = "norma", CommandClass = "material", Readonly = true,
                Summary = key == "grade" ? "铸铁牌号及化学成分查表（机械材料）：覆盖全部分支及命名/热处理代号，质量分数%；保留配方行序、边界与出处" : "铸铁材料力学性能（材料强度）查表：覆盖全部分支，含硬度/挠度等；区分试样、壁厚、状态及温度，保留最小/最大和参考值",
                Example = "norma.material." + key + (key == "grade" ? " grade=KmTBCr15Mo" : " grade=HT250"),
                Parameters = parameters,
                Handler = CommandDescriptor.Sync(c =>
                {
                    try
                    {
                        var f = CastIron.Search(key, c.GetString("q"), c.GetString("family"), c.GetString("grade"), c.GetString("table"), c.GetString("section"), c.GetInt("offset", 0), c.GetInt("limit", 20));
                        return CommandResult.Ok($"铸铁{(key == "grade" ? "牌号及化学成分" : "材料力学性能")}：匹配{f.Total}行，返回{f.Items.Count}行；表头条件/脚注见tables", new
                        {
                            scope = CastIron.Scope, total = f.Total, offset = f.Offset, returned = f.Items.Count, nextOffset = f.NextOffset,
                            catalog = CastIron.Tables.Select(t => new { id = t.Id, title = t.Title, family = t.Family, kind = t.Kind, rows = t.Rows.Length }),
                            tables = f.Tables.Select(t => new { id = t.Id, title = t.Title, sourceId = t.SourceId, sourceFile = t.SourceFile, byteOffset = t.ByteOffset,
                                directoryPath = t.DirectoryPath, notes = t.Notes, fields = t.Fields.Select(x => new { key = x.Key, name = x.Name, unit = x.Unit, headers = x.Headers, semantics = x.Semantics, role = x.Role }) }),
                            items = f.Items.Select(r => new { id = r.Id, table = r.TableId, family = r.Family, grade = r.Grade, kind = r.Kind, note = r.Note, relatedId = r.RelatedId,
                                properties = r.Properties.ToDictionary(p => p.Key, p => Cell(p.Value)) })
                        });
                    }
                    catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
                })
            });
        }
    }
    private static object Cell(CastIron.Cell c) => new { text = c.Text, unit = c.Unit, kind = c.Kind, value = c.Value, min = c.Min, max = c.Max,
        minInclusive = c.MinInclusive, maxInclusive = c.MaxInclusive, options = c.Options, undefined = c.Undefined, parenthesized = c.Parenthesized,
        parts = c.Parts?.ToDictionary(p => p.Key, p => Cell(p.Value)) };
}
