using HistoryVulcan.Core.Commands;
using Norma.Formulas;

namespace Norma;

internal static class PhysicalCommands
{
    internal static void Register(ICommandRegistrar registry)
    {
        foreach (var table in PhysicalTables.Tables)
        {
            var parameters = new List<ParameterSpec>
            {
                new() { Name = "q", Type = ParamType.String, Description = "材料名称或返回的条目ID；精确名称优先，否则按空格分词全部包含；省略浏览整表，原表缺名用未辨材料占位；返回属性：" + string.Join("、", table.Fields.Select(f => f.Key + "=" + f.Title + (f.Unit.Length == 0 ? "" : "(" + f.Unit + ")"))) },
                new() { Name = "offset", Type = ParamType.Int, Default = "0", Description = "跳过匹配条数，≥0，缺省0；按 nextOffset 继续翻页" },
                new() { Name = "limit", Type = ParamType.Int, Default = "20", Description = "每页条数1～100，缺省20；0条也是正常查询结果" },
            };
            if (table.Key == "expansion") parameters.Add(new ParameterSpec
            {
                Name = "period", Type = ParamType.String, AllowedValues = PhysicalTables.Periods,
                Description = "原表温度范围（℃），不是瞬时温度；省略返回全部列；只能选原表区间，不插值"
            });
            registry.Register(new CommandDescriptor
            {
                Name = "norma.material." + table.Key, Domain = "norma", CommandClass = "material", Readonly = true,
                Summary = table.Title + "查表（机械材料）：保留区间、单位与无定义项，省略q浏览整表；机械工程师手册",
                Example = "norma.material." + table.Key + (table.Key == "bulk" ? " q=焦炭" : table.Key == "expansion" ? " q=碳钢 period=20~100" : " q=铅"),
                Parameters = parameters,
                Handler = CommandDescriptor.Sync(c =>
                {
                    try
                    {
                        var found = PhysicalTables.Search(table.Key, c.GetString("q"), c.GetString("period"), c.GetInt("offset", 0), c.GetInt("limit", 20));
                        return CommandResult.Ok($"{found.Title}：匹配{found.Total}条，返回{found.Items.Count}条；范围、单位与缺值原因见data", Payload(found));
                    }
                    catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
                }),
            });
        }
    }

    private static object Payload(PhysicalTables.Found found) => new
    {
        table = found.Table, title = found.Title, source = found.Source, scope = found.Scope,
        total = found.Total, offset = found.Offset, returned = found.Items.Count, nextOffset = found.NextOffset,
        items = found.Items.Select(row => new
        {
            id = row.Id, material = row.Material, note = row.Note,
            properties = row.Properties.ToDictionary(pair => pair.Key, pair => new
            {
                text = pair.Value.Text, unit = pair.Value.Unit, value = pair.Value.Value,
                min = pair.Value.Min, max = pair.Value.Max, undefined = pair.Value.Undefined,
            }),
        }),
    };
}
