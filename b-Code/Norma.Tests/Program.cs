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
    ("drive efficiency reference table", Sync(TestDriveEfficiency)),
    ("cast iron coverage and conditional values", Sync(TestCastIron)),
    ("cast iron scalar and explicit variable lookup", Sync(TestCastIronLookup)),
    ("compact material passports and optional filters", Sync(TestMaterialLibrary)),
    ("steel grade cards and source conditions", Sync(TestSteelMaterials)),
    ("nonferrous and special alloy grade cards", Sync(TestNonFerrousMaterials)),
    ("named nonmetal and composite material cards", Sync(TestNamedMaterials)),
    ("product specifications, combinations and tolerance boundaries", Sync(TestProductSpecs)),
    ("materials volume omissions and complete audit", Sync(TestMaterialSupplement)),
    ("consolidated material and specification tools", Sync(TestConsolidatedMaterials)),
    ("basic standards, source rules and minimal query surface", Sync(TestBasicStandards)),
    ("IT grade selection, manufacture and relative costs", Sync(TestToleranceSelection)),
    ("shaft limit deviations and original image tables", Sync(TestShaftLimitDeviations)),
    ("band selection ranks and orthogonal filtering", Sync(TestBandUsage)),
    ("fit cards, preferred selection and original limit cells", Sync(TestFitStandards)),
    ("fit applications and dimension-free source filtering", Sync(TestFitApplications)),
    ("statistical fit limits on existing cards", Sync(TestFitStatistics)),
    ("angular series, geometric keys and source precision", Sync(TestAngularSeries)),
    ("conical references merged into existing methods", Sync(TestConicalReferences)),
    ("cone axial transforms and AT length lookup", Sync(TestConeMethods)),
    ("limits and fits chapter coverage and corrected cell invariants", Sync(TestChapterClosure)),
    ("shaft basic deviation applications", Sync(TestBandApplications)),
    ("general tolerances with two coordinates and optional property", Sync(TestGeneralTolerances)),
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
        Console.Error.WriteLine($"FAIL {test.Name}: {ex}");
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

static int ExpectedCommandCount() => Catalog.All.Count + 22;

