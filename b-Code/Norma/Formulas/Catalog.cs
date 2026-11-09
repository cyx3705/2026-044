namespace Norma.Formulas;

/// <summary>全部计算的目录。新增一类计算：写一个静态类的 <c>All()</c>，在这里加一行。</summary>
public static class Catalog
{
    private static readonly Lazy<IReadOnlyList<Calculation>> Items = new(() =>
    {
        var all = Gears.All()
            .Concat(Shafts.All())
            .Concat(Joints.All())
            .Concat(Elements.All())
            .Concat(Materials.All())
            .ToList();
        var duplicate = all.GroupBy(item => item.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"计算编号重复：{duplicate.Key}");
        return all;
    });

    public static IReadOnlyList<Calculation> All => Items.Value;

    public static Calculation? Find(string id)
        => All.FirstOrDefault(item => string.Equals(item.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>主题的中文名，目录与页面分组用。</summary>
    public static string TopicTitle(string topic) => topic switch
    {
        "gear" => "齿轮",
        "drive" => "传动基础",
        "shaft" => "轴",
        "fatigue" => "疲劳",
        "key" => "键",
        "bolt" => "螺纹连接",
        "screw" => "螺旋传动",
        "weld" => "焊缝",
        "fit" => "公差与配合",
        "bearing" => "滚动轴承",
        "spring" => "弹簧",
        "belt" => "带传动",
        "chain" => "链传动",
        "beam" => "梁",
        "material" => "材料强度",
        _ => topic,
    };
}
