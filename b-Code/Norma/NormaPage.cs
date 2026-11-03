using System.Text.Encodings.Web;
using System.Text.Json;

namespace Norma;

/// <summary>
/// Aurora 页面协议 V1：一页，上面一块控制面板放按钮，下面一张表。
/// </summary>
/// <remarks>
/// 三条规矩（离线测试守着）：owner 与 scene 用 <see cref="NormaIdentity.PageOwner"/>；
/// 每个按钮的 action 都在 <see cref="Actions()"/> 里声明；每个动作指向的指令都已注册。
/// </remarks>
internal static class NormaPage
{
    public const string PageId = NormaIdentity.Domain;
    public const string PanelId = "catalog-actions";
    public const string CatalogTableId = "catalog";
    public const string CatalogView = "catalog";
    public const string ListActionId = NormaIdentity.Domain + ".formula.list";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Describe() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        owner = NormaIdentity.PageOwner,
        pages = new object[]
        {
            new
            {
                id = PageId,
                title = NormaIdentity.PageTitle,
                scene = NormaIdentity.PageOwner,
                placement = new { side = "center", visible = true, singleton = true },
                content = new
                {
                    type = "stack",
                    gap = "tight",
                    children = new object[]
                    {
                        new
                        {
                            type = "panel",
                            id = PanelId,
                            text = "操作",
                            rows = new object[]
                            {
                                new
                                {
                                    mode = "even",
                                    widgets = new object[]
                                    {
                                        new { kind = "button", action = ListActionId, text = "列出目录到控制台" },
                                    },
                                },
                            },
                        },
                        new
                        {
                            type = "text",
                            style = "caption",
                            text = "每条计算就是一条指令，在控制台或经 MCP 调用；参数与出处见 norma.formula.show。",
                        },
                        new
                        {
                            type = "table",
                            id = CatalogTableId,
                            dataSource = new
                            {
                                command = NormaIdentity.Domain + ".ui.data",
                                args = new { view = CatalogView },
                            },
                            columns = new object[]
                            {
                                new { key = "topic", title = "类", width = "90" },
                                new { key = "command", title = "指令", width = "200" },
                                new { key = "title", title = "名称", width = "220" },
                                new { key = "source", title = "出处", width = "*" },
                            },
                        },
                    },
                },
            },
        },
    }, Options);

    public static string Actions() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        owner = NormaIdentity.PageOwner,
        actions = new object[]
        {
            new
            {
                id = ListActionId,
                title = "列出目录",
                command = NormaIdentity.Domain + ".formula.list",
                summary = "把全部计算列到控制台",
            },
        },
    }, Options);
}
