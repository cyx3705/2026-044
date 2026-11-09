using HistoryVulcan.Core.Commands;

namespace Norma;

internal static class ManualCommands
{
    // 临时开发脚手架：用户明确要求固定本机手册默认路径，未来随查询工具拆除。
    internal const string DefaultRoot = @"C:\Users\Administrator\Desktop\easyTOOL\机械工程师手册";
    internal static void Register(ICommandRegistrar registry)
    {
        registry.Register(new CommandDescriptor
        {
            Name = "norma.manual.search", Domain = "norma", CommandClass = "manual", Readonly = true,
            Summary = "搜索本地机械工程师手册的标题或正文，返回可复用页 ID、出处与摘要；q 省略可浏览目录",
            Example = "norma.manual.search q=硬度 chapter=sjzl",
            Parameters = [Root(), Text("q", "关键词，空格分隔为全部匹配；省略列目录，如 硬度 强度"),
                Choice("scope", "匹配标题或正文；title 查目录更快，text 包含表格文字", "title", ["title", "text"]),
                Text("chapter", "章节标识，如 sjzl、13-01、25-04/24-04；省略搜索全书；从页 ID 冒号前取得"),
                Number("offset", "跳过匹配条数，≥0，缺省 0，用于连续翻页", "0"),
                Number("limit", "每页返回条数，1～100，缺省 20", "20")],
            Handler = CommandDescriptor.Sync(c => Execute(() => Manual.Search((c.GetString("root") ?? DefaultRoot), c.GetString("q") ?? "",
                c.GetString("scope") ?? "title", c.GetString("chapter"), c.GetInt("offset", 0), c.GetInt("limit", 20))))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.manual.read", Domain = "norma", CommandClass = "manual", Readonly = true,
            Summary = "按搜索返回的页 ID 读取手册正文或原始 HTML，分页保留表格文字、来源字节位置和图片引用",
            Example = "norma.manual.read id=sjzl:17",
            Parameters = [Root(), new ParameterSpec { Name = "id", Description = "搜索返回的页 ID，如 sjzl:17；冒号后是条目序号，不是印刷页码", Type = ParamType.String, Required = true },
                Choice("format", "text 为正文/表格文本，html 为原始页面（可核对合并单元格）", "text", ["text", "html"]),
                Number("offset", "正文字符偏移，≥0，缺省 0；下页使用返回的 nextOffset", "0"),
                Number("length", "本次最多返回字符数，1～20000，缺省 6000", "6000")],
            Handler = CommandDescriptor.Sync(c => Execute(() => Manual.Read((c.GetString("root") ?? DefaultRoot), c.RequireString("id"),
                c.GetString("format") ?? "text", c.GetInt("offset", 0), c.GetInt("length", 6000))))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.manual.screenshot", Domain = "norma", CommandClass = "manual", Readonly = true,
            Summary = "仅截原手册右侧正文为PNG（自动识别正文控件，排除左侧目录和工具栏）；支持正文内局部裁切；先manual.open按名称打开页；mode=html为提取HTML",
            Example = "norma.manual.screenshot mode=window",
            Parameters = [Root(), Choice("mode", "window为原手册当前窗口系统截图；html为提取页渲染（不能代替原版核对）", "window", ["window", "html"]),
                Text("id", "仅mode=html必填，manual.search返回的页ID；window请省略且先在原手册打开所需页面"),
                Number("x", "仅window：相对右侧正文左上角的横向裁切像素，0～12000", "0"),
                Number("y", "window：相对右侧正文纵向裁切像素；html：页面纵向偏移像素；默认0", "0"),
                Number("width", "window：裁切宽度，0截到右边缘；html：640～2400，0使用1400", "0"),
                Number("height", "window：裁切高度，0截到底边缘；html：300～4000，0使用1600", "0")],
            Handler = CommandDescriptor.Sync(c => Execute(() =>
            {
                var mode = c.GetString("mode") ?? "window";
                var width = c.GetInt("width", 0);
                var height = c.GetInt("height", 0);
                if (mode == "window") return ManualWindowScreenshot.Capture(c.GetString("root") ?? DefaultRoot, c.GetString("id"),
                    c.GetInt("x", 0), c.GetInt("y", 0), width, height);
                if (mode != "html") throw new ArgumentException("mode应为window或html");
                if (c.GetInt("x", 0) != 0) throw new ArgumentException("html模式不支持x裁切");
                return ManualScreenshot.Capture(c.GetString("root") ?? DefaultRoot, c.RequireString("id"), c.GetInt("y", 0),
                    width == 0 ? 1400 : width, height == 0 ? 1600 : height);
            }))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.manual.open", Domain = "norma", CommandClass = "manual", Readonly = true,
            Summary = "按完整页名称在原手册打开页面，选择后核实右侧正文标题；同名页用directory返回的完整path区分，不修改手册文件",
            Example = "norma.manual.open name=常用材料弹性模量及泊松比",
            Parameters = [Root(), new ParameterSpec { Name = "name", Description = "原手册目录中的完整页名称，严格匹配；如常用材料弹性模量及泊松比", Type = ParamType.String, Required = true },
                Text("path", "可选：directory返回的完整目录路径，/分隔；逐级展开祖先并区分同名页")],
            Handler = CommandDescriptor.Sync(c => Execute(() => ManualWindowScreenshot.Open(c.GetString("root") ?? DefaultRoot, c.RequireString("name"), c.GetString("path"))))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.manual.directory", Domain = "norma", CommandClass = "manual", Readonly = true,
            Summary = "文字查看原手册左侧目录：只返回文字树与条目的名称、完整path、选中和展开状态，不生成目录截图；支持过滤分页",
            Example = "norma.manual.directory q=常用材料 limit=30",
            Parameters = [Root(), Text("q", "按目录名称包含匹配；省略浏览当前可访问目录（折叠分组可能尚未暴露子页）"),
                Number("offset", "跳过匹配条数，≥0", "0"), Number("limit", "返回条数，1～100", "30")],
            Handler = CommandDescriptor.Sync(c => Execute(() => ManualWindowScreenshot.DirectoryView(c.GetString("root") ?? DefaultRoot, c.GetString("q"), c.GetInt("offset", 0), c.GetInt("limit", 30))))
        });
    }
    private static ParameterSpec Root() => new() { Name = "root", Description = "开发脚手架的手册目录；省略使用固定的本机默认路径，可显式覆盖为软件根目录、base 或 jxgcsdzsc", Type = ParamType.String, Default = DefaultRoot };
    private static ParameterSpec Text(string name, string description) => new() { Name = name, Description = description, Type = ParamType.String };
    private static ParameterSpec Number(string name, string description, string value) => new() { Name = name, Description = description, Type = ParamType.Int, Default = value };
    private static ParameterSpec Choice(string name, string description, string value, string[] choices) => new() { Name = name, Description = description, Type = ParamType.String, Default = value, AllowedValues = choices };
    private static CommandResult Execute(Func<object> operation)
    {
        try { return CommandResult.Ok("手册查询完成；结果与后续分页位置见 data", operation()); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { return CommandResult.Fail("手册查询失败：" + ex.Message); }
    }
}