static void TestCommandRegistration()
{
    var registry = Registry();
    var domain = NormaIdentity.Domain;
    var expected = Catalog.All.Select(item => domain + "." + item.Id)
        .Concat([domain + ".formula.list", domain + ".formula.show", domain + ".std.lookup", domain + ".metal.strength", domain + ".manual.search", domain + ".manual.read", domain + ".manual.screenshot", domain + ".manual.open", domain + ".manual.directory", domain + ".ui.describe", domain + ".ui.actions", domain + ".ui.data"])
        .ToList();
    expected.Add("norma.drive.efficiency");
    expected.Add("norma.product.spec");
    expected.AddRange(["norma.basic.tolerance", "norma.basic.band", "norma.basic.fit", "norma.basic.general", "norma.basic.angular", "norma.basic.coneangle"]);
    expected.AddRange(new[] { "norma.material.grade", "norma.material.library" });
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

static void TestBasicStandards()
{
    static JsonElement Q(string name, object? spec = null, string? property = null, string? standard = null, int offset = 0, int limit = 20)
    {
        var fields = spec is null ? new Dictionary<string, object>() : (Dictionary<string, object>)spec;
        string? Field(string key) => fields.TryGetValue(key, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null;
        return JsonSerializer.SerializeToElement(name == "标准公差"
            ? BasicStandards.QueryTolerance(Field("基本尺寸"), Field("等级"), standard, offset, limit)
            : BasicStandards.QueryBand(Field("代号") is { } code ? code + Field("等级") : null, Field("基本尺寸"), property, standard, offset, limit));
    }
    static JsonElement IT(double size, int grade) => Q("标准公差", new Dictionary<string, object> { ["基本尺寸"] = size, ["等级"] = grade }).GetProperty("data").GetProperty("公差");
    static JsonElement FD(string code, double size, int grade) => Q("基本偏差", new Dictionary<string, object> { ["代号"] = code, ["基本尺寸"] = size, ["等级"] = grade }).GetProperty("data").GetProperty("基本偏差");
    static void V(double expected, JsonElement result) => Near(expected, result.GetProperty("value").GetDouble(), 1e-10, result.GetRawText());
    Equal(3, BasicStandards.Pages.Count);
    var pages = BasicStandards.Pages;
    Equal(38513L, pages[0].ByteOffset);
    Equal("GB/T 1800.3—1998", pages[2].Standard);
    // All 378 printed standard-tolerance cells must be queryable with their original units.
    for (var r = 3; r < pages[2].Table.Length; r++)
    for (var g = 1; g <= 18; g++)
    {
        var size = double.Parse(pages[2].Table[r][1], System.Globalization.CultureInfo.InvariantCulture);
        var fact = IT(size, g);
        V(double.Parse(pages[2].Table[r][g + 1], System.Globalization.CultureInfo.InvariantCulture), fact);
        Equal(g <= 11 ? "μm" : "mm", fact.GetProperty("unit").GetString());
        Equal(r, fact.GetProperty("source").GetProperty("row").GetInt32());
    }
    V(21, IT(30, 7)); V(25, IT(30.00001, 7)); V(63, IT(500, 7)); V(70, IT(500.00001, 7)); V(210, IT(3150, 7));
    True(IT(1, 14).GetProperty("value").ValueKind == JsonValueKind.Null, "≤1mm不得补IT14");
    V(0.25, IT(1.00001, 14));
    True(IT(501, 1).GetProperty("notes")[0].GetString()!.Contains("试行"), "大尺寸低等级试行脚注丢失");
    Equal("no_match", Q("标准公差", new Dictionary<string, object> { ["基本尺寸"] = 3150.1, ["等级"] = 7 }).GetProperty("status").GetString());
    Equal("no_match", Q("标准公差", new Dictionary<string, object> { ["基本尺寸"] = 25, ["等级"] = 7 }, standard: "2020").GetProperty("status").GetString());
    V(-7, FD("g", 25, 7)); V(7, FD("G", 25, 7));
    V(6, FD("K", 25, 7)); V(-31, FD("S", 25, 6));
    V(-9, FD("M", 250.00001, 6)); V(-9, FD("M", 315, 6)); V(-10, FD("M", 315.00001, 6));
    V(0, FD("k", 3, 6)); V(1, FD("k", 3.00001, 6)); V(0, FD("k", 4, 8));
    True(FD("a", 1, 7).GetProperty("value").ValueKind == JsonValueKind.Null, "a≤1不适用");
    True(FD("N", 1, 9).GetProperty("value").ValueKind == JsonValueKind.Null, "N>IT8且≤1不适用");
    V(-270, FD("a", 1.0001, 7));
    True(FD("cd", 25, 7).GetProperty("value").ValueKind == JsonValueKind.Null, "原表空格不补0");
    True(FD("K", 25, 2).GetProperty("value").ValueKind == JsonValueKind.Null, "未给Δ等级不猜");
    var bad = FD("P", 350, 8);
    Equal("-62", bad.GetProperty("raw").GetString());
    V(-62, bad);
    V(-1150, FD("U", 1100, 8)); V(-840, FD("T", 1200, 8)); V(340, FD("B", 190, 7));
    foreach (var code in new[] { "js", "JS" })
    {
        var odd = Q("基本偏差", new Dictionary<string, object> { ["代号"] = code, ["基本尺寸"] = 25, ["等级"] = 7 }).GetProperty("data");
        V(10, odd.GetProperty("上偏差")); V(-10, odd.GetProperty("下偏差"));
        Equal(0, odd.GetProperty("上偏差").GetProperty("dependencies").GetArrayLength());
        var even = Q("基本偏差", new Dictionary<string, object> { ["代号"] = code, ["基本尺寸"] = 25, ["等级"] = 6 }).GetProperty("data");
        V(6.5, even.GetProperty("上偏差"));
    }
    TestHoleLimitDeviations();
    var toleranceMissing = Q("标准公差");
    Equal(1, toleranceMissing.GetProperty("missing").GetArrayLength());
    True(!toleranceMissing.GetRawText().Contains("术语"), "查表不应包含术语");
    var missing = Q("基本偏差");
    Equal("needs_input", missing.GetProperty("status").GetString());
    Equal(2, missing.GetProperty("missing").GetArrayLength());
    True(missing.GetProperty("default").ValueKind == JsonValueKind.Null && !missing.TryGetProperty("rows", out _), "缺参不是展开");
    var upperOnly = JsonSerializer.SerializeToElement(BasicStandards.QueryBand(size: "25", property: "上偏差", limit: 100));
    var codeOptions = upperOnly.GetProperty("missing");
    Equal(1, codeOptions.GetArrayLength());
    Equal("band", codeOptions[0].GetProperty("missing").GetString());
    Equal(1008, codeOptions[0].GetProperty("total").GetInt32());
    True(codeOptions[0].GetProperty("options").EnumerateArray().Any(x => x.GetString() == "a9"), "轴极限偏差必须参与候选收窄");
    var basicOnly = Q("基本偏差", new Dictionary<string, object> { ["代号"] = "*", ["基本尺寸"] = 25, ["等级"] = 7 }, property: "基本偏差", limit: 100);
    Equal(54, basicOnly.GetProperty("total").GetInt32());
    foreach (var row in basicOnly.GetProperty("rows").EnumerateArray())
    {
        True(!row.GetProperty("spec").GetProperty("公差带").GetString()!.StartsWith("js", StringComparison.OrdinalIgnoreCase), "属性须先收窄代号");
        True(row.GetProperty("data").TryGetProperty("基本偏差", out _), "变化表不返回属性过滤后的空卡");
    }
    var tableSpec = new Dictionary<string, object> { ["基本尺寸"] = 25, ["等级"] = "*" };
    var table = Q("标准公差", tableSpec, limit: 2);
    Equal(20, table.GetProperty("total").GetInt32()); Equal(2, table.GetProperty("rows").GetArrayLength()); Equal(2, table.GetProperty("nextOffset").GetInt32());
    var second = Q("标准公差", tableSpec, offset: 2, limit: 2);
    Equal("IT1", second.GetProperty("rows")[0].GetProperty("spec").GetProperty("等级").GetString());
    var stillMissing = JsonSerializer.SerializeToElement(BasicStandards.QueryBand("H*"));
    Equal(1, stillMissing.GetProperty("missing").GetArrayLength());
    Equal("size", stillMissing.GetProperty("missing")[0].GetProperty("missing").GetString());
    foreach (var (size, grade) in new[] { ("0", "7"), ("-1", "7"), ("NaN", "7"), ("Infinity", "7"), ("尺寸", "7"), ("25", "00"), ("25", "IT001"), ("25", "19"), ("25", "7.5"), ("*", "*"), ("25*", "7") })
    {
        var rejects = false;
        try { BasicStandards.QueryTolerance(size, grade); } catch (ArgumentException) { rejects = true; }
        True(rejects, "未拒绝非法尺寸/等级：" + size + "/" + grade);
    }
    var multipleStarsRejected = false;
    try { BasicStandards.QueryBand("*7", "25", "*"); } catch (ArgumentException) { multipleStarsRejected = true; }
    True(multipleStarsRejected, "偏差查询未拒绝多*跨维度展开");
    foreach (var (offset, limit) in new[] { (-1, 20), (0, 0), (0, 101) })
    {
        var rejects = false;
        try { BasicStandards.QueryTolerance(offset: offset, limit: limit); } catch (ArgumentException) { rejects = true; }
        True(rejects, "分页校验遗漏");
    }
    var h7 = JsonSerializer.SerializeToElement(BasicStandards.QueryBand("H7", "25"));
    V(21, h7.GetProperty("data").GetProperty("公差"));
    V(0, h7.GetProperty("data").GetProperty("基本偏差"));
    Equal("H7", h7.GetProperty("spec").GetProperty("公差带").GetString());
    var g6 = JsonSerializer.SerializeToElement(BasicStandards.QueryBand("g6", "25"));
    V(13, g6.GetProperty("data").GetProperty("公差"));
    V(-7, g6.GetProperty("data").GetProperty("基本偏差"));
    var hMissingGrade = JsonSerializer.SerializeToElement(BasicStandards.QueryBand("H", "25"));
    Equal("band", hMissingGrade.GetProperty("missing")[0].GetProperty("missing").GetString());
    Equal(18, hMissingGrade.GetProperty("missing")[0].GetProperty("total").GetInt32());
    var hGrades = JsonSerializer.SerializeToElement(BasicStandards.QueryBand("H*", "25", limit: 100));
    Equal(18, hGrades.GetProperty("total").GetInt32());
    foreach (var invalidBand in new[] { "H7/g6", "H7 g6", "H7,g6", "H7-g6", "H7g6", "**", "H0", "H01", "H19" })
    {
        var rejects = false;
        try { BasicStandards.QueryBand(invalidBand, "25"); } catch (ArgumentException) { rejects = true; }
        True(rejects, "未拒绝配合组合/非法公差带：" + invalidBand);
    }
    var registry = Registry();
    True(!registry.TryGet("norma.basic.query", out _), "不得保留按表名分发的旧入口");
    True(!registry.TryGet("norma.basic.deviation", out _), "公差带卡不得额外注册重复的偏差入口");
    foreach (var command in new[] { "norma.basic.tolerance", "norma.basic.band" })
    {
        registry.TryGet(command, out var entry);
        Equal("basic", entry!.CommandClass);
        True(entry.Parameters.All(p => !p.Required), "缺条件应一次给齐选项");
        True(entry.Parameters.All(p => p.Name is not ("name" or "spec" or "table")), "独立切面不得要求表名或动态JSON条件");
        True(NormaCommands.PageRows().Single(r => r["command"] == command)["topic"] == "基础标准", "基础标准目录遗漏");
    }
    registry.TryGet("norma.basic.tolerance", out var descriptor);
    Equal("size,grade,property,process,feature,standard,offset,limit", string.Join(",", descriptor!.Parameters.Select(p => p.Name)));
    var args = new Dictionary<string, string> { ["size"] = "25", ["grade"] = "IT7" };
    var result = descriptor.Handler(new CommandContext(descriptor, args, "test", null, CancellationToken.None)).Result;
    True(result.Success, result.Message);
    V(21, JsonSerializer.SerializeToElement(result.Data).GetProperty("data").GetProperty("公差"));
    registry.TryGet("norma.basic.band", out var deviationDescriptor);
    Equal("band,size,property,usage,standard,offset,limit", string.Join(",", deviationDescriptor!.Parameters.Select(p => p.Name)));
    args = new Dictionary<string, string> { ["band"] = "K7", ["size"] = "25" };
    var deviationResult = deviationDescriptor.Handler(new CommandContext(deviationDescriptor, args, "test", null, CancellationToken.None)).Result;
    True(deviationResult.Success, deviationResult.Message);
    V(6, JsonSerializer.SerializeToElement(deviationResult.Data).GetProperty("data").GetProperty("基本偏差"));
    var singleCard = JsonSerializer.SerializeToElement(BasicStandards.QueryBand("JS7", "25", offset: 100, limit: 1));
    Equal(4, singleCard.GetProperty("data").EnumerateObject().Count());
    registry.TryGet("norma.formula.list", out var list);
    var listing = list!.Handler(new CommandContext(list, new Dictionary<string, string> { ["topic"] = "basic" }, "test", null, CancellationToken.None)).Result;
    Equal(6, JsonSerializer.SerializeToElement(listing.Data).GetArrayLength());
}

static void TestToleranceSelection()
{
    static JsonElement Q(string? grade = null, string? property = null, string? process = null, string? feature = null,
        string? size = null, string? standard = null, int offset = 0, int limit = 100)
        => JsonSerializer.SerializeToElement(BasicStandards.QueryTolerance(size, grade, standard, offset, limit, property, process, feature));
    Equal(5, ToleranceSelection.Pages.Count);
    Equal(264, ToleranceSelection.Facts.Count);
    Equal(4246979L, ToleranceSelection.Pages[0].ByteOffset);
    Equal(4360957L, ToleranceSelection.Pages[4].ByteOffset);
    foreach (var (property, count) in new[] { ("应用范围", 30), ("选用举例", 20), ("可达加工方法", 74), ("经济加工方法", 19), ("加工成本比", 121) })
    {
        Equal(count, ToleranceSelection.Facts.Count(f => f.Property == property));
        var enumeration = Q("*", property);
        var seen = 0;
        foreach (var card in enumeration.GetProperty("rows").EnumerateArray())
        {
            Equal(1, card.GetProperty("data").EnumerateObject().Count());
            var grade = card.GetProperty("spec").GetProperty("等级").GetString();
            var facts = card.GetProperty("data").GetProperty(property);
            Equal(ToleranceSelection.Facts.Count(f => f.Property == property && f.Grade == grade), facts.GetArrayLength());
            seen += facts.GetArrayLength();
            foreach (var fact in facts.EnumerateArray())
                True(fact.GetProperty("source").GetProperty("byteOffset").GetInt64() > 0, "IT选用来源缺失");
        }
        Equal(count, seen);
    }
    var card7 = Q("IT7").GetProperty("data");
    True(card7.GetProperty("选用举例")[0].GetProperty("value").GetString()!.Contains("联轴器"), "IT7举例丢失");
    Equal("needs_input", card7.GetProperty("公差").GetProperty("status").GetString());
    True(card7.GetProperty("应用范围").EnumerateArray().Any(f => f.GetProperty("value").GetString() == "配合尺寸"), "IT7灰底范围错位");
    var it01 = Q("01").GetProperty("data");
    Equal("块规", it01.GetProperty("应用范围")[0].GetProperty("value").GetString());
    True(it01.GetProperty("公差").GetProperty("value").ValueKind == JsonValueKind.Null, "IT01不得补公差数值");
    Equal("IT0", Q("0", "选用举例").GetProperty("spec").GetProperty("等级").GetString());
    Equal("needs_input", Q(property: "公差").GetProperty("status").GetString());
    Equal(2, Q(property: "公差").GetProperty("missing").GetArrayLength());
    Equal(1, Q(property: "可达加工方法", process: "研磨").GetProperty("missing").GetArrayLength());
    var grindingGrades = Q("*", "可达加工方法", "研磨");
    Equal(7, grindingGrades.GetProperty("total").GetInt32()); // IT01,0,1..5
    Equal("no_match", Q("6", "可达加工方法", "研磨").GetProperty("status").GetString());
    Equal(1, Q("7", "可达加工方法", "铰孔").GetProperty("data").GetProperty("可达加工方法").GetArrayLength());
    // Same grade, distinct hole/shaft economic groups. Footnotes must survive scalar filters.
    var shaft = Q("7", "经济加工方法", feature: "轴").GetProperty("data").GetProperty("经济加工方法");
    var hole = Q("7", "经济加工方法", feature: "孔").GetProperty("data").GetProperty("经济加工方法");
    True(shaft[0].GetProperty("value").GetString()!.Contains("金刚石"), "轴经济方法错位");
    True(hole[0].GetProperty("value").GetString()!.Contains("金刚石"), "孔经济方法错位");
    Equal(3, hole[0].GetProperty("notes").GetArrayLength());
    Equal("no_match", Q("4", "经济加工方法", feature: "孔").GetProperty("status").GetString());
    Equal(1, Q("4", "经济加工方法", feature: "轴").GetProperty("data").GetProperty("经济加工方法").GetArrayLength());
    foreach (var (g, expected) in new[] { ("6", 5d), ("9", 2.5), ("12", 1d) })
    {
        var cost = Q(g, "加工成本比", "普通车削", "外径").GetProperty("data").GetProperty("加工成本比");
        Equal(1, cost.GetArrayLength()); Near(expected, cost[0].GetProperty("value").GetDouble(), 0, "成本图例映射");
        Equal("1", cost[0].GetProperty("unit").GetString());
    }
    Near(5, Q("8", "加工成本比", "普通车削", "长度").GetProperty("data").GetProperty("加工成本比")[0].GetProperty("value").GetDouble(), 0, "长度/外径条件隔离");
    Equal("no_match", Q("7", "加工成本比", "钻", "外径").GetProperty("status").GetString());
    Equal("no_match", Q("7", "经济加工方法", standard: "1998").GetProperty("status").GetString());
    Equal(1, Q("7", size: "25", standard: "1998").GetProperty("data").EnumerateObject().Count());
    True(!Q("7", standard: "手册").GetProperty("data").TryGetProperty("公差", out _), "来源过滤混入数值标准");
    Near(21, Q("7", size: "25").GetProperty("data").GetProperty("公差").GetProperty("value").GetDouble(), 0, "完整卡保留数值");
    var full = Q("7", size: "25", limit: 1).GetProperty("data");
    Equal(8, full.EnumerateObject().Count());
    True(full.GetProperty("可达加工方法").GetArrayLength() > 1, "完整卡被分页截断");
    Equal(20, Q("*", "选用举例", limit: 1).GetProperty("total").GetInt32());
    Equal("IT0", Q("*", "选用举例", offset: 1, limit: 1).GetProperty("rows")[0].GetProperty("spec").GetProperty("等级").GetString());
    Equal(22, Q("7", size: "*").GetProperty("total").GetInt32());
    True(Q("7", size: "*").GetProperty("rows").EnumerateArray().All(r => !r.GetProperty("data").TryGetProperty("应用范围", out _)), "尺寸展开重复非尺寸资料");
    foreach (var action in new Action[] { () => Q("7", "*", size: "*"), () => Q("*", process: "*"), () => Q("7", feature: "随便"), () => Q("7", standard: "*") })
    {
        var rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
        True(rejected, "IT卡非法过滤未拒绝");
    }
    var registry = Registry(); registry.TryGet("norma.basic.tolerance", out var descriptor);
    var result = descriptor!.Handler(new CommandContext(descriptor, new Dictionary<string, string> { ["grade"] = "IT7", ["property"] = "可达加工方法", ["process"] = "铰孔" }, "test", null, CancellationToken.None)).Result;
    True(result.Success, result.Message);
    Equal(1, JsonSerializer.SerializeToElement(result.Data).GetProperty("data").GetProperty("可达加工方法").GetArrayLength());
}

static void TestHoleLimitDeviations()
{
    static JsonElement Card(string band, string size, string? standard = null, string? property = null)
        => JsonSerializer.SerializeToElement(BasicStandards.QueryBand(band, size, property, standard));
    static JsonElement Fact(string band, string size, string property = "上偏差") => Card(band, size).GetProperty("data").GetProperty(property);
    static void V(double expected, JsonElement fact) => Near(expected, fact.GetProperty("value").GetDouble(), 1e-10, fact.GetRawText());
    Equal(15, BandLimits.HolePages.Count());
    // Every projected cell traces to an original DOM cell. H/JS Word tables have differing
    // header/data colspans: their 18 unique cells must follow the visual grade order.
    foreach (var page in BandLimits.HolePages)
    {
        foreach (var group in page.Records.GroupBy(r => r.Row))
        {
            var original = page.Table[group.Key].Skip(2).DistinctBy(c => string.Join(",", c.Origin)).ToArray();
            Equal(original.Length, group.Count());
            foreach (var (entry, i) in group.Select((r, i) => (r, i)))
            {
                Equal(original[i].Text, entry.Raw);
                Equal(original[i].Origin[1], entry.Column);
                var gradeCells = page.Table[1].Where(c => c.Origin[1] >= 2).DistinctBy(c => string.Join(",", c.Origin)).ToArray();
                Equal(int.Parse(gradeCells[i].Text), entry.Grade);
                var projected = JsonSerializer.SerializeToElement(BandLimits.Resolve(entry.Code, entry.Grade, entry.Lower, entry.Upper));
                var fact = projected.GetProperty("上偏差");
                // J9 deliberately follows the original explicit JS cross-reference.
                if (entry.Code == "J" && entry.Grade >= 9) continue;
                Equal(entry.Raw, fact.GetProperty("raw").GetString());
                Equal(entry.Unit, fact.GetProperty("unit").GetString());
                Equal(page.Id, fact.GetProperty("source").GetProperty("id").GetString());
                Equal(entry.Row, fact.GetProperty("source").GetProperty("row").GetInt32());
                if (entry.Raw.Length == 0) True(fact.GetProperty("value").ValueKind == JsonValueKind.Null, "原表空格不得补值");
            }
        }
    }
    V(21, Fact("H7", "25")); V(0, Fact("H7", "25", "下偏差"));
    V(6, Fact("K7", "25")); V(-15, Fact("K7", "25", "下偏差"));
    V(-31, Fact("S6", "25")); V(-44, Fact("S6", "25", "下偏差"));
    V(-9, Fact("M6", "315")); V(-41, Fact("M6", "315", "下偏差"));
    V(-10, Fact("M6", "315.0001"));
    V(0.25, Fact("H14", "3")); V(0.4, Fact("H15", "3")); V(0.6, Fact("H16", "3"));
    V(0.125, Fact("JS14", "3")); V(0.2, Fact("JS15", "3")); V(0.3, Fact("JS16", "3"));
    V(0.84, Fact("H15", "25")); Equal("mm", Fact("H15", "25").GetProperty("unit").GetString());
    V(1.8, Fact("H17", "18")); V(-28, Fact("R10", "25"));

    V(45, Fact("JS11", "10"));

    foreach (var (band, size) in new[] { ("H14", "1"), ("N9", "1"),
        ("A9", "1"), ("H17", "3"), ("T6", "24"), ("V6", "14"), ("Y6", "18"), ("K9", "25"), ("A9", "501") })
    {
        var fact = Fact(band, size);
        True(fact.GetProperty("value").ValueKind == JsonValueKind.Null && fact.GetProperty("reason").GetString()!.Length > 0,
            "异常/空白/不适用组合不得自动补值：" + band + "/" + size);
    }
    V(0.25, Fact("H14", "1.0001")); V(-4, Fact("N9", "1.0001"));
    V(-37, Fact("T6", "24.0001")); V(-36, Fact("V6", "14.0001")); V(-59, Fact("Y6", "18.0001"));
    V(210, Fact("H7", "3150")); V(63, Fact("H7", "500")); V(70, Fact("H7", "500.0001"));
    True(Fact("H1", "501").GetProperty("notes").EnumerateArray().Any(n => n.GetString()!.Contains("试用")), "试用注丢失");
    V(26, Fact("J9", "25"));
    Equal(1, Fact("J9", "25").GetProperty("dependencies").GetArrayLength());
    Equal("GB/T18004—1999", Fact("P7", "25").GetProperty("source").GetProperty("standard").GetString());
    var newer = Card("H7", "25", "1999").GetProperty("data");
    Equal(3, newer.EnumerateObject().Count()); V(21, newer.GetProperty("上偏差"));
    var older = Card("H7", "25", "1998").GetProperty("data");
    Equal(2, older.EnumerateObject().Count()); True(!older.TryGetProperty("上偏差", out _), "指定版本不得混入1999值");
    V(10, Card("JS7", "25", "1998").GetProperty("data").GetProperty("上偏差"));
    V(-7, Card("g6", "25", "1999").GetProperty("data").GetProperty("上偏差"));
    Equal(1, Card("H7", "25", property: "上偏差").GetProperty("data").EnumerateObject().Count());
}

static void TestShaftLimitDeviations()
{
    static JsonElement Card(string band, string size, string? standard = null, string? property = null, int offset = 0, int limit = 20)
        => JsonSerializer.SerializeToElement(BasicStandards.QueryBand(band, size, property, standard, offset, limit));
    static JsonElement Fact(string band, string size, string property = "上偏差") => Card(band, size).GetProperty("data").GetProperty(property);
    static void V(double expected, JsonElement fact) => Near(expected, fact.GetProperty("value").GetDouble(), 1e-10, fact.GetRawText());
    Equal(16, BandLimits.ShaftPages.Count());
    Equal(4907, BandLimits.ShaftPages.Sum(p => p.Records.Length));
    var pageCounts = new[] { 368, 315, 294, 336, 168, 378, 378, 315, 294, 168, 304, 304, 360, 375, 300, 250 };
    foreach (var (page, i) in BandLimits.ShaftPages.Select((p, i) => (p, i)))
    {
        Equal(pageCounts[i], page.Records.Length);
        Equal($"13-02:{20 + i}", page.Id);
        Equal("GB/T 1800.4—1999", page.Standard);
        foreach (var r in page.Records)
        {
            Equal(page.Table[r.Row][r.Column].Text, r.Raw);
            Equal(int.Parse(page.Table[1][r.Column].Text), r.Grade);
            Equal(page.Id == "13-02:32" && r.Column <= 5 ? "r" : r.Code, page.Table[0][r.Column].Text);
            var pair = JsonSerializer.SerializeToElement(BandLimits.Resolve(r.Code, r.Grade, r.Lower, r.Upper));
            foreach (var key in new[] { "上偏差", "下偏差" })
            {
                var fact = pair.GetProperty(key);
                Equal(r.Raw, fact.GetProperty("raw").GetString());
                Equal(r.Unit, fact.GetProperty("unit").GetString());
                Equal(page.Id, fact.GetProperty("source").GetProperty("id").GetString());
                Equal(r.Row, fact.GetProperty("source").GetProperty("row").GetInt32());
                Equal(r.Column, fact.GetProperty("source").GetProperty("column").GetInt32());
                Equal(key == "上偏差" ? "es" : "ei", fact.GetProperty("symbol").GetString());
                if (r.Raw.Length == 0) True(fact.GetProperty("value").ValueKind == JsonValueKind.Null, "原表空格不补值");
                if (page.SourceKind == "image-transcription")
                {
                    Equal("image-transcription", fact.GetProperty("source").GetProperty("kind").GetString());
                    Equal(r.Image, fact.GetProperty("source").GetProperty("image").GetString());
                    True(page.ImageReferences!.Contains(r.Image), "图片转录必须保留原图片引用");
                }
            }
        }
    }
    // Independent original-page references, including thousands, decimals and signs.
    V(-7, Fact("g6", "25")); V(-20, Fact("g6", "25", "下偏差"));
    V(0, Fact("h7", "100")); V(-35, Fact("h7", "100", "下偏差"));
    V(17, Fact("js7", "100")); V(-17, Fact("js7", "100", "下偏差"));
    Equal("13-02:26", Fact("js7", "100").GetProperty("source").GetProperty("id").GetString());
    V(35, Fact("js10", "18")); V(45, Fact("js11", "10")); V(.405, Fact("js13", "315"));
    V(-1.8, Fact("h17", "18", "下偏差")); V(-.81, Fact("h13", "315", "下偏差"));
    V(-185, Fact("h10", "250", "下偏差")); V(-33, Fact("h18", "3150", "下偏差"));
    Equal("mm", Fact("h18", "3150").GetProperty("unit").GetString());
    V(-1650, Fact("a9", "500")); V(-1805, Fact("a9", "500", "下偏差"));
    V(1535, Fact("s6", "3150")); V(1400, Fact("s6", "3150", "下偏差"));
    V(15, Fact("k6", "25")); V(2, Fact("k6", "25", "下偏差"));
    V(21, Fact("m6", "25")); V(8, Fact("m6", "25", "下偏差"));
    V(28, Fact("n6", "25")); V(15, Fact("n6", "25", "下偏差"));
    V(35, Fact("p6", "25")); V(22, Fact("p6", "25", "下偏差"));
    V(64, Fact("t6", "30.0001")); // next band is >30~40, not a substitute u band
    V(54, Fact("t6", "24.0001")); V(41, Fact("t6", "24.0001", "下偏差"));
    V(50, Fact("v6", "14.0001")); V(76, Fact("y6", "18.0001"));
    V(-6, Fact("g3", "18")); V(-9, Fact("g3", "18", "下偏差"));
    V(9, Fact("p4", "3")); V(6, Fact("p4", "3", "下偏差"));
    Equal("-6-9", Fact("g3", "18").GetProperty("raw").GetString());
    foreach (var (band, size) in new[] { ("a9", "1"), ("b8", "1"), ("h14", "1"), ("js18", "1"),
        ("h17", "3"), ("js17", "3"), ("t6", "24"), ("v6", "14"), ("y6", "18"),
        ("cd6", "25"), ("ef6", "25"), ("fg6", "25"), ("a9", "501"), ("j9", "25"), ("u10", "25") })
    {
        var fact = Fact(band, size);
        True(fact.GetProperty("value").ValueKind == JsonValueKind.Null && fact.GetProperty("reason").GetString()!.Length > 0,
            "原表空白/异常/未列不能补值：" + band + "/" + size);
    }
    V(42, Fact("m9", "10")); V(6, Fact("m9", "10", "下偏差")); V(125, Fact("k7", "1600"));
    V(2.5, Fact("k3", "10"));

    V(1100, Fact("z7", "450", "下偏差"));

    V(-270, Fact("a9", "1.0001")); V(-.25, Fact("h14", "1.0001", "下偏差"));
    V(-70, Fact("h7", "500.0001", "下偏差"));
    True(Fact("h1", "501").GetProperty("notes").EnumerateArray().Any(n => n.GetString()!.Contains("试用")), "试用注丢失");
    var modern = Card("g6", "25", "1999").GetProperty("data");
    Equal(3, modern.EnumerateObject().Count()); V(-7, modern.GetProperty("上偏差"));
    var old = Card("g6", "25", "1998").GetProperty("data");
    Equal(2, old.EnumerateObject().Count()); True(!old.TryGetProperty("上偏差", out _), "版本过滤不能混入1999事实");
    Equal("13-02:2", Card("js7", "100", "1998").GetProperty("data").GetProperty("上偏差").GetProperty("source").GetProperty("id").GetString());
    Equal(7, Card("g6", "25", offset: 100, limit: 1).GetProperty("data").EnumerateObject().Count());
    Equal(1, Card("g6", "25", property: "下偏差").GetProperty("data").EnumerateObject().Count());
    Equal("no_match", Card("g6", "3150.0001").GetProperty("status").GetString());
    var expanded = Card("h*", "25", limit: 100);
    Equal(18, expanded.GetProperty("total").GetInt32());
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

static void TestBandUsage()
{
    JsonElement Q(string? band = null, string? size = "25", string? usage = null, string? standard = null, string? property = "常用级别", int offset = 0, int limit = 100)
        => JsonSerializer.SerializeToElement(BasicStandards.QueryBand(band, size, property, standard, offset, limit, usage));
    int Rank(string band, string size = "25") => Q(band, size).GetProperty("data").GetProperty("常用级别").GetProperty("value").GetInt32();
    Equal("Js7", Q("JS7").GetProperty("data").GetProperty("常用级别").GetProperty("raw").GetString());
    Equal(2, BandUsage.Pages.Count);
    foreach (var p in BandUsage.Pages)
    {
        var size = p.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var e in p.Entries)
        {
            Equal(e.Level, Rank(e.Band, size));
            var source = Q(e.Band, size).GetProperty("data").GetProperty("常用级别").GetProperty("source");
            Equal(p.Id, source.GetProperty("id").GetString());
            Equal(p.ByteOffset, source.GetProperty("byteOffset").GetInt64());
            Equal(e.Raw, Q(e.Band, size).GetProperty("data").GetProperty("常用级别").GetProperty("raw").GetString());
        }
    }
    var small = BandUsage.Pages[0];
    Equal(105, small.Entries.Count(e => e.Kind == "孔")); Equal(116, small.Entries.Count(e => e.Kind == "轴"));
    Equal(44, small.Entries.Count(e => e.Kind == "孔" && e.Level >= 3)); Equal(59, small.Entries.Count(e => e.Kind == "轴" && e.Level >= 3));
    Equal(13, small.Entries.Count(e => e.Kind == "孔" && e.Level == 4)); Equal(13, small.Entries.Count(e => e.Kind == "轴" && e.Level == 4));
    Equal(31, BandUsage.Pages[1].Entries.Count(e => e.Kind == "孔")); Equal(41, BandUsage.Pages[1].Entries.Count(e => e.Kind == "轴"));
    True(BandUsage.Pages[1].Entries.All(e => e.Level == 3), "大尺寸图全在虚线框内且无圈选");
    Equal(4, Rank("H7", "500")); Equal(3, Rank("H7", "500.0001")); Equal(3, Rank("H7", "3150"));
    Equal(2, Rank("H1")); Equal(1, Rank("H1", "501")); Equal(1, Rank("H14"));
    Equal(2, Rank("g8")); Equal(2, Rank("G8")); Equal(3, Rank("y6")); Equal(2, Rank("y7")); Equal(2, Rank("y8"));
    Equal(3, Rank("B12")); Equal(3, Rank("b12")); Equal(4, Rank("g6")); Equal(3, Rank("G6"));
    Equal("no_match", Q("H7", "3150.001").GetProperty("status").GetString());
    Equal("no_match", Q("Js7").GetProperty("status").GetString());
    Equal("no_match", Q("H7", usage: "3").GetProperty("status").GetString());
    Equal("no_match", Q("H7", usage: "4", standard: "1998").GetProperty("status").GetString());
    Equal("4", Q("H7", usage: "4", standard: "1998").GetProperty("usage").GetString());
    Equal("no_match", Q("*", "501", "4").GetProperty("status").GetString());
    Equal(1, Q("H7", standard: "1801", property: null).GetProperty("data").EnumerateObject().Count());
    True(!Q("H7", standard: "1998", property: null).GetProperty("data").TryGetProperty("常用级别", out _), "1998不应混入选用级别");
    var restricted = Q("H14", "0.5", property: null).GetProperty("data");
    Equal(1, restricted.GetProperty("常用级别").GetProperty("value").GetInt32());
    True(restricted.GetProperty("公差").GetProperty("value").ValueKind == JsonValueKind.Null, "未列入不得补数值");
    var missing = Q(usage: "4"); Equal("needs_input", missing.GetProperty("status").GetString());
    Equal(26, missing.GetProperty("missing")[0].GetProperty("total").GetInt32());
    var both = Q(size: null, usage: "4"); Equal(2, both.GetProperty("missing").GetArrayLength());
    var all = Q("*"); var sum = 0;
    foreach (var level in new[] { "1", "2", "3", "4" })
    {
        var table = Q("*", usage: level); sum += table.GetProperty("total").GetInt32();
        foreach (var row in table.GetProperty("rows").EnumerateArray())
            Equal(int.Parse(level), row.GetProperty("data").GetProperty("常用级别").GetProperty("value").GetInt32());
    }
    Equal(all.GetProperty("total").GetInt32(), sum);
    var preferred = Q("*", usage: "4", offset: 2, limit: 3);
    Equal(26, preferred.GetProperty("total").GetInt32()); Equal(3, preferred.GetProperty("rows").GetArrayLength()); Equal(5, preferred.GetProperty("nextOffset").GetInt32());
    Equal(5, Q("H7", property: null, offset: 100, limit: 1).GetProperty("data").EnumerateObject().Count());
    foreach (var invalid in new[] { "0", "5", "IT4", "*", "3,4" })
    {
        var rejected = false; try { Q("H7", usage: invalid); } catch (ArgumentException) { rejected = true; }
        True(rejected, "usage必须严格接受1～4：" + invalid);
    }
}

static void TestFitStandards()
{
    JsonElement Q(string? fit = "H7/g6", string? size = "25", string? property = null, string? usage = null, string? standard = null, int offset = 0, int limit = 100)
        => JsonSerializer.SerializeToElement(FitStandards.Query(fit, size, property, usage, standard, offset, limit));
    JsonElement F(string fit, string size, string property) => Q(fit, size).GetProperty("data").GetProperty(property);
    void V(string fit, string size, double high, double low)
    {
        Near(high, F(fit, size, "最大间隙或最小过盈").GetProperty("value").GetDouble(), 0, fit + "上极限");
        Near(low, F(fit, size, "最小间隙或最大过盈").GetProperty("value").GetDouble(), 0, fit + "下极限");
    }
    Equal(3, FitStandards.Pages.Count); Equal(110, FitStandards.Selections.Count); Equal(2650, FitStandards.Limits.Count);
    Equal(102, FitStandards.Selections.Select(s => s.Fit).Distinct().Count());
    Equal(63, FitStandards.Selections.Count(s => s.Page == "13-02:38")); Equal(47, FitStandards.Selections.Count(s => s.Page == "13-02:39"));
    Equal(13, FitStandards.Selections.Count(s => s.Page == "13-02:38" && s.Usage == 3));
    Equal(13, FitStandards.Selections.Count(s => s.Page == "13-02:39" && s.Usage == 3));
    foreach (var l in FitStandards.Limits)
    {
        var p = FitStandards.Pages.Single(p => p.Id == l.Page); var cell = p.Tables[l.Table][l.Row][l.Column];
        Equal(l.Raw, cell.Raw); True(cell.Rowspan >= 1 && cell.Colspan >= 1, "原合并格必须保留");
        var parsed = FitStandards.Parse(l);
        True(parsed.Reason is not null || parsed.High >= parsed.Low, "不允许倒置极限伪装有效值");
    }
    V("H7/g6", "25", 41, 7); V("H7/k6", "25", 19, -15); V("H7/s6", "25", -14, -48);
    V("H6/js5", "100", 29.5, -7.5); V("JS6/h5", "100", 26, -11);
    V("H8/r7", "100", 3, -86); V("H7/p6", "3", 4, -12); V("P7/h6", "3", 0, -16);
    V("H7/g6", "3", 18, 2); V("H7/g6", "3.0001", 24, 4);
    V("H7/g6", "500", 123, 20);
    Equal("过渡配合", F("H7/p6", "3", "配合类型").GetProperty("value").GetString());
    Equal("过盈配合", F("H7/p6", "3.0001", "配合类型").GetProperty("value").GetString());
    foreach (var (key, size) in new[] { ("H7/t6", "10"), ("H8/v7", "25") })
    {
        var fact = F(key, size, "最大间隙或最小过盈");
        True(fact.GetProperty("value").ValueKind == JsonValueKind.Null && fact.GetProperty("reason").GetString()!.Length > 0, "空白/错列/倒置不应反算：" + key);
    }
    V("D10/h10", "25", 233, 65); V("D11/h11", "25", 325, 65);
    V("H8/js7", "350", 117, -28); V("H8/js7", "400", 117, -28);
    V("K7/h6", "3", 6, -10); V("H6/p5", "3", 0, -10);
    foreach (var key in new[] { "D10/h10", "D11/h11", "K7/h6", "H6/p5" })
    foreach (var entry in FitStandards.Limits.Where(l => l.Fit == key))
    {
        var parts = key.Split('/');
        JsonElement Band(string code) => JsonSerializer.SerializeToElement(BasicStandards.QueryBand(code,
            entry.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture), standard: "1999")).GetProperty("data");
        var hole = Band(parts[0]); var shaft = Band(parts[1]);
        double N(JsonElement card, string property) => card.GetProperty(property).GetProperty("value").GetDouble()
            * (card.GetProperty(property).GetProperty("unit").GetString() == "mm" ? 1000 : 1);
        V(key, entry.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture),
            N(hole, "上偏差") - N(shaft, "下偏差"), N(hole, "下偏差") - N(shaft, "上偏差"));
    }
    True(!F("D11/h11", "25", "最大间隙或最小过盈").GetProperty("source").TryGetProperty("alternatives", out _), "只返回唯一权威值");
    Equal(3, F("H7/g6", "25", "选用级别").GetProperty("value").GetInt32());
    Equal(2, F("H7/f6", "25", "选用级别").GetProperty("value").GetInt32());
    Equal(1, F("A1/a1", "25", "选用级别").GetProperty("value").GetInt32());
    Equal(3, F("H9/d9", "25", "选用级别").GetProperty("value").GetInt32());
    foreach (var key in FitStandards.Selections.Select(s => s.Fit).Distinct())
        Equal(5 + FitApplications.Resolve(key).Count + (FitStatistics.Has(key) ? 2 : 0), Q(key, offset: 999, limit: 1).GetProperty("data").EnumerateObject().Count());
    var missing = Q(null, null); Equal("needs_input", missing.GetProperty("status").GetString()); Equal(2, missing.GetProperty("missing").GetArrayLength());
    Equal(102, Q(null).GetProperty("missing")[0].GetProperty("total").GetInt32());
    Equal(22, Q(null, usage: "3").GetProperty("missing")[0].GetProperty("total").GetInt32());
    Equal(102, Q("*").GetProperty("total").GetInt32());
    Equal(22, Q("*", usage: "3").GetProperty("total").GetInt32());
    Equal(80, Q("*", usage: "2").GetProperty("total").GetInt32());
    Equal("no_match", Q("*", usage: "1").GetProperty("status").GetString());
    Equal("no_match", Q(usage: "2").GetProperty("status").GetString());
    Equal("no_match", Q(size: "500.001").GetProperty("status").GetString());
    Equal("no_match", Q(standard: "1998").GetProperty("status").GetString());
    Equal(5, Q(standard: "1801").GetProperty("data").EnumerateObject().Count());
    var page = Q("H7/*", offset: 1, limit: 2); Equal(16, page.GetProperty("total").GetInt32()); Equal(2, page.GetProperty("rows").GetArrayLength()); Equal(3, page.GetProperty("nextOffset").GetInt32());
    True(Q("*/h6").GetProperty("total").GetInt32() > 1, "固定轴枚举失败");
    Equal(25, Q(size: "*").GetProperty("total").GetInt32());
    Equal(10, Q(property: "*").GetProperty("total").GetInt32());
    Equal(1, Q(property: "选用级别").GetProperty("data").EnumerateObject().Count());
    foreach (var key in new[] { "h7/g6", "H7/G6", "H7", "H/g6", "H7/g6/h6", "H7/**", "H7/g0", "H7/g19", "Js7/h6", "*/*" })
    {
        var rejected = false; try { Q(key); } catch (ArgumentException) { rejected = true; } True(rejected, "非法主键未拒绝：" + key);
    }
    foreach (var size in new[] { "0", "-1", "NaN", "Infinity", "2*" })
    {
        var rejected = false; try { Q(size: size); } catch (ArgumentException) { rejected = true; } True(rejected, "非法尺寸未拒绝");
    }
    foreach (var usage in new[] { "4", "*", "IT3", "2,3" })
    {
        var rejected = false; try { Q(usage: usage); } catch (ArgumentException) { rejected = true; } True(rejected, "非法选用级别未拒绝");
    }
    var multiRejected = false; try { Q("*", "*"); } catch (ArgumentException) { multiRejected = true; } True(multiRejected, "多变化轴须拒绝");
    var registry = Registry(); registry.TryGet("norma.basic.fit", out var descriptor);
    Equal("fit,size,property,usage,standard,offset,limit", string.Join(",", descriptor!.Parameters.Select(p => p.Name)));
    True(descriptor.Readonly && descriptor.Parameters.All(p => !p.Required), "配合卡契约错误");
    Equal("基础标准", NormaCommands.PageRows().Single(r => r["command"] == "norma.basic.fit")["topic"]);
}

