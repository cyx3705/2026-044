using System.Globalization;

namespace Norma.Formulas;

// 本目录是纯计算核心：不引用宿主，将来可原样抽成独立 NuGet 包。离线测试守着这条。
//
// 单位约定（全库统一，元数据里每个量也写明）：长度 mm、力 N、扭矩/弯矩 N·m、应力与弹性模量 MPa、
// 功率 kW、转速 r/min、角度 °（输入输出都用度，内部换弧度）、寿命 h 或 10⁶ 转。

/// <summary>一个输入量：数值量带范围与缺省，选项量带可选值。</summary>
public sealed record Input(string Name, string Symbol, string Unit, string Description)
{
    /// <summary>缺省值；null 表示必填。</summary>
    public double? Default { get; init; }

    /// <summary>示例值：自描述示例行用它（必填量必须给）。</summary>
    public double Example { get; init; }

    /// <summary>下限（含）。</summary>
    public double Min { get; init; } = double.NegativeInfinity;

    /// <summary>下限是否不含等号（如「必须大于 0」）。</summary>
    public bool MinExclusive { get; init; }

    /// <summary>上限（含）。</summary>
    public double Max { get; init; } = double.PositiveInfinity;

    /// <summary>非 null 时这是选项量，取值只能是其中之一。</summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>选项量的缺省；null 表示必填。</summary>
    public string? DefaultChoice { get; init; }

    public bool IsChoice => Choices is not null;

    public bool IsOptional => IsChoice ? DefaultChoice is not null : Default is not null;

    /// <summary>大于 0 的数值量（最常见）。</summary>
    public static Input Positive(string name, string symbol, string unit, string description, double example)
        => new(name, symbol, unit, description) { Min = 0, MinExclusive = true, Example = example };

    /// <summary>选项量。</summary>
    public static Input Choice(string name, string symbol, string description, string? defaultChoice, params string[] choices)
        => new(name, symbol, "", description) { Choices = choices, DefaultChoice = defaultChoice };

    /// <summary>人读的范围说明，进自描述。</summary>
    public string RangeText()
    {
        if (IsChoice)
            return "可选 " + string.Join("/", Choices!);
        var low = double.IsNegativeInfinity(Min) ? null : (MinExclusive ? "> " : "≥ ") + Num.Format(Min);
        var high = double.IsPositiveInfinity(Max) ? null : "≤ " + Num.Format(Max);
        return (low, high) switch
        {
            (null, null) => "",
            (not null, null) => low,
            (null, not null) => high,
            _ => low + " 且 " + high,
        };
    }
}

/// <summary>一个输出量。</summary>
public sealed record Output(string Name, string Symbol, string Unit, string Description);

/// <summary>一条判据：实际值对许用值。</summary>
public sealed record Check(string Name, double Actual, string Relation, double Limit, bool Pass, string Rule);

/// <summary>一条计算的元数据与算法。元数据就是自描述的唯一来源。</summary>
public sealed class Calculation
{
    /// <summary>形如 <c>gear.geometry</c>：类.方法，全小写；指令名为 <c>norma.</c> + Id。</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>注册目录中的一句话说明；出处与适用范围由公式详情提供。</summary>
    internal string? CatalogSummary { get; init; }

    /// <summary>出处：标准条款或公认教材的公式名，供核对。</summary>
    public required string Source { get; init; }

    /// <summary>适用范围与前提。</summary>
    public required string Scope { get; init; }

    public required IReadOnlyList<Input> Inputs { get; init; }

    public required IReadOnlyList<Output> Outputs { get; init; }

    public required Action<Args, Result> Evaluate { get; init; }

    public string Topic => Id[..Id.IndexOf('.')];

