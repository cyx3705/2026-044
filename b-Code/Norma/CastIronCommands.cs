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
                new() { Name = "grade", Type = ParamType.String, Description = "牌号或品种精确匹配，忽略空格和推荐标记*；省略返回default=null及牌号选项，不自动浏览数据；命名/热处理页用铸铁名称/状态名称。单独*=变化值" },
                new() { Name = "property", Type = ParamType.String, Description = "所求属性/元素，如抗拉强度、硬度、Cr、C；省略返回属性选项；*=变化值，返回属性→值表。值自带单位及≥/≤/区间，成分为质量分数%，强度MPa；缺值不填0" },
                new() { Name = "condition", Type = ParamType.String, Description = "试样、壁厚、温度/热处理或同名配方条件，按缺参选项原样填写，如单铸试棒；省略时唯一条件自动确定，多条件返回选项。*=只展开条件；不插值、不取中值" },
                new() { Name = "family", Type = ParamType.String, Description = "分支精确名称，仅同名跨分支时需补充；省略且存在多个分支时返回选项。*=展开分支；grade/property/condition/family一次只允许一个*，其他变量必须唯一" },
                new() { Name = "offset", Type = ParamType.Int, Default = "0", Description = "选项/变化表的分页偏移≥0，默认0；单值不分页" },
                new() { Name = "limit", Type = ParamType.Int, Default = "20", Description = "选项/变化表每页1～100项，默认20；按nextOffset续页" },
            };
            registry.Register(new CommandDescriptor
            {
                Name = "norma.material." + key, Domain = "norma", CommandClass = "material", Readonly = true,
                Summary = key == "grade" ? "铸铁牌号及化学成分取值：参数齐全只返回值；缺变量返回默认值和选项；仅显式一个参数=*时返回表" : "铸铁材料力学性能（材料强度）取值：参数齐全只返回值；缺变量返回默认值和选项；仅显式一个参数=*时返回表",
                Example = "norma.material." + key + (key == "grade" ? " grade=KmTBCr15Mo property=Cr" : " grade=HT250 property=抗拉强度 condition=单铸试棒"),
                Parameters = parameters,
                Handler = CommandDescriptor.Sync(c =>
                {
                    try
                    {
                        var value = CastIronLookup.Lookup(key, c.GetString("family"), c.GetString("grade"), c.GetString("property"), c.GetString("condition"), c.GetInt("offset", 0), c.GetInt("limit", 20));
                        return CommandResult.Ok(value is string s ? s : "取值结果", value);
                    }
                    catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
                })
            });
        }
    }
}
