using System.Globalization;
using HistoryVulcan.Core.Commands;
using Norma.Formulas;

namespace Norma;

/// <summary>
/// 把 <see cref="Catalog"/> 里的每条计算投成一条指令：参数、示例、出处都由元数据生成，
/// 自描述（diana.docs.read domain=norma）因此就是公式说明书，不另写文档。
/// </summary>
internal static class NormaCommands
{
    public static CommandDescriptor For(Calculation calculation)
    {
        var name = NormaIdentity.Domain + "." + calculation.Id;
        return new CommandDescriptor
        {
            Name = name,
            Domain = NormaIdentity.Domain,
            CommandClass = calculation.Topic,
            Summary = Summary(calculation),
            Example = name + " " + string.Join(" ", calculation.Inputs
                .Where(input => !input.IsOptional)
                .Select(input => input.Name + "=" + (input.IsChoice ? input.Choices![0] : Num.Format(input.Example)))),
            Readonly = true,
            Parameters = calculation.Inputs.Select(Spec).ToList(),
            Handler = CommandDescriptor.Sync(context => Run(calculation, context)),
        };
    }

    /// <summary>宿主自描述审计要求一句话说明不超过这么多字。</summary>
    internal const int SummaryLimit = 120;

    /// <summary>
    /// 一句话说明：名称加出处简称（出处里第一个冒号或分号之前的部分）。完整出处与适用范围
    /// 太长，放在 formula.show 和每次计算结果的 source / scope 里。
    /// </summary>
    internal static string Summary(Calculation calculation)
    {
        var source = calculation.Source;
        var cut = source.IndexOfAny(['：', '；']);
        var brief = cut > 0 ? source[..cut] : source;
        if (brief.Length > 50)
        {
            var comma = brief.IndexOf('，');
            brief = comma is > 0 and <= 50 ? brief[..comma] : brief[..49] + "…";
        }

        var tail = $"；完整出处与适用范围见 {NormaIdentity.Domain}.formula.show";
        var head = $"{calculation.Title}（{brief}）";
        return head.Length + tail.Length <= SummaryLimit ? head + tail : head;
    }

    private static ParameterSpec Spec(Input input)
    {
        var symbol = string.IsNullOrEmpty(input.Symbol) ? "" : $"（{input.Symbol}）";
        var unit = string.IsNullOrEmpty(input.Unit) ? "" : $"，单位 {input.Unit}";
        var range = input.RangeText();
        return new ParameterSpec
        {
            Name = input.Name,
            Description = input.Description + symbol + unit + (range.Length == 0 ? "" : "，" + range),
            Type = input.IsChoice ? ParamType.String : ParamType.Double,
            Required = !input.IsOptional,
            Default = input.IsChoice ? input.DefaultChoice : input.Default?.ToString("R", CultureInfo.InvariantCulture),
            AllowedValues = input.Choices?.ToArray(),
        };
    }

    private static CommandResult Run(Calculation calculation, CommandContext context)
    {
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in calculation.Inputs)
        {
            if (!context.Has(input.Name))
                continue;
            if (input.IsChoice)
                choices[input.Name] = context.GetString(input.Name) ?? "";
            else
                numbers[input.Name] = context.GetDouble(input.Name, double.NaN);
        }

        Result result;
        try
        {
            result = calculation.Run(numbers, choices);
        }
        catch (ArgumentException ex)
        {
            return CommandResult.Fail($"{calculation.Title}：{ex.Message}");
        }

