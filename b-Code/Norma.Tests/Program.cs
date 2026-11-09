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
    ("manual search and source reading", Sync(TestManual)),
    ("formula metadata is complete", Sync(TestFormulaMetadata)),
    ("every formula runs on its example", Sync(TestFormulaExamples)),
    ("hand-checked reference values", Sync(TestReferenceValues)),
    ("input validation rejects bad values", Sync(TestValidation)),
    ("formula core has no host reference", Sync(TestCoreIsHostFree)),
    ("standards table is complete", Sync(TestStandardsTable)),
    ("standard lookup works both ways", Sync(TestStandardLookup)),
    ("metal strength conversion and domains", Sync(TestMetalStrength)),
    ("mechanical material tables and classification", Sync(TestPhysicalTables)),
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

static int ExpectedCommandCount() => Catalog.All.Count + 17;

static void TestCommandRegistration()
{
    var registry = Registry();
    var domain = NormaIdentity.Domain;
    var expected = Catalog.All.Select(item => domain + "." + item.Id)
        .Concat([domain + ".formula.list", domain + ".formula.show", domain + ".std.lookup", domain + ".metal.strength", domain + ".manual.search", domain + ".manual.read", domain + ".manual.screenshot", domain + ".manual.open", domain + ".manual.directory", domain + ".ui.describe", domain + ".ui.actions", domain + ".ui.data"])
        .ToList();
    expected.AddRange(PhysicalTables.Tables.Select(t => "norma.material." + t.Key));
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
    TestMaterialLimits();
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

    Equal(14, MetalStrength.Scales.Count);
    Equal(3, MetalStrength.Families.Count);
    Equal(10, MetalStrength.Steels.Count);
    Equal(21, MetalStrength.Grades.Count);

    // 自描述必须把「族 × 硬度制」矩阵和族牌号列表直接写给 AI，不能只靠运行时报错。
    var familySpec = command.Parameters.Single(item => item.Name == "family");
    True(familySpec.Description.Contains("steel→", StringComparison.Ordinal)
         && familySpec.Description.Contains("aluminum→", StringComparison.Ordinal)
         && familySpec.Description.Contains("copper→", StringComparison.Ordinal),
        "family 参数缺少族 × 硬度制矩阵");
    var gradeSpec = command.Parameters.Single(item => item.Name == "grade");
    True(gradeSpec.Description.Contains("steel=generic", StringComparison.Ordinal)
         && gradeSpec.Description.Contains("aluminum=a211_212_annealed", StringComparison.Ordinal)
         && gradeSpec.Description.Contains("copper=brass_plate", StringComparison.Ordinal),
        "grade 参数缺少族牌号列表");

    // ---- 钢：节点值、分钢种、定义域 ----
    var hrc45 = Metal(command, "family=steel value=45 scale=hrc grade=generic");
    var hardness = hrc45.GetProperty("hardness");
    Near(45, hardness.GetProperty("hrc").GetDouble(), 1e-9, "HRC 节点");
    Near(73.2, hardness.GetProperty("hra").GetDouble(), 0.05, "HRC45 → HRA");
    Near(82.9, hardness.GetProperty("hr15n").GetDouble(), 0.05, "HRC45 → HR15N");
    Near(436, hardness.GetProperty("hv").GetDouble(), 0.6, "HRC45 → HV");
    Near(424, hardness.GetProperty("hb").GetDouble(), 0.6, "HRC45 → HB");
    Near(1480, hrc45.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "HRC45 不分钢种 σb");
    True(hrc45.GetRawText().Length < 700, $"钢回执过长：{hrc45.GetRawText().Length}");
    var message = Execute(command, "family=steel value=45 scale=hrc grade=stainless").Message;
    True(message.Length < 300, $"钢消息过长：{message.Length}");

    var stainless = Metal(command, "family=steel value=45 scale=hrc grade=stainless");
    Near(1457, stainless.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "HRC45 不锈钢 σb");
    Near(1480, stainless.GetProperty("strength").GetProperty("sigma_b_generic").GetDouble(), 0.6, "HRC45 不分钢种对照");

    // 源表铬钢 HRC45 印成 1488（与碳钢同值），按上下行 1403、1497 改为 1450。
    var chrome45 = Metal(command, "family=steel value=45 scale=hrc grade=chrome");
    Near(1450, chrome45.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "铬钢 HRC45 σb（已修正为 1450）");

    // 铬钼钢 HRC39=1246 没有足够证据，保持原表值不改。
    var chromeMo39 = Metal(command, "family=steel value=39 scale=hrc grade=chrome_mo");
    Near(1246, chromeMo39.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "铬钼钢 HRC39 保持原表值");

    // 兼容别名：旧调用 steel= 仍然可用。
    var alias = Metal(command, "value=45 scale=hrc steel=stainless");
    Near(1457, alias.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "steel= 兼容别名");

    // 反算：HV 436 回到 HRC 45。
    var fromHv = Metal(command, "family=steel value=436 scale=hv grade=generic");
    Near(45, fromHv.GetProperty("hardness").GetProperty("hrc").GetDouble(), 0.05, "HV436 → HRC");

    // HRC60：HB/HRB/HBS 没有定义，必须显式说明，不能猜。
    var hrc60 = Metal(command, "family=steel value=60 scale=hrc grade=generic");
    True(!hrc60.GetProperty("hardness").TryGetProperty("hb", out _), "HRC60 的 HB 不应出现");
    True(hrc60.GetProperty("undefined").TryGetProperty("hb", out var hbWhy), "HRC60 应说明 HB 无定义");
    True(hbWhy.GetString()!.Contains("211–501", StringComparison.Ordinal), "HB 定义域说明不对：" + hbWhy);
    Near(2607, hrc60.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "HRC60 不分钢种 σb");

    // HRA 从 HRC39 起才有定义。
    var hrc38 = Metal(command, "family=steel value=38 scale=hrc grade=generic");
    True(!hrc38.GetProperty("hardness").TryGetProperty("hra", out _), "HRC38 的 HRA 不应出现");
    True(hrc38.GetProperty("undefined").TryGetProperty("hra", out var hraWhy), "HRC38 应说明 HRA 无定义");
    True(hraWhy.GetString()!.Contains("70–86.6", StringComparison.Ordinal), "HRA 定义域说明不对：" + hraWhy);

    // HRB 段：σb 用低硬度表；HRC 不定义。
    var hrb90 = Metal(command, "family=steel value=90 scale=hrb grade=generic");
    Near(183, hrb90.GetProperty("hardness").GetProperty("hv").GetDouble(), 0.6, "HRB90 → HV");
    Near(629, hrb90.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "HRB90 不分钢种 σb");
    True(!hrb90.GetProperty("hardness").TryGetProperty("hrc", out _), "HRB90 的 HRC 不应出现");

    // 钢种在 HRB 段没有数据：报无定义，不回落到别的钢种。
    var lowSteel = Metal(command, "family=steel value=90 scale=hrb grade=carbon");
    True(lowSteel.GetProperty("strength_undefined").TryGetProperty("sigma_b", out var lowWhy), "碳钢在 HRB90 应无定义");
    Equal("HRC段", lowWhy.GetString());

    // HRB100 与高硬度表在 HV 上重叠：应能补出 HRC/HB。
    var hrb100 = Metal(command, "family=steel value=100 scale=hrb grade=generic");
    Near(803, hrb100.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "HRB100 不分钢种 σb");
    True(hrb100.GetProperty("hardness").GetProperty("hrc").GetDouble() is > 21 and < 23, "HRB100 应换算出 HRC≈22");
    Near(233, hrb100.GetProperty("hardness").GetProperty("hb").GetDouble(), 5, "HRB100 → HB");

    // ---- 钢：肖氏 ----
    var shore = Metal(command, "family=steel value=68 scale=hrc grade=generic");
    Near(96.6, shore.GetProperty("hardness").GetProperty("hs").GetDouble(), 0.05, "HRC68 → HS");
    var fromHs = Metal(command, "family=steel value=96.6 scale=hs grade=generic");
    Near(68, fromHs.GetProperty("hardness").GetProperty("hrc").GetDouble(), 0.05, "HS96.6 → HRC");
    var shoreLow = Metal(command, "family=steel value=20 scale=hrc grade=generic");
    Near(34.0, shoreLow.GetProperty("hardness").GetProperty("hs").GetDouble(), 0.05, "HRC20 → HS");

    // ---- 铝合金 ----
    var aluminum = Metal(command, "family=aluminum value=120 scale=hb grade=a211_212_annealed");
    Near(126.5, aluminum.GetProperty("hardness").GetProperty("hv").GetDouble(), 0.6, "铝 HB120 → HV");
    Near(69.3, aluminum.GetProperty("hardness").GetProperty("hrb").GetDouble(), 0.05, "铝 HB120 → HRB");
    Near(425, aluminum.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "2A11/2A12 退火 σb");
    True(aluminum.GetRawText().Length < 700, $"铝回执过长：{aluminum.GetRawText().Length}");
    var a7a04 = Metal(command, "family=aluminum value=120 scale=hb grade=a7a04");
    Near(444, a7a04.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "7A04 σb");
    var aluminumHigh = Metal(command, "family=aluminum value=170 scale=hb grade=a211_212_annealed");
    True(!aluminumHigh.GetProperty("hardness").TryGetProperty("hv", out _), "铝 HB170 的 HV 不应出现");
    Near(624, aluminumHigh.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "铝 HB170 σb");
    True(aluminumHigh.GetProperty("notes").GetArrayLength() > 0, "铝 HB170 应有重复段说明");
    var aluminumA7High = Metal(command, "family=aluminum value=170 scale=hb grade=a7a04");
    True(aluminumA7High.GetProperty("strength_undefined").TryGetProperty("sigma_b", out var a7Why), "7A04 在 HB170 应无定义");
    Equal("55–164", a7Why.GetString());

    // ---- 铜合金 ----
    var brass = Metal(command, "family=copper value=120 scale=hb grade=brass_plate");
    Near(121.9, brass.GetProperty("hardness").GetProperty("hv").GetDouble(), 0.6, "黄铜 HB120 → HV");
    Near(398, brass.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "黄铜板材 σb");
    var beryllium = Metal(command, "family=copper value=200 scale=hb grade=beryllium_plate");
    Near(657, beryllium.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "铍青铜板材 σb");
    Near(577, beryllium.GetProperty("strength").GetProperty("sigma_02").GetDouble(), 0.6, "铍青铜板材 σ0.2");
    Near(404, beryllium.GetProperty("strength").GetProperty("sigma_001").GetDouble(), 0.6, "铍青铜板材 σ0.01");
    var berylliumHigh = Metal(command, "family=copper value=300 scale=hb grade=beryllium_plate");
    Near(32.4, berylliumHigh.GetProperty("hardness").GetProperty("hrc").GetDouble(), 0.05, "铍青铜 HB300 → HRC");
    Near(1018, berylliumHigh.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "铍青铜 HB300 σb");
    True(berylliumHigh.GetRawText().Length < 700, $"铜回执过长：{berylliumHigh.GetRawText().Length}");
    var brassHigh = Metal(command, "family=copper value=300 scale=hb grade=brass_plate");
    True(brassHigh.GetProperty("strength_undefined").TryGetProperty("sigma_b", out var brassWhy), "黄铜在 HB300 应无定义");
    Equal("110–200", brassWhy.GetString());
    var copperGap = Execute(command, "family=copper value=250 scale=hb");
    True(!copperGap.Success, "铜 HB250 应被拒绝");
    True(copperGap.Message.Contains("缺行", StringComparison.Ordinal), "铜缺口理由不对：" + copperGap.Message);
    var copper220 = Metal(command, "family=copper value=220 scale=hb grade=beryllium_bar");
    Near(808, copper220.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "铍青铜 HB220 σb");
    var copper288 = Metal(command, "family=copper value=288 scale=hb grade=beryllium_bar");
    Near(1047, copper288.GetProperty("strength").GetProperty("sigma_b").GetDouble(), 0.6, "铍青铜 HB288 σb");

    // ---- 拒绝 ----
    foreach (var args in new[]
             {
                 "family=steel value=75 scale=hrc",
                 "family=steel value=100 scale=hv",
                 "family=steel value=50 scale=hrb",
                 "family=steel value=100 scale=hs",
                 "family=aluminum value=120 scale=hs",
                 "family=copper value=25 scale=hrc",
             })
    {
        var outcome = Execute(command, args);
        True(!outcome.Success, $"{args} 应被拒绝");
        True(outcome.Message.Contains("超出定义域", StringComparison.Ordinal)
             || outcome.Message.Contains("不支持", StringComparison.Ordinal),
            $"{args} 的拒绝理由不对：{outcome.Message}");
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
    True(nodes.Any(node => node.TryGetProperty("id", out var id) && id.GetString() == NormaPage.MetalFamilyId),
        "页面缺少材料族选择框");
    True(nodes.Any(node => node.TryGetProperty("id", out var id) && id.GetString() == NormaPage.MetalGradeId),
        "页面缺少牌号选择框");
    var metal = actions.RootElement.GetProperty("actions").EnumerateArray()
        .Single(action => action.GetProperty("id").GetString() == "norma.metal.strength");
    Equal("{metal-family}", metal.GetProperty("args").GetProperty("family").GetString());
    Equal("{metal-value}", metal.GetProperty("args").GetProperty("value").GetString());
    Equal("{metal-scale}", metal.GetProperty("args").GetProperty("scale").GetString());
    Equal("{metal-grade}", metal.GetProperty("args").GetProperty("grade").GetString());

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

        // REQ-MATERIAL-001：隔离宿主参数校验、null 序列化与实际调用。
        var limits = await Probe(archive, "norma.material.limits material=aluminum sigma_b=300");
        var limitResult = limits.GetProperty("data").GetProperty("result");
        True(limitResult.GetProperty("success").GetBoolean(), limitResult.GetRawText());
        True(limitResult.GetProperty("message").GetString()!.Contains("123.5", StringComparison.Ordinal), "宿主铝合金计算错误");
        if (Environment.GetEnvironmentVariable("NORMA_MANUAL_TEST_ROOT") is { Length: > 0 })
        {
            var manual = await Probe(archive, "norma.manual.read id=sjzl:7 length=100");
            var manualResult = manual.GetProperty("data").GetProperty("result");
            True(manualResult.GetProperty("success").GetBoolean(), manualResult.GetRawText());
        }

        // 新物理表经隔离宿主装载后的实际命令调用。
        foreach (var (method, query, field, expected) in new[]
        {
            ("elastic", "铅", "e", 16d), ("density", "铅", "rho", 11.37),
            ("thermal", "铜", "conductivity", 384d), ("expansion", "碳钢", "20~100", 10.6),
            ("bulk", "焦炭", "rho_bulk", 0.36),
        })
        {
            var physical = await Probe(archive, $"norma.material.{method} q={query}");
            var physicalResult = physical.GetProperty("data").GetProperty("result");
            True(physicalResult.GetProperty("success").GetBoolean(), physicalResult.GetRawText());
            var payload = physicalResult.GetProperty("data");
            var row = payload.GetProperty("items")[0];
            Near(expected, row.GetProperty("properties").GetProperty(field).GetProperty("min").GetDouble(), 1e-9, "宿主物理表 " + method);
        }

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
        True(metalResult.GetProperty("message").GetString()!.Contains("σb=1457", StringComparison.Ordinal),
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

// REQ-MANUAL-001/002: synthetic source with Chinese, merged cells, images and pagination.
static void TestManual()
{
    var nativeRejected = false;
    try { ManualWindowScreenshot.Validate(-1, 0, 0, 0); } catch (ArgumentException) { nativeRejected = true; }
    True(nativeRejected, "原窗口截图必须拒绝负裁切坐标");
    ManualWindowScreenshot.Validate(0, 0, 0, 0);
    if (Environment.GetEnvironmentVariable("NORMA_NATIVE_SCREENSHOT_TEST") == "1")
    {
        Console.WriteLine(JsonSerializer.Serialize(ManualWindowScreenshot.DirectoryView(ManualCommands.DefaultRoot, "常用材料", 0, 30)));
        Console.WriteLine(JsonSerializer.Serialize(ManualWindowScreenshot.Open(ManualCommands.DefaultRoot, "常用材料弹性模量及泊松比", null)));
        var capture = JsonSerializer.SerializeToElement(ManualWindowScreenshot.Capture(ManualCommands.DefaultRoot, null, 0, 0, 0, 0));
        Equal("content", capture.GetProperty("capture").GetProperty("region").GetString()!);
        True(capture.GetProperty("capture").GetProperty("width").GetInt32() < capture.GetProperty("capture").GetProperty("windowWidth").GetInt32(), "正文截图没有排除左侧目录");
        Equal("常用材料弹性模量及泊松比", capture.GetProperty("capture").GetProperty("contentTitle").GetString()!);
        Console.WriteLine(capture.GetRawText());
        var missingRejected = false;
        try { ManualWindowScreenshot.Open(ManualCommands.DefaultRoot, "不存在的手册页-验收", null); } catch (IOException) { missingRejected = true; }
        True(missingRejected, "不存在的原页面被报告为打开成功");
    }

    foreach (var input in new[] { (-1, 1400, 1600), (0, 639, 1600), (0, 1400, 4001) })
    {
        var rejected = false;
        try { ManualScreenshot.Validate(input.Item1, input.Item2, input.Item3); } catch (ArgumentException) { rejected = true; }
        True(rejected, "非法截图尺寸未拒绝");
    }
    var safe = ManualScreenshot.Html(new Manual.Page("test:1", "<标题>", "test.ysc", 1, 0,
        "<body><meta http-equiv=refresh content='0;url=https://example.com'><table><tr><td>核对</td></tr></table><script>alert(1)</script></body>"), 500);
    True(safe.Contains("default-src 'none'") && safe.Contains("&lt;标题&gt;") && safe.Contains("top:-500px"), "截图安全边界或裁切偏移丢失");
    True(!safe.Contains("<script") && !safe.Contains("http-equiv=refresh"), "源页面活动内容未清除");
    True(safe.Contains(ManualScreenshot.EvidenceNote) && safe.Contains("<td>核对</td>"), "证据标记或表格丢失");
    if (Environment.GetEnvironmentVariable("NORMA_MANUAL_SCREENSHOT_ROOT") is { Length: > 0 } captureRoot)
    {
        foreach (var id in new[] { "sjzl:5", "sjzl:6", "sjzl:8", "sjzl:9", "sjzl:30" })
        {
            var capture = JsonSerializer.SerializeToElement(ManualScreenshot.Capture(captureRoot, id, 0, 1400, 2600));
            True(File.Exists(capture.GetProperty("imagePath").GetString()), "未生成截图");
            Console.WriteLine(capture.GetRawText());
        }
    }
    var root = Path.Combine(Path.GetTempPath(), "norma-manual-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var source = "yingkeyushouceku_m1_2005" + new string('\0', 120) +
            "<html><head><title>钢的硬度</title><style>hidden</style></head><body><table><tr><td rowspan=2>HRC</td><td>强度</td></tr><tr><td>123</td></tr></table><img src='a.gif'></body></html>" +
            "<html><head><title>单位</title></head><body>兆帕 &amp; 牛顿</body></html>";
        File.WriteAllBytes(Path.Combine(root, "sjzl.ysc"), Encoding.GetEncoding(936).GetBytes(source));
        JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
        var labelBytes = new byte[113];
        Encoding.ASCII.GetBytes("yingkeyushouceku_m1_2005").CopyTo(labelBytes, 0);
        Encoding.GetEncoding(936).GetBytes("目录原标签").CopyTo(labelBytes, 25);
        File.WriteAllBytes(Path.Combine(root, "untitled.ysc"), labelBytes.Concat(Encoding.ASCII.GetBytes("<html><body>data</body></html>")).ToArray());
        Equal("目录原标签", Json(Manual.Read(root, "untitled:1", "text", 0, 20)).GetProperty("title").GetString()!);
        File.Delete(Path.Combine(root, "untitled.ysc"));
        var result = Json(Manual.Search(root, "硬度", "title", null, 0, 20));
        Equal(1, result.GetProperty("total").GetInt32());
        Equal("sjzl:1", result.GetProperty("items")[0].GetProperty("id").GetString()!);
        Equal(1, Json(Manual.Search(root, "123", "text", "sjzl", 0, 20)).GetProperty("total").GetInt32());
        Equal(0, Json(Manual.Search(root, "不存在", "text", null, 0, 20)).GetProperty("total").GetInt32());
        Equal(1, Json(Manual.Search(root, "", "title", null, 1, 1)).GetProperty("returned").GetInt32());
        var page = Json(Manual.Read(root, "sjzl:1", "text", 0, 20000));
        var content = page.GetProperty("content").GetString()!;
        True(content.Contains("HRC\t强度") && content.Contains("123"), "表格行/单元格丢失");
        True(!content.Contains("hidden"), "样式泄漏正文");
        Equal("a.gif", page.GetProperty("images")[0].GetString()!);
        var first = Json(Manual.Read(root, "sjzl:1", "text", 0, 3));
        var rest = Json(Manual.Read(root, "sjzl:1", "text", 3, 20000));
        Equal(content, first.GetProperty("content").GetString()! + rest.GetProperty("content").GetString()!);
        True(Json(Manual.Read(root, "sjzl:1", "html", 0, 20000)).GetProperty("content").GetString()!.Contains("rowspan=2"), "合并单元格原文丢失");
        if (Environment.GetEnvironmentVariable("NORMA_MANUAL_TEST_ROOT") is { Length: > 0 } realRoot)
        {
            var actual = Manual.Load(realRoot).ToArray();
            True(actual.Length > 1000, "本地手册页数异常");
            True(actual.All(p => p.Title.Length > 0), "本地手册标题有缺失");
            True(actual.Select(p => p.Id).Distinct().Count() == actual.Length, "真实手册页 ID 有冲突");
            var standard = Json(Manual.Read(realRoot, "sjzl:17", "text", 0, 20000));
            True(standard.GetProperty("title").GetString()!.Contains("国内部分标准代号"), "真实页序号与标题不一致");
            True(standard.GetProperty("content").GetString()!.Contains("国家标准"), "真实 GBK 表格正文无法读取");
            var matches = Json(Manual.Search(realRoot, "硬度", "title", null, 0, 20));
            True(matches.GetProperty("total").GetInt32() > 0, "全书标题搜索失败");
            var registry = Registry();
            registry.TryGet("norma.manual.read", out var command);
            var outcome = command!.Handler(new CommandContext(command, new Dictionary<string, string> { ["root"] = realRoot, ["id"] = "sjzl:17" }, "test", null, CancellationToken.None)).Result;
            True(outcome.Success, outcome.Message);
            // REQ-MANUAL-003：真实默认路径，不传 root 调用命令。
            foreach (var method in new[] { "search", "read" })
            {
                registry.TryGet("norma.manual." + method, out var defaultCommand);
                var arguments = method == "search"
                    ? new Dictionary<string, string> { ["q"] = "极限强度", ["chapter"] = "sjzl" }
                    : new Dictionary<string, string> { ["id"] = "sjzl:7" };
                var actualDefault = defaultCommand!.Handler(new CommandContext(defaultCommand, arguments, "test", null, CancellationToken.None)).Result;
                True(actualDefault.Success, "省略 root 的 " + method + " 失败：" + actualDefault.Message);
                var defaultJson = Json(actualDefault.Data!);
                if (method == "search")
                    Equal("sjzl:7", defaultJson.GetProperty("items")[0].GetProperty("id").GetString()!);
                else Equal("常用材料极限强度的近似关系", defaultJson.GetProperty("title").GetString()!);
            }
            Console.WriteLine($"  Local manual: {actual.Length} pages / {actual.Select(p => p.File).Distinct().Count()} chapters; real command read passed");
        }
        foreach (var id in new[] { "../sjzl:1", "sjzl:0", "sjzl:999" })
        {
            var rejected = false;
            try { Manual.Read(root, id, "text", 0, 20); } catch (ArgumentException) { rejected = true; }
            True(rejected, "应拒绝非法或不存在的 ID");
        }
    }
    finally { Directory.Delete(root, true); }
}

// REQ-MATERIAL-001：独立手算对照每一列，包含区间、缺值、命令 JSON 和输入拒绝。
static void TestMaterialLimits()
{
    var steel = Calc("material.limits", "material=structural_steel sigma_b=600");
    var names = new[] { "sigma_m1t", "sigma_m1", "tau_m1_min", "tau_m1_max", "sigma_0t", "sigma_0", "tau_0" };
    var expected = new[] { 180d, 258d, 150d, 150d, 255.6, 343.14, 225d };
    for (var i = 0; i < names.Length; i++) Near(expected[i], steel[names[i]], 1e-9, "结构钢 " + names[i]);
    var cast = Calc("material.limits", "material=cast_iron sigma_b=200");
    var castExpected = new[] { 45d, 90d, 72d, 72d, 63.9, 121.5, 97.2 };
    for (var i = 0; i < names.Length; i++) Near(castExpected[i], cast[names[i]], 1e-9, "铸铁 " + names[i]);

    var aluminum = Calc("material.limits", "material=aluminum sigma_b=300");
    Near(123.5, aluminum["sigma_m1t"], 1e-9, "铝拉压");
    Near(123.5, aluminum["sigma_m1"], 1e-9, "铝弯曲");
    Near(67.925, aluminum["tau_m1_min"], 1e-9, "铝扭转下界");
    Near(71.63, aluminum["tau_m1_max"], 1e-9, "铝扭转上界");
    Near(185.25, aluminum["sigma_0t"], 1e-9, "铝脉动拉压");
    True(double.IsNaN(aluminum["sigma_0"]) && double.IsNaN(aluminum["tau_0"]), "原表—不可补猜");
    True(NormaCommands.Message(aluminum).Contains("σ₀=无定义") && !NormaCommands.Message(aluminum).Contains("NaN"), "无定义项的人读摘要");
    var json = JsonSerializer.SerializeToElement(NormaCommands.Payload(aluminum));
    var outputs = json.GetProperty("outputs").EnumerateArray().ToDictionary(o => o.GetProperty("name").GetString()!);
    Equal(JsonValueKind.Null, outputs["sigma_0"].GetProperty("value").ValueKind);
    Equal(JsonValueKind.Null, outputs["tau_0"].GetProperty("value").ValueKind);
    foreach (var bad in new[] { "material=aluminum sigma_b=0", "material=aluminum sigma_b=-1", "material=aluminum sigma_b=NaN", "material=aluminum sigma_b=Infinity", "material=unknown sigma_b=600", "material=structural_steel" })
    {
        var rejected = false;
        try { Calc("material.limits", bad); } catch (ArgumentException) { rejected = true; }
        True(rejected, "必须拒绝 " + bad);
    }
    var registry = Registry();
    foreach (var method in new[] { "search", "read" })
    {
        registry.TryGet("norma.manual." + method, out var descriptor);
        var root = descriptor!.Parameters.Single(p => p.Name == "root");
        True(!root.Required, "root 必须可省略");
        Equal(ManualCommands.DefaultRoot, root.Default);
    }
}

// REQ-MATERIAL-002/003：独立表值、整表覆盖、缺值与筛选、目录和分类。
static void TestPhysicalTables()
{
    var expectedCounts = new[] { 28, 56, 14, 17, 50 };
    for (var i = 0; i < PhysicalTables.Tables.Length; i++)
    {
        var t = PhysicalTables.Tables[i];
        var all = PhysicalTables.Search(t.Key, null, null, 0, 100);
        Equal(expectedCounts[i], all.Total);
        Equal(expectedCounts[i], all.Items.Count);
        Equal(all.Total, all.Items.Select(row => row.Id).Distinct().Count());
        foreach (var row in all.Items)
        {
            Equal(t.Fields.Length, row.Properties.Count);
            foreach (var value in row.Properties.Values)
            {
                if (value.Undefined is null)
                    True(value.Min.HasValue && value.Max.HasValue && value.Min <= value.Max, "表范围错误");
                else True(value.Value is null && value.Min is null && value.Max is null, "缺值不能造数");
            }
        }
        var first = PhysicalTables.Search(t.Key, null, null, 0, 2);
        var second = PhysicalTables.Search(t.Key, null, null, first.NextOffset!.Value, 2);
        Equal(all.Items[2].Id, second.Items[0].Id);
        Equal(0, PhysicalTables.Search(t.Key, "不存在的材料", null, 0, 20).Total);
        var registry = Registry();
        True(registry.TryGet("norma.material." + t.Key, out var descriptor), "未注册物理表");
        Equal("material", descriptor!.CommandClass);
        var outcome = descriptor.Handler(new CommandContext(descriptor, new Dictionary<string, string>(), "test", null, CancellationToken.None)).Result;
        True(outcome.Success, outcome.Message);
        JsonSerializer.Serialize(outcome.Data);
    }
    var steel = PhysicalTables.Search("elastic", "碳钢", null, 0, 20).Items.Single();
    Near(206, steel.Properties["e"].Value!.Value, 0, "钢 E/GPa");
    Near(79.4, steel.Properties["g"].Value!.Value, 0, "钢 G/GPa");
    Near(0.3, steel.Properties["nu"].Value!.Value, 0, "钢泊松比");
    Near(11.37, PhysicalTables.Search("density", "铅", null, 0, 20).Items.Single().Properties["rho"].Value!.Value, 0, "铅密度");
    var carbon = PhysicalTables.Search("density", "碳钢", null, 0, 20).Items.Single().Properties["rho"];
    Near(7.3, carbon.Min!.Value, 0, "碳钢密度下界");
    Near(7.85, carbon.Max!.Value, 0, "碳钢密度上界");
    True(carbon.Value is null, "范围不能自动取中值");
    var copper = PhysicalTables.Search("thermal", "铜", null, 0, 20).Items.Single();
    Near(1083, copper.Properties["melting"].Value!.Value, 0, "铜熔点");
    Near(384, copper.Properties["conductivity"].Value!.Value, 0, "铜导热");
    Near(0.394, copper.Properties["specific_heat"].Value!.Value, 0, "铜比热");
    var expansion = PhysicalTables.Search("expansion", "碳钢", "20~100", 0, 20).Items.Single();
    Equal(1, expansion.Properties.Count);
    Near(10.6, expansion.Properties["20~100"].Min!.Value, 0, "线膨胀下界");
    Near(12.2, expansion.Properties["20~100"].Max!.Value, 0, "线膨胀上界");
    var coke = PhysicalTables.Search("bulk", "焦炭", null, 0, 20).Items.Single();
    Near(0.36, coke.Properties["rho_bulk"].Min!.Value, 0, "焦炭堆密度");
    Near(35, coke.Properties["angle_moving"].Value!.Value, 0, "运动角");
    Near(50, coke.Properties["angle_static"].Value!.Value, 0, "静止角");
    Equal("原表未给出", PhysicalTables.Search("elastic", "球墨铸铁", null, 0, 20).Items.Single().Properties["g"].Undefined!);
    Equal("源页该值缺损，待核", PhysicalTables.Search("thermal", "灰铸铁", null, 0, 20).Items.Single().Properties["melting"].Undefined!);
    Equal(2, PhysicalTables.Search("density", "未辨材料", null, 0, 20).Total);
    Equal(1, PhysicalTables.Search("thermal", "未辨材料", null, 0, 20).Total);
    var registry2 = Registry();
    foreach (var name in new[] { "norma.material.limits", "norma.metal.strength" })
    {
        registry2.TryGet(name, out var descriptor);
        Equal("material", descriptor!.CommandClass);
    }
    Equal(7, NormaCommands.PageRows().Count(row => row["topic"] == "机械材料"));
    foreach (var (offset, limit, period) in new[] { (-1, 20, (string?)null), (0, 0, (string?)null), (0, 101, (string?)null), (0, 20, (string?)"20~150") })
    {
        var rejected = false;
        try { PhysicalTables.Search("expansion", null, period, offset, limit); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "无效分页或温区必须拒绝");
    }
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
