using HistoryVulcan.Core.Commands;
using Norma.Formulas;

namespace Norma;

internal static class DriveCommands
{
    internal static void Register(ICommandRegistrar registry) => registry.Register(new CommandDescriptor
    {
        Name = "norma.drive.efficiency", Domain = "norma", CommandClass = "drive", Readonly = true,
        Summary = "查询机械传动的概略效率区间",
        Example = "norma.drive.efficiency q=圆柱齿轮传动",
        Parameters =
        [
            new() { Name = "q", Type = ParamType.String, Description = "类别、传动型式、精度/润滑/角度关键词或条目ID；空格分词全部匹配，类别/型式或完整类别/型式精确匹配优先；省略浏览整表。如 圆柱齿轮传动 8级；η无量纲（单位1），仅供概略估算，不自动连乘" },
            new() { Name = "offset", Type = ParamType.Int, Default = "0", Description = "跳过匹配条数，≥0，默认0；按nextOffset继续分页" },
            new() { Name = "limit", Type = ParamType.Int, Default = "20", Description = "每页条数1～100，默认20；零匹配是正常结果" },
        ],
        Handler = CommandDescriptor.Sync(c =>
        {
            try
            {
                var found = DriveEfficiency.Search(c.GetString("q"), c.GetInt("offset", 0), c.GetInt("limit", 20));
                return CommandResult.Ok($"{DriveEfficiency.Title}：匹配{found.Total}条，返回{found.Items.Count}条；条件、范围和源表异常见data", new
                {
                    title = DriveEfficiency.Title, source = found.Source, scope = found.Scope,
                    total = found.Total, offset = found.Offset, returned = found.Items.Count, nextOffset = found.NextOffset,
                    items = found.Items.Select(row => new
                    {
                        id = row.Id, category = row.Category, form = row.Form,
                        efficiency = new { text = row.Efficiency.Text, unit = row.Efficiency.Unit, value = row.Efficiency.Value,
                            min = row.Efficiency.Min, max = row.Efficiency.Max, undefined = row.Efficiency.Undefined },
                        note = row.Note,
                    }),
                });
            }
            catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
        }),
    });
}
