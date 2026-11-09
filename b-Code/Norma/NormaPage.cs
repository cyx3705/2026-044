using System.Text.Encodings.Web;
using System.Text.Json;
using Norma.Formulas;

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
    public const string LookupPanelId = "std-lookup";
    public const string LookupBoxId = "q";
    public const string MetalPanelId = "metal-strength";
    public const string MetalValueId = "metal-value";
    public const string MetalScaleId = "metal-scale";
    public const string MetalFamilyId = "metal-family";
    public const string MetalGradeId = "metal-grade";
    public const string ListActionId = NormaIdentity.Domain + ".formula.list";
    public const string LookupActionId = NormaIdentity.Domain + ".std.lookup";
    public const string MetalActionId = NormaIdentity.Domain + ".metal.strength";

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
                            type = "panel",
                            id = LookupPanelId,
                            text = "标准代号查表",
                            rows = new object[]
                            {
                                new
                                {
                                    mode = "flex",
                                    widgets = new object[]
                                    {
                                        new { kind = "textbox", id = LookupBoxId, label = "代号/名称", flex = true, minWidth = 180 },
                                        new { kind = "button", action = LookupActionId, text = "查代号" },
                                    },
                                },
                            },
                        },
                        new
                        {
                            type = "panel",
                            id = MetalPanelId,
                            text = "机械材料 · 金属强度换算",
                            rows = new object[]
                            {
                                new
                                {
                                    mode = "flex",
                                    widgets = new object[]
                                    {
                                        new
                                        {
                                            kind = "textbox",
                                            id = MetalFamilyId,
                                            label = "材料",
                                            mode = "select",
                                            options = MetalStrength.Families.Select(item => item.Key).ToArray(),
                                            value = "steel",
                                            minWidth = 110,
                                        },
                                        new { kind = "textbox", id = MetalValueId, label = "硬度值", flex = true, minWidth = 90 },
                                        new
                                        {
                                            kind = "textbox",
                                            id = MetalScaleId,
                                            label = "硬度制",
                                            mode = "select",
                                            options = MetalStrength.Scales.Select(item => item.Key).ToArray(),
                                            value = "hrc",
                                            minWidth = 90,
                                        },
                                        new
                                        {
                                            kind = "textbox",
                                            id = MetalGradeId,
                                            label = "牌号",
                                            mode = "select",
                                            options = new[] { "auto" }.Concat(MetalStrength.Grades.Select(item => item.Key)).ToArray(),
                                            value = "auto",
                                            minWidth = 120,
                                        },
                                        new { kind = "button", action = MetalActionId, text = "换算" },
                                    },
                                },
                            },
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
            new
            {
                id = LookupActionId,
                title = "查代号",
                command = NormaIdentity.Domain + ".std.lookup",
                args = new { q = "{" + LookupBoxId + "}" },
                summary = "按代号或名称查国内外标准代号，结果显示在控制台",
            },
            new
            {
                id = MetalActionId,
                title = "机械材料 · 金属强度换算",
                command = NormaIdentity.Domain + ".metal.strength",
                args = new
                {
                    family = "{" + MetalFamilyId + "}",
                    value = "{" + MetalValueId + "}",
                    scale = "{" + MetalScaleId + "}",
                    grade = "{" + MetalGradeId + "}",
                },
                summary = "按材料族、硬度值与牌号换算各硬度制与强度，结果显示在控制台",
            },
        },
    }, Options);
}
