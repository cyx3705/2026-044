using HistoryVulcan.Core.Commands;
using Norma.Formulas;

namespace Norma;

internal static class NamedMaterialCommands
{
    internal static void Register(ICommandRegistrar registry) => registry.Register(new CommandDescriptor
    {
        Name = "norma.material.library", Domain = "norma", CommandClass = "material", Readonly = true,
        Summary = "材料库：按名称/型号查询非金属、复合材料，以及无牌号类别/产品的通用物性、堆积和摩擦数据；category可选分类。具体金属牌号性能见material.grade；尺寸、重量、公差见product.spec",
        Example = "norma.material.library name=PTFE",
        Parameters =
        [
            new() { Name = "name", Type = ParamType.String, Description = "材料名称、型号、配对或对象名，如PTFE、T300/4211、铅、碳钢、钢-钢、滚动轴承/深沟球轴承/轴向载荷；精确返回完整卡，部分名/歧义名仅给选项。省略返回default=null及分页名称选项；*=展开名称。通用参考值不作为任何具体牌号的保证指标" },
            new() { Name = "category", Type = ParamType.String, AllowedValues = ReferenceMaterials.Categories, Description = "可选分类：非金属/复合材料/弹性/密度/热学/线膨胀/堆积/滑动摩擦/物体摩擦/滚动摩擦力臂/产品性能；省略按名称合并全部来源工况。滚动摩擦力臂为mm，摩擦系数无量纲。产品性能是未明确牌号的钢带/钢丝等原表，不推定牌号" },
            new() { Name = "property", Type = ParamType.String, Description = "可选性能/成分过滤，精确表头优先，否则包含匹配，如密度、抗拉强度；唯一属性及条件返回值，原注随必要脚注保留。省略保留全部；*=展开属性" },
            new() { Name = "condition", Type = ParamType.String, Description = "可选：按卡片完整条件精确过滤，保留配方、方向、温度、厚度/直径、等级及工艺；省略保留全部；*=展开条件。线膨胀温区属于条件，不插值、不把通用物性拼给具体牌号" },
            new() { Name = "family", Type = ParamType.String, Description = "可选类别，如非金属材料、复合材料，或资料卡中的完整类别；省略合并同名材料在不同章节的性能，保留来源条件。*=展开类别；一次最多一个*，其他多值坐标缺参时返回选项" },
            new() { Name = "offset", Type = ParamType.Int, Default = "0", Description = "选项/显式变化表分页偏移≥0，默认0；完整资料卡不分页" },
            new() { Name = "limit", Type = ParamType.Int, Default = "20", Description = "选项/显式变化表每页1～100项，默认20；不截断资料卡" },
        ],
        Handler = CommandDescriptor.Sync(c =>
        {
            try
            {
                var result = NamedMaterials.Query(c.GetString("name"), c.GetString("property"), c.GetString("condition"),
                    c.GetString("family"), c.GetInt("offset", 0), c.GetInt("limit", 20), c.GetString("category"));
                return CommandResult.Ok(result is string text ? text : "材料资料卡", result);
            }
            catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
        })
    });
}
