using HistoryVulcan.Core.Commands;
using HistoryVulcan.Core.Modules;
using Norma.Formulas;

namespace Norma;

/// <summary>
/// 模块装配入口：每条计算一条只读指令（见 <see cref="NormaCommands"/>），加目录两条与 Aurora 页面协议三条。
/// </summary>
/// <remarks>
/// 全部指令只读、无状态：相同输入永远得到相同输出，谁调都安全（mcpExposure=readonly 时全部投影到 MCP）。
/// 界面内部协议一律带 <c>HiddenReason</c>。
/// </remarks>
public sealed class NormaModule : IModuleContextAware
{
    private const string Hidden = "Aurora 页面内部协议，不对远程消费面暴露";

    /// <summary>宿主在装载时注入命令注册器。</summary>
    public void Attach(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RegisterCommands(Register);
    }

    internal static void Register(ICommandRegistrar registry)
    {
        foreach (var calculation in Catalog.All)
            registry.Register(NormaCommands.For(calculation));

        registry.Register(new CommandDescriptor
        {
            Name = NormaIdentity.Domain + ".formula.list",
            Domain = NormaIdentity.Domain,
            CommandClass = "formula",
            Summary = "列出全部计算（指令名、名称、出处），可按类筛选",
            Example = NormaIdentity.Domain + ".formula.list topic=gear",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "topic",
                    Description = "只列这一类，如 gear、bolt、bearing；省略列全部",
                    Type = ParamType.String,
                    AllowedValues = Catalog.All.Select(item => item.Topic).Distinct().ToArray(),
                },
            ],
            Handler = CommandDescriptor.Sync(context =>
            {
                var rows = NormaCommands.CatalogRows(context.GetString("topic"));
                return CommandResult.Ok($"共 {rows.Count} 条计算", rows);
            }),
        });

        registry.Register(new CommandDescriptor
        {
            Name = NormaIdentity.Domain + ".formula.show",
            Domain = NormaIdentity.Domain,
            CommandClass = "formula",
            Summary = "查看一条计算的完整元数据：出处、适用范围、输入输出的符号与单位",
            Example = NormaIdentity.Domain + ".formula.show id=gear.contact",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "id",
                    Description = "计算编号，即指令名去掉 norma. 前缀，如 gear.contact",
                    Type = ParamType.String,
                    Required = true,
                    Position = 0,
                },
            ],
            Handler = CommandDescriptor.Sync(context =>
            {
                var id = context.RequireString("id");
                if (id.StartsWith(NormaIdentity.Domain + ".", StringComparison.OrdinalIgnoreCase))
                    id = id[(NormaIdentity.Domain.Length + 1)..];
                return Catalog.Find(id) is { } calculation
                    ? CommandResult.Ok($"{calculation.Title}：{calculation.Source}", NormaCommands.Describe(calculation))
                    : CommandResult.Fail($"没有计算 {id}；用 {NormaIdentity.Domain}.formula.list 查看全部");
            }),
        });

        registry.Register(new CommandDescriptor
        {
            Name = NormaIdentity.Domain + ".std.lookup",
            Domain = NormaIdentity.Domain,
            CommandClass = "std",
            Summary = "国内外标准代号查表：按代号或名称互查（GB→国家标准、国际标准化→ISO）",
            Example = NormaIdentity.Domain + ".std.lookup q=JB",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "q",
                    Description = "标准代号或名称；代号如 GB、JB/ZQ、ISO、DIN，名称按包含匹配",
                    Type = ParamType.String,
                    Required = true,
                    Position = 0,
                },
                new ParameterSpec
                {
                    Name = "limit",
                    Description = "最多返回多少条，1～100，缺省 20",
                    Type = ParamType.Int,
                    Default = "20",
                },
            ],
            Handler = CommandDescriptor.Sync(context =>
            {
                var query = context.RequireString("q").Trim();
                if (query.Length == 0)
                    return CommandResult.Fail("q 不能为空");
                var limit = context.GetInt("limit", 20);
                if (limit is < 1 or > 100)
                    return CommandResult.Fail("limit 应在 1～100");
                var found = Standards.Lookup(query, limit);
                return CommandResult.Ok(StandardMessage(found), StandardPayload(found));
            }),
        });

        registry.Register(new CommandDescriptor
        {
            Name = NormaIdentity.Domain + ".metal.strength",
            Domain = NormaIdentity.Domain,
            CommandClass = "metal",
            Summary = "金属硬度与抗拉强度换算：按手册表 PCHIP 插值，分钢种并标明各硬度制的无定义区间",
            Example = NormaIdentity.Domain + ".metal.strength value=45 scale=hrc steel=generic",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "value",
                    Description = "硬度值；按 scale 指定的硬度制",
                    Type = ParamType.Double,
                    Required = true,
                    Position = 0,
                },
                new ParameterSpec
                {
                    Name = "scale",
                    Description = "输入硬度制：hrc/hra/hr15n/hr30n/hr45n/hv/hb/hrb/hr15t/hr30t/hr45t/hbs",
                    Type = ParamType.String,
                    Default = "hrc",
                    AllowedValues = MetalStrength.Scales.Select(item => item.Key).ToArray(),
                },
                new ParameterSpec
                {
                    Name = "steel",
                    Description = "强度口径：generic 不分钢种，其余为手册钢种列",
                    Type = ParamType.String,
                    Default = MetalStrength.GenericSteel,
                    AllowedValues = MetalStrength.Steels.Select(item => item.Key).ToArray(),
                },
            ],
            Handler = CommandDescriptor.Sync(context =>
            {
                var value = context.GetDouble("value", double.NaN);
                try
                {
                    var result = MetalStrength.Convert(
                        context.GetString("scale") ?? "hrc",
                        value,
                        context.GetString("steel") ?? MetalStrength.GenericSteel);
                    return CommandResult.Ok(MetalMessage(result), MetalPayload(result));
                }
                catch (ArgumentException ex)
                {
                    return CommandResult.Fail(ex.Message);
                }
            }),
        });

        registry.Register(Internal("describe", "返回页面描述", _ => Json(NormaPage.Describe())));
        registry.Register(Internal("actions", "返回页面动作声明", _ => Json(NormaPage.Actions())));
        registry.Register(new CommandDescriptor
        {
            Name = NormaIdentity.Domain + ".ui.data",
            Domain = NormaIdentity.Domain,
            CommandClass = "ui",
            Summary = "返回页面表格的行",
            Readonly = true,
            HiddenReason = Hidden,
            AllowUnspecifiedParameters = true,
            Handler = CommandDescriptor.Sync(context =>
            {
                var view = context.GetString("view")?.Trim().ToLowerInvariant();
                return view switch
                {
                    null or "" or NormaPage.CatalogView
                        => CommandResult.Ok("目录", NormaCommands.PageRows()),
                    _ => CommandResult.Fail($"未知 view；支持 {NormaPage.CatalogView}"),
                };
            }),
        });
    }

    private static CommandResult Json(string json) => CommandResult.Ok(json, json);

    private static string StandardMessage(StandardLookup found)
    {
        if (found.Items.Count == 0)
            return $"没有找到「{found.Query}」；代号示例 GB、JB/ZQ、ISO，名称示例 国家标准、国际标准化";
        var shown = string.Join("；", found.Items.Take(5).Select(item =>
            $"{item.Code}={item.Name}{(item.Note is null ? "" : $"（{item.Note}）")}"));
        var total = found.Total > found.Items.Count ? $"共 {found.Total} 条，返回前 {found.Items.Count} 条：" : "";
        return $"{(found.ByCode ? "代号" : "名称")}「{found.Query}」{total}{shown}";
    }

    private static object StandardPayload(StandardLookup found) => new
    {
        query = found.Query,
        mode = found.ByCode ? "code" : "name",
        total = found.Total,
        returned = found.Items.Count,
        items = found.Items.Select(item => new
        {
            code = item.Code,
            name = item.Name,
            scope = item.Scope,
            note = item.Note,
        }),
    };

    private static string MetalMessage(MetalResult result)
    {
        var steelTitle = MetalStrength.SteelTitle(result.Steel);
        var defined = MetalStrength.Scales
            .Where(item => result.Values.ContainsKey(item.Key))
            .Select(item => $"{item.Title}={Num.Format(result.Values[item.Key])}");
        var text = $"硬度换算（{MetalStrength.ScaleTitle(result.Scale)}={Num.Format(result.Value)}）：" +
                   string.Join("，", defined);

        if (result.SigmaB is { } sigma)
        {
            text += $"；σb({steelTitle})={Num.Format(sigma)} MPa";
            if (result.Steel != MetalStrength.GenericSteel && result.SigmaBGeneric is { } generic)
                text += $"（不分钢种 {Num.Format(generic)} MPa）";
        }
        else if (result.SigmaBUndefined is { } why)
        {
            text += $"；σb({steelTitle})：{why}";
        }

        var undefined = MetalStrength.Scales
            .Where(item => result.Undefined.ContainsKey(item.Key))
            .Select(item => $"{item.Title}=无定义（{result.Undefined[item.Key]}）")
            .ToList();
        return undefined.Count == 0 ? text : text + "；" + string.Join("；", undefined);
    }

    private static object MetalPayload(MetalResult result) => new
    {
        input = new { scale = result.Scale, value = result.Value },
        steel = new { key = result.Steel, title = MetalStrength.SteelTitle(result.Steel) },
        hardness = MetalStrength.Scales.ToDictionary(
            item => item.Key,
            item => result.Values.TryGetValue(item.Key, out var value) ? (double?)value : null),
        undefined = result.Undefined,
        sigma_b = new
        {
            steel = result.SigmaB,
            generic = result.SigmaBGeneric,
            undefined = result.SigmaBUndefined,
        },
        source = MetalStrength.Source,
        scope = MetalStrength.Scope,
    };

    private static CommandDescriptor Internal(string method, string summary, Func<CommandContext, CommandResult> handler)
        => new()
        {
            Name = NormaIdentity.Domain + ".ui." + method,
            Domain = NormaIdentity.Domain,
            CommandClass = "ui",
            Summary = summary,
            Readonly = true,
            HiddenReason = Hidden,
            Handler = CommandDescriptor.Sync(handler),
        };
}