        return CommandResult.Ok(Message(result), Payload(result));
    }

    /// <summary>一行给人看：全部输出与判据结论。</summary>
    internal static string Message(Result result)
    {
        var values = string.Join("，", result.Calculation.Outputs.Select(output =>
            double.IsNaN(result[output.Name]) ? $"{output.Symbol}=无定义" : $"{output.Symbol}={Num.Format(result[output.Name])}{(output.Unit.Length == 0 ? "" : " " + output.Unit)}"));
        var verdict = result.Checks.Count == 0
            ? ""
            : result.AllPass
                ? "；判据全部满足"
                : "；不满足：" + string.Join("、", result.Checks.Where(check => !check.Pass).Select(check =>
                    $"{check.Name}（{Num.Format(check.Actual)} {check.Relation} {Num.Format(check.Limit)} 不成立）"));
        return $"{result.Calculation.Title}：{values}{verdict}";
    }

    /// <summary>结构化结果：带单位与出处，便于 AI 复核与引用。非有限值（如不受载时的安全系数）记为 null。</summary>
    internal static object Payload(Result result)
    {
        var calculation = result.Calculation;
        return new
        {
            id = calculation.Id,
            title = calculation.Title,
            source = calculation.Source,
            scope = calculation.Scope,
            inputs = calculation.Inputs.Select(input => new
            {
                name = input.Name,
                symbol = input.Symbol,
                unit = input.Unit,
                value = input.IsChoice ? (object?)result.InputChoices[input.Name] : Finite(result.Inputs[input.Name]),
            }),
            outputs = calculation.Outputs.Select(output => new
            {
                name = output.Name,
                symbol = output.Symbol,
                unit = output.Unit,
                description = output.Description,
                value = Finite(result[output.Name]),
            }),
            checks = result.Checks.Select(check => new
            {
                name = check.Name,
                actual = Finite(check.Actual),
                relation = check.Relation,
                limit = Finite(check.Limit),
                pass = check.Pass,
                rule = check.Rule,
            }),
            pass = result.AllPass,
            notes = result.Notes,
        };
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    /// <summary>目录的行：页面表格与 formula.list 共用。</summary>
    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> CatalogRows(string? topic)
        => Catalog.All
            .Where(item => string.IsNullOrWhiteSpace(topic) || item.Topic == topic.Trim().ToLowerInvariant())
            .Select(item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["id"] = item.Id,
                ["topic"] = Catalog.TopicTitle(item.Topic),
                ["command"] = NormaIdentity.Domain + "." + item.Id,
                ["title"] = item.Title,
                ["source"] = item.Source,
            })
            .ToList();

    /// <summary>不是计算公式、但属于本模块的查表与换算工具。</summary>
    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> ToolRows()
        =>
        [
            new Dictionary<string, string>
            {
                ["id"] = "std.lookup",
                ["topic"] = "标准代号",
                ["command"] = NormaIdentity.Domain + ".std.lookup",
                ["title"] = "国内外标准代号查表：按代号或名称互查",
                ["source"] = $"机械工程师手册 国内/国外部分标准代号（{Standards.All.Count} 条）",
            },
            new Dictionary<string, string>
            {
                ["id"] = "metal.strength",
                ["topic"] = "金属强度",
                ["command"] = NormaIdentity.Domain + ".metal.strength",
                ["title"] = "金属硬度与强度换算：钢/铝/铜，PCHIP 插值，分牌号并标注无定义区间",
                ["source"] = MetalStrength.Source,
            },
        ];

    /// <summary>页面那张表：计算在前，工具接在后面，同一个表格里显示。</summary>
    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> PageRows()
        => CatalogRows(null).Concat(ToolRows()).ToList();

    /// <summary>一条计算的完整元数据。</summary>
    internal static object Describe(Calculation calculation) => new
    {
        id = calculation.Id,
        command = NormaIdentity.Domain + "." + calculation.Id,
        topic = Catalog.TopicTitle(calculation.Topic),
        title = calculation.Title,
        source = calculation.Source,
        scope = calculation.Scope,
        inputs = calculation.Inputs.Select(input => new
        {
            name = input.Name,
            symbol = input.Symbol,
            unit = input.Unit,
            description = input.Description,
            required = !input.IsOptional,
            @default = input.IsChoice ? (object?)input.DefaultChoice : input.Default,
            range = input.RangeText(),
            choices = input.Choices,
        }),
        outputs = calculation.Outputs.Select(output => new
        {
            name = output.Name,
            symbol = output.Symbol,
            unit = output.Unit,
            description = output.Description,
        }),
    };
}
