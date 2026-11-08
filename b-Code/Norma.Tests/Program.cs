using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Norma;
using Norma.Formulas;
using HistoryVulcan.Core.Commands;
using System.Diagnostics;
using System.Reflection;

Console.OutputEncoding = Encoding.UTF8;

// 全部离线。最后一组把打好的包交给已发布宿主的命令行 `--probe` 装一遍（宿主 5.9.0 起的统一契约）——
// 版本漂移、manifest 写错、身份类缺失在宿主那边都是「静默跳过整个模块」，只有这样才能在出包前拦住。
// 测试只看宿主的装载结果与指令结果，不引用宿主的实现程序集，宿主内部怎么改都不影响这里。
var tests = new (string Name, Func<Task> Run)[]
{
    ("command registration", Sync(TestCommandRegistration)),
    ("formula metadata is complete", Sync(TestFormulaMetadata)),
    ("every formula runs on its example", Sync(TestFormulaExamples)),
    ("hand-checked reference values", Sync(TestReferenceValues)),
    ("input validation rejects bad values", Sync(TestValidation)),
    ("formula core has no host reference", Sync(TestCoreIsHostFree)),
    ("standards table is complete", Sync(TestStandardsTable)),
    ("standard lookup works both ways", Sync(TestStandardLookup)),
    ("metal strength conversion and domains", Sync(TestMetalStrength)),
    ("page owner follows domain", Sync(TestPageOwner)),
    ("buttons, actions and commands line up", Sync(TestPageWiring)),
    ("versions agree", Sync(TestVersionsAgree)),
    ("host loads the package", TestHostLoadsPackage),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static Func<Task> Sync(Action test) => () =>
{
    test();
    return Task.CompletedTask;
};

static Registrar Registry()
{
    var registry = new Registrar();
    NormaModule.Register(registry);
    return registry;
}

static int ExpectedCommandCount() => Catalog.All.Count + 7;

static void TestCommandRegistration()
{
    var registry = Registry();
    var domain = NormaIdentity.Domain;
    var expected = Catalog.All.Select(item => domain + "." + item.Id)
        .Concat([domain + ".formula.list", domain + ".formula.show", domain + ".std.lookup", domain + ".metal.strength", domain + ".ui.describe", domain + ".ui.actions", domain + ".ui.data"])
        .ToList();
    foreach (var name in expected)
    {
        True(registry.TryGet(name, out var descriptor), $"未注册 {name}");
        Equal(domain, descriptor!.Domain!);
        True(descriptor.Readonly, $"{name} 必须只读");
    }

    Equal(ExpectedCommandCount(), registry.All.Count(d => d.Name.StartsWith(domain + ".", StringComparison.Ordinal)));
    True(Catalog.All.Count >= 30, $"计算条数异常：{Catalog.All.Count}");

    // 与宿主 diana.docs.audit 的 summary-too-long 同一把尺子。
    foreach (var descriptor in registry.All)
        True(descriptor.Summary.Length <= NormaCommands.SummaryLimit, $"{descriptor.Name} 一句话说明 {descriptor.Summary.Length} 字，超过 {NormaCommands.SummaryLimit}");

    foreach (var method in new[] { "describe", "actions", "data" })
    {
        True(registry.TryGet(domain + ".ui." + method, out var ui), $"缺少 {domain}.ui.{method}");
        True(ui!.Readonly, $"{domain}.ui.{method} 必须只读");
        True(ui.HiddenReason is not null, $"{domain}.ui.{method} 是界面内部协议，必须 HiddenReason");
    }
}

static void TestFormulaMetadata()
{
    foreach (var item in Catalog.All)
    {
        True(System.Text.RegularExpressions.Regex.IsMatch(item.Id, "^[a-z]+\\.[a-z0-9]+$"), $"计算编号必须是 类.方法 小写：{item.Id}");
        foreach (var text in new[] { item.Title, item.Source, item.Scope })
            True(!string.IsNullOrWhiteSpace(text), $"{item.Id} 缺名称、出处或适用范围");
        True(item.Outputs.Count > 0, $"{item.Id} 没有输出");
        var names = item.Inputs.Select(input => input.Name).Concat(item.Outputs.Select(output => output.Name)).ToList();
        foreach (var input in item.Inputs)
        {
            True(System.Text.RegularExpressions.Regex.IsMatch(input.Name, "^[a-z][a-z0-9_]*$"), $"{item.Id} 参数名必须小写：{input.Name}");
            True(!string.IsNullOrWhiteSpace(input.Description), $"{item.Id}.{input.Name} 缺说明");
            if (input.IsChoice)
                True(input.DefaultChoice is null || input.Choices!.Contains(input.DefaultChoice), $"{item.Id}.{input.Name} 缺省不在可选值内");
            else
                True(input.Unit is not null, $"{item.Id}.{input.Name} 缺单位");
        }

        True(item.Inputs.Select(input => input.Name).Distinct().Count() == item.Inputs.Count, $"{item.Id} 输入重名");
        True(item.Outputs.Select(output => output.Name).Distinct().Count() == item.Outputs.Count, $"{item.Id} 输出重名");
        foreach (var output in item.Outputs)
            True(!string.IsNullOrWhiteSpace(output.Description) && !string.IsNullOrWhiteSpace(output.Symbol), $"{item.Id}.{output.Name} 缺符号或说明");
        True(names.Count > 0, item.Id);
    }
}

// 每条计算按示例值（必填量用 Example，选填量用缺省）都能跑通，输出全为有限值——
// 这也是自描述示例行能直接复制执行的保证。
static void TestFormulaExamples()
{
    var registry = Registry();
    foreach (var item in Catalog.All)
    {
        var result = Run(item.Id, ExampleArgs(item));
        foreach (var output in item.Outputs)
            True(double.IsFinite(result[output.Name]), $"{item.Id}.{output.Name} 示例结果不是有限值：{result[output.Name]}");

        True(registry.TryGet(NormaIdentity.Domain + "." + item.Id, out var descriptor), item.Id);
        var outcome = descriptor!.Handler(new CommandContext(descriptor, ExampleArgs(item), "test", null, CancellationToken.None)).Result;
        True(outcome.Success, $"{item.Id} 指令执行失败：{outcome.Message}");
        JsonSerializer.Serialize(outcome.Data);
    }
}

static Dictionary<string, string> ExampleArgs(Calculation item)
    => item.Inputs.Where(input => !input.IsOptional).ToDictionary(
        input => input.Name,
        input => input.IsChoice ? input.Choices![0] : input.Example.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

static Result Run(string id, IReadOnlyDictionary<string, string> args)
{
    var item = Catalog.Find(id) ?? throw new InvalidOperationException($"没有计算 {id}");
    var numbers = new Dictionary<string, double>();
    var choices = new Dictionary<string, string>();
    foreach (var (name, value) in args)
    {
        var input = item.Inputs.Single(candidate => candidate.Name == name);
        if (input.IsChoice)
            choices[name] = value;
        else
            numbers[name] = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    return item.Run(numbers, choices);
}

static Result Calc(string id, string args)
    => Run(id, args.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => pair[1]));

static void Near(double expected, double actual, double tolerance, string what)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{what}：期望 {expected}±{tolerance}，实际 {actual}");
}

// 手算或标准表值对照。每条都能用计算器独立复核。
static void TestReferenceValues()
{
    Near(65.862, Calc("drive.torque", "p=10 n=1450")["t"], 0.001, "T = 9550·10/1450");

    var gear = Calc("gear.geometry", "mn=2 z1=20 z2=40");
    Near(40, gear["d1"], 1e-9, "d₁");
    Near(44, gear["da1"], 1e-9, "dₐ₁");
    Near(35, gear["df1"], 1e-9, "d_f1");
    Near(60, gear["aw"], 1e-9, "标准中心距");
    Near(1.635, gear["eps_alpha"], 0.003, "z=20/40 标准直齿端面重合度");
    Near(0.0, gear["dy"], 1e-12, "零变位 Δy");
    Near(1 - 20 * Math.Pow(Math.Sin(20 * Math.PI / 180), 2) / 2, gear["xmin1"], 1e-9, "x_min = 1 − z sin²α/2");

    // 正变位：αwt 必须大于 αt，中心距增大
    var shifted = Calc("gear.geometry", "mn=2 z1=20 z2=40 x1=0.3 x2=0.2");
    True(shifted["aw"] > 60 && shifted["alpha_wt"] > 20 && shifted["dy"] > 0, "正变位后中心距、啮合角、Δy 应增大");

    var contact = Calc("gear.contact", "mn=2 z1=20 z2=40 t1=100 b=40 sigma_hlim=1100");
    Near(189.8, contact["ze"], 0.1, "钢对钢 Z_E");
    Near(Math.Sqrt(2 / (Math.Cos(20 * Math.PI / 180) * Math.Sin(20 * Math.PI / 180))), contact["zh"], 1e-6, "标准直齿 Z_H");
    Near(5000, contact["ft"], 1e-9, "F_t = 2000T/d");

    // ISO 6336-3 方法 B 的量级：z=20 标准齿 Y_F 约 1.4～1.6、Y_S 约 1.9～2.1；齿数越多 Y_F 越小
    var bending = Calc("gear.bending", "mn=2 z1=20 z2=40 t1=100 b=40 sigma_flim1=350 sigma_flim2=300");
    True(bending["yf1"] is > 1.3 and < 1.7, $"Y_F1={bending["yf1"]}");
    True(bending["ys1"] is > 1.8 and < 2.2, $"Y_S1={bending["ys1"]}");
    True(bending["yf2"] < bending["yf1"], "大轮 Y_F 应小于小轮");
    var positive = Calc("gear.bending", "mn=2 z1=20 z2=40 x1=0.5 t1=100 b=40 sigma_flim1=350 sigma_flim2=300");
    True(positive["yf1"] < bending["yf1"], "正变位应减小 Y_F");

    var life = Calc("bearing.life", "c=30000 p=3000 n=1000 type=ball");
    Near(1000, life["l10"], 1e-9, "L₁₀ = (C/P)³");
    Near(1e9 / 60000, life["lh"], 1e-6, "L_h = 10⁶L/(60n)");
    Near(250, Calc("bearing.life", "c=30000 p=3000 n=1000 type=ball reliability=99")["lnm"], 1e-9, "a₁(99%) = 0.25");
    Near(30000, Calc("bearing.required", "p=3000 n=1000 lh=16666.666666666667 type=ball")["c_req"], 1e-6, "寿命反解回原值");

    var spring = Calc("spring.compression", "d=4 dm=32 n=10 f=200");
    Near(8, spring["c"], 1e-12, "C");
    Near(1.184018, spring["k_wahl"], 1e-6, "Wahl K(8)");
    Near(301.5, spring["tau"], 0.1, "τ = 8KFD/(πd³)");
    Near(7.7148, spring["rate"], 1e-4, "k = Gd⁴/(8D³n)");
    Near(4, Calc("spring.wire", $"f=200 c=8 tau_allow={spring["tau"].ToString("R", System.Globalization.CultureInfo.InvariantCulture)}")["d"], 1e-9, "簧丝直径反解回原值");

    Near(57.99, Calc("bolt.thread", "d=10 p=1.5")["as"], 0.01, "M10 应力截面积（ISO 898-1 表值 58.0）");
    Near(84.27, Calc("bolt.thread", "d=12 p=1.75")["as"], 0.01, "M12 应力截面积（表值 84.3）");
    Near(448.3, Calc("bolt.tension", "d=10 p=1.5 f0=20000 sigma_s=640")["sigma_ca"], 0.1, "1.3F/A_s");
    Near(40, Calc("bolt.torque", "f0=20000 d=10")["t"], 1e-9, "T = KFd");
    Near(20000, Calc("bolt.preload", "t=40 d=10")["f0"], 1e-9, "F₀ = T/(Kd)");

    Near(28, Calc("shaft.torsion", "p=7.5 n=480")["d0"], 1e-9, "112·∛(7.5/480)");
    Near(65.789, Calc("key.flat", "t=200 d=40 b=12 h=8 l=50")["sigma_p"], 0.001, "2T/(k l d)");

    Near(21, Calc("fit.it", "size=25 grade=7")["it_rounded"], 0, "IT7 18～30 mm（表值 21）");
    Near(16, Calc("fit.it", "size=40 grade=6")["it_rounded"], 0, "IT6 30～50 mm（表值 16）");
    Near(46, Calc("fit.it", "size=60 grade=8")["it_rounded"], 0, "IT8 50～80 mm（表值 46）");
    Near(35, Calc("fit.it", "size=100 grade=7")["it_rounded"], 0, "IT7 80～120 mm（表值 35）");

    Near(1000.0 / 9.6, Calc("beam.deflection", "case=simple_point f=1000 l=1000 e=200000 i=1000000")["deflection"] * 1000, 1e-6, "FL³/(48EI)");

    var chain = Calc("chain.geometry", "p=12.7 z1=21 z2=63 a0=500 n1=960");
    Near(121.875, chain["lp0"], 0.01, "链节数");
    Near(122, chain["lp"], 0, "圆整为偶数");
    Near(500.8, chain["a"], 0.1, "理论中心距");

    Near(1709.2, Calc("belt.geometry", "d1=125 d2=315 a0=500 n1=1450")["l0"], 0.1, "开口带长");

    var goodman = Calc("fatigue.safety", "sigma_a=80 sigma_m=120 se=200 sut=600 sy=355");
    Near(1 / (80.0 / 200 + 120.0 / 600), goodman["n"], 1e-12, "Goodman");

    var press = Calc("fit.press", "d=50 l=60 delta=40 d2=100");
    // 同材料实心轴：p = E δ (d₂² − d²) / (2 d d₂²)
    Near(206000 * 0.04 * (100.0 * 100 - 2500) / (2 * 50 * 100 * 100), press["p"], 1e-9, "同材料实心轴 Lamé 闭式解");
}

static void TestValidation()
{
    static void Rejects(string id, string args, string because)
    {
        try
        {
            Calc(id, args);
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException($"{id} {args} 应被拒绝：{because}");
    }

    Rejects("drive.torque", "p=-1 n=1000", "功率必须大于 0");
    Rejects("drive.torque", "n=1000", "缺必填量");
    Rejects("gear.geometry", "mn=2 z1=40 z2=20", "z₂ 不能小于 z₁");
    Rejects("bearing.life", "c=1 p=1 n=1 type=needle", "选项不在可选值内");
    Rejects("fit.press", "d=50 l=60 delta=40 d2=40", "轮毂外径必须大于配合直径");
    Rejects("fit.it", "size=600 grade=7", "超出 500 mm");
}

static void TestCoreIsHostFree()
{
    var core = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Norma", "Formulas"));
    True(Directory.Exists(core), $"找不到公式核心目录 {core}");
    foreach (var file in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories))
        True(!File.ReadAllText(file).Contains("HistoryVulcan", StringComparison.Ordinal),
            $"{Path.GetFileName(file)} 引用了宿主；Formulas/ 必须能原样抽成独立 NuGet 包");
}

static void TestStandardsTable()
{
    var all = Standards.All;
    True(all.Count >= 100, $"标准代号条数异常：{all.Count}");
    True(all.Count(item => item.Scope == "国内") >= 20, "国内标准代号太少");
    True(all.Count(item => item.Scope == "国外") >= 70, "国外标准代号太少");
    True(all.Select(item => Standards.NormalizeCode(item.Code)).Distinct().Count() == all.Count,
        "标准代号有重复");
    foreach (var item in all)
    {
        True(item.Code.Length > 0 && item.Name.Length > 0, "代号或名称为空");
        True(item.Scope is "国内" or "国外", $"{item.Code} 的 scope 应为 国内/国外");
    }
}

static void TestStandardLookup()
{
    static StandardCode First(string query)
    {
        var found = Standards.Lookup(query, 20);
        True(found.Items.Count > 0, $"「{query}」没有命中");
        return found.Items[0];
    }

    Equal("国家标准", First("GB").Name);
    Equal("JB/ZQ", First("JB/ZQ").Code);
    Equal("国家军用标准", First("gjb").Name);
    Equal("国际标准化组织标准", First("ISO").Name);
    Equal("德国工业标准", First("DIN").Name);
    Equal("GB", First("GB/T 5782").Code);
    True(Standards.Lookup("国际标准化", 20).Items.Any(item => item.Code == "ISO"), "按名称找 ISO 失败");
    True(Standards.Lookup("德国", 20).Items.Any(item => item.Code == "DIN"), "按名称找 DIN 失败");
    Equal(0, Standards.Lookup("不存在的标准xyz", 20).Total);
    var limited = Standards.Lookup("标准", 3);
    True(limited.Total > 3 && limited.Items.Count == 3, "limit 未生效");

    var registry = Registry();
    True(registry.TryGet("norma.std.lookup", out var descriptor), "未注册 norma.std.lookup");
    var outcome = descriptor!.Handler(new CommandContext(
        descriptor, new Dictionary<string, string> { ["q"] = "GB" }, "test", null, CancellationToken.None)).Result;
    True(outcome.Success, "std.lookup 执行失败：" + outcome.Message);
    True(outcome.Message.Contains("国家标准", StringComparison.Ordinal), "std.lookup 消息不对：" + outcome.Message);
    JsonSerializer.Serialize(outcome.Data);
}

static void TestMetalStrength()
{
    var registry = Registry();
    True(registry.TryGet("norma.metal.strength", out var descriptor), "未注册 norma.metal.strength");
    var command = descriptor!;

    static Dictionary<string, string> Args(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);

    static CommandResult Execute(CommandDescriptor descriptor, string args)
        => descriptor.Handler(new CommandContext(descriptor, Args(args), "test", null, CancellationToken.None)).Result;

    static JsonElement Metal(CommandDescriptor descriptor, string args)
    {
        var outcome = Execute(descriptor, args);
        True(outcome.Success, $"metal.strength {args} 失败：{outcome.Message}");
        return JsonSerializer.SerializeToElement(outcome.Data);
    }

    Equal(12, MetalStrength.Scales.Count);
    Equal(10, MetalStrength.Steels.Count);

    // 手册 HRC45 行的原值：PCHIP 节点处必须原样返回。
    var hrc45 = Metal(command, "value=45 scale=hrc steel=generic");
    var hardness = hrc45.GetProperty("hardness");
    Near(45, hardness.GetProperty("hrc").GetDouble(), 1e-9, "HRC 节点");
    Near(73.2, hardness.GetProperty("hra").GetDouble(), 0.05, "HRC45 → HRA");
    Near(82.9, hardness.GetProperty("hr15n").GetDouble(), 0.05, "HRC45 → HR15N");
    Near(436, hardness.GetProperty("hv").GetDouble(), 0.6, "HRC45 → HV");
    Near(424, hardness.GetProperty("hb").GetDouble(), 0.6, "HRC45 → HB");
    Near(1480, hrc45.GetProperty("sigma_b").GetProperty("steel").GetDouble(), 0.6, "HRC45 不分钢种 σb");

    var stainless = Metal(command, "value=45 scale=hrc steel=stainless");
    Near(1457, stainless.GetProperty("sigma_b").GetProperty("steel").GetDouble(), 0.6, "HRC45 不锈钢 σb");
    Near(1480, stainless.GetProperty("sigma_b").GetProperty("generic").GetDouble(), 0.6, "HRC45 不分钢种对照");

    // 反算：HV 436 回到 HRC 45。
    var fromHv = Metal(command, "value=436 scale=hv steel=generic");
    Near(45, fromHv.GetProperty("hardness").GetProperty("hrc").GetDouble(), 0.05, "HV436 → HRC");

    // HRC60：HB/HRB/HBS 没有定义，必须显式说明，不能猜。
    var hrc60 = Metal(command, "value=60 scale=hrc steel=generic");
    Equal(JsonValueKind.Null, hrc60.GetProperty("hardness").GetProperty("hb").ValueKind);
    True(hrc60.GetProperty("undefined").TryGetProperty("hb", out var hbWhy), "HRC60 应说明 HB 无定义");
    True(hbWhy.GetString()!.Contains("211–501", StringComparison.Ordinal), "HB 定义域说明不对：" + hbWhy);
    Near(2607, hrc60.GetProperty("sigma_b").GetProperty("steel").GetDouble(), 0.6, "HRC60 不分钢种 σb");

    // HRA 从 HRC39 起才有定义。
    var hrc38 = Metal(command, "value=38 scale=hrc steel=generic");
    Equal(JsonValueKind.Null, hrc38.GetProperty("hardness").GetProperty("hra").ValueKind);
    True(hrc38.GetProperty("undefined").TryGetProperty("hra", out var hraWhy), "HRC38 应说明 HRA 无定义");
    True(hraWhy.GetString()!.Contains("70–86.6", StringComparison.Ordinal), "HRA 定义域说明不对：" + hraWhy);

    // HRB 段：σb 用低硬度表；HRC 不定义。
    var hrb90 = Metal(command, "value=90 scale=hrb steel=generic");
    Near(183, hrb90.GetProperty("hardness").GetProperty("hv").GetDouble(), 0.6, "HRB90 → HV");
    Near(629, hrb90.GetProperty("sigma_b").GetProperty("steel").GetDouble(), 0.6, "HRB90 不分钢种 σb");
    Equal(JsonValueKind.Null, hrb90.GetProperty("hardness").GetProperty("hrc").ValueKind);

    // 钢种在 HRB 段没有数据：报无定义，不回落到别的钢种。
    var lowSteel = Metal(command, "value=90 scale=hrb steel=carbon");
    Equal(JsonValueKind.Null, lowSteel.GetProperty("sigma_b").GetProperty("steel").ValueKind);
    True(lowSteel.GetProperty("sigma_b").GetProperty("undefined").GetString()!.Contains("碳钢", StringComparison.Ordinal),
        "碳钢无定义的理由不对");

    // HRB100 与高硬度表在 HV 上重叠：应能补出 HRC/HB。
    var hrb100 = Metal(command, "value=100 scale=hrb steel=generic");
    Near(803, hrb100.GetProperty("sigma_b").GetProperty("steel").GetDouble(), 0.6, "HRB100 不分钢种 σb");
    True(hrb100.GetProperty("hardness").GetProperty("hrc").GetDouble() is > 21 and < 23, "HRB100 应换算出 HRC≈22");
    Near(233, hrb100.GetProperty("hardness").GetProperty("hb").GetDouble(), 5, "HRB100 → HB");

    // 超出定义域直接失败，不返回外推值。
    foreach (var args in new[] { "value=75 scale=hrc", "value=100 scale=hv", "value=50 scale=hrb" })
    {
        var outcome = Execute(command, args);
        True(!outcome.Success, $"{args} 应被拒绝");
        True(outcome.Message.Contains("超出定义域", StringComparison.Ordinal), $"{args} 的拒绝理由不对：{outcome.Message}");
    }
}

static void TestPageOwner()
{
    // Aurora 的判据：owner = "History" + 首字母大写的指令域；对不上整页被静默拒收。
    var expected = "History" + char.ToUpperInvariant(NormaIdentity.Domain[0]) + NormaIdentity.Domain[1..];
    Equal(expected, NormaIdentity.PageOwner);

    using var description = JsonDocument.Parse(NormaPage.Describe());
    var root = description.RootElement;
    Equal(expected, root.GetProperty("owner").GetString()!);
    Equal(1, root.GetProperty("schemaVersion").GetInt32());
    foreach (var page in root.GetProperty("pages").EnumerateArray())
        Equal(expected, page.GetProperty("scene").GetString()!);

    using var actions = JsonDocument.Parse(NormaPage.Actions());
    Equal(expected, actions.RootElement.GetProperty("owner").GetString()!);
}

static void TestPageWiring()
{
    var registry = Registry();
    using var actions = JsonDocument.Parse(NormaPage.Actions());
    var declared = new HashSet<string>(StringComparer.Ordinal);
    foreach (var action in actions.RootElement.GetProperty("actions").EnumerateArray())
    {
        var id = action.GetProperty("id").GetString()!;
        var command = action.GetProperty("command").GetString()!;
        True(declared.Add(id), $"动作 id 重复：{id}");
        True(registry.TryGet(command.Split(' ')[0], out _), $"动作 {id} 指向未注册的指令 {command}");
    }

    using var description = JsonDocument.Parse(NormaPage.Describe());
    var nodes = Descendants(description.RootElement).ToList();
    foreach (var button in nodes.Where(node => node.TryGetProperty("kind", out var kind) && kind.GetString() == "button"))
    {
        var action = button.GetProperty("action").GetString()!;
        True(declared.Contains(action), $"按钮指向未声明的动作 {action}");
    }

    foreach (var source in nodes.Where(node => node.TryGetProperty("dataSource", out _)))
    {
        var command = source.GetProperty("dataSource").GetProperty("command").GetString()!;
        True(registry.TryGet(command, out _), $"表格取数指令未注册：{command}");
    }

    True(nodes.Any(node => node.TryGetProperty("id", out var id) && id.GetString() == "q"),
        "页面缺少标准代号查询输入框");
    Equal(1, nodes.Count(node => node.TryGetProperty("type", out var type) && type.GetString() == "table"));
    True(!nodes.Any(node => node.TryGetProperty("style", out var style) && style.GetString() == "caption"),
        "页面不应再有 caption 小字");
    var lookup = actions.RootElement.GetProperty("actions").EnumerateArray()
        .Single(action => action.GetProperty("id").GetString() == "norma.std.lookup");
    Equal("{q}", lookup.GetProperty("args").GetProperty("q").GetString());

    True(nodes.Any(node => node.TryGetProperty("id", out var id) && id.GetString() == NormaPage.MetalValueId),
        "页面缺少金属强度换算的硬度值输入框");
    True(nodes.Any(node => node.TryGetProperty("id", out var id) && id.GetString() == NormaPage.MetalScaleId),
        "页面缺少硬度制选择框");
    True(nodes.Any(node => node.TryGetProperty("id", out var id) && id.GetString() == NormaPage.MetalSteelId),
        "页面缺少钢种选择框");
    var metal = actions.RootElement.GetProperty("actions").EnumerateArray()
        .Single(action => action.GetProperty("id").GetString() == "norma.metal.strength");
    Equal("{metal-value}", metal.GetProperty("args").GetProperty("value").GetString());
    Equal("{metal-scale}", metal.GetProperty("args").GetProperty("scale").GetString());
    Equal("{metal-steel}", metal.GetProperty("args").GetProperty("steel").GetString());

    True(registry.TryGet("norma.ui.data", out var uiData), "缺少 norma.ui.data");
    var outcome = uiData!.Handler(new CommandContext(
        uiData, new Dictionary<string, string> { ["view"] = "catalog" }, "test", null, CancellationToken.None)).Result;
    True(outcome.Success, "ui.data view=catalog 失败：" + outcome.Message);
    var rows = JsonSerializer.SerializeToElement(outcome.Data);
    True(rows.ValueKind == JsonValueKind.Array && rows.EnumerateArray().Any(row =>
            row.GetProperty("command").GetString() == "norma.std.lookup"),
        "目录里没有 norma.std.lookup");
}

static void TestVersionsAgree()
{
    var manifest = ReadManifest(Path.Combine(AppContext.BaseDirectory, "module.manifest.json"));
    Equal(NormaIdentity.Name, manifest.GetProperty("name").GetString()!);
    Equal(NormaIdentity.Name + ".dll", manifest.GetProperty("artifact").GetString()!);
    Equal(new ModuleInfo().Version, manifest.GetProperty("version").GetString()!);
    Equal(NormaIdentity.Name, new ModuleInfo().ModuleName);
    Equal(NormaIdentity.Description, manifest.GetProperty("description").GetString()!);
}

static async Task TestHostLoadsPackage()
{
    var root = Path.Combine(Path.GetTempPath(), NormaIdentity.Name + ".Tests", Guid.NewGuid().ToString("N"));
    var package = Path.Combine(root, NormaIdentity.Name);
    Directory.CreateDirectory(package);
    try
    {
        var manifest = ReadManifest(Path.Combine(AppContext.BaseDirectory, "module.manifest.json"));
        foreach (var file in new[]
                 {
                     "module.manifest.json",
                     manifest.GetProperty("artifact").GetString()!,
                     manifest.GetProperty("docs").GetString()!,
                 })
        {
            var from = Path.Combine(AppContext.BaseDirectory, file);
            True(File.Exists(from), $"构建产物缺少 {file}");
            File.Copy(from, Path.Combine(package, file));
        }

        // 宿主 7.0 起只接受 nupkg；只在临时目录构造测试候选，不安装或修改运行区。
        var archive = Path.Combine(root, NormaIdentity.Name + ".nupkg");
        WriteNugetPackage(package, archive, manifest.GetProperty("version").GetString()!);

        var torque = await Probe(archive, NormaIdentity.Domain + ".drive.torque p=10 n=1450");
        True(torque.GetProperty("data").ValueKind == JsonValueKind.Object, "宿主探针未返回装载数据：" + torque.GetRawText());
        var module = torque.GetProperty("data").GetProperty("module");
        True(module.GetProperty("attached").GetBoolean(),
            "宿主没有接上模块：" + string.Join("；", module.GetProperty("attachFailures").EnumerateArray().Select(item => item.GetString()))
            + "；诊断：" + string.Join("；", torque.GetProperty("diagnostics").EnumerateArray().Select(item => item.GetString())));
        Equal(NormaIdentity.Name, module.GetProperty("name").GetString());
        Equal(ExpectedCommandCount(), module.GetProperty("commandCount").GetInt32());
        var result = torque.GetProperty("data").GetProperty("result");
        True(result.GetProperty("success").GetBoolean(), result.GetProperty("message").GetString() ?? "");
        True(result.GetProperty("message").GetString()!.Contains("T=65.86 N·m", StringComparison.Ordinal),
            "宿主执行结果不对：" + result.GetProperty("message").GetString());

        // 选项参数与缺省值经宿主参数校验后仍能正确到达计算。
        var life = await Probe(archive, NormaIdentity.Domain + ".bearing.life c=30000 p=3000 n=1000 type=ball reliability=99");
        var lifeResult = life.GetProperty("data").GetProperty("result");
        True(lifeResult.GetProperty("success").GetBoolean(), lifeResult.GetRawText());
        True(lifeResult.GetProperty("message").GetString()!.Contains("L_nm=250 10⁶ r", StringComparison.Ordinal),
            "宿主执行结果不对：" + lifeResult.GetProperty("message").GetString());

        // 标准代号表是嵌入资源，宿主装载后同样能查到。
        var std = await Probe(archive, NormaIdentity.Domain + ".std.lookup q=ISO");
        var stdResult = std.GetProperty("data").GetProperty("result");
        True(stdResult.GetProperty("success").GetBoolean(), stdResult.GetRawText());
        True(stdResult.GetProperty("message").GetString()!.Contains("国际标准化组织标准", StringComparison.Ordinal),
            "宿主查标准代号结果不对：" + stdResult.GetProperty("message").GetString());

        // 硬度—强度表的嵌入资源与 PCHIP 在宿主上下文里同样可用。
        var metal = await Probe(archive, NormaIdentity.Domain + ".metal.strength value=45 scale=hrc steel=stainless");
        var metalResult = metal.GetProperty("data").GetProperty("result");
        True(metalResult.GetProperty("success").GetBoolean(), metalResult.GetRawText());
        True(metalResult.GetProperty("message").GetString()!.Contains("1457 MPa", StringComparison.Ordinal),
            "宿主金属强度换算结果不对：" + metalResult.GetProperty("message").GetString());
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

// 调已发布宿主的 `HistoryVulcan.Cli.exe --probe`：宿主根目录在构建时写进本程序集（见 csproj 的 HistoryVulcanHostRoot）。
static async Task<JsonElement> Probe(string package, string command)
{
    var hostRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(item => item.Key == "HistoryVulcanHostRoot")?.Value;
    if (string.IsNullOrEmpty(hostRoot))
        throw new InvalidOperationException("构建时没有写入 HistoryVulcanHostRoot。");
    var cli = Path.Combine(hostRoot, "HistoryVulcan.Cli.exe");
    True(File.Exists(cli), $"找不到宿主命令行：{cli}（宿主 5.9.0 起提供 --probe）");

    var start = new ProcessStartInfo(cli)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        StandardOutputEncoding = Encoding.UTF8,
    };
    foreach (var argument in new[] { "--probe", package, "--format", "json", "--cli" }.Concat(command.Split(' ')))
        start.ArgumentList.Add(argument);

    using var process = Process.Start(start)!;
    var output = await process.StandardOutput.ReadToEndAsync();
    var error = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    True(output.TrimStart().StartsWith('{'), $"--probe 没有输出 JSON（退出码 {process.ExitCode}）：{output}{error}");
    using var document = JsonDocument.Parse(output);
    return document.RootElement.Clone();
}

static JsonElement ReadManifest(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    return document.RootElement.Clone();
}

// 与 eng/Build-*Package.ps1 同一格式：大写十六进制、两个空格、正斜杠路径、无 BOM。
// 与宿主实际 nupkg 相同：nuspec 与模块有效载荷平铺，不带运行区状态或数据。
static void WriteNugetPackage(string package, string archive, string version)
{
    File.WriteAllText(Path.Combine(package, NormaIdentity.Name + ".nuspec"), $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata><id>{NormaIdentity.Name}</id><version>{version}</version>
          <authors>HistoryVulcan</authors><description>{NormaIdentity.Name}</description></metadata>
        </package>
        """, new UTF8Encoding(false));
    ZipFile.CreateFromDirectory(package, archive);
}

static IEnumerable<JsonElement> Descendants(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        yield return element;
        foreach (var property in element.EnumerateObject())
            foreach (var child in Descendants(property.Value))
                yield return child;
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray())
            foreach (var child in Descendants(item))
                yield return child;
    }
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
}

/// <summary>
/// 模块登记口的测试替身：只记下登记了哪些指令。宿主 6.0.0 起注册表是宿主内部类，
/// 登记口 ICommandRegistrar 就是模块能看到的全部；来源由宿主盖章，这里不再核对。
/// </summary>
sealed class Registrar : ICommandRegistrar
{
    private readonly Dictionary<string, CommandDescriptor> _commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<CommandDescriptor> All => _commands.Values;

    public void Register(CommandDescriptor descriptor)
    {
        if (!_commands.TryAdd(descriptor.Name, descriptor))
            throw new InvalidOperationException($"重复登记 {descriptor.Name}");
    }

    public bool TryGet(string name, out CommandDescriptor? descriptor) => _commands.TryGetValue(name, out descriptor);
}