static void TestGeneralTolerances()
{
    JsonElement Q(string? size = "25", string? grade = "m") => JsonSerializer.SerializeToElement(GeneralTolerances.Query(size, grade));
    JsonElement Fact(string size, string grade, string property) => Q(size, grade).GetProperty("data").GetProperty(property);
    double V(string size, string grade, string property) => Fact(size, grade, property).GetProperty("value").GetDouble();
    var properties = new[] { "线性尺寸", "倒圆半径和倒角高度", "角度尺寸" };
    Equal(3, GeneralTolerances.Pages.Count);
    Equal(68, GeneralTolerances.Pages.Sum(p => p.Entries.Length));
    double?[][][] expected =
    [
        [[.05,.05,.1,.15,.2,.3,.5,null], [.1,.1,.2,.3,.5,.8,1.2,2], [.2,.3,.5,.8,1.2,2,3,4], [null,.5,1,1.5,2.5,4,6,8]],
        [[.2,.5,1,2], [.2,.5,1,2], [.4,1,2,4], [.4,1,2,4]],
        [[1,.5,1d/3,1d/6,1d/12], [1,.5,1d/3,1d/6,1d/12], [1.5,1,.5,.25,1d/6], [3,2,1,.5,1d/3]]
    ];
    foreach (var (page, pi) in GeneralTolerances.Pages.Select((p, i) => (p, i)))
    {
        Equal(new[] { 4485338L,4496323L,4501455L }[pi], page.ByteOffset);
        foreach (var (g, gi) in "fmcv".Select((g,i) => (g.ToString(),i)))
        foreach (var (e, ei) in page.Entries.Where(e => e.Grade == g).Select((e,i) => (e,i)))
        {
            Equal(e.Raw, page.Table[e.Row][e.Column].Raw);
            var sample = e.Upper ?? e.Lower + 1;
            var size = sample.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var f = Fact(size, g, properties[pi]);
            var known = expected[pi][gi][ei];
            if (known is { } value)
            {
                Near(value, f.GetProperty("value").GetDouble(), 1e-12, "独立原表参考值");
                Near(-value, f.GetProperty("lowerDeviation").GetDouble(), 1e-12, "负下偏差");
                Near(value, f.GetProperty("upperDeviation").GetDouble(), 1e-12, "正上偏差");
            }
            else Equal(JsonValueKind.Null, f.GetProperty("value").ValueKind);
            Equal(page.Id, f.GetProperty("source").GetProperty("id").GetString());
            Equal(pi == 2 ? "°" : "mm", f.GetProperty("unit").GetString());
            if (e.Lower > 0)
            {
                var below = (e.Lower + 1e-6).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Equal(e.Raw, Fact(below, g, properties[pi]).GetProperty("raw").GetString());
            }
        }
    }
    Near(.2, V("25", "m", properties[0]), 0, "线性25m");
    Near(1, V("25", "m", properties[1]), 0, "倒角25m");
    Near(.5, V("25", "m", properties[2]), 0, "短边25m");
    Equal("±30′", Fact("25","m",properties[2]).GetProperty("raw").GetString());
    Near(.05, V("0.5", "f", properties[0]), 0, "首段含0.5");
    Equal(JsonValueKind.Null, Fact("0.4999","f",properties[0]).GetProperty("value").ValueKind);
    Near(1, V("0.1", "m", properties[2]), 0, "短边小于0.5仍有角度项");
    Near(.1, V("3", "m", properties[0]), 0, "段上界");
    Near(.3, V("3.0001", "c", properties[0]), 0, "后段下开");
    Near(2, V("4000", "m", properties[0]), 0, "线性4000含上界");
    Equal(JsonValueKind.Null, Fact("4000.01","m",properties[0]).GetProperty("value").ValueKind);
    Near(2, V("10000", "m", properties[1]), 0, "倒圆大于30无上限");
    Near(1d/12, V("10000", "m", properties[2]), 1e-12, "角度大于400无上限");
    Equal(2, Q(null,null).GetProperty("missing").GetArrayLength());
    Equal(JsonValueKind.Null, Q(null,null).GetProperty("default").ValueKind);
    Equal(1, Q(null).GetProperty("missing").GetArrayLength());
    Equal(1, Q(grade:null).GetProperty("missing").GetArrayLength());
    Equal(4, Q(grade:"*").GetProperty("total").GetInt32());
    Equal(12, Q("*").GetProperty("total").GetInt32());
    Equal(1, Q("*",null).GetProperty("missing").GetArrayLength());
    Equal(1, Q(null,"*").GetProperty("missing").GetArrayLength());
    foreach (var g in "fmcv")
    foreach (var row in Q("*",g.ToString()).GetProperty("rows").EnumerateArray())
    {
        var coordinate = row.GetProperty("size");
        var lo = coordinate.GetProperty("lower").GetDouble();
        var hi = coordinate.GetProperty("upper");
        var sample = lo == 0 ? .25 : hi.ValueKind == JsonValueKind.Null ? lo+1 : (lo+hi.GetDouble())/2;
        Equal(Q(sample.ToString(System.Globalization.CultureInfo.InvariantCulture),g.ToString()).GetProperty("data").GetRawText(), row.GetProperty("data").GetRawText());
    }
    foreach (var size in new[] { "0", "-1", "NaN", "Infinity", "3mm", "**" }) Throws(() => Q(size));
    foreach (var grade in new[] { "M", "IT7", "精密", "fm", "**" }) Throws(() => Q(grade:grade));
    Throws(() => Q("*","*"));
    var registry = Registry(); True(registry.TryGet("norma.basic.general", out var descriptor), "独立工具注册");
    Equal("size grade property", string.Join(" ", descriptor!.Parameters.Select(p => p.Name)));
    void Throws(Action action)
    {
        var rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
        True(rejected, "必须拒绝非法输入");
    }
    True(descriptor.Readonly && descriptor.Parameters.All(p => !p.Required), "两坐标只读缺参契约");
    JsonElement Filter(string? property, string? size = "25", string? grade = "m")
        => JsonSerializer.SerializeToElement(GeneralTolerances.Query(size, grade, property));
    foreach (var property in properties)
    {
        var selected = Filter(property).GetProperty("data");
        Equal(1, selected.EnumerateObject().Count());
        Equal(Q().GetProperty("data").GetProperty(property).GetRawText(), selected.GetProperty(property).GetRawText());
        foreach (var row in Filter(property, "*").GetProperty("rows").EnumerateArray())
            Equal(1, row.GetProperty("data").EnumerateObject().Count());
        foreach (var row in Filter(property, grade:"*").GetProperty("rows").EnumerateArray())
            Equal(1, row.GetProperty("data").EnumerateObject().Count());
    }
    Equal(Q().GetRawText(), Filter("  ").GetRawText());
    Equal(3, Filter("*").GetProperty("total").GetInt32());
    Equal(2, Filter("角度尺寸",null,null).GetProperty("missing").GetArrayLength());
    Equal(2, Filter("*",null,null).GetProperty("missing").GetArrayLength());
    Throws(() => Filter("表49")); Throws(() => Filter("*","*")); Throws(() => Filter("*",grade:"*"));

    var args = new Dictionary<string,string> { ["size"] = "25", ["grade"] = "m" };
    var result = descriptor.Handler(new CommandContext(descriptor, args, "test", null, CancellationToken.None)).Result;
    True(result.Success, result.Message);
    True(result.Data is JsonElement, "注册结果必须使用JsonElement序列化");
    Equal(3, ((JsonElement)result.Data!).GetProperty("data").EnumerateObject().Count());
    Equal("基础标准", NormaCommands.PageRows().Single(r => r["command"] == "norma.basic.general")["topic"]);
}

static void TestBandApplications()
{
    JsonElement Q(string? band = "g6", string? size = null, string? property = "基本偏差应用", string? standard = null,
        string? usage = null, int offset = 0, int limit = 100)
        => JsonSerializer.SerializeToElement(BasicStandards.QueryBand(band, size, property, standard, offset, limit, usage));
    var source = BandApplications.Source;
    Equal(21, source.Entries.Length); Equal(12, source.Rows.Length);
    Equal("a b c d e f g h js k m n p r s t u v x y z", string.Join(" ", source.Entries.Select(e => e.Code).OrderBy(c => c, StringComparer.Ordinal)));
    foreach (var entry in source.Entries)
    {
        Equal(entry.Text, source.Rows[entry.Row][entry.Column].GetProperty("raw").GetString());
        True(source.Rows[entry.KeyRow][entry.KeyColumn].GetProperty("raw").GetString()!.Split('、').Contains(entry.Code), "键必须来自原格");
        var fact = Q(entry.Code + "6").GetProperty("data").GetProperty("基本偏差应用");
        Equal(entry.Text, fact.GetProperty("value").GetString());
        Equal(entry.Code, fact.GetProperty("code").GetString());
        Equal("13-02:46", fact.GetProperty("source").GetProperty("id").GetString());
    }
    Equal(378, Q("*").GetProperty("total").GetInt32());
    Equal(21, Q("*6").GetProperty("total").GetInt32());
    Equal(18, Q("g*").GetProperty("total").GetInt32());
    Equal(18, Q("g").GetProperty("missing")[0].GetProperty("total").GetInt32());
    Equal(378, Q(null).GetProperty("missing")[0].GetProperty("total").GetInt32());
    foreach (var band in new[] { "G6", "H7", "j6", "cd6", "za6" })
        Equal("no_match", Q(band).GetProperty("status").GetString());
    Equal("no_match", Q(size: "*").GetProperty("status").GetString());
    Equal("no_match", Q(standard: "1998").GetProperty("status").GetString());
    Equal(1, Q(standard: "手册", property: null).GetProperty("data").EnumerateObject().Count());
    Equal(Q().GetProperty("data").GetRawText(), Q("g18", "9999").GetProperty("data").GetRawText());
    True(Q().GetProperty("data").GetProperty("基本偏差应用").GetProperty("value").GetString()!.Contains("IT5～IT7"), "推荐范围保留");
    Equal("no_match", Q("g18", usage: "4").GetProperty("status").GetString());
    Equal("size", Q(usage: "4").GetProperty("missing")[0].GetProperty("missing").GetString());
    Equal(2, Q(null, usage: "4").GetProperty("missing").GetArrayLength());
    Equal("no_match", Q(size: "3151", usage: "4").GetProperty("status").GetString());
    var selected = Q("*", "25", usage: "4");
    foreach (var row in selected.GetProperty("rows").EnumerateArray())
        Equal(4, BandUsage.Level(row.GetProperty("spec").GetProperty("公差带").GetString()!, 25));
    Equal(2, Q("*", offset: 1, limit: 2).GetProperty("rows").GetArrayLength());
    Equal(3, Q("*", offset: 1, limit: 2).GetProperty("nextOffset").GetInt32());
    Equal(1, Q(offset: 999, limit: 1).GetProperty("data").EnumerateObject().Count());
    var full = Q(size: "25", property: null).GetProperty("data");
    Equal(7, full.EnumerateObject().Count());
    Near(-7, full.GetProperty("上偏差").GetProperty("value").GetDouble(), 0, "应用不能改动数值");
    foreach (var row in Q(size: "*", property: null).GetProperty("rows").EnumerateArray())
        True(!row.GetProperty("data").TryGetProperty("基本偏差应用", out _), "尺寸展开不能重复应用");
    True(source.Entries.Single(e => e.Code == "n").Text.Contains("≤3mm"), "n配合尺寸条件");
    True(source.Entries.Single(e => e.Code == "p").Text.Contains("H7/p6"), "p配合完整条件");
    True(!source.Entries.Single(e => e.Code == "js").Text.Contains("±IT/2"), "js说明不能覆盖奇数公差规则");
}

// REQ-BASIC-025/026: independent source examples and sign/endpoint invariants.
static void TestChapterClosure()
{
    var assembly = typeof(Catalog).Assembly;
    using var stream = assembly.GetManifestResourceStream("Norma.Formulas.limits-fits-audit.json")!;
    using var audit = JsonDocument.Parse(stream);
    var pages = audit.RootElement.GetProperty("pages"); Equal(62, pages.GetArrayLength());
    Equal(62, pages.EnumerateArray().Select(p => p.GetProperty("id").GetString()).Distinct().Count());
    True(pages.EnumerateArray().All(p => p.GetProperty("byteOffset").GetInt64() > 0 && p.GetProperty("targets").GetArrayLength() > 0), "每页明确来源与归属");
    foreach (var page in BandLimits.Pages)
    foreach (var entry in page.Records.Where(e => e.Raw.Length > 0))
    {
        var data = JsonSerializer.SerializeToElement(BandLimits.Resolve(entry.Code, entry.Grade, entry.Lower, entry.Upper));
        var hi = data.GetProperty("上偏差"); var lo = data.GetProperty("下偏差");
        if (hi.GetProperty("value").ValueKind != JsonValueKind.Number) continue;
        var unit = hi.GetProperty("unit").GetString() == "mm" ? 1000 : 1;
        var width = (hi.GetProperty("value").GetDouble() - lo.GetProperty("value").GetDouble()) * unit;
        var it = BasicStandards.Pages[2].Table.Skip(3).First(r => entry.Upper <= double.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture));
        var expected = double.Parse(it[entry.Grade+1].Replace(" ",""), System.Globalization.CultureInfo.InvariantCulture) * (entry.Grade <= 11 ? 1 : 1000);
        if (entry.Code is "js" or "JS" && entry.Grade is >= 7 and <= 11 && expected % 2 == 1 && width == expected-1) continue;
        Near(expected, width, 1e-8, page.Id + "/" + entry.Code + entry.Grade + "/" + entry.Upper);
    }
    JsonElement Q(string? band="g6",string? size="25",string? standard=null) => JsonSerializer.SerializeToElement(BasicStandards.QueryBand(band,size,ConeAxialDeviations.Property,standard,limit:100));
    double Value(string band,string size) => Q(band,size).GetProperty("data").GetProperty(ConeAxialDeviations.Property).GetProperty("value").GetDouble();
    Near(.07,Value("g6","25"),1e-12,"第58页g"); Near(-.02,Value("k6","25"),1e-12,"第58页k4～7");
    Near(0,Value("k8","25"),0,"k其他等级"); Near(.04,Value("j5","25"),0,"j5/6");
    Near(.08,Value("j7","25"),0,"j7"); Near(.105,Value("js7","25"),1e-12,"js本页Tze/2不减1");
    Near(-26,Value("zc8","500"),1e-12,"最大尺寸zc"); Near(2.7,Value("a7","1"),0,"本页首段包含1mm");
    Equal(2,Q(null,null).GetProperty("missing").GetArrayLength());
    Equal("no_match",Q("H7").GetProperty("status").GetString()); Equal("no_match",Q(size:"501").GetProperty("status").GetString());
    Equal("no_match",Q(standard:"1999").GetProperty("status").GetString());
    Near(.07,Q(standard:"手册").GetProperty("data").GetProperty(ConeAxialDeviations.Property).GetProperty("value").GetDouble(),0,"来源过滤");
    Equal(26,Q(size:"*").GetProperty("total").GetInt32());
    var axial = JsonSerializer.SerializeToElement(BasicStandards.QueryBand("g6","25")).GetProperty("data");
    True(axial.TryGetProperty(ConeAxialDeviations.Property,out _),"完整公差带卡纳入轴向属性");
    Equal(25,ConeAxialDeviations.Rows.Count);
    foreach (var row in ConeAxialDeviations.Rows)
    foreach (var band in new[] { "a7","g6","h7","j7","k6","m6","zc8" })
    {
        var match=System.Text.RegularExpressions.Regex.Match(band,@"^([a-z]+)(\d+)$");
        var fd=JsonSerializer.SerializeToElement(BasicStandards.QueryBand(band,row.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture),"基本偏差"));
        if (!fd.TryGetProperty("data",out var card) || !card.TryGetProperty("基本偏差",out var fact) || fact.GetProperty("value").ValueKind!=JsonValueKind.Number)continue;
        Near(-fact.GetProperty("value").GetDouble()/100,Value(band,row.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture)),1e-10,"固定锥度与独立径向表交叉验证");
    }
}

static void TestConeMethods()
{
    JsonElement Q(string? grade = "AT7", string? length = "50") => JsonSerializer.SerializeToElement(ConeAngleTolerances.Query(grade, length));
    Near(.07, Calc("cone.fundamental", "taper=0.1 deviation=-7")["axial"], 1e-12, "外锥g基本偏差反号");
    var g = Calc("cone.limits", "taper=0.1 upper=-7 lower=-20");
    Near(.2, g["upper"], 1e-12, "外锥轴向上偏差"); Near(.07, g["lower"], 1e-12, "外锥轴向下偏差");
    Near(.13, g["tolerance"], 1e-12, "公差宽度");
    foreach (var (upper, lower) in new[] { (21d,0d), (0d,-13d), (6.5,-6.5), (34d,21d), (0d,0d) })
    {
        var r = Catalog.Find("cone.limits")!.Run(new Dictionary<string,double> { ["taper"] = .1, ["upper"] = upper, ["lower"] = lower }, new Dictionary<string,string>());
        Near(-lower / 100, r["upper"], 1e-12, "H/h/js/过盈端点");
        Near(-upper / 100, r["lower"], 1e-12, "反号与上下交换");
        Near((upper-lower)/100, r["tolerance"], 1e-12, "Tz=IT/C");
    }
    void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("应拒绝输入"); }
    Reject(() => Calc("cone.limits", "taper=0.1 upper=-20 lower=-7"));
    Reject(() => Calc("cone.fundamental", "taper=0 deviation=7"));
    Equal(120, ConeAngleTolerances.Entries.Count);
    foreach (var e in ConeAngleTolerances.Entries)
    {
        var card = Q(e.Grade, e.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Near(e.Microradians, card.GetProperty("data").GetProperty("锥角公差").GetProperty("value").GetDouble(), 0, "逐格AT原值");
    }
    Near(.6,Q("AT2","7.3").GetProperty("data").GetProperty("直径公差").GetProperty("value").GetDouble(),0,"按最大值0.8的一位有效数字修约");
    var at = Q().GetProperty("data");
    Near(315, at.GetProperty("锥角公差").GetProperty("value").GetDouble(), 0, "原注AT7 L50");
    Near(15.8, at.GetProperty("直径公差").GetProperty("value").GetDouble(), 0, "原注三位有效数字");
    Near(20, Q(length:"63").GetProperty("data").GetProperty("直径公差").GetProperty("value").GetDouble(), 0, "原注L63");
    Near(800, Q(length:"6").GetProperty("data").GetProperty("锥角公差").GetProperty("value").GetDouble(), 0, "6包含");
    Near(800, Q(length:"10").GetProperty("data").GetProperty("锥角公差").GetProperty("value").GetDouble(), 0, "10上闭");
    Near(630, Q(length:"10.01").GetProperty("data").GetProperty("锥角公差").GetProperty("value").GetDouble(), 0, "10下开");
    foreach (var size in new[] { "5.999", "630.001" }) Equal("no_match", Q(length:size).GetProperty("status").GetString());
    Equal(12, Q("*", "50").GetProperty("total").GetInt32());
    var ranges = Q("AT8", "*"); Equal(10, ranges.GetProperty("total").GetInt32());
    Equal(">16.0～25.0", ranges.GetProperty("rows")[3].GetProperty("data").GetProperty("直径公差").GetProperty("rawRange").GetString()!.Replace("＞", ">"));
    True(ranges.GetProperty("rows")[0].GetProperty("data").GetProperty("直径公差").GetProperty("value").ValueKind == JsonValueKind.Null, "不取长度中点");
    Equal(2, Q(null,null).GetProperty("missing").GetArrayLength());
    foreach (var bad in new[] { "IT7", "at7", "AT13", "AT7*" }) Reject(() => Q(bad));
    Reject(() => Q("*", "*")); Reject(() => Q(length:"NaN"));
}