    /// <summary>
    /// 校验输入、补缺省、执行。数值量从 <paramref name="numbers"/> 取，选项量从 <paramref name="choices"/> 取；
    /// 缺省或越界抛 <see cref="ArgumentException"/>，消息可直接给人看。
    /// </summary>
    public Result Run(IReadOnlyDictionary<string, double> numbers, IReadOnlyDictionary<string, string> choices)
    {
        var args = new Args(this);
        foreach (var input in Inputs)
        {
            if (input.IsChoice)
            {
                var value = choices.TryGetValue(input.Name, out var given) ? given.Trim().ToLowerInvariant() : input.DefaultChoice;
                if (value is null)
                    throw new ArgumentException($"缺少 {input.Name}（{input.Description}，{input.RangeText()}）");
                if (!input.Choices!.Contains(value, StringComparer.Ordinal))
                    throw new ArgumentException($"{input.Name}={value} 不在可选值内：{string.Join("/", input.Choices!)}");
                args.Choices[input.Name] = value;
            }
            else
            {
                double value;
                if (numbers.TryGetValue(input.Name, out var given))
                    value = given;
                else if (input.Default is { } fallback)
                    value = fallback;
                else
                    throw new ArgumentException($"缺少 {input.Name}（{input.Description}，单位 {input.Unit}）");
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException($"{input.Name} 不是有限数值");
                var tooLow = input.MinExclusive ? value <= input.Min : value < input.Min;
                if (tooLow || value > input.Max)
                    throw new ArgumentException($"{input.Name}={Num.Format(value)} 超出范围（{input.RangeText()}）");
                args.Numbers[input.Name] = value;
            }
        }

        var result = new Result(this);
        foreach (var (name, value) in args.Numbers)
            result.Inputs[name] = value;
        foreach (var (name, value) in args.Choices)
            result.InputChoices[name] = value;
        Evaluate(args, result);
        foreach (var output in Outputs)
        {
            if (!result.Values.ContainsKey(output.Name))
                throw new InvalidOperationException($"{Id} 没有给出输出 {output.Name}");
        }

        return result;
    }
}

/// <summary>已校验的输入。</summary>
public sealed class Args(Calculation calculation)
{
    internal Dictionary<string, double> Numbers { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, string> Choices { get; } = new(StringComparer.Ordinal);

    public double this[string name] => Numbers.TryGetValue(name, out var value)
        ? value
        : throw new InvalidOperationException($"{calculation.Id} 未声明数值输入 {name}");

    public string Choice(string name) => Choices.TryGetValue(name, out var value)
        ? value
        : throw new InvalidOperationException($"{calculation.Id} 未声明选项输入 {name}");
}

/// <summary>计算结果：输出值、判据与提示。</summary>
public sealed class Result(Calculation calculation)
{
    public Calculation Calculation { get; } = calculation;

    public Dictionary<string, double> Inputs { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> InputChoices { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, double> Values { get; } = new(StringComparer.Ordinal);

    public List<Check> Checks { get; } = [];

    public List<string> Notes { get; } = [];

    public double this[string name]
    {
        get => Values[name];
        set
        {
            if (!Calculation.Outputs.Any(output => output.Name == name))
                throw new InvalidOperationException($"{Calculation.Id} 未声明输出 {name}");
            Values[name] = value;
        }
    }

    public bool AllPass => Checks.All(check => check.Pass);

    /// <summary>判据：actual ≤ limit。</summary>
    public void AtMost(string name, double actual, double limit, string rule)
        => Checks.Add(new Check(name, actual, "≤", limit, actual <= limit, rule));

    /// <summary>判据：actual ≥ limit。</summary>
    public void AtLeast(string name, double actual, double limit, string rule)
        => Checks.Add(new Check(name, actual, "≥", limit, actual >= limit, rule));

    public void Note(string text) => Notes.Add(text);
}

/// <summary>数值格式与常用换算。</summary>
public static class Num
{
    public const double Deg = Math.PI / 180;

    /// <summary>4 位有效数字，不带千分位，不用科学计数（极大极小除外）。</summary>
    public static string Format(double value)
    {
        if (value == 0 || double.IsNaN(value) || double.IsInfinity(value))
            return value.ToString(CultureInfo.InvariantCulture);
        var magnitude = Math.Abs(value);
        if (magnitude >= 1e9 || magnitude < 1e-4)
            return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
        var decimals = Math.Max(0, 3 - (int)Math.Floor(Math.Log10(magnitude)));
        return Math.Round(value, decimals).ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>渐开线函数 inv α = tan α − α（弧度）。</summary>
    public static double Inv(double alpha) => Math.Tan(alpha) - alpha;

    /// <summary>反渐开线：已知 inv α 求 α（弧度），牛顿迭代。</summary>
    public static double InvInverse(double value)
    {
        var alpha = Math.Pow(3 * value, 1.0 / 3);
        for (var i = 0; i < 50; i++)
        {
            var tan = Math.Tan(alpha);
            var step = (tan - alpha - value) / (tan * tan);
            alpha -= step;
            if (Math.Abs(step) < 1e-14)
                break;
        }

        return alpha;
    }
}
