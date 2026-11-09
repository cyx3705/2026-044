using HistoryVulcan.Core.Commands;
using Norma.Formulas;

namespace Norma;

internal static class NamedMaterialCommands
{
    internal static void Register(ICommandRegistrar registry) => registry.Register(new CommandDescriptor
    {
        Name = "norma.material.library", Domain = "norma", CommandClass = "material", Readonly = true,
        Summary = "非金属及复合材料库：按材料名称/型号返回各条件性能的精简JSON资料卡；含橡胶、塑料、陶瓷、木材、绝缘/摩擦材料及复合材料；金属牌号见material.grade",
        Example = "norma.material.library name=PTFE",
        Parameters =
        [
            new() { Name = "name", Type = ParamType.String, Description = "材料名称、型号或原文代号，如聚四氟乙烯、PTFE、T300/4211；精确查询返回完整卡，部分名称返回选项；PVC等歧义代号列选项。省略返回default=null及材料选项；*=展开名称" },
            new() { Name = "property", Type = ParamType.String, Description = "可选性能/成分过滤，精确表头优先，否则包含匹配，如密度、抗拉强度；唯一属性及条件返回值，原注随必要脚注保留。省略保留全部；*=展开属性" },
            new() { Name = "condition", Type = ParamType.String, Description = "可选：按资料卡返回的完整条件精确过滤，保留基体/增强体/含量基准、方向、温度、尺寸、等级及工艺；省略保留全部；*=展开条件" },
            new() { Name = "family", Type = ParamType.String, Description = "可选类别，如非金属材料、复合材料，或资料卡中的完整类别；省略合并同名材料在不同章节的性能，保留来源条件。*=展开类别；一次最多一个*，其他多值坐标缺参时返回选项" },
            new() { Name = "offset", Type = ParamType.Int, Default = "0", Description = "选项/显式变化表分页偏移≥0，默认0；完整资料卡不分页" },
            new() { Name = "limit", Type = ParamType.Int, Default = "20", Description = "选项/显式变化表每页1～100项，默认20；不截断资料卡" },
        ],
        Handler = CommandDescriptor.Sync(c =>
        {
            try
            {
                var result = NamedMaterials.Query(c.GetString("name"), c.GetString("property"), c.GetString("condition"),
                    c.GetString("family"), c.GetInt("offset", 0), c.GetInt("limit", 20));
                return CommandResult.Ok(result is string text ? text : "材料资料卡", result);
            }
            catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
        })
    });
}
