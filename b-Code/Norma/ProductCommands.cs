using HistoryVulcan.Core.Commands;
using Norma.Formulas;

namespace Norma;

internal static class ProductCommands
{
    internal static void Register(ICommandRegistrar registry) => registry.Register(new CommandDescriptor
    {
        Name = "norma.product.spec", Domain = "norma", CommandClass = "product", Readonly = true,
        Summary = "查询产品规格、理论重量和尺寸公差",
        Example = "norma.product.spec name=热轧普通工字钢 spec=20a property=理论重量",
        Parameters =
        [
            new() { Name = "name", Type = ParamType.String, Description = "产品完整名称或原页名；部分名称仅返回候选；省略给分页目录，*=展开目录。公差专表用其完整名称。已收录产品及各自spec字段如下，括号不是名称的一部分（尺寸默认mm，型号无量纲，带材原文单位例外保留）：" + ProductSpecs.InputGuide },
            new() { Name = "spec", Type = ParamType.String, Description = "JSON对象字符串，非嵌套对象：如{\"外径\":32,\"壁厚\":3}；或直接填写型号20a。常用字段：型号、类别、厚度、宽度、外径、内径、精度；型钢符号B/b、H/h严格区分大小写，按缺参返回字段填写。尺寸默认mm；数值匹配原表离散尺寸/明示区间，不能把范围当离散供应系列。一个字段可填*，如{\"外径\":32,\"壁厚\":\"*\"}。省略不是展开全部；缺必要坐标返回default=null及选项。公差分组不默认普通级" },
            new() { Name = "property", Type = ParamType.String, Description = "可选返回项，精确字段优先，否则包含：如理论重量、截面面积、厚度允许偏差、允许偏差；只有唯一属性/条件才返回值，必要原注保留。省略返回完整规格卡；*=列该规格全部属性。一次最多一个*，其他多值尺寸仍需确定。重量取原表，不调用密度计算、不插值或外推；—/空白不当有效组合" },
            new() { Name = "standard", Type = ParamType.String, Description = "可选：按原页出处筛选标准及年代，如GB/T706、1988；保留手册旧版年份，不代表现行标准；不支持*" },
            new() { Name = "offset", Type = ParamType.Int, Default = "0", Description = "目录/选项/显式变化表分页偏移≥0，默认0；完整单规格卡不分页" },
            new() { Name = "limit", Type = ParamType.Int, Default = "20", Description = "目录/选项/显式变化表每页1～100，默认20；不截断单规格卡" },
        ],
        Handler = CommandDescriptor.Sync(c =>
        {
            try
            {
                var result = ProductSpecs.Query(c.GetString("name"), c.GetString("spec"), c.GetString("property"),
                    c.GetString("standard"), c.GetInt("offset", 0), c.GetInt("limit", 20));
                return CommandResult.Ok(result is string value ? value : "产品规格", result);
            }
            catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
        })
    });
}
