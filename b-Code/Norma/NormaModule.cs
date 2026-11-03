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
                context.GetString("view")?.Trim().ToLowerInvariant() is null or NormaPage.CatalogView
                    ? CommandResult.Ok("计算目录", NormaCommands.CatalogRows(null))
                    : CommandResult.Fail($"未知 view；支持 {NormaPage.CatalogView}")),
        });
    }

    private static CommandResult Json(string json) => CommandResult.Ok(json, json);

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