static void TestConicalReferences()
{
    JsonElement T(string? grade = "IT7", string? size = "25", string? property = null, string? standard = null, string? process = null, string? feature = null, int offset = 0, int limit = 100)
        => JsonSerializer.SerializeToElement(BasicStandards.QueryTolerance(size, grade, standard, offset, limit, property, process, feature));
    JsonElement A(string? designation = "C1:10", string? property = null, string? standard = null, string? series = null)
        => JsonSerializer.SerializeToElement(AngularSeries.Query(designation, property, series, standard, limit: 100));
    Equal(426, ConicalReferences.Facts.Count); Equal(260, ConicalReferences.Facts.Count(f => f.Property == "圆锥最大角误差"));
    Equal(130, ConicalReferences.Facts.Count(f => f.Property == "圆锥轴向公差")); Equal(36, ConicalReferences.Facts.Count(f => f.Property == "圆锥换算系数"));
    foreach (var fact in ConicalReferences.Facts)
    {
        var page = ConicalReferences.Pages.Single(p => p.Id == fact.Page);
        Equal(fact.Raw, page.Table[fact.Row][fact.Column].Raw);
        if (fact.Property == ConicalReferences.FactorProperty)
        {
            var data = A(fact.Key, fact.Property).GetProperty("data").GetProperty(fact.Property);
            Near(fact.Value, data.GetProperty("value").GetDouble(), 0, "原表系数");
            Equal("1:10", data.GetProperty("conditions").GetProperty("基准锥度").GetString());
            if (fact.Page == "13-02:60") Near(double.Parse(fact.Key.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture) / 10, fact.Value, 1e-12, "一般用途系数独立关系");
            continue;
        }
        var response = T(fact.Key, fact.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture), fact.Property);
        var value = response.GetProperty("data").GetProperty(fact.Property);
        Near(fact.Value, value.GetProperty("value").GetDouble(), 0, "IT逐格投影");
        var below = T(fact.Key, Math.BitIncrement(fact.Lower).ToString(System.Globalization.CultureInfo.InvariantCulture), fact.Property);
        Near(fact.Value, below.GetProperty("data").GetProperty(fact.Property).GetProperty("value").GetDouble(), 0, "圆锥尺寸边界下开上闭");
        if (fact.Key is not ("IT01" or "IT0"))
        {
            var baseValue = JsonSerializer.SerializeToElement(BasicStandards.QueryToleranceValue(fact.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture), fact.Key)).GetProperty("data").GetProperty("公差");
            var toleranceUm = baseValue.GetProperty("value").GetDouble() * (baseValue.GetProperty("unit").GetString() == "mm" ? 1000 : 1);
            Near(toleranceUm * (fact.Property == "圆锥最大角误差" ? 10 : .01), fact.Value, 1e-10, "独立IT值与L或锥度关系");
        }
    }
    var full = T(limit: 1, offset: 999).GetProperty("data"); Equal(8, full.EnumerateObject().Count());
    Near(210, full.GetProperty("圆锥最大角误差").GetProperty("value").GetDouble(), 0, "25mm IT7角误差");
    Near(.21, full.GetProperty("圆锥轴向公差").GetProperty("value").GetDouble(), 0, "25mm IT7轴向公差");
    Near(1100, T("IT11", "18", "圆锥最大角误差").GetProperty("data").GetProperty("圆锥最大角误差").GetProperty("value").GetDouble(), 0, "角误差核定值");
    Near(.025, T("IT3", "10", "圆锥轴向公差").GetProperty("data").GetProperty("圆锥轴向公差").GetProperty("value").GetDouble(), 0, "轴向公差核定值");
    Equal(2, T(null, null, "圆锥最大角误差").GetProperty("missing").GetArrayLength());
    Equal(20, T(null, null, "圆锥最大角误差").GetProperty("missing")[0].GetProperty("total").GetInt32());
    Equal(10, T(null, null, "圆锥轴向公差").GetProperty("missing")[0].GetProperty("total").GetInt32());
    Equal(13, T(size: "*", property: "圆锥轴向公差").GetProperty("total").GetInt32());
    Equal(20, T("*", property: "圆锥最大角误差").GetProperty("total").GetInt32());
    Equal("no_match", T("IT01", property: "圆锥轴向公差").GetProperty("status").GetString());
    Equal(13, T("IT01", "*", "圆锥最大角误差").GetProperty("total").GetInt32());
    foreach (var query in new[] { T(size: "501", property: "圆锥轴向公差"), T(property: "圆锥最大角误差", standard: "1998"), T(property: "圆锥轴向公差", process: "车"), T(property: "圆锥轴向公差", feature: "长度") })
        Equal("no_match", query.GetProperty("status").GetString());
    Equal(1, T(standard: "1998").GetProperty("data").EnumerateObject().Count());
    True(!T(size: "501").GetProperty("data").TryGetProperty("圆锥最大角误差", out _), "超范围只移除新增事实");
    Equal(36, A("*", "圆锥换算系数").GetProperty("total").GetInt32());
    Equal(36, A(null, "圆锥换算系数").GetProperty("total").GetInt32());
    Equal(36, A("*", standard: "手册").GetProperty("total").GetInt32());
    Equal("no_match", A("β45°", "圆锥换算系数").GetProperty("status").GetString());
    Equal("no_match", A(property: "圆锥换算系数", standard: "157").GetProperty("status").GetString());
    Equal(1, A(standard: "手册").GetProperty("data").EnumerateObject().Count());
    Near(1.92, A("MT0").GetProperty("data").GetProperty("圆锥换算系数").GetProperty("value").GetDouble(), 0, "莫氏别名关联换算系数");
    Equal(56, Registry().All.Count());
}

static void TestAngularSeries()
{
    JsonElement Q(string? code = "C1:10", string? property = null, string? series = null, string? standard = null, int offset = 0, int limit = 100)
        => JsonSerializer.SerializeToElement(AngularSeries.Query(code, property, series, standard, offset, limit));
    Equal(71, AngularSeries.Entries.Count); Equal(50, AngularSeries.Pages[0].Table.Length); Equal(36, AngularSeries.Pages[1].Table.Length);
    Equal(5038046L, AngularSeries.Pages[0].ByteOffset); Equal(5100036L, AngularSeries.Pages[1].ByteOffset);
    foreach (var entry in AngularSeries.Entries)
    {
        var card = Q(entry.Key, offset: 999, limit: 1);
        Equal(entry.Key, card.GetProperty("spec").GetProperty("designation").GetString());
        Equal(entry.Data.Count + ConicalReferences.ResolveFactor(entry.Key).Count, card.GetProperty("data").EnumerateObject().Count());
        foreach (var alias in entry.Aliases) Equal(card.GetProperty("data").GetRawText(), Q(alias).GetProperty("data").GetRawText());
        var d = entry.Data;
        var angle = d.TryGetValue("圆锥角", out var a) ? a : d["棱体角"];
        var ratio = d.TryGetValue("锥度", out var c) ? c : entry.Key.StartsWith('S') ? d["斜度"] : d["比率"];
        if (angle.GetProperty("value").ValueKind == JsonValueKind.Number && ratio.GetProperty("value").ValueKind == JsonValueKind.Number)
        {
            var r = ratio.GetProperty("value").GetDouble();
            var expected = (entry.Key.StartsWith('S') ? Math.Atan(r) : 2 * Math.Atan(r / 2)) * 180 / Math.PI;
            Near(expected, angle.GetProperty("value").GetDouble(), entry.Scope == "特殊用途" ? 0.0002 : 0.00005, "独立几何关系与舍入精度");
        }
        foreach (var fact in d.Values)
        {
            var source = fact.GetProperty("source");
            var cell = AngularSeries.Pages.Single(p => p.Id == entry.Page).Table[source.GetProperty("row").GetInt32()][source.GetProperty("column").GetInt32()];
            True(cell.Raw.Length > 0 || fact.GetProperty("value").ValueKind == JsonValueKind.Null, "事实必须有原格");
        }
    }
    Near(5.724810, Q().GetProperty("data").GetProperty("圆锥角").GetProperty("value").GetDouble(), 0, "圆锥全角");
    Near(1.432320, Q("C1:40").GetProperty("data").GetProperty("圆锥角").GetProperty("value").GetDouble(), 0, "1:40核定锥角");
    Near(Math.Atan(.01) * 180 / Math.PI, Q("S1:100").GetProperty("data").GetProperty("棱体角").GetProperty("value").GetDouble(), .000014, "1:100核定斜角");
    Near(1, Q("β45°").GetProperty("data").GetProperty("斜度").GetProperty("value").GetDouble(), 0, "S=tanβ");
    Near(1 / 1.207107, Q("β45°").GetProperty("data").GetProperty("比率").GetProperty("value").GetDouble(), 0, "Cp与S独立");
    Equal(JsonValueKind.Null, Q("β90°").GetProperty("data").GetProperty("斜度").GetProperty("value").ValueKind);
    Equal("C1:19.212", Q("MT0").GetProperty("spec").GetProperty("designation").GetString());
    Equal("C1:12.262", Q("贾氏2").GetProperty("spec").GetProperty("designation").GetString());
    Equal(71, Q(null).GetProperty("total").GetInt32()); Equal(JsonValueKind.Null, Q(null).GetProperty("default").ValueKind);
    Equal(42, Q("*", standard: "157").GetProperty("total").GetInt32()); Equal(29, Q("*", standard: "4096").GetProperty("total").GetInt32());
    Equal(6, Q("S*").GetProperty("total").GetInt32());
    Equal(1, Q(property: "圆锥角").GetProperty("data").EnumerateObject().Count());
    Equal(5, Q(property: "*").GetProperty("total").GetInt32());
    Equal(3, Q("*", offset: 2, limit: 3).GetProperty("rows").GetArrayLength());
    foreach (var code in new[] { "60°", "α61°", "MT7", "S1:40" }) Equal("no_match", Q(code).GetProperty("status").GetString());
    Equal("no_match", Q("β45°", standard: "157").GetProperty("status").GetString());
    Equal("no_match", Q("MT0", series: "1").GetProperty("status").GetString());
    Equal("no_match", Q("S1:10", series: "2").GetProperty("status").GetString());
    Equal(Q().GetRawText(), Q("C=1∶10").GetRawText());
    foreach (var action in new Action[] { () => Q("*", "*"), () => Q(series: "3"), () => Q(standard: "*") })
    { var rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } True(rejected, "非法展开或系列应拒绝"); }
    var registry = Registry(); True(registry.TryGet("norma.basic.angular", out var desc), "独立方法注册");
    var result = desc!.Handler(new CommandContext(desc, new Dictionary<string,string> { ["designation"]="C1:10" }, "test", null, CancellationToken.None)).Result;
    True(result.Success, result.Message); Equal("C1:10", JsonSerializer.SerializeToElement(result.Data).GetProperty("spec").GetProperty("designation").GetString());
    Equal("基础标准", NormaCommands.PageRows().Single(r => r["command"] == "norma.basic.angular")["topic"]);
}

static void TestFitStatistics()
{
    JsonElement Q(string? fit = "H7/g6", string? size = "25", string? property = null, string? standard = null, string? usage = null, int offset = 0, int limit = 100)
        => JsonSerializer.SerializeToElement(FitStandards.Query(fit, size, property, usage, standard, offset, limit));
    var hi = FitStatistics.Properties[0]; var lo = FitStatistics.Properties[1];
    Equal(2450, FitStatistics.Entries.Count); Equal(98, FitStatistics.Entries.Select(e => e.Fit).Distinct().Count());
    Equal(197, FitStatistics.Source.Table.Length);
    foreach (var e in FitStatistics.Entries)
    {
        var data = Q(e.Fit, e.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture), standard: "9184").GetProperty("data");
        Equal(2, data.EnumerateObject().Count());
        Equal(e.Raw, data.GetProperty(hi).GetProperty("raw").GetString());
        if (e.High is double high) Near(high, data.GetProperty(hi).GetProperty("value").GetDouble(), 0, "统计上极限");
        else Equal(JsonValueKind.Null, data.GetProperty(hi).GetProperty("value").ValueKind);
        if (e.Low is double low) Near(low, data.GetProperty(lo).GetProperty("value").GetDouble(), 0, "统计下极限");
        else Equal(JsonValueKind.Null, data.GetProperty(lo).GetProperty("value").ValueKind);
        Equal("2:1", data.GetProperty(hi).GetProperty("conditions").GetProperty("tToWc").GetString());
        Equal("86%", data.GetProperty(hi).GetProperty("conditions").GetProperty("pcMin").GetString());
    }
    var card = Q(limit: 1, offset: 999).GetProperty("data");
    Near(36, card.GetProperty(hi).GetProperty("value").GetDouble(), 0, "统计间隙");
    Near(12, card.GetProperty(lo).GetProperty("value").GetDouble(), 0, "统计间隙");
    Near(41, card.GetProperty("最大间隙或最小过盈").GetProperty("value").GetDouble(), 0, "绝对极限保持独立");
    Equal(10, card.EnumerateObject().Count());
    foreach (var fit in new[] { "H11/c11", "C11/h11" })
        Near(730, Q(fit, "170").GetProperty("data").GetProperty("最大间隙或最小过盈").GetProperty("value").GetDouble(), 0, "IT11=250的绝对极限同步核定");
    Equal(5, Q(standard: "1801").GetProperty("data").EnumerateObject().Count());
    Equal(7, Q(standard: "1999").GetProperty("data").EnumerateObject().Count());
    Equal("no_match", Q(property: hi, standard: "1801").GetProperty("status").GetString());
    Equal("no_match", Q("H8/v7", property: hi).GetProperty("status").GetString());
    Equal("no_match", Q(size: "501", property: hi).GetProperty("status").GetString());
    Equal(2, Q(null, null, hi).GetProperty("missing").GetArrayLength());
    Equal(98, Q(null, null, hi).GetProperty("missing")[0].GetProperty("total").GetInt32());
    Equal(98, Q("*", property: hi).GetProperty("total").GetInt32());
    Equal(25, Q(size: "*", property: hi).GetProperty("total").GetInt32());
    Equal(2, Q(property: "*", standard: "9184").GetProperty("total").GetInt32());
    Equal("no_match", Q(property: "配合类型", standard: "9184").GetProperty("status").GetString());
    True(Q("*", property: hi, usage: "3").GetProperty("rows").EnumerateArray().All(r =>
        FitStandards.Selections.Any(s => s.Fit == r.GetProperty("spec").GetProperty("fit").GetString() && s.Usage == 3)), "统计属性与usage正交");
    foreach (var range in FitStatistics.Entries.Where(e => e.Fit == "H7/g6"))
    {
        Near(range.High!.Value, Q(size: range.Upper.ToString(System.Globalization.CultureInfo.InvariantCulture), property: hi).GetProperty("data").GetProperty(hi).GetProperty("value").GetDouble(), 0, "尺寸段上界包含");
        Near(range.High.Value, Q(size: Math.BitIncrement(range.Lower).ToString(System.Globalization.CultureInfo.InvariantCulture), property: hi).GetProperty("data").GetProperty(hi).GetProperty("value").GetDouble(), 0, "尺寸段下界严格开放");
    }
    Equal(3, Q("*", property: hi, offset: 2, limit: 3).GetProperty("rows").GetArrayLength());
    var rejected = false; try { Q("*", "*", hi); } catch (ArgumentException) { rejected = true; } True(rejected, "统计查询拒绝多个变化轴");
    foreach (var (row, column, raw) in new (int, int, string)[] {
        (34,4,"+168 +108"),(38,4,"+226 +138"),(46,1,"+401 +259"),(62,5,"+188 +32"),
        (64,4,"+287 +103"),(64,6,"+518 +222"),(74,3,"+657 +303"),(78,1,"+1466 +1014"),
        (82,5,"+683 +117"),(83,1,"+2333 +1767"),(88,7,"+20 -1.8"),(100,1,"+3.9 -27"),
        (110,1,"+8.1 -41"),(143,3,"-9 -17"),(150,2,"-31 -50"),(173,7,"-2.2 -29"),
        (188,10,"-202 -288"),(189,10,"-228 -314"),(195,6,"-1201 -1276") })
        Equal(raw.Replace(" ", ""), FitStatistics.Source.Table[row][column].Raw.Replace(" ", ""));
    // Independent IT11 and shaft fundamental deviations, rather than the defective old absolute-fit cells.
    foreach (var (fit, size, tolerance, deviation) in new[] { ("H11/d11", "25", 130.0, 65.0), ("H11/c11", "170", 250.0, 230.0) })
    {
        var data = Q(fit, size, standard: "9184").GetProperty("data");
        var center = deviation + tolerance;
        Near(Math.Round(center + tolerance / Math.Sqrt(2)), data.GetProperty(hi).GetProperty("value").GetDouble(), 0, "独立IT统计上限");
        Near(Math.Round(center - tolerance / Math.Sqrt(2)), data.GetProperty(lo).GetProperty("value").GetDouble(), 0, "独立IT统计下限");
    }
    var registry = Registry(); registry.TryGet("norma.basic.fit", out var descriptor);
    var result = descriptor!.Handler(new CommandContext(descriptor, new Dictionary<string,string> { ["fit"]="H7/g6", ["size"]="25", ["property"]=hi }, "test", null, CancellationToken.None)).Result;
    True(result.Success, result.Message);
    Near(36, JsonSerializer.SerializeToElement(result.Data).GetProperty("data").GetProperty(hi).GetProperty("value").GetDouble(), 0, "真实注册序列化");
}

