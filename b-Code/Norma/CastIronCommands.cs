using HistoryVulcan.Core.Commands;
using Norma.Formulas;

namespace Norma;

internal static class CastIronCommands
{
    internal static void Register(ICommandRegistrar registry)
    {
        const string key = "grade";
        // A single descriptor replaces the former grade/strength pair.
        {
            var parameters = new List<ParameterSpec>
            {
                new() { Name = "grade", Type = ParamType.String, Description = "材料牌号或品种名，忽略空格和推荐标记；只填此项返回材料卡片；手册明确对应的代号/旧牌号可查，如ZL101、LY12，歧义返回目标牌号选项；无牌号配方默认返回非等同代表选型卡。省略返回牌号选项；*=兼容单变量展开" },
                new() { Name = "property", Type = ParamType.String, Description = "可选属性/元素过滤，如抗拉强度、Cr。唯一条件返回带单位的值；有来源疑点/必要脚注时附原注，多条件返回对应性能；省略保留全部。*=兼容属性展开" },
                new() { Name = "condition", Type = ParamType.String, Description = "可选条件精确过滤：试样/壁厚/温度/热处理/同名配方，按卡片条件填写；省略保留所有场景，不插值；*=兼容条件展开" },
                new() { Name = "family", Type = ParamType.String, Description = "可选材料分支；可用钢、有色金属、高温合金、耐蚀合金、粉末冶金筛选；同牌号跨产品合并，其他同名跨分支返回选项；一次最多一个参数为*" },
                new() { Name = "category", Type = ParamType.String, AllowedValues = ["成分", "力学", "物理", "工艺"], Description = "可选性能分类：成分/力学/物理/工艺；省略全部。原material.strength改用category=力学；分类不新增工具" },
                new() { Name = "basis", Type = ParamType.String, Default = "representative", AllowedValues = ["representative", "source"], Description = "名称查询口径：representative默认返回代表牌号及原配方，明确非等同；source返回原配方。直接查正式牌号不受影响；属性过滤跟随所选口径" },
                new() { Name = "offset", Type = ParamType.Int, Default = "0", Description = "选项/显式变化表的分页偏移≥0，默认0；材料卡片完整返回，不分页" },
                new() { Name = "limit", Type = ParamType.Int, Default = "20", Description = "选项/显式变化表每页1～100项，默认20；材料卡片不受此参数截断" },
            };
            registry.Register(new CommandDescriptor
            {
                Name = "norma.material." + key, Domain = "norma", CommandClass = "material", Readonly = true,
                Summary = "材料牌号库：以牌号为主键，返回成分、各场景力学/物理性能和热处理工艺；含铸铁、钢、有色及特种合金。category可选性能分类；非金属、无牌号材料及通用物性见material.library，尺寸规格见product.spec",
                Example = "norma.material." + key + " grade=HT250",
                Parameters = parameters,
                Handler = CommandDescriptor.Sync(c =>
                {
                    try
                    {
                        var value = MaterialLibrary.Query(key, c.GetString("family"), c.GetString("grade"), c.GetString("property"), c.GetString("condition"), c.GetInt("offset", 0), c.GetInt("limit", 20), c.GetString("basis") ?? "representative", c.GetString("category"));
                        return CommandResult.Ok(value is string s ? s : "材料查询结果", value);
                    }
                    catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
                })
            });
        }
    }
}