static void TestFitApplications()
{
    static JsonElement Q(string? fit = "H7/g6", string? size = null, string? property = "应用举例", string? standard = null,
        string? usage = null, int offset = 0, int limit = 100)
        => JsonSerializer.SerializeToElement(FitStandards.Query(fit, size, property, usage, standard, offset, limit));
    Equal("13-02:47", FitApplications.SourcePage.Id);
    Equal(4425145L, FitApplications.SourcePage.ByteOffset);
    Equal(48, FitApplications.SourcePage.Table.Length);
    Equal(265, FitApplications.Facts.Count);
    Equal(78, FitApplications.Facts.Select(f => f.Fit).Distinct().Count());
    foreach (var (property, count) in new[] { ("装配方法", 78), ("配合特性及使用条件", 79), ("应用举例", 83), ("过盈概率", 25) })
    {
        var all = Q("*", property: property);
        var total = 0;
        foreach (var card in all.GetProperty("rows").EnumerateArray())
        {
            Equal(1, card.GetProperty("data").EnumerateObject().Count());
            var fit = card.GetProperty("spec").GetProperty("fit").GetString();
            var facts = card.GetProperty("data").GetProperty(property);
            Equal(FitApplications.Facts.Count(f => f.Fit == fit && f.Property == property), facts.GetArrayLength());
            total += facts.GetArrayLength();
            foreach (var fact in facts.EnumerateArray())
            {
                Equal("13-02:47", fact.GetProperty("source").GetProperty("id").GetString());
                var raw = string.Join("；", fact.GetProperty("source").GetProperty("cells").EnumerateArray().Select(c =>
                    FitApplications.SourcePage.Table[c.GetProperty("row").GetInt32()][c.GetProperty("column").GetInt32()].Raw));
                Equal(raw, fact.GetProperty("raw").GetString());
            }
        }
        Equal(count, total);
    }
    var g6 = Q().GetProperty("data").GetProperty("应用举例");
    Equal(1, g6.GetArrayLength());
    True(g6[0].GetProperty("value").GetString()!.Contains("矩形花键"), "H7/g6应用举例错行");
    Equal("手旋进", Q(property: "装配方法").GetProperty("data").GetProperty("装配方法")[0].GetProperty("value").GetString());
    True(Q(property: "配合特性及使用条件").GetProperty("data").GetProperty("配合特性及使用条件")[0].GetProperty("value").GetString()!.Contains("精密定位"), "特性合并格错误");
    // One shaft-basis key spans two examples; do not discard either, or cross-pair its constituent bands.
    Equal(2, Q("G7/h6").GetProperty("data").GetProperty("应用举例").GetArrayLength());
    Equal(3, Q("U7/h6").GetProperty("data").GetProperty("应用举例").GetArrayLength());
    Equal(2, Q("U7/h6", property: "配合特性及使用条件").GetProperty("data").GetProperty("配合特性及使用条件").GetArrayLength());
    Equal(1, Q("H7/h6").GetProperty("data").GetProperty("应用举例").GetArrayLength());
    Equal(2, Q("H7/h6").GetProperty("data").GetProperty("应用举例")[0].GetProperty("source").GetProperty("keys").GetArrayLength());
    True(Q("P7/h6").GetProperty("data").GetProperty("应用举例")[0].GetProperty("value").GetString()!.Contains("冲击振动"), "轻型配合权威代号错误");
    True(!Q("R7/h6").GetProperty("data").GetProperty("应用举例").EnumerateArray().Any(f => f.GetProperty("value").GetString()!.Contains("冲击振动")), "相邻配合举例不得混入");
    var p = Q("H7/k6", property: "过盈概率").GetProperty("data").GetProperty("过盈概率")[0];
    Equal("%", p.GetProperty("unit").GetString());
    Near(41.7, p.GetProperty("min").GetDouble(), 0, "概率下限"); Near(45, p.GetProperty("max").GetDouble(), 0, "概率上限");
    True(p.GetProperty("value").ValueKind == JsonValueKind.Null, "概率区间不取中点");
    Near(80, Q("N6/h5", property: "过盈概率").GetProperty("data").GetProperty("过盈概率")[0].GetProperty("value").GetDouble(), 0, "单概率");
    var missingProbability = Q("H6/m5", property: "过盈概率").GetProperty("data").GetProperty("过盈概率")[0];
    True(missingProbability.GetProperty("value").ValueKind == JsonValueKind.Null && missingProbability.GetProperty("min").ValueKind == JsonValueKind.Null, "标题格不得推定概率");
    Equal("no_match", Q("H7/g6", property: "过盈概率").GetProperty("status").GetString());
    Equal("no_match", Q("A1/a1").GetProperty("status").GetString());
    Equal(78, Q("*", limit: 1).GetProperty("total").GetInt32());
    Equal(1, Q("*", offset: 1, limit: 1).GetProperty("rows").GetArrayLength());
    Equal(1, Q(null).GetProperty("missing").GetArrayLength());
    Equal("fit", Q(null).GetProperty("missing")[0].GetProperty("missing").GetString());
    Equal(78, Q(null).GetProperty("missing")[0].GetProperty("total").GetInt32());
    Equal(2, Q(null, property: null).GetProperty("missing").GetArrayLength());
    Equal("no_match", Q(standard: "1801").GetProperty("status").GetString());
    Equal(3, Q(property: null, standard: "手册").GetProperty("data").EnumerateObject().Count());
    Equal(3, Q(property: "*", standard: "手册").GetProperty("total").GetInt32());
    Equal("no_match", Q(property: "配合类型", standard: "手册").GetProperty("status").GetString());
    Equal("no_match", Q(size: "*").GetProperty("status").GetString());
    Equal(Q().GetRawText(), Q(size: "900").GetRawText()); // This source does not assert a numeric diameter range.
    var full = Q(size: "25", property: null, limit: 1).GetProperty("data");
    Equal(10, full.EnumerateObject().Count()); Near(41, full.GetProperty("最大间隙或最小过盈").GetProperty("value").GetDouble(), 0, "数值回归");
    Equal(3, Q("U7/h6", size: "25", property: null, limit: 1).GetProperty("data").GetProperty("应用举例").GetArrayLength());
    True(Q(size: "*", property: null).GetProperty("rows").EnumerateArray().All(r => r.GetProperty("data").EnumerateObject().Count() == 7), "尺寸变化表不重复应用资料");
    Equal(22, Q("*", size: "25", property: "选用级别", usage: "3").GetProperty("total").GetInt32());
    var registry = Registry(); registry.TryGet("norma.basic.fit", out var descriptor);
    var result = descriptor!.Handler(new CommandContext(descriptor, new Dictionary<string, string> { ["fit"] = "H7/g6", ["property"] = "应用举例" }, "test", null, CancellationToken.None)).Result;
    True(result.Success, result.Message);
    Equal(1, JsonSerializer.SerializeToElement(result.Data).GetProperty("data").GetProperty("应用举例").GetArrayLength());
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
        var basicProbe = await Probe(archive, "norma.basic.tolerance size=25 grade=IT7");
        var basicResult = basicProbe.GetProperty("data").GetProperty("result");
        True(basicResult.GetProperty("success").GetBoolean(), basicResult.GetRawText());
        Near(21, basicResult.GetProperty("data").GetProperty("data").GetProperty("公差").GetProperty("value").GetDouble(), 0, "隔离宿主标准公差");
        foreach (var (query, property, count) in new[]
        {
            ("grade=IT7 property=可达加工方法 process=铰孔", "可达加工方法", 1),
            ("grade=IT9 property=加工成本比 process=普通车削 feature=外径", "加工成本比", 1),
            ("grade=IT01 property=选用举例", "选用举例", 1)
        })
        {
            var itProbe = await Probe(archive, "norma.basic.tolerance " + query);
            var itResult = itProbe.GetProperty("data").GetProperty("result");
            True(itResult.GetProperty("success").GetBoolean(), itResult.GetRawText());
            var itData = itResult.GetProperty("data").GetProperty("data");
            Equal(1, itData.EnumerateObject().Count());
            Equal(count, itData.GetProperty(property).GetArrayLength());
            if (property == "加工成本比") Near(2.5, itData.GetProperty(property)[0].GetProperty("value").GetDouble(), 0, "隔离宿主成本图例");
        }
        var conicalProbe = await Probe(archive, "norma.basic.tolerance grade=IT7 size=25 property=圆锥轴向公差");
        var conicalResult = conicalProbe.GetProperty("data").GetProperty("result");
        True(conicalResult.GetProperty("success").GetBoolean(), conicalResult.GetRawText());
        Near(.21, conicalResult.GetProperty("data").GetProperty("data").GetProperty("圆锥轴向公差").GetProperty("value").GetDouble(), 0, "宿主轴向公差");
        var axialProbe = await Probe(archive, "norma.basic.band band=js7 size=25 property=圆锥轴向基本偏差");
        var axialResult = axialProbe.GetProperty("data").GetProperty("result");
        True(axialResult.GetProperty("success").GetBoolean(), axialResult.GetRawText());
        Near(.105, axialResult.GetProperty("data").GetProperty("data").GetProperty("圆锥轴向基本偏差").GetProperty("value").GetDouble(), 1e-12, "宿主圆锥js");
        var atProbe = await Probe(archive, "norma.basic.coneangle grade=AT7 length=50");
        var atResult = atProbe.GetProperty("data").GetProperty("result");
        True(atResult.GetProperty("success").GetBoolean(), atResult.GetRawText());
        Near(15.8, atResult.GetProperty("data").GetProperty("data").GetProperty("直径公差").GetProperty("value").GetDouble(), 0, "宿主AT实际长度");
        var coneProbe = await Probe(archive, "norma.cone.limits taper=0.1 upper=-7 lower=-20");
        var coneResult = coneProbe.GetProperty("data").GetProperty("result");
        True(coneResult.GetProperty("success").GetBoolean(), coneResult.GetRawText());
        Near(.2, coneResult.GetProperty("data").GetProperty("outputs")[0].GetProperty("value").GetDouble(), 1e-12, "宿主轴向上偏差");
        var coefficientProbe = await Probe(archive, "norma.basic.angular designation=MT0 property=圆锥换算系数");
        Near(1.92, coefficientProbe.GetProperty("data").GetProperty("result").GetProperty("data").GetProperty("data").GetProperty("圆锥换算系数").GetProperty("value").GetDouble(), 0, "宿主莫氏系数");
        var angularProbe = await Probe(archive, "norma.basic.angular designation=C1:10");
        var angularResult = angularProbe.GetProperty("data").GetProperty("result");
        True(angularResult.GetProperty("success").GetBoolean(), angularResult.GetRawText());
        Near(5.724810, angularResult.GetProperty("data").GetProperty("data").GetProperty("圆锥角").GetProperty("value").GetDouble(), 0, "宿主圆锥全角");
        var morseProbe = await Probe(archive, "norma.basic.angular designation=MT0");
        True(morseProbe.GetProperty("data").ValueKind == JsonValueKind.Object, morseProbe.GetRawText());
        True(morseProbe.GetProperty("data").GetProperty("result").GetProperty("success").GetBoolean(), morseProbe.GetRawText());
        Equal("C1:19.212", morseProbe.GetProperty("data").GetProperty("result").GetProperty("data").GetProperty("spec").GetProperty("designation").GetString());
        var generalProbe = await Probe(archive, "norma.basic.general size=25 grade=m");
        var generalResult = generalProbe.GetProperty("data").GetProperty("result");
        True(generalResult.GetProperty("success").GetBoolean(), generalResult.GetRawText());
        var generalData = generalResult.GetProperty("data").GetProperty("data");
        Equal(3, generalData.EnumerateObject().Count());
        Near(.2, generalData.GetProperty("线性尺寸").GetProperty("value").GetDouble(), 0, "宿主一般公差");
        Equal("±30′", generalData.GetProperty("角度尺寸").GetProperty("raw").GetString());
        var selectedGeneralProbe = await Probe(archive, "norma.basic.general size=25 grade=m property=角度尺寸");
        var selectedGeneralResult = selectedGeneralProbe.GetProperty("data").GetProperty("result");
        True(selectedGeneralResult.GetProperty("success").GetBoolean(), selectedGeneralResult.GetRawText());
        var selectedGeneralData = selectedGeneralResult.GetProperty("data").GetProperty("data");
        Equal(1, selectedGeneralData.EnumerateObject().Count());
        Equal("±30′", selectedGeneralData.GetProperty("角度尺寸").GetProperty("raw").GetString());

        var deviationProbe = await Probe(archive, "norma.basic.band band=K7 size=25");
        var deviationResult = deviationProbe.GetProperty("data").GetProperty("result");
        True(deviationResult.GetProperty("success").GetBoolean(), deviationResult.GetRawText());
        Near(6, deviationResult.GetProperty("data").GetProperty("data").GetProperty("基本偏差").GetProperty("value").GetDouble(), 0, "隔离宿主基本偏差");
        Near(6, deviationResult.GetProperty("data").GetProperty("data").GetProperty("上偏差").GetProperty("value").GetDouble(), 0, "隔离宿主孔上偏差");
        Near(-15, deviationResult.GetProperty("data").GetProperty("data").GetProperty("下偏差").GetProperty("value").GetDouble(), 0, "隔离宿主孔下偏差");
        foreach (var (property, expected) in new[] { ("上偏差", 21d), ("下偏差", 0d) })
        {
            var holeProbe = await Probe(archive, $"norma.basic.band band=H7 size=25 property={property} standard=1999");
            var holeResult = holeProbe.GetProperty("data").GetProperty("result");
            True(holeResult.GetProperty("success").GetBoolean(), holeResult.GetRawText());
            var holeData = holeResult.GetProperty("data").GetProperty("data");
            Equal(1, holeData.EnumerateObject().Count());
            Near(expected, holeData.GetProperty(property).GetProperty("value").GetDouble(), 0, "隔离宿主孔极限偏差属性/版本过滤");
        }
        var shaftCardProbe = await Probe(archive, "norma.basic.band band=g6 size=25");
        var shaftCard = shaftCardProbe.GetProperty("data").GetProperty("result").GetProperty("data").GetProperty("data");
        Equal(7, shaftCard.EnumerateObject().Count());
        var appProbe = await Probe(archive, "norma.basic.band band=g6 property=基本偏差应用");
        var appResult = appProbe.GetProperty("data").GetProperty("result");
        True(appResult.GetProperty("success").GetBoolean(), appResult.GetRawText());
        Equal("g", appResult.GetProperty("data").GetProperty("data").GetProperty("基本偏差应用").GetProperty("code").GetString());
        Near(-7, shaftCard.GetProperty("上偏差").GetProperty("value").GetDouble(), 0, "隔离宿主轴上偏差");
        Near(-20, shaftCard.GetProperty("下偏差").GetProperty("value").GetDouble(), 0, "隔离宿主轴下偏差");
        foreach (var (band, size, property, expected) in new[] { ("js7", "100", "上偏差", 17d), ("h7", "100", "下偏差", -35d) })
        {
            var shaftProbe = await Probe(archive, $"norma.basic.band band={band} size={size} property={property} standard=1999");
            var shaftResult = shaftProbe.GetProperty("data").GetProperty("result");
            True(shaftResult.GetProperty("success").GetBoolean(), shaftResult.GetRawText());
            var shaftData = shaftResult.GetProperty("data").GetProperty("data");
            Equal(1, shaftData.EnumerateObject().Count());
            Near(expected, shaftData.GetProperty(property).GetProperty("value").GetDouble(), 0, "隔离宿主轴图片表属性/版本过滤");
            Equal("image-transcription", shaftData.GetProperty(property).GetProperty("source").GetProperty("kind").GetString());
        }
        var selectionProbe = await Probe(archive, "norma.basic.band band=* size=25 usage=4 property=常用级别 standard=1801 limit=100");
        var selectionResult = selectionProbe.GetProperty("data").GetProperty("result");
        True(selectionResult.GetProperty("success").GetBoolean(), selectionResult.GetRawText());
        Equal(26, selectionResult.GetProperty("data").GetProperty("total").GetInt32());
        foreach (var (size, expected) in new[] { ("500", 4), ("500.0001", 3) })
        {
            var boundary = await Probe(archive, $"norma.basic.band band=H7 size={size} property=常用级别 standard=1801");
            Equal(expected, boundary.GetProperty("data").GetProperty("result").GetProperty("data").GetProperty("data").GetProperty("常用级别").GetProperty("value").GetInt32());
        }
        var fitProbe = await Probe(archive, "norma.basic.fit fit=H7/g6 size=25");
        var fitResult = fitProbe.GetProperty("data").GetProperty("result");
        True(fitResult.GetProperty("success").GetBoolean(), fitResult.GetRawText());
        Equal(10, fitResult.GetProperty("data").GetProperty("data").EnumerateObject().Count());
        Near(41, fitResult.GetProperty("data").GetProperty("data").GetProperty("最大间隙或最小过盈").GetProperty("value").GetDouble(), 0, "宿主配合上极限");
        var statisticalProbe = await Probe(archive, "norma.basic.fit fit=H7/g6 size=25 standard=9184");
        var statisticalData = statisticalProbe.GetProperty("data").GetProperty("result").GetProperty("data").GetProperty("data");
        Equal(2, statisticalData.EnumerateObject().Count());
        Near(36, statisticalData.GetProperty("统计最大间隙或最小过盈").GetProperty("value").GetDouble(), 0, "宿主统计上极限");
        Near(12, statisticalData.GetProperty("统计最小间隙或最大过盈").GetProperty("value").GetDouble(), 0, "宿主统计下极限");
        var applicationProbe = await Probe(archive, "norma.basic.fit fit=H7/g6 property=应用举例");
        var applicationResult = applicationProbe.GetProperty("data").GetProperty("result");
        True(applicationResult.GetProperty("success").GetBoolean(), applicationResult.GetRawText());
        True(applicationResult.GetProperty("data").GetProperty("data").GetProperty("应用举例")[0].GetProperty("value").GetString()!.Contains("矩形花键"), "隔离宿主无尺寸配合应用");
        var probabilityProbe = await Probe(archive, "norma.basic.fit fit=H7/k6 property=过盈概率 standard=手册");
        var probabilityResult = probabilityProbe.GetProperty("data").GetProperty("result");
        True(probabilityResult.GetProperty("success").GetBoolean(), probabilityResult.GetRawText());
        Near(41.7, probabilityResult.GetProperty("data").GetProperty("data").GetProperty("过盈概率")[0].GetProperty("min").GetDouble(), 0, "隔离宿主概率区间");
        var preferredFits = await Probe(archive, "norma.basic.fit fit=* size=25 usage=3 property=选用级别 standard=1801 limit=100");
        Equal(22, preferredFits.GetProperty("data").GetProperty("result").GetProperty("data").GetProperty("total").GetInt32());
        var conflictFit = await Probe(archive, "norma.basic.fit fit=D11/h11 size=25 property=最大间隙或最小过盈");
        Near(325, conflictFit.GetProperty("data").GetProperty("result").GetProperty("data").GetProperty("data").GetProperty("最大间隙或最小过盈").GetProperty("value").GetDouble(), 0, "宿主返回唯一配合值");
        var basicMissing = await Probe(archive, "norma.basic.band");
        Equal(2, basicMissing.GetProperty("data").GetProperty("result").GetProperty("data").GetProperty("missing").GetArrayLength());
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

        // Merged named/reference library is exercised through the real host.
        foreach (var (query, category, property, expected) in new[]
        {
            ("铅", "密度", "密度", "11.37 g/cm³（=t/m³）"),
            ("铅", "弹性", "弹性模量", "16 GPa"),
            ("铜", "热学", "热导率", "384 W/(m·K)"),
            ("钢-钢", "滑动摩擦", "静摩擦/无润滑剂", "0.15 1"),
            ("滚动轴承/深沟球轴承/轴向载荷", "物体摩擦", "摩擦系数", "0.004 1"),
            ("橡胶轮胎对沥青路面", "滚动摩擦力臂", "滚动摩擦力臂", "2.5 mm"),
            ("PTFE", "非金属", "密度", "2.1~2.2 g·cm^-3"),
        })
        {
            var probe = await Probe(archive, $"norma.material.library name={query} category={category} property={property}");
            var materialResult = probe.GetProperty("data").GetProperty("result");
            True(materialResult.GetProperty("success").GetBoolean(), materialResult.GetRawText());
            Equal(expected, materialResult.GetProperty("data").GetString()!);
        }

        var efficiencyProbe = await Probe(archive, "norma.drive.efficiency q=自锁蜗杆");
        var efficiencyResult = efficiencyProbe.GetProperty("data").GetProperty("result");
        True(efficiencyResult.GetProperty("success").GetBoolean(), efficiencyResult.GetRawText());
        Near(0.4, efficiencyResult.GetProperty("data").GetProperty("items")[0].GetProperty("efficiency").GetProperty("min").GetDouble(), 0, "隔离宿主传动效率");

        var specProbe = await Probe(archive, "norma.product.spec name=结构用热轧无缝钢管 spec=\"{\\\"外径\\\":32,\\\"壁厚\\\":3}\" property=理论重量");
        var specResult = specProbe.GetProperty("data").GetProperty("result");
        True(specResult.GetProperty("success").GetBoolean(), specResult.GetRawText());
        True(specResult.GetProperty("data").ValueKind == JsonValueKind.String, specResult.GetRawText());
        Equal("2.15 kg·m^-1", specResult.GetProperty("data").GetString()!);

        foreach (var (key, args, expected) in new[] { ("grade", "property=Cr", "14～18 %"), ("grade", "category=力学 property=硬度HRC condition=硬化态或硬化态并去应力处理", "≥58 HRC") })
        {
            var ironProbe = await Probe(archive, $"norma.material.{key} grade=KmTBCr15Mo {args}");
            var ironResult = ironProbe.GetProperty("data").GetProperty("result");
            True(ironResult.GetProperty("success").GetBoolean(), ironResult.GetRawText());
            Equal(expected, ironResult.GetProperty("data").GetString()!);
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
    True(document.RootElement.GetProperty("data").ValueKind == JsonValueKind.Object, $"宿主探针失败 {command}：{output}{error}");
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
    ManualWindowScreenshot.ValidateFullPage(true, 0, 0, 0, 0);
    foreach (var crop in new[] { (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 100, 0), (0, 0, 0, 100) })
    {
        var rejected = false;
        try { ManualWindowScreenshot.ValidateFullPage(true, crop.Item1, crop.Item2, crop.Item3, crop.Item4); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "整页截图不能静默忽略局部裁切");
    }
    var screenshotRegistry = Registry();
    screenshotRegistry.TryGet("norma.manual.screenshot", out var screenshotCommand);
    var fullPageParameter = screenshotCommand!.Parameters.Single(p => p.Name == "full_page");
    Equal("false", fullPageParameter.Default);
    var invalidFullPage = screenshotCommand.Handler(new CommandContext(screenshotCommand,
        new Dictionary<string, string> { ["mode"] = "html", ["full_page"] = "true", ["id"] = "13-02:2" }, "test", null, CancellationToken.None)).Result;
    True(!invalidFullPage.Success, "提取HTML模式不得冒充原窗口整页截图");
    if (Environment.GetEnvironmentVariable("NORMA_FULLPAGE_SCREENSHOT_TEST") == "1")
    {
        var full = JsonSerializer.SerializeToElement(ManualWindowScreenshot.Capture(ManualCommands.DefaultRoot, null, 0, 0, 0, 0, true)).GetProperty("capture");
        True(full.GetProperty("complete").GetBoolean() && full.GetProperty("scrollRestored").GetBoolean(), "整页覆盖或滚动恢复未确认");
        var tiles = full.GetProperty("tiles").EnumerateArray().ToArray();
        long pixels = tiles.Sum(t => (long)t.GetProperty("width").GetInt32() * t.GetProperty("height").GetInt32());
        Equal((long)full.GetProperty("width").GetInt32() * full.GetProperty("height").GetInt32(), pixels);
        True(File.Exists(full.GetProperty("imagePath").GetString()), "未保存整页PNG");
        Console.WriteLine("Native full page: " + full.GetRawText());
    }
    if (Environment.GetEnvironmentVariable("NORMA_NATIVE_SCREENSHOT_TEST") == "1")
    {
        var directory = JsonSerializer.SerializeToElement(ManualWindowScreenshot.DirectoryView(ManualCommands.DefaultRoot, "常用材料", 0, 30)).GetProperty("directory");
        True(directory.GetProperty("total").GetInt32() > 0 && directory.GetProperty("text").GetString()!.Contains("常用材料"), "目录文字查看器没有返回匹配目录");
        True(!directory.TryGetProperty("imagePath", out _), "目录查看器不应生成截图");
        Console.WriteLine("Native text directory: " + directory.GetProperty("total").GetInt32() + " matches; no screenshot");
        Console.WriteLine(JsonSerializer.Serialize(ManualWindowScreenshot.Open(ManualCommands.DefaultRoot, "常用材料弹性模量及泊松比", null)));
        var capture = JsonSerializer.SerializeToElement(ManualWindowScreenshot.Capture(ManualCommands.DefaultRoot, null, 0, 0, 0, 0));
        Equal("content", capture.GetProperty("capture").GetProperty("region").GetString()!);
        True(capture.GetProperty("capture").GetProperty("width").GetInt32() < capture.GetProperty("capture").GetProperty("windowWidth").GetInt32(), "正文截图没有排除左侧目录");
        Equal("常用材料弹性模量及泊松比", capture.GetProperty("capture").GetProperty("contentTitle").GetString()!);
        Console.WriteLine(capture.GetRawText());
        var scrolled = JsonSerializer.SerializeToElement(ManualWindowScreenshot.Open(ManualCommands.DefaultRoot, "松散物料的堆密度和安息角", null, 5000)).GetProperty("page");
        True(scrolled.GetProperty("verified").GetBoolean(), "长表滚动后标题未经核实");
        Equal(5000, scrolled.GetProperty("scrollY").GetInt32());
        var bottom = JsonSerializer.SerializeToElement(ManualWindowScreenshot.Capture(ManualCommands.DefaultRoot, null, 0, 0, 0, 0)).GetProperty("capture");
        Equal("松散物料的堆密度和安息角", bottom.GetProperty("contentTitle").GetString()!);
        Console.WriteLine("Native scrolled content: " + bottom.GetRawText());
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
// REQ-DRIVE-001: independent references from native manual, including damaged-source repairs.
static void TestMaterialLibrary()
{
    JsonElement J(object v) => JsonSerializer.SerializeToElement(v);
    object Q(string grade, string? property = null, string? condition = null, string? family = null, string tool = "grade")
        => MaterialLibrary.Query(tool, family, grade, property, condition);
    var ht = J(Q("HT250"));
    Equal("HT250", ht.GetProperty("牌号").GetString()!);
    Equal("原表未给出", ht.GetProperty("化学成分").GetString()!);
    var tensile = ht.GetProperty("力学性能").GetProperty("抗拉强度");
    Equal(25, tensile.EnumerateObject().Count());
    Equal("≥250 MPa", tensile.GetProperty("单铸试棒").GetString()!);
    True(tensile.EnumerateObject().Any(p => p.Name.Contains("20<壁厚≤40 mm")), "壁厚开闭边界可读");
    Equal(1, ht.GetProperty("来源").GetArrayLength());
    var white = J(Q("KmTBCr15Mo"));
    Equal("14～18 %", white.GetProperty("化学成分").GetProperty("Cr").GetString()!);
    Equal(3, white.GetProperty("力学性能").EnumerateObject().Count());
    Equal("14～18 %", (string)Q("KmTBCr15Mo", "Cr"));
    Equal("≥250 MPa", (string)Q("HT250", "抗拉强度", "单铸试棒"));
    Equal(25, J(Q("HT250", "抗拉强度")).GetProperty("力学性能").GetProperty("抗拉强度").EnumerateObject().Count());
    var cylinder = J(Q("铬钼铜铸铁", family: "汽缸套用耐磨铸铁"));
    Equal(3, cylinder.GetProperty("化学成分").EnumerateObject().Count());
    Equal(3, cylinder.GetProperty("力学性能").EnumerateObject().Count());
    Equal("铬钼铜铸铁", cylinder.GetProperty("材料名称").GetString()!);
    True(!cylinder.TryGetProperty("牌号", out _), "品种名不能冒充牌号");
    Equal(4, J(Q("磷铸铁")).GetProperty("牌号映射").GetArrayLength());
    Equal("HT250", J(MaterialLibrary.Query("grade", grade: "HT 250*")).GetProperty("牌号").GetString()!);
    Equal("T", (string)Q("退火态", "状态代号"));
    var strength = J(Q("HT250", tool: "strength"));
    True(!strength.TryGetProperty("化学成分", out _), "兼容性能入口不混成分");
    var options = J(MaterialLibrary.Query("grade", limit: 7));
    Equal(7, options.GetProperty("options").GetArrayLength());
    Equal(7, options.GetProperty("nextOffset").GetInt32());
    var alias = J(MaterialLibrary.Query("grade", grade: "磷铸铁"));
    Equal(4, alias.GetProperty("牌号映射").GetArrayLength());
    Equal("MTP15", alias.GetProperty("牌号映射")[0].GetString()!);
    // User-selected purpose baselines must never relabel the original alloy recipe as grade data.
    Equal(29, MaterialRepresentatives.Selections.Length);
    Equal(27, MaterialRepresentatives.Selections.Select(s => s.Material).Distinct().Count());
    Equal(5, MaterialRepresentatives.Selections.Select(s => s.Grade).Distinct().Count());
    var tungsten = J(Q("钨铸铁"));
    Equal("DH33", tungsten.GetProperty("牌号").GetString()!);
    Equal("厂家未公开", tungsten.GetProperty("化学成分").GetString()!);
    Equal("≥450 MPa", tungsten.GetProperty("力学性能").GetProperty("厂家公布值").GetProperty("抗弯强度").GetString()!);
    Equal("0.4~0.35（原表区间倒置，待核）", tungsten.GetProperty("原配方（非代表牌号指标）").GetProperty("化学成分").GetProperty("W").GetString()!);
    Equal("用途基准，非等同或替代认证", tungsten.GetProperty("选型关系").GetString()!);
    var directRing = J(Q("DH33"));
    Equal("240～330 HV30", directRing.GetProperty("力学性能").GetProperty("厂家公布值").GetProperty("硬度").GetString()!);
    True(!directRing.GetProperty("力学性能").GetProperty("厂家公布值").TryGetProperty("弹性模量", out _), "未标弹性模量单位不能推断");
    var rawTungsten = J(MaterialLibrary.Query("grade", grade: "钨铸铁", basis: "source"));
    Equal("钨铸铁", rawTungsten.GetProperty("材料名称").GetString()!);
    Equal("DH33", rawTungsten.GetProperty("代表牌号").GetString()!);
    Equal("HT350", J(Q("稀土铸铁")).GetProperty("牌号").GetString()!);
    Equal("KmTBNi4Cr2-GT", J(Q("镍铬铜冷硬铸铁")).GetProperty("牌号").GetString()!);
    Equal("KmTBCr2", J(Q("普通冷硬铸铁")).GetProperty("牌号").GetString()!);
    Equal(5, J(Q("普通冷硬铸铁")).GetProperty("原配方（非代表牌号指标）").GetProperty("化学成分").EnumerateObject().Count());
    Equal("family", J(Q("磷稀土铸铁")).GetProperty("missing").GetString()!);
    var filteredRing = J(Q("钨铸铁", "硬度"));
    Equal("240～330 HV30", filteredRing.GetProperty("取值").GetString()!);
    Equal("DH33", filteredRing.GetProperty("牌号").GetString()!);
    Equal("≥450 MPa", J(MaterialLibrary.Query("strength", grade: "DH33", property: "*")).GetProperty("抗弯强度").GetString()!);
    Equal("0.4~0.35（原表区间倒置，待核）", (string)MaterialLibrary.Query("grade", grade: "钨铸铁", property: "W", basis: "source"));
    foreach (var selection in MaterialRepresentatives.Selections)
    {
        var selected = J(Q(selection.Material, family: selection.Family));
        Equal(selection.Grade, selected.GetProperty("牌号").GetString()!);
        True(selected.TryGetProperty("原配方（非代表牌号指标）", out _) && selected.TryGetProperty("选型说明", out _), "全部代表选型必须可追溯");
        var canonical = J(Q(selection.Grade));
        Equal(canonical.GetProperty("化学成分").GetRawText(), selected.GetProperty("化学成分").GetRawText());
        Equal(canonical.GetProperty("力学性能").GetRawText(), selected.GetProperty("力学性能").GetRawText());
    }
    var guide = J(Q("MTCrMoCu35"));
    Equal("0.15～0.25 %", guide.GetProperty("化学成分").GetProperty("Cr").GetString()!);
    var references = guide.GetProperty("同族参考配方（非本牌号标准指标）");
    Equal(3, references.EnumerateObject().Count());
    Equal(3, references.GetProperty("汽缸套用耐磨铸铁/铬钼铜铸铁").GetProperty("化学成分").EnumerateObject().Count());
    Equal(17, MaterialLibrary.Data.Where(f => f.Family == "机床导轨用耐磨铸铁").Select(f => f.Grade).Distinct()
        .Count(g => J(Q(g)).TryGetProperty("同族参考配方（非本牌号标准指标）", out _)));
    var serialization = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    var sizes = new List<int>();
    foreach (var group in MaterialLibrary.Data.Where(f => !f.RowId.StartsWith("steel:") && !f.RowId.StartsWith("nonferrous:")).GroupBy(f => (f.Family, Grade: System.Text.RegularExpressions.Regex.Replace(f.Grade, @"\s|\*", "").ToUpperInvariant())))
    {
        var card = J(Q(group.First().Grade, family: group.Key.Family));
        True(!card.TryGetProperty("missing", out _) && !card.TryGetProperty("error", out _), "单牌号卡片不要求补属性/条件");
        sizes.Add(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(card, serialization)));
    }
    Console.WriteLine($"Material cards: {sizes.Count}, median {sizes.Order().ElementAt(sizes.Count / 2)} B, max {sizes.Max()} B");
    True(sizes.Max() < 5000, "包括同族配方的卡片大小受限");
    var command = Registry().All.Single(c => c.Name == "norma.material.grade");
    True(command.Parameters.All(p => !p.Required), "查询过滤参数全部可选");
    var response = command.Handler(new CommandContext(command, new Dictionary<string, string> { ["grade"] = "HT250" }, "test", null, CancellationToken.None)).Result;
    Equal("HT250", J(response.Data!).GetProperty("牌号").GetString()!);
    var representativeResponse = command.Handler(new CommandContext(command, new Dictionary<string, string> { ["grade"] = "钨铸铁" }, "test", null, CancellationToken.None)).Result;
    Equal("DH33", J(representativeResponse.Data!).GetProperty("牌号").GetString()!);
    var sourceResponse = command.Handler(new CommandContext(command, new Dictionary<string, string> { ["grade"] = "钨铸铁", ["basis"] = "source" }, "test", null, CancellationToken.None)).Result;
    Equal("钨铸铁", J(sourceResponse.Data!).GetProperty("材料名称").GetString()!);
}

// REQ-STEEL-001: independent native-table references, units, products and complete cards.
static void TestSteelMaterials()
{
    var tables = SteelMaterials.Tables;
    Equal(227, tables.Count);
    Equal(153, tables.Select(t => t.Key).Distinct().Count());
    Equal(3112, tables.Sum(t => t.Rows.Length));
    True(tables.All(t => t.SourceId.StartsWith("jxclsj:") && t.ByteOffset > 0 && t.HeaderRows.Length > 0), "钢材源表必须可追溯");
    True(tables.All(t => t.Rows.All(r => r.Length == t.Fields.Length)), "源表行列完整");
    var facts = SteelMaterials.Facts;
    string V(string row, string p) => facts.Single(f => f.RowId == row && f.Property == p).Value;
    Equal("0.42~0.50 %", V("steel:30:1:12", "C"));
    Equal("≥600 MPa", V("steel:31:1:12", "抗拉强度"));
    Equal("0.80~1.10 %", V("steel:56:2:11", "Cr"));
    Equal("≥980 MPa", V("steel:57:1:29", "抗拉强度"));
    Equal("≤207 HBS", V("steel:57:1:29", "硬度HBS"));
    True(facts.Single(f => f.RowId == "steel:57:1:29" && f.Property == "硬度HBS").Condition.Contains("供应状态"), "退火供应硬度不能假装调质试样硬度");
    Equal("179~207 HBS", V("steel:27:1:3", "硬度HBS"));
    Equal("YB/T1-1980", tables.Single(t => t.Id == "steel:27:1").Standard.Single());
    Equal("GB/T3086-1982", tables.Single(t => t.Id == "steel:27:2").Standard.Single());
    True(facts.Single(f => f.RowId == "steel:27:1:3" && f.Property == "硬度HBS").Condition.Contains("退火后"), "轴承钢硬度状态保留");
    Equal("≥590 MPa", V("steel:59:1:1", "抗拉强度"));
    Equal("≥18 %", V("steel:59:1:25", "伸长率δ5"));
    Equal("≥235 MPa", V("steel:104:1:1", "屈服点"));
    Equal("620 MPa", V("steel:73:1:1", "抗拉强度"));
    Equal("≤0.20 %", V("steel:2:1:1", "C"));
    True(facts.Any(f => f.RowId.StartsWith("steel:170:") && f.Tool == "strength" && f.Property == "抗拉强度" && f.Grade == "1Cr18Ni9Ti" && f.Condition.Contains("钢丝状态=R")), "最右侧牌号布局不得丢掉钢丝性能");
    True(!facts.Any(f => f.Grade.EndsWith("-R")), "钢丝状态不混入材料牌号");
    True(facts.Any(f => f.Property == "压痕直径" && f.Value == "≥4.3 mm"), "压痕直径不能带硬度单位");
    True(facts.Any(f => f.Value.Contains("原表区间倒置")), "原表倒置区间必须标注");
    True(!facts.Any(f => f.Property == "值"), "横置表必须返回具体属性和单位");
    var sizes = new List<int>();
    var encoder = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    foreach (var group in facts.GroupBy(f => f.Grade))
    {
        var card = JsonSerializer.SerializeToElement(MaterialLibrary.Query("grade", grade: group.Key));
        Equal("钢", card.GetProperty("类别").GetString()!);
        True(!card.TryGetProperty("missing", out _), "钢跨产品和标准合一张牌号卡");
        // Optional paging is never allowed to truncate a full grade card.
        Equal(card.GetRawText(), JsonSerializer.SerializeToElement(MaterialLibrary.Query("grade", grade: group.Key, offset: 10, limit: 1)).GetRawText());
        foreach (var value in group.Select(f => f.Value).Distinct())
            True(card.GetRawText().Contains(JsonSerializer.Serialize(value)), "卡片遗漏已收录值 " + group.Key + " " + value);
        sizes.Add(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(card, encoder)));
    }
    Console.WriteLine($"Steel cards: {sizes.Count}, median {sizes.Order().ElementAt(sizes.Count / 2)} B, max {sizes.Max()} B; {facts.Count} facts");
    Equal("1.40~1.65 %", (string)MaterialLibrary.Query("grade", grade: "GCr15", property: "Cr"));
    var command = Registry().All.Single(c => c.Name == "norma.material.grade");
    var response = command.Handler(new CommandContext(command, new Dictionary<string, string> { ["grade"] = "GCr15" }, "test", null, CancellationToken.None)).Result;
    Equal("GCr15", JsonSerializer.SerializeToElement(response.Data!).GetProperty("牌号").GetString()!);
}

// REQ-MATERIAL-009: independent native references, alias ambiguity and complete condition sets.
// REQ-MATERIAL-010: independent native reference cells, formulation isolation and terse lookup.
static void TestProductSpecs()
{
    JsonElement J(object value) => JsonSerializer.SerializeToElement(value);
    Equal(112, ProductSpecs.Pages.Count);
    Equal(164, ProductSpecs.Entries.SelectMany(e => e.Values).Select(c => (c.Page, c.Table)).Distinct().Count());
    Equal(9806, ProductSpecs.Entries.Count);
    Equal("2.15 kg·m^-1", (string)ProductSpecs.Query("结构用热轧无缝钢管", "{\"外径\":32,\"壁厚\":3}", "理论重量"));
    var beam = J(ProductSpecs.Query("热轧普通工字钢", "20a"));
    Equal("27.929 kg·m^-1", beam.GetProperty("数据").GetProperty("理论重量").GetString()!);
    Equal("200 mm", beam.GetProperty("数据").GetProperty("h").GetString()!);
    True(beam.GetProperty("原注").EnumerateArray().Any(v => v.GetString()!.Contains("原表")), "可疑原表值的注释不能丢失");
    var missing = J(ProductSpecs.Query("结构用热轧无缝钢管", "{\"外径\":32}"));
    Equal("spec.壁厚", missing.GetProperty("missing").GetString()!);
    Equal(JsonValueKind.Null, missing.GetProperty("default").ValueKind);
    var hole = J(ProductSpecs.Query("结构用热轧无缝钢管", "{\"外径\":32,\"壁厚\":8.5}"));
    Equal("spec.壁厚", hole.GetProperty("missing").GetString()!);
    True(!hole.GetProperty("options").EnumerateArray().Any(v => v.GetString() == "8.5 mm"), "空格被误收为供应组合");
    var gradeMissing = J(ProductSpecs.Query("铝青铜板的尺寸及厚度允许偏差", "{\"厚度\":1.2,\"宽度\":400}", "厚度允许偏差"));
    Equal("spec.精度", gradeMissing.GetProperty("missing").GetString()!);
    Equal("±0.075 mm", (string)ProductSpecs.Query("铝青铜板的尺寸及厚度允许偏差", "{\"厚度\":1.2,\"宽度\":400,\"精度\":\"普通级\"}", "厚度允许偏差"));
    Equal("±0.060 mm", (string)ProductSpecs.Query("铝青铜板的尺寸及厚度允许偏差", "{\"厚度\":1.2,\"宽度\":400,\"精度\":\"较高级\"}", "厚度允许偏差"));
    Equal("±0.25 mm", (string)ProductSpecs.Query("热轧圆钢和方钢的尺寸允许偏差", "{\"尺寸\":20,\"精度\":\"1组\"}", "允许偏差"));
    Equal("±0.30 mm", (string)ProductSpecs.Query("热轧圆钢和方钢的尺寸允许偏差", "{\"尺寸\":20.01,\"精度\":\"1组\"}", "允许偏差"));
    Equal("+1.5 -0.5 mm", (string)ProductSpecs.Query("聚四氟乙烯棒材", "{\"直径\":50}", "直径允许公差"));
    Equal("+15 0 mm", (string)ProductSpecs.Query("钛-钢复合板", "{\"厚度\":10,\"宽度\":1000}", "宽度允许偏差"));
    Equal("+10 0 mm", (string)ProductSpecs.Query("钛-不锈钢复合板", "{\"厚度\":10,\"宽度\":1000}", "宽度允许偏差"));
    var variable = J(ProductSpecs.Query("结构用热轧无缝钢管", "{\"外径\":32,\"壁厚\":\"*\"}", "理论重量", limit: 2));
    Equal(2, variable.GetProperty("rows").GetArrayLength());
    Equal(2, variable.GetProperty("nextOffset").GetInt32());
    True(variable.GetProperty("total").GetInt32() > 2, "变化表未分页");
    var properties = J(ProductSpecs.Query("热轧普通工字钢", "20a", "*", limit: 100));
    True(properties.GetProperty("rows").GetArrayLength() >= 15 && properties.TryGetProperty("原注", out _), "属性表或原注丢失");
    Equal(beam.GetRawText(), J(ProductSpecs.Query("热轧普通工字钢", "20a", offset: 999, limit: 1)).GetRawText());
    True(J(ProductSpecs.Query("无缝钢管")).GetProperty("options").GetArrayLength() > 1, "部分产品名不应自动选首项");
    foreach (var request in new Func<object>[]
    {
        () => ProductSpecs.Query("结构用热轧无缝钢管", "{\"外径\":\"*\",\"壁厚\":\"*\"}"),
        () => ProductSpecs.Query("结构用热轧无缝钢管", "{\"外径\":32,\"壁厚\":\"*\"}", "*"),
        () => ProductSpecs.Query("工字钢", "{bad}"),
        () => ProductSpecs.Query("工字钢", "{\"型号\":null}"),
        () => ProductSpecs.Query("工字钢", "{\"型号\":20,\"型号\":20}"),
        () => ProductSpecs.Query(limit: 101), () => ProductSpecs.Query(offset: -1),
    })
    {
        var rejected = false;
        try { request(); } catch (ArgumentException) { rejected = true; }
        True(rejected, "非法规格请求未拒绝");
    }
    True(!ProductSpecs.Matches(new ProductSpecs.Axis(">7~20", "mm", true), "7"), "开区间边界错误");
    True(ProductSpecs.Matches(new ProductSpecs.Axis(">7~20", "mm", true), "20 mm"), "闭区间/显式单位匹配错误");
    True(!ProductSpecs.Matches(new ProductSpecs.Axis("20~7", "mm", true), "10"), "倒置区间不可猜测");
    True(!ProductSpecs.Matches(new ProductSpecs.Axis("20±1", "mm", true), "20"), "尺寸与公差不可混作连续范围");
    var command = Registry().All.Single(c => c.Name == "norma.product.spec");
    True(command.Readonly && command.Parameters.All(p => !p.Required), "产品规格必须只读且参数可选");
    True(command.Parameters.Single(p => p.Name == "name").Description.Contains("结构用热轧无缝钢管(外径/壁厚)"), "产品及输入选项未写入工具提示词");
    var response = command.Handler(new CommandContext(command, new Dictionary<string, string>
        { ["name"] = "结构用热轧无缝钢管", ["spec"] = "{\"外径\":32,\"壁厚\":3}", ["property"] = "理论重量" }, "test", null, CancellationToken.None)).Result;
    Equal("2.15 kg·m^-1", response.Data!.ToString()!);
    True(NormaCommands.PageRows().Any(r => r["id"] == "product.spec" && r["topic"] == "机械材料"), "产品库目录遗漏");
}

static void TestNamedMaterials()
{
    var facts = NamedMaterials.Facts;
    Equal(124, NamedMaterials.Pages.Count);
    Equal(159, facts.Select(f => (f.Page, f.Table)).Distinct().Count());
    Equal(6762, facts.Count);
    Equal(514, facts.Select(f => f.Name).Distinct().Count());
    Equal("2.1~2.2 g·cm^-3", (string)NamedMaterials.Query("PTFE", "密度"));
    var pvc = JsonSerializer.SerializeToElement(NamedMaterials.Query("PVC"));
    Equal("name", pvc.GetProperty("missing").GetString()!);
    Equal(2, pvc.GetProperty("options").GetArrayLength());
    Equal(2, JsonSerializer.SerializeToElement(NamedMaterials.Query("POM")).GetProperty("options").GetArrayLength());
    Equal("34.8", facts.Single(f => f.Name == "冷杉" && f.Property.Contains("顺纹抗压强度")).Value);
    True(facts.Single(f => f.Name == "冷杉" && f.Property.Contains("顺纹抗压强度")).Condition.Contains("四川大渡河、青衣江"), "木材地区条件丢失");
    var hybrid = facts.Single(f => f.Name == "Q-C/G-4" && f.Property == "纵向抗拉强度/MPa");
    Equal("945", hybrid.Value);
    True(hybrid.Condition.Contains("QY8911双马来体系") && !hybrid.Condition.Contains("4211"), "混杂层板基体被混淆");
    var carbon = facts.Where(f => f.Name == "T300/4211" && f.Property == "纵向抗拉强度/MPa").ToArray();
    True(carbon.Any(f => f.Value == "1396") && carbon.Any(f => f.Value == "1310" && f.Condition.Contains("=-60")), "碳纤层板常温/低温数据丢失");
    True(carbon.Single(f => f.Value == "1396").Condition.Contains("体积分数Φf=(60±3)%"), "T300/4211与脚注②的含量关联丢失");
    Equal("6.723", facts.Single(f => f.Name == "铸造铜-石墨复合材料" && f.Property.StartsWith("密度")).Value);
    True(facts.Where(f => f.Name == "铸造石墨铝合金复合材料" && f.Property.StartsWith("密度")).Select(f => f.Value).SequenceEqual(["2.63", "2.52"]), "同页铜基和铝基数据混合");
    True(facts.Any(f => f.Page == 4002 && f.Value == "—"), "耐蚀评价的—是“不推荐”，不能当空值删除");
    True(facts.Any(f => f.Page == 4282 && f.Condition.Contains("体积分数")), "玻纤体积分数丢失");
    True(facts.Any(f => f.Page == 4288 && f.Name.Contains("质量分数")), "碳纤质量分数丢失");
    True(JsonSerializer.Serialize(NamedMaterials.Query("T300/4211")).Contains("\\u2460"), "碳纤维体积分数脚注丢失");
    var first = JsonSerializer.SerializeToElement(NamedMaterials.Query(limit: 1));
    Equal("name", first.GetProperty("missing").GetString()!);
    Equal(JsonValueKind.Null, first.GetProperty("default").ValueKind);
    Equal(1, first.GetProperty("options").GetArrayLength());
    Equal(1, first.GetProperty("nextOffset").GetInt32());
    Equal(JsonSerializer.Serialize(NamedMaterials.Query("PTFE")), JsonSerializer.Serialize(NamedMaterials.Query("PTFE", offset: 10000, limit: 1)));
    var table = JsonSerializer.SerializeToElement(NamedMaterials.Query("PTFE", "*", "常用工程塑料物理-力学性能"));
    True(table.GetProperty("rows").GetArrayLength() >= 10, "显式属性变化未返回表");
    foreach (var request in new Func<object>[] { () => NamedMaterials.Query("PTFE", "*", "*"), () => NamedMaterials.Query(limit: 101), () => NamedMaterials.Query(offset: -1) })
    {
        var rejected = false;
        try { request(); } catch (ArgumentException) { rejected = true; }
        True(rejected, "非法材料查询未拒绝");
    }
    var sizes = new List<int>();
    foreach (var group in facts.GroupBy(f => (f.Name, f.Family)))
    {
        var card = NamedMaterials.Query(group.Key.Name, family: group.Key.Family);
        var json = JsonSerializer.Serialize(card);
        True(!JsonSerializer.SerializeToElement(card).TryGetProperty("missing", out _), "具体材料卡不应要求补参");
        foreach (var value in group.Select(f => f.Value).Distinct())
            True(json.Contains(JsonSerializer.Serialize(value)[1..^1]), "资料卡遗漏原表单元格：" + group.Key.Name + "/" + value);
        sizes.Add(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(card, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
    }
    Console.WriteLine($"Named material cards: {sizes.Count}, median {sizes.Order().ElementAt(sizes.Count / 2)} B, max {sizes.Max()} B");
    var command = Registry().All.Single(c => c.Name == "norma.material.library");
    True(command.Readonly && command.Parameters.All(p => !p.Required), "材料库只读且过滤参数均可选");
    var response = command.Handler(new CommandContext(command, new Dictionary<string, string> { ["name"] = "PTFE", ["property"] = "密度" }, "test", null, CancellationToken.None)).Result;
    Equal("2.1~2.2 g·cm^-3", response.Data!.ToString()!);
}

static void TestNonFerrousMaterials()
{
    var tables = NonFerrousMaterials.Tables;
    Equal(282, tables.Count);
    Equal(139, tables.Select(t => t.Key).Distinct().Count());
    Equal(3524, tables.Sum(t => t.Rows.Length));
    True(tables.All(t => t.SourceFile == "jxclsj/jxclsj.ysc" && t.Path.Length > 0 && t.HeaderRows.Length > 0), "新材料源表可追溯");
    True(tables.All(t => t.Rows.All(r => r.Length == t.Fields.Length)), "新材料表行列完整");
    True(tables.All(t => t.ByteOffset > 0 && t.SourceId.StartsWith("jxclsj:")), "新材料原文件页索引完整");
    True(tables.Single(t => t.Key == 1076).Scenario.Contains(">0.5mm"), "黄铜带厚度条件来自正文，不能依赖截断窗口标题");
    True(tables.Single(t => t.Key == 1046 && t.Id.EndsWith(":3")).Scenario.Contains("高温持久"), "高温子表不沿用室温主标题");
    var facts = NonFerrousMaterials.Facts;
    foreach (var grade in tables.SelectMany(t => t.Rows.SelectMany(r => SteelMaterials.Grades(r[t.GradeColumn], t.Key))).Distinct(StringComparer.OrdinalIgnoreCase))
        True(facts.Any(f => f.Grade.Equals(grade, StringComparison.OrdinalIgnoreCase)), "原表牌号不能因全部性能空缺而消失 " + grade);
    CastIronLookup.Fact F(int page, string grade, string property) => facts.First(f => f.RowId.StartsWith("nonferrous:" + page + ":") && f.Grade == grade && f.Property == property);
    Equal("≥99.95 %", F(1001, "T1", "Cu+Ag").Value);
    Equal("≤0.001 %", F(1001, "T1", "P").Value);
    Equal("6.0～7.0 %", F(1005, "QSn6.5-0.1", "Sn").Value);
    Equal("0.10～0.25 %", F(1005, "QSn6.5-0.1", "P").Value);
    Equal("240 MPa", F(1002, "T2", "抗拉强度").Value);
    Equal("110000 MPa", F(1002, "T2", "弹性模量").Value);
    True(facts.Any(f => f.Grade == "QBe2" && f.Property == "硬度HV" && f.Value.Contains("90^⑤ HV")), "青铜脚注⑤明确是HV，不得沿用HBS表头");
    True(!facts.Any(f => f.Tool == "grade" && f.Value.Contains("Ti-6Al-4V")), "钛合金成分组是身份条件，不能加百分号当元素量");
    Equal("≥155 MPa", F(1017, "ZAlSi7Mg", "抗拉强度").Value);
    Equal("2.66 g/cm³", F(1020, "ZAlSi7Mg", "密度ρ/g·cm^-3").Value);
    Equal("4.5 g/cm³", F(1029, "TA1", "0℃密度ρ/g·cm^-3").Value);
    Equal("4.0～6.0 %", F(1036, "CuPb9Sn5", "Sn").Value);
    Equal("≥100 MPa", F(1095, "F0001J", "抗拉强度").Value);
    Equal("≥4.59×10^-15", F(1107, "F2100G5", "渗透系数/m^2").Value);
    Equal("515 MPa", F(2031, "NS111", "抗拉强度").Value);
    True(facts.Any(f => f.Grade == "6061" && f.Tool == "grade") && facts.Any(f => f.Grade == "6061" && f.Tool == "strength"), "四位铝牌号成分与力学性能齐全");
    True(facts.Any(f => f.Grade == "H96" && f.Value.Contains("原表区间倒置")), "原表倒置区间保留");
    True(facts.Any(f => f.Grade == "GH1015" && f.Condition.Contains("单位倍率疑点")), "异常倍率不得换算");
    True(!facts.Any(f => f.Value.Contains("余～量")), "排版空格不构造成分区间");
    var encoder = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    var sizes = new List<int>();
    foreach (var group in facts.GroupBy(f => f.Grade, StringComparer.OrdinalIgnoreCase))
    {
        var card = JsonSerializer.SerializeToElement(MaterialLibrary.Query("grade", grade: group.Key));
        True(!card.TryGetProperty("missing", out _) && !card.TryGetProperty("error", out _), "新牌号直接得到卡片 " + group.Key);
        Equal(card.GetRawText(), JsonSerializer.SerializeToElement(MaterialLibrary.Query("grade", grade: group.Key, limit: 1, offset: 100)).GetRawText());
        foreach (var value in group.Select(f => f.Value).Distinct())
            True(card.GetRawText().Contains(JsonSerializer.Serialize(value)), "新卡片遗漏值 " + group.Key + " " + value);
        sizes.Add(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(card, encoder)));
    }
    Console.WriteLine($"New material cards: {sizes.Count}, median {sizes.Order().ElementAt(sizes.Count / 2)} B, max {sizes.Max()} B; {facts.Count} facts");
    var castAl = JsonSerializer.SerializeToElement(MaterialLibrary.Query("grade", grade: "ZL101"));
    Equal("ZAlSi7Mg", castAl.GetProperty("牌号").GetString()!);
    True(castAl.TryGetProperty("化学成分", out _) && castAl.TryGetProperty("力学性能", out _) && castAl.TryGetProperty("物理及使用性能", out _), "铸铝代号关联全部数据");
    Equal("2A12", JsonSerializer.SerializeToElement(MaterialLibrary.Query("grade", grade: "LY12")).GetProperty("牌号").GetString()!);
    var ambiguous = JsonSerializer.SerializeToElement(MaterialLibrary.Query("grade", grade: "GH140"));
    Equal("grade", ambiguous.GetProperty("missing").GetString()!);
    Equal(2, ambiguous.GetProperty("options").GetArrayLength());
    var density = F(1020, "ZAlSi7Mg", "密度ρ/g·cm^-3");
    Equal(density.Value, (string)MaterialLibrary.Query("grade", grade: "ZL101", property: density.Property, condition: density.Condition));
    var strengthOnly = JsonSerializer.SerializeToElement(MaterialLibrary.Query("strength", grade: "ZL101"));
    True(!strengthOnly.TryGetProperty("化学成分", out _) && !strengthOnly.TryGetProperty("物理及使用性能", out _), "力学入口不混入成分和物理性能");
}

// REQ-MATERIAL-011/012, REQ-PRODUCT-003: independent native reference cells and omissions.
// REQ-MATERIAL-013: two material keys, one specification surface, no duplicate registrations.
static void TestConsolidatedMaterials()
{
    JsonElement J(object value) => JsonSerializer.SerializeToElement(value);
    var registry = Registry();
    var retired = PhysicalTables.Tables.Select(t => "norma.material." + t.Key).Append("norma.material.strength").ToArray();
    foreach (var name in retired)
    {
        True(!registry.TryGet(name, out _), "重复工具仍在注册：" + name);
        True(!NormaCommands.PageRows().Any(r => r["command"] == name), "退役工具仍在目录");
    }
    Equal(56, registry.All.Count());
    var grade = registry.All.Single(c => c.Name == "norma.material.grade");
    True(grade.Parameters.Any(p => p.Name == "grade") && !grade.Parameters.Any(p => p.Name == "name"), "牌号是独立主键");
    var mechanical = J(MaterialLibrary.Query("grade", grade: "QT900-2", category: "力学"));
    True(mechanical.TryGetProperty("力学性能", out _) && !mechanical.TryGetProperty("化学成分", out _), "可选分类替代独立强度工具");
    Equal("≥250 MPa", (string)MaterialLibrary.Query("grade", grade: "HT250", property: "抗拉强度", condition: "单铸试棒", category: "力学"));
    Equal("11.37 g/cm³（=t/m³）", (string)NamedMaterials.Query("铅", "密度", category: "密度"));
    Equal("2.5 mm", (string)NamedMaterials.Query("橡胶轮胎对沥青路面", "滚动摩擦力臂", category: "滚动摩擦力臂"));
    Equal("10.6~12.2 10⁻⁶/℃", (string)NamedMaterials.Query("碳钢", "线膨胀系数", "常用材料线膨胀系数（通用参考，非牌号指标）；温区=20~100℃", category: "线膨胀"));
    var options = J(NamedMaterials.Query(category: "滑动摩擦", limit: 2));
    Equal("name", options.GetProperty("missing").GetString()!);
    Equal(JsonValueKind.Null, options.GetProperty("default").ValueKind);
    Equal(2, options.GetProperty("options").GetArrayLength());
    var unknown = J(NamedMaterials.Query("球墨铸铁", "切变模量", category: "弹性"));
    Equal(JsonValueKind.Null, unknown.GetProperty("value").ValueKind);
    Equal("原表未给出", unknown.GetProperty("reason").GetString()!);
    var inverted = J(NamedMaterials.Query("离合器", "摩擦系数", category: "物体摩擦"));
    // A partial object name must offer choices; it cannot select a different load case.
    True(inverted.TryGetProperty("missing", out _) || inverted.TryGetProperty("原注", out _), "对象工况不可自动选首项");
    foreach (var f in ReferenceMaterials.All.Facts.Where(f => f.Page < 10000).GroupBy(f => f.Name))
    {
        var card = NamedMaterials.Query(f.Key, family: "通用参考");
        var serialized = JsonSerializer.Serialize(card);
        foreach (var raw in f.Select(f => f.Value).Distinct())
            True(serialized.Contains(JsonSerializer.Serialize(raw)[1..^1], StringComparison.Ordinal), "合并丢失参考值 " + f.Key + ":" + raw);
        Equal(serialized, JsonSerializer.Serialize(NamedMaterials.Query(f.Key, family: "通用参考", offset: 9999, limit: 1)));
    }
    True(ReferenceMaterials.All.Facts.Where(f => f.Page < 10000).Count() == PhysicalTables.Tables.Sum(t => PhysicalTables.Search(t.Key, null, null, 0, 100).Items.Count * t.Fields.Length), "八表全部单元格必须纳入");
    var productValues = ProductSpecs.Entries.SelectMany(e => e.Values).ToArray();
    Equal(productValues.Length, ProductSpecs.SpecificationEntries.SelectMany(e => e.Values).Count() + productValues.Count(ProductSpecs.IsPerformance));
    True(!ProductSpecs.SpecificationEntries.SelectMany(e => e.Values).Any(ProductSpecs.IsPerformance), "规格库不重复发布纯材料性能");
    foreach (var f in ReferenceMaterials.All.Facts.Where(f => f.Page >= 10000).GroupBy(f => f.Name))
    {
        var card = JsonSerializer.Serialize(NamedMaterials.Query(f.Key, category: "产品性能"));
        foreach (var raw in f.Select(f => f.Value).Distinct()) True(card.Contains(JsonSerializer.Serialize(raw)[1..^1], StringComparison.Ordinal), "迁移丢失产品性能 " + f.Key + ":" + raw);
    }
    True(J(NamedMaterials.Query("HT250")).GetProperty("options").GetArrayLength() == 0, "名称材料库不冒充牌号库");
    foreach (var request in new Action[] { () => NamedMaterials.Query("PTFE", category: "未知"), () => NamedMaterials.Query(name: "*", property: "*"), () => MaterialLibrary.Query("grade", grade: "HT250", category: "未知") })
    {
        var rejected = false; try { request(); } catch (ArgumentException) { rejected = true; }
        True(rejected, "非法分类或多变量未拒绝");
    }
}

static void TestMaterialSupplement()
{
    JsonElement J(object v) => JsonSerializer.SerializeToElement(v);
    var facts = MaterialSupplement.Facts;
    Equal(17, MaterialSupplement.Pages.Count);
    Equal(2828, facts.Count);
    var density = facts.Single(f => f.Grade == "1Cr18Ni9" && f.Property == "密度");
    Equal("7.93 g·cm^-3", density.Value);
    var filtered = J(MaterialLibrary.Query("grade", grade: "1Cr18Ni9", property: "密度", condition: density.Condition));
    Equal(density.Value, filtered.GetProperty("value").GetString()!);
    True(filtered.GetProperty("原注").GetArrayLength() > 0, "单值必须保留该表的疑点原注");
    True(facts.Any(f => f.Grade == "20R" && f.Condition.Contains("21～36mm") && f.Condition.Contains("200℃") && f.Value == "≥186 MPa"), "压力容器板高温强度必须保留厚度与温度");
    True(facts.Any(f => f.Grade == "ZAlSi7MgA" && f.Property == "固溶温度" && f.Value == "535±5 ℃"), "固溶温度原值");
    True(J(MaterialLibrary.Query("grade", grade: "ZL101A")).TryGetProperty("热处理工艺", out _), "热处理并入原牌号卡");
    True(facts.Any(f => f.Grade == "65Mn" && f.Condition.Contains("0.08mm") && f.Condition.Contains("Ⅰ级") && f.Value == "≥29 次"), "弯曲次数按牌号、厚度和强度级别分开");
    Equal("65~75 %", (string)MaterialLibrary.Query("grade", grade: "F1001G", property: "铁"));
    var suspect = J(MaterialLibrary.Query("grade", grade: "F1106G", property: "铅"));
    Equal("68 %", suspect.GetProperty("value").GetString()!);
    True(suspect.GetProperty("原注")[0].GetString()!.Contains("疑似列名有误"), "不丢掉成分表头疑点");
    var variable = J(MaterialLibrary.Query("grade", grade: "F1106G", property: "*", condition: facts.First(f => f.Grade == "F1106G").Condition));
    True(variable.TryGetProperty("原注", out _), "显式变化表也保留疑点");
    True(!facts.Any(f => f.Grade == "4Nb" || f.Grade == "0Cr17Ni4Cu"), "原文拆行不能猜成两个牌号");
    foreach (var g in facts.GroupBy(f => (f.Family, f.Grade)))
    {
        var card = J(MaterialLibrary.Query("grade", family: g.Key.Family, grade: g.Key.Grade));
        True(!card.TryGetProperty("missing", out _), "补充牌号完整卡不要求额外条件 " + g.Key);
        foreach (var v in g.Select(f => f.Value).Distinct())
            True(card.GetRawText().Contains(JsonSerializer.Serialize(v)), "补充卡丢值 " + g.Key + " " + v);
    }
    Equal("≥7.4 mm", (string)NamedMaterials.Query("连续热镀锌薄钢板和钢带的杯突试验值", "杯突冲压深度", "厚度=0.5mm；加工性能=SC", category: "产品性能"));
    Equal("5.05/2.25 g/m（原表单位）", J(ProductSpecs.Query("电镀锡薄钢板和钢带的镀锡量", "{\"镀锡符号\":\"D1\"}", "最小平均镀锡量")).GetProperty("value").GetString()!);
    Equal("0.25 mm", (string)ProductSpecs.Query("压力容器用钢板公称厚度的附加值", "{\"厚度\":10,\"宽度\":1000}", "计算重量厚度附加值"));
    Equal("0.5~2.5 mm", (string)ProductSpecs.Query("耐蚀合金冷轧无缝管直径及壁厚", "{\"直径\":10}", "壁厚范围"));
    Equal("≤60º", (string)ProductSpecs.Query("铝及铝合金挤压棒材（方、六角棒）的扭拧度", "{\"内切圆直径\":14,\"精度\":\"普通级\",\"长度基准\":\"1m长度上\"}", "扭拧度上限"));
    var wave = J(ProductSpecs.Query("铝及铝合金中间浪或边缘浪偏差", "{\"合金类别\":\"1×××系，8×××系，除3004外的3×××系及5005，5050\",\"厚度\":1,\"波峰中心距\":500}", "中间浪或边缘浪偏差"));
    True(wave.GetProperty("原注").EnumerateArray().Any(n => n.GetString()!.Contains("不适用于O、F、Hx8")), "波浪公差适用状态脚注必须随值返回");
    using var stream = typeof(MaterialLibrary).Assembly.GetManifestResourceStream("Norma.Formulas.materials-volume-audit.json")!;
    var audit = JsonDocument.Parse(stream).RootElement;
    var pages = audit.GetProperty("pages").EnumerateArray().ToArray();
    Equal(911, pages.Length);
    Equal(911, pages.Select(p => p.GetProperty("path").GetString()).Distinct().Count());
    Equal(303, pages.Count(p => p.GetProperty("status").GetString() == "pending_product"));
    True(pages.All(p => p.GetProperty("reason").GetString()!.Length > 0), "审计不能没有范围解释");
}

static void TestCastIronLookup()
{
    JsonElement J(object v) => JsonSerializer.SerializeToElement(v);
    object L(string tool, string? grade = null, string? property = null, string? condition = null, string? family = null)
        => CastIronLookup.Lookup(tool, family, grade, property, condition);
    Equal("≥250 MPa", (string)L("strength", "HT250", "抗拉强度", "单铸试棒"));
    Equal("14～18 %", (string)L("grade", "KmTBCr15Mo", "Cr"));
    Equal("≤1 %", (string)L("grade", "STSi15R", "C")); // max inferred from source header
    Equal("原表未给出", (string)L("grade", "QT900-2", "化学成分"));
    Equal("0.06～0.08 %（加入量）", (string)L("grade", "磷锑铸铁", "Sb"));
    Equal("T", (string)L("grade", "退火态", "状态代号"));
    Equal("0.4~0.35（原表区间倒置，待核）", (string)L("grade", "钨铸铁", "W"));
    var missing = J(L("strength", "HT250"));
    Equal("property", missing.GetProperty("missing").GetString()!);
    Equal(JsonValueKind.Null, missing.GetProperty("default").ValueKind);
    True(missing.GetProperty("options").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "抗拉强度" }), "缺参返回选项，不自动展开");
    var conditions = J(L("strength", "HT250", "抗拉强度"));
    Equal("condition", conditions.GetProperty("missing").GetString()!);
    True(conditions.GetProperty("options").EnumerateArray().Any(v => v.GetString() == "单铸试棒"), "试样选项遗漏");
    var table = J(L("strength", "QT900-2", "*"));
    Equal("≥900 MPa", table.GetProperty("抗拉强度").GetString()!);
    Equal("280～360 HBS", table.GetProperty("硬度").GetString()!);
    True(!table.TryGetProperty("catalog", out _) && !table.TryGetProperty("tables", out _), "不返回目录与表头包装");
    Equal("≥900 MPa", (string)L("strength", "QT900-2", "抗拉强度"));
    var varyingConditions = J(L("strength", "HT250", "抗拉强度", "*"));
    True(varyingConditions.TryGetProperty("values", out _), "变化条件长表应分页");
    Equal("condition", J(L("strength", "HT250", "*")).GetProperty("missing").GetString()!);
    Equal("14 MPa", (string)L("strength", "RTSi5", "抗拉强度", "700℃高温短时"));
    Equal("≥58 HRC", (string)L("strength", "KmTBCr15Mo", "硬度HRC", "硬化态或硬化态并去应力处理"));
    Equal("condition", J(L("strength", "镍铬铜冷硬铸铁", "白口层硬度")).GetProperty("missing").GetString()!);
    Equal("50～55 HRC", (string)L("strength", "镍铬铜冷硬铸铁", "白口层硬度", "600℃回火后"));
    var cylinder = J(L("strength", "铬钼铜铸铁", "抗拉强度", family: "汽缸套用耐磨铸铁"));
    Equal("condition", cylinder.GetProperty("missing").GetString()!);
    Equal(3, cylinder.GetProperty("options").GetArrayLength());
    Equal("≥280 MPa", (string)L("strength", "铬钼铜铸铁", "抗拉强度", "同名配方3", "汽缸套用耐磨铸铁"));
    var manyStars = false; try { L("strength", "*", "*"); } catch (ArgumentException) { manyStars = true; }
    True(manyStars, "禁止多维展开");
    // Every normalized value must be addressable without dumping unrelated rows or choosing the first value.
    foreach (var fact in CastIronLookup.Facts)
    {
        var result = L(fact.Tool, fact.Grade, fact.Property, fact.Condition, fact.Family);
        if (result is string value) Equal(fact.Value, value);
        else True(J(result).TryGetProperty("error", out _), "完整参数不能返回缺参/整库");
    }
    var command = Registry().All.Single(c => c.Name == "norma.material.grade");
    var response = command.Handler(new CommandContext(command, new Dictionary<string, string> { ["grade"] = "HT250", ["property"] = "抗拉强度", ["condition"] = "单铸试棒" }, "test", null, CancellationToken.None)).Result;
    Equal("≥250 MPa", J(response.Data!).GetString()!);
    True(J(response.Data!).GetRawText().Length < 30, "正常取值必须保持常数大小");
}

static void TestCastIron()
{
    // Independent inventory from the native directory and reviewed tables, including both two-table pages.
    int[] entries = [214,891,346,614,296,294,295,295,297,574,574,575,489,490,192,193,471,470,344,345,301,300,303,302,551,550,299,298,402,403];
    int[] counts = [15,39,20,5,6,6,22,20,17,8,10,8,10,10,5,5,5,5,9,9,17,17,13,13,10,10,13,13,9,9];
    Equal(30, CastIron.Tables.Count);
    Equal(28, CastIron.Tables.Select(t => t.Entry).Distinct().Count());
    Equal(358, CastIron.Tables.Sum(t => t.Rows.Length));
    Equal(2265, CastIron.Tables.Sum(t => t.Rows.Sum(r => r.Length)));
    for (var i = 0; i < counts.Length; i++)
    {
        var t = CastIron.Tables[i]; Equal(entries[i], t.Entry); Equal(counts[i], t.Rows.Length);
        True(t.ByteOffset > 0 && t.SourceFile == "jxclsj/jxclsj.ysc" && t.Notes.Length > 0, "出处/脚注遗漏");
        True(t.Fields.All(f => f.Headers.Length > 0 && f.Name.Length > 0), "表头遗漏");
        foreach (var row in t.Rows) Equal(t.Fields.Length, row.Length);
    }
    CastIron.Row R(string table, string grade) => CastIron.Search("strength", table: table, grade: grade, limit: 100).Items.First();
    Near(300, R("iron:346:1", "KTH300-06").Properties["c2"].Min!.Value, 0, "可锻最低抗拉");
    True(R("iron:346:1", "KTH300-06").Properties["c2"].Value is null, "最低值不是实测值");
    True(R("iron:346:1", "KTH300-06").Properties["c1"].Options!.SequenceEqual(new double[] { 12, 15 }), "离散直径不能变成范围");
    Equal(3, CastIron.Search("strength", grade: "KTB450-07").Total);
    Near(335, R("iron:614:1", "RuT420").Properties["c2"].Min!.Value, 0, "蠕墨屈服");
    Equal(8, CastIron.Search("strength", grade: "HT250").Total);
    var walls = R("iron:295:1", "HT250").Properties["c2"];
    True(walls.Min == 4 && walls.Max == 10 && walls.MinInclusive == false && walls.MaxInclusive == true, "壁厚开闭区间");
    var gray = CastIron.Search("strength", table: "iron:295:2", grade: "HT300").Items[2];
    Near(200, gray.Properties["c5"].Min!.Value, 0, "原窗口恢复R25值");
    var rh = CastIron.Search("strength", table: "iron:297:1", q: "260").Items.Single();
    Near(171, rh.Properties["c1"].Value!.Value, 0, "灰铁RH表");
    Near(400, R("iron:574:2", "QT700-2A").Properties["c3"].Min!.Value, 0, "附铸表头最小值");
    Equal(2, CastIron.Search("strength", grade: "Q600-3TA").Total);
    Equal(0, CastIron.Search("strength", grade: "QT600-3A").Total);
    Near(800, R("iron:575:1", "QT-H300").Properties["c2"].Min!.Value, 0, "硬度等级参考最小抗拉");
    Near(14, R("iron:490:1", "RTSi5").Properties["c5"].Value!.Value, 0, "700℃短时值不可按常识改写");
    Near(48, R("iron:193:1", "STSi15R").Properties["c3"].Max!.Value, 0, "硬度最大值");
    Equal("HBC", R("iron:193:1", "STSi15R").Properties["c3"].Unit);
    Near(44, R("iron:470:1", "MQTMn6").Properties["c6"].Min!.Value, 0, "耐磨括号硬度下限");
    Equal("HBC", R("iron:470:1", "MQTMn6").Properties["c6"].Unit);
    Near(58, R("iron:345:1", "KmTBCr15Mo").Properties["c3"].Min!.Value, 0, "抗磨白口硬化态HRC");
    Near(3.2, R("iron:300:1", "MTCrCu35").Properties["c4"].Min!.Value, 0, "导轨挠度");
    Near(1000, R("iron:302:1", "稀土铸铁").Properties["c3"].Max!.Value, 0, "机床零件抗弯区间");
    Equal(3, CastIron.Search("strength", family: "汽缸套", grade: "铬钼铜铸铁").Total);
    var cylinder = R("iron:550:1", "磷铬铸铁").Properties["c1"];
    True(cylinder.Min == 200 && cylinder.MinInclusive == false, "严格大于不能改≥");
    var ring = R("iron:298:1", "镍铬铸铁");
    True(ring.Properties["c6"].Value is null && ring.Properties["c6"].Undefined == "原表未给出", "原表空白不填0");
    var chill = CastIron.Search("strength", table: "iron:403:1", grade: "普通冷硬铸铁").Items;
    Equal(5, chill.Count); Equal("HB", chill[1].Properties["c1"].Unit);
    Near(450, chill[1].Properties["c1"].Min!.Value, 0, "混排硬度单位");
    Near(50, R("iron:403:1", "镍铬铜冷硬铸铁").Properties["c1"].Parts!["600℃回火后"].Min!.Value, 0, "回火条件");
    var chemical = CastIron.Search("grade", table: "iron:192:1", grade: "STSi15R").Items.Single();
    True(chemical.Properties["c1"].Value is null && chemical.Properties["c1"].Max == 1, "化学表头最大值");
    var inverted = CastIron.Search("grade", table: "iron:299:1", grade: "钨铸铁").Items.Single().Properties["c6"];
    True(inverted.Value is null && inverted.Min is null && inverted.Undefined == "原表区间倒置，待核", "倒置不可排序");
    Equal(39, CastIron.Search("grade", section: "heat_state", limit: 100).Total);
    var other = CastIron.Search("grade", table: "iron:551:1", grade: "磷锑铸铁").Items.Single();
    Near(0.06, other.Properties["c9"].Parts!["Sb"].Min!.Value, 0, "其他元素可数值取用");
    True(other.Properties["c9"].Text.Contains("加入量"), "加入量条件不能丢");
    Equal("iron:550:1:9", other.RelatedId!);
    True(CastIron.Search("grade", grade: "QT900-2").Items.All(r => r.Kind == "catalog" && r.Note!.Contains("未在此表给出")), "无化学数据明确说明");
    foreach (var tool in new[] { "grade", "strength" })
    {
        var all = new List<CastIron.Row>(); int? start = 0;
        while (start is not null)
        {
            var page = CastIron.Search(tool, offset: start.Value, limit: 7);
            all.AddRange(page.Items); start = page.NextOffset;
        }
        Equal(CastIron.Search(tool).Total, all.Count);
        Equal(all.Count, all.Select(r => r.Id).Distinct().Count());
        foreach (var row in all) foreach (var cell in row.Properties.Values)
            True(cell.Kind != "text" || cell.Unit.Length == 0, "未解析数值单元格：" + row.Id + " " + cell.Text);
        Equal(0, CastIron.Search(tool, q: "不存在的材料").Total);
        var registry = Registry(); True(registry.TryGet("norma.material.grade", out var command), "缺少铸铁工具");
        Equal("material", command!.CommandClass);
        var result = command.Handler(new CommandContext(command, new Dictionary<string, string> { ["grade"] = "KmTBCr15Mo" }, "test", null, CancellationToken.None)).Result;
        True(result.Success, result.Message); JsonSerializer.Serialize(result.Data);
    }
    foreach (var (start, limit) in new[] { (-1, 20), (0, 0), (0, 101) })
    {
        var rejected = false; try { CastIron.Search("strength", offset: start, limit: limit); } catch (ArgumentException) { rejected = true; }
        True(rejected, "非法分页拒绝");
    }
    foreach (var key in new[] { "grade", "library" }) True(NormaCommands.PageRows().Any(r => r["id"] == "material." + key), "目录遗漏");
}

static void TestDriveEfficiency()
{
    var all = DriveEfficiency.Search(null, 0, 100);
    Equal(53, all.Total);
    Equal(13, all.Items.Select(r => r.Category).Distinct().Count());
    Equal(53, all.Items.Select(r => r.Id).Distinct().Count());
    Equal(3, all.Items.Count(r => r.Efficiency.Undefined is not null));
    foreach (var row in all.Items)
    {
        var e = row.Efficiency;
        Equal("1", e.Unit);
        if (e.Undefined is null) True(e.Min > 0 && e.Min <= e.Max && e.Max <= 1, "效率域错误");
        else True(e.Value is null && e.Min is null && e.Max is null && row.Note is not null, "源表倒置不能造数");
    }
    var rows = new List<string>();
    int? offset = 0;
    while (offset is not null)
    {
        var page = DriveEfficiency.Search(null, offset.Value, 7);
        rows.AddRange(page.Items.Select(r => r.Id));
        offset = page.NextOffset;
    }
    True(rows.SequenceEqual(all.Items.Select(r => r.Id)), "分页有丢失或重复");
    Equal(0, DriveEfficiency.Search("不存在的传动", 0, 20).Total);
    var gears = DriveEfficiency.Search("圆柱齿轮传动 8级", 0, 20).Items.Single();
    Near(0.97, gears.Efficiency.Value!.Value, 0, "8级圆柱齿轮稀油润滑");
    Equal(gears.Id, DriveEfficiency.Search(gears.Id, 0, 20).Items.Single().Id);
    Equal(5, DriveEfficiency.Search("滑动轴承", 0, 20).Total); // category plus identically named composite-wheel form
    var worm = DriveEfficiency.Search("自锁蜗杆", 0, 20).Items.Single().Efficiency;
    Near(0.4, worm.Min!.Value, 0, "自锁蜗杆下界");
    Near(0.45, worm.Max!.Value, 0, "自锁蜗杆上界");
    True(worm.Value is null, "效率范围不可取中值");
    Near(0.96, DriveEfficiency.Search("同步带", 0, 20).Items.Single().Efficiency.Min!.Value, 0, "同步带效率");
    Near(0.96, DriveEfficiency.Search("滚子链", 0, 20).Items.Single().Efficiency.Value!.Value, 0, "滚子链效率");
    var variable = DriveEfficiency.Search("无级变速器", 0, 20).Items.Single();
    Near(0.92, variable.Efficiency.Min!.Value, 0, "无级变速器下界");
    Near(0.95, variable.Efficiency.Max!.Value, 0, "恢复无级变速器上界");
    True(variable.Note!.Contains("滚动轴承的损耗考虑在内"), "减速器脚注遗漏");
    Equal(10, all.Items.Count(r => r.Note?.Contains(DriveEfficiency.ReducerNote) == true));
    var main = DriveEfficiency.Search("轧机主减速器", 0, 20).Items.Single().Efficiency;
    Near(0.93, main.Min!.Value, 0, "恢复轧机主减速器下界");
    Near(0.96, main.Max!.Value, 0, "恢复轧机主减速器上界");
    Near(0.3, DriveEfficiency.Search("滑动丝杠", 0, 20).Items.Single().Efficiency.Min!.Value, 0, "滑动丝杠");
    Near(0.90, DriveEfficiency.Search("滚动丝杠", 0, 20).Items.Single().Efficiency.Max!.Value, 0, "滚动丝杠");
    Near(0.97, DriveEfficiency.Search("万向联轴器(α≤3°)", 0, 20).Items.Single().Efficiency.Min!.Value, 0, "万向节小角度");
    Near(0.95, DriveEfficiency.Search("万向联轴器(α＞3°)", 0, 20).Items.Single().Efficiency.Min!.Value, 0, "万向节大角度");
    Equal("0.98~0.90", DriveEfficiency.Search("槽摩擦轮", 0, 20).Items.Single().Efficiency.Text);
    Equal("0.99~0.95", DriveEfficiency.Search("复合轮组 滚动轴承", 0, 20).Items.Single().Efficiency.Text);
    True(all.Items[0].Note!.Contains("错字"), "首行精度异常必须说明");
    Near(0.93, DriveEfficiency.Search("轧机人字齿轮座(滑动轴承)", 0, 20).Items.Single().Efficiency.Value!.Value, 0, "相同端点照录");
    foreach (var (start, limit) in new[] { (-1, 20), (0, 0), (0, 101) })
    {
        var rejected = false;
        try { DriveEfficiency.Search(null, start, limit); } catch (ArgumentException) { rejected = true; }
        True(rejected, "非法分页必须拒绝");
    }
    var registry = Registry();
    True(registry.TryGet("norma.drive.efficiency", out var command), "未注册效率工具");
    Equal("drive", command!.CommandClass);
    var result = command.Handler(new CommandContext(command, new Dictionary<string, string> { ["q"] = "槽摩擦轮" }, "test", null, CancellationToken.None)).Result;
    True(result.Success, result.Message);
    var data = JsonSerializer.SerializeToElement(result.Data);
    Equal(JsonValueKind.Null, data.GetProperty("items")[0].GetProperty("efficiency").GetProperty("value").ValueKind);
    True(NormaCommands.PageRows().Any(row => row["id"] == "drive.efficiency" && row["topic"] == Catalog.TopicTitle("drive")), "目录分类错误");
}

static void TestPhysicalTables()
{
    var expectedCounts = new[] { 28, 56, 14, 17, 50, 20, 28, 11 };
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
        True(registry.TryGet("norma.material.library", out var descriptor), "未注册合并材料库");
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
    // REQ-MATERIAL-004: values and names verified against native manual screenshots.
    Near(103, PhysicalTables.Search("elastic", "铸铝青铜", null, 0, 20).Items.Single().Properties["e"].Value!.Value, 0, "铸铝青铜E");
    Near(1200, PhysicalTables.Search("thermal", "灰铸铁", null, 0, 20).Items.Single().Properties["melting"].Value!.Value, 0, "灰铸铁熔点");
    Near(8.62, PhysicalTables.Search("density", "铸造黄铜", null, 0, 20).Items.Single().Properties["rho"].Value!.Value, 0, "铸造黄铜密度");
    var wood = PhysicalTables.Search("density", "木材", null, 0, 20).Items.Single().Properties["rho"];
    Near(0.4, wood.Min!.Value, 0, "木材密度下界");
    Near(0.75, wood.Max!.Value, 0, "木材密度上界");
    Near(0.31, PhysicalTables.Search("thermal", "聚酰胺", null, 0, 20).Items.Single().Properties["conductivity"].Value!.Value, 0, "聚酰胺导热");
    var peat = PhysicalTables.Search("bulk", "泥煤(湿)", null, 0, 20).Items.Single().Properties["rho_bulk"];
    Near(0.5, peat.Min!.Value, 0, "湿泥煤下界");
    Near(0.65, peat.Max!.Value, 0, "湿泥煤上界");
    Equal("原表未给出", PhysicalTables.Search("bulk", "粘土(湿)", null, 0, 20).Items.Single().Properties["angle_moving"].Undefined!);
    foreach (var table in PhysicalTables.Tables)
        foreach (var row in PhysicalTables.Search(table.Key, null, null, 0, 100).Items)
        {
            True(!row.Material.StartsWith("未辨材料"), "原窗口已确认全部材料名称");
            True(row.Properties.Values.All(v => v.Text != "?"), "不应遗留已核对的缺损占位");
        }
    var registry2 = Registry();
    foreach (var name in new[] { "norma.material.limits", "norma.metal.strength" })
    {
        registry2.TryGet(name, out var descriptor);
        Equal("material", descriptor!.CommandClass);
    }
    // REQ-MATERIAL-005: native-page references, condition columns and source anomalies.
    var steelFriction = PhysicalTables.Search("friction", "钢-钢", null, 0, 20).Items.Single();
    Near(0.15, steelFriction.Properties["static_dry"].Value!.Value, 0, "钢钢静摩擦无润滑");
    Near(0.1, steelFriction.Properties["static_lubricated"].Min!.Value, 0, "钢钢静摩擦有润滑下界");
    Near(0.12, steelFriction.Properties["static_lubricated"].Max!.Value, 0, "钢钢静摩擦有润滑上界");
    Near(0.15, steelFriction.Properties["sliding_dry"].Value!.Value, 0, "钢钢动摩擦无润滑");
    Near(0.05, steelFriction.Properties["sliding_lubricated"].Min!.Value, 0, "钢钢动摩擦有润滑下界");
    Near(0.1, steelFriction.Properties["sliding_lubricated"].Max!.Value, 0, "钢钢动摩擦有润滑上界");
    Near(0.18, PhysicalTables.Search("friction", "铸铁-铸铁", null, 0, 20).Items.Single().Properties["static_lubricated"].Value!.Value, 0, "恢复HTML缺失值");
    Equal("原表未给出", PhysicalTables.Search("friction", "钢-软钢", null, 0, 20).Items.Single().Properties["static_dry"].Undefined!);
    var axial = PhysicalTables.Search("object_friction", "深沟球轴承 轴向载荷", null, 0, 20).Items.Single();
    Near(0.004, axial.Properties["f"].Value!.Value, 0, "轴向载荷与径向载荷不能混用");
    Near(0.003, PhysicalTables.Search("object_friction", "特殊密封全液体", null, 0, 20).Items.Single().Properties["f"].Min!.Value, 0, "恢复全液体轴承名称及数据");
    var arm = PhysicalTables.Search("rolling_arm", "橡胶轮胎对沥青路面", null, 0, 20).Items.Single().Properties["k"];
    Near(2.5, arm.Value!.Value, 0, "沥青路面滚动力臂");
    Equal("mm", arm.Unit);
    foreach (var (key, query, field, original) in new[]
    { ("rolling_arm", "木材与木材", "k", "0.5~0.3"), ("object_friction", "离合器", "f", "0.43~0.4") })
    {
        var row = PhysicalTables.Search(key, query, null, 0, 20).Items.Single();
        var cell = row.Properties[field];
        Equal(original, cell.Text);
        Equal("原表区间倒置，待核", cell.Undefined!);
        True(cell.Value is null && cell.Min is null && cell.Max is null && row.Note is not null, "倒置范围不能自动排序猜改");
    }
    Equal(5, NormaCommands.PageRows().Count(row => row["topic"] == "机械材料"));
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
