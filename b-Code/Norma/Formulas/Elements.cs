namespace Norma.Formulas;

/// <summary>滚动轴承、弹簧、带与链传动、标准公差、梁。</summary>
internal static class Elements
{
    public static IEnumerable<Calculation> All()
    {
        yield return BearingLoad();
        yield return BearingLife();
        yield return BearingRequired();
        yield return BearingStatic();
        yield return Spring();
        yield return SpringWire();
        yield return Belt();
        yield return Chain();
        yield return Tolerance();
        yield return Beam();
    }

    private static readonly Input BearingType =
        Input.Choice("type", "", "轴承类型：ball 球轴承（ε = 3）、roller 滚子轴承（ε = 10/3）", null, "ball", "roller");

    private static double LifeExponent(Args args) => args.Choice("type") == "ball" ? 3 : 10.0 / 3;

    /// <summary>ISO 281 可靠度寿命修正系数 a₁。</summary>
    private static double A1(string reliability) => reliability switch
    {
        "95" => 0.64,
        "96" => 0.55,
        "97" => 0.47,
        "98" => 0.37,
        "99" => 0.25,
        _ => 1.0,
    };

    private static Calculation BearingLoad() => new()
    {
        Id = "bearing.load",
        Title = "滚动轴承当量动载荷",
        Source = "GB/T 6391 / ISO 281：P = f_p (X F_r + Y F_a)",
        Scope = "X、Y 按轴承型号与 F_a/C₀、F_a/F_r 查样本或 GB/T 6391 后输入；f_p 为载荷系数",
        Inputs =
        [
            new Input("fr", "F_r", "N", "径向载荷") { Min = 0, Example = 3000 },
            new Input("fa", "F_a", "N", "轴向载荷") { Default = 0, Min = 0 },
            new Input("x", "X", "", "径向载荷系数") { Default = 1, Min = 0, Max = 1 },
            new Input("y", "Y", "", "轴向载荷系数") { Default = 0, Min = 0, Max = 5 },
            new Input("fp", "f_p", "", "载荷系数；平稳 1.0～1.2，中等冲击 1.2～1.8") { Default = 1.2, Min = 1, Max = 3 },
        ],
        Outputs = [new("p", "P", "N", "当量动载荷"), new("ratio", "F_a/F_r", "", "轴径向载荷比，用于对照判断 e 值")],
        Evaluate = (args, r) =>
        {
            r["p"] = args["fp"] * (args["x"] * args["fr"] + args["y"] * args["fa"]);
            r["ratio"] = args["fr"] == 0 ? double.PositiveInfinity : args["fa"] / args["fr"];
        },
    };

    private static Calculation BearingLife() => new()
    {
        Id = "bearing.life",
        Title = "滚动轴承基本额定寿命与修正寿命",
        Source = "ISO 281:2007：L₁₀ = (C/P)^ε（10⁶ 转），L_nm = a₁ a_ISO L₁₀，L_h = 10⁶ L/(60n)；a₁ 取 ISO 281 可靠度系数",
        Scope = "疲劳寿命；a_ISO（润滑、污染、疲劳极限载荷）由调用方按 ISO 281 或样本给定，缺省 1 即只做可靠度修正；f_t 为温度系数",
        Inputs =
        [
            Input.Positive("c", "C", "N", "基本额定动载荷", 30000),
            Input.Positive("p", "P", "N", "当量动载荷", 3000),
            Input.Positive("n", "n", "r/min", "转速", 1000),
            BearingType,
            Input.Choice("reliability", "", "可靠度（%）", "90", "90", "95", "96", "97", "98", "99"),
            new Input("aiso", "a_ISO", "", "寿命修正系数") { Default = 1, Min = 0.1, Max = 50 },
            new Input("ft", "f_t", "", "温度系数（≤120℃ 为 1）") { Default = 1, Min = 0.4, Max = 1 },
            new Input("lh_req", "L_h′", "h", "要求寿命，0 表示不校核") { Default = 0, Min = 0 },
        ],
        Outputs =
        [
            new("l10", "L₁₀", "10⁶ r", "基本额定寿命"),
            new("a1", "a₁", "", "可靠度寿命修正系数"),
            new("lnm", "L_nm", "10⁶ r", "修正额定寿命"),
            new("lh", "L_nmh", "h", "修正额定寿命（小时）"),
        ],
        Evaluate = (args, r) =>
        {
            var l10 = Math.Pow(args["ft"] * args["c"] / args["p"], LifeExponent(args));
            var a1 = A1(args.Choice("reliability"));
            var lnm = a1 * args["aiso"] * l10;
            var lh = lnm * 1e6 / (60 * args["n"]);
            r["l10"] = l10;
            r["a1"] = a1;
            r["lnm"] = lnm;
            r["lh"] = lh;
            if (args["lh_req"] > 0)
                r.AtLeast("轴承寿命", lh, args["lh_req"], "L_nmh ≥ L_h′");
            if (args["p"] > 0.5 * args["c"])
                r.Note("P > 0.5C 已超出寿命公式的常规适用范围，应向轴承厂家核实");
        },
    };

    private static Calculation BearingRequired() => new()
    {
        Id = "bearing.required",
        Title = "按要求寿命求所需额定动载荷",
        Source = "由 ISO 281 寿命式反解：C′ = P/f_t·(60 n L_h′/(10⁶ a₁ a_ISO))^(1/ε)",
        Scope = "选型初算；选出的轴承 C 应不小于 C′",
        Inputs =
        [
            Input.Positive("p", "P", "N", "当量动载荷", 3000),
            Input.Positive("n", "n", "r/min", "转速", 1000),
            Input.Positive("lh", "L_h′", "h", "要求寿命", 20000),
            BearingType,
            Input.Choice("reliability", "", "可靠度（%）", "90", "90", "95", "96", "97", "98", "99"),
            new Input("aiso", "a_ISO", "", "寿命修正系数") { Default = 1, Min = 0.1, Max = 50 },
            new Input("ft", "f_t", "", "温度系数") { Default = 1, Min = 0.4, Max = 1 },
        ],
        Outputs = [new("c_req", "C′", "N", "所需基本额定动载荷")],
        Evaluate = (args, r) =>
        {
            var revolutions = 60 * args["n"] * args["lh"] / (1e6 * A1(args.Choice("reliability")) * args["aiso"]);
            r["c_req"] = args["p"] / args["ft"] * Math.Pow(revolutions, 1 / LifeExponent(args));
        },
    };

    private static Calculation BearingStatic() => new()
    {
        Id = "bearing.static",
        Title = "滚动轴承静强度安全系数",
        Source = "ISO 76：P₀ = X₀ F_r + Y₀ F_a（且不小于 F_r），S₀ = C₀ / P₀",
        Scope = "低速、摆动或冲击载荷下的静载校核；X₀、Y₀ 按轴承类型查取",
        Inputs =
        [
            Input.Positive("c0", "C₀", "N", "基本额定静载荷", 20000),
            new Input("fr", "F_r", "N", "径向载荷") { Min = 0, Example = 5000 },
            new Input("fa", "F_a", "N", "轴向载荷") { Default = 0, Min = 0 },
            new Input("x0", "X₀", "", "径向静载荷系数（深沟球 0.6）") { Default = 0.6, Min = 0, Max = 1 },
            new Input("y0", "Y₀", "", "轴向静载荷系数（深沟球 0.5）") { Default = 0.5, Min = 0, Max = 5 },
            new Input("s0_req", "S₀′", "", "要求的静强度安全系数；平稳 0.8～1.2，冲击 1.5～2") { Default = 1, Min = 0.5, Max = 5 },
        ],
        Outputs = [new("p0", "P₀", "N", "当量静载荷"), new("s0", "S₀", "", "静强度安全系数")],
        Evaluate = (args, r) =>
        {
            var p0 = Math.Max(args["x0"] * args["fr"] + args["y0"] * args["fa"], args["fr"]);
            if (p0 == 0)
                throw new ArgumentException("F_r 与 F_a 不能同时为 0");
            r["p0"] = p0;
            r["s0"] = args["c0"] / p0;
            r.AtLeast("静强度安全系数", args["c0"] / p0, args["s0_req"], "S₀ ≥ S₀′");
        },
    };

    private static double Wahl(double c) => (4 * c - 1) / (4 * c - 4) + 0.615 / c;

    private static Calculation Spring() => new()
    {
        Id = "spring.compression",
        Title = "圆柱螺旋压缩弹簧应力与刚度",
        Source = "GB/T 23935 / EN 13906-1：旋绕比 C = D/d，曲度系数 K = (4C−1)/(4C−4) + 0.615/C（Wahl），τ = 8KFD/(πd³)，刚度 k = Gd⁴/(8D³n)",
        Scope = "圆截面冷卷压缩弹簧，静载或循环次数不高；n 为有效圈数",
        Inputs =
        [
            Input.Positive("d", "d", "mm", "簧丝直径", 4),
            Input.Positive("dm", "D", "mm", "弹簧中径", 32),
            Input.Positive("n", "n", "", "有效圈数", 10),
            Input.Positive("f", "F", "N", "工作载荷", 200),
            new Input("g", "G", "MPa", "切变模量；弹簧钢 79000，不锈钢 71000") { Default = 79000, Min = 10000 },
            new Input("tau_allow", "[τ]", "MPa", "许用切应力，0 表示不校核") { Default = 0, Min = 0 },
        ],
        Outputs =
        [
            new("c", "C", "", "旋绕比"),
            new("k_wahl", "K", "", "曲度系数"),
            new("tau", "τ", "MPa", "最大切应力"),
            new("rate", "k", "N/mm", "刚度"),
            new("deflection", "λ", "mm", "工作载荷下的变形量"),
        ],
        Evaluate = (args, r) =>
        {
            double d = args["d"], dm = args["dm"];
            var c = dm / d;
            if (c <= 1)
                throw new ArgumentException("中径 D 必须大于簧丝直径 d");
            var k = Wahl(c);
            var tau = 8 * k * args["f"] * dm / (Math.PI * d * d * d);
            var rate = args["g"] * Math.Pow(d, 4) / (8 * Math.Pow(dm, 3) * args["n"]);
            r["c"] = c;
            r["k_wahl"] = k;
            r["tau"] = tau;
            r["rate"] = rate;
            r["deflection"] = args["f"] / rate;
            r.AtLeast("旋绕比下限", c, 4, "C 宜在 4～16（太小难卷制）");
            r.AtMost("旋绕比上限", c, 16, "C 宜在 4～16（太大易颤动）");
            if (args["tau_allow"] > 0)
                r.AtMost("切应力", tau, args["tau_allow"], "τ ≤ [τ]");
        },
    };

    private static Calculation SpringWire() => new()
    {
        Id = "spring.wire",
        Title = "按强度求压缩弹簧簧丝直径",
        Source = "由 τ = 8KFC/(πd²) 反解：d ≥ 1.6 √(K F C / [τ])",
        Scope = "已定旋绕比 C 的初步设计；结果圆整到标准线径后用 spring.compression 复算",
        Inputs =
        [
            Input.Positive("f", "F", "N", "最大工作载荷", 200),
            new Input("c", "C", "", "选定的旋绕比") { Min = 3, Max = 20, Example = 8 },
            Input.Positive("tau_allow", "[τ]", "MPa", "许用切应力", 450),
        ],
        Outputs = [new("k_wahl", "K", "", "曲度系数"), new("d", "d", "mm", "簧丝直径最小值"), new("dm", "D", "mm", "对应中径 C d")],
        Evaluate = (args, r) =>
        {
            var k = Wahl(args["c"]);
            var d = Math.Sqrt(8 * k * args["f"] * args["c"] / (Math.PI * args["tau_allow"]));
            r["k_wahl"] = k;
            r["d"] = d;
            r["dm"] = args["c"] * d;
        },
    };

    private static Calculation Belt() => new()
    {
        Id = "belt.geometry",
        Title = "带传动几何：带长、中心距、包角、带速",
        Source = "开口传动：L₀ = 2a₀ + π(d₁ + d₂)/2 + (d₂ − d₁)²/(4a₀)；a ≈ a₀ + (L_d − L₀)/2；α₁ = 180° − (d₂ − d₁)/a·57.3°；v = πd₁n₁/60000",
        Scope = "V 带或平带开口传动；d 为基准直径；功率与根数须另按带型额定功率表计算",
        Inputs =
        [
            Input.Positive("d1", "d_d1", "mm", "小带轮基准直径", 125),
            Input.Positive("d2", "d_d2", "mm", "大带轮基准直径", 315),
            Input.Positive("a0", "a₀", "mm", "初定中心距；常取 0.7(d₁+d₂)～2(d₁+d₂)", 500),
            new Input("ld", "L_d", "mm", "选定的标准基准长度；0 表示按计算带长") { Default = 0, Min = 0 },
            Input.Positive("n1", "n₁", "r/min", "小带轮转速", 1450),
        ],
        Outputs =
        [
            new("l0", "L₀", "mm", "按初定中心距的计算带长"),
            new("a", "a", "mm", "按 L_d 的实际中心距（近似）"),
            new("alpha1", "α₁", "°", "小带轮包角"),
            new("v", "v", "m/s", "带速"),
            new("i", "i", "", "传动比 d₂/d₁（不计滑动）"),
        ],
        Evaluate = (args, r) =>
        {
            double d1 = args["d1"], d2 = args["d2"], a0 = args["a0"];
            var l0 = 2 * a0 + Math.PI * (d1 + d2) / 2 + Math.Pow(d2 - d1, 2) / (4 * a0);
            var ld = args["ld"] > 0 ? args["ld"] : l0;
            var a = a0 + (ld - l0) / 2;
            var alpha1 = 180 - Math.Abs(d2 - d1) / a * (180 / Math.PI);
            var v = Math.PI * d1 * args["n1"] / 60000;
            r["l0"] = l0;
            r["a"] = a;
            r["alpha1"] = alpha1;
            r["v"] = v;
            r["i"] = d2 / d1;
            r.AtLeast("小带轮包角", alpha1, 120, "V 带 α₁ ≥ 120°");
            r.AtLeast("带速下限", v, 5, "普通 V 带 v 宜在 5～25 m/s");
            r.AtMost("带速上限", v, 25, "普通 V 带 v 宜在 5～25 m/s");
        },
    };

    private static Calculation Chain() => new()
    {
        Id = "chain.geometry",
        Title = "滚子链传动几何：链节数、中心距、链速",
        Source = "L_p = 2a₀/p + (z₁+z₂)/2 + ((z₂−z₁)/(2π))²·p/a₀，取偶数；a = p/4·[(L_p − (z₁+z₂)/2) + √((L_p − (z₁+z₂)/2)² − 8((z₂−z₁)/(2π))²)]；链轮分度圆 d = p/sin(180°/z)",
        Scope = "单排或多排滚子链开口传动；承载能力须另按链条额定功率曲线校核",
        Inputs =
        [
            Input.Positive("p", "p", "mm", "节距（08A 为 12.7）", 12.7),
            new Input("z1", "z₁", "", "小链轮齿数") { Min = 9, Max = 150, Example = 21 },
            new Input("z2", "z₂", "", "大链轮齿数") { Min = 9, Max = 150, Example = 63 },
            Input.Positive("a0", "a₀", "mm", "初定中心距；常取 (30～50)p", 500),
            Input.Positive("n1", "n₁", "r/min", "小链轮转速", 960),
        ],
        Outputs =
        [
            new("lp0", "L_p0", "节", "计算链节数"),
            new("lp", "L_p", "节", "圆整后的链节数（偶数）"),
            new("a", "a", "mm", "理论中心距"),
            new("d1", "d₁", "mm", "小链轮分度圆直径"),
            new("d2", "d₂", "mm", "大链轮分度圆直径"),
            new("v", "v", "m/s", "平均链速"),
        ],
        Evaluate = (args, r) =>
        {
            double p = args["p"], z1 = args["z1"], z2 = args["z2"], a0 = args["a0"];
            var sum = (z1 + z2) / 2;
            var diff = (z2 - z1) / (2 * Math.PI);
            var lp0 = 2 * a0 / p + sum + diff * diff * p / a0;
            var lp = Math.Ceiling(lp0);
            if (lp % 2 == 1)
                lp += 1;
            var a = p / 4 * (lp - sum + Math.Sqrt(Math.Pow(lp - sum, 2) - 8 * diff * diff));
            r["lp0"] = lp0;
            r["lp"] = lp;
            r["a"] = a;
            r["d1"] = p / Math.Sin(Math.PI / z1);
            r["d2"] = p / Math.Sin(Math.PI / z2);
            r["v"] = z1 * args["n1"] * p / 60000;
            r.Note("安装中心距宜比理论值小 0.2%～0.4%，以得到适当垂度");
        },
    };

    // GB/T 1800.1 / ISO 286-1 的尺寸分段（≤ 500 mm）与 IT5～IT18 的公差单位倍数。
    private static readonly double[] Steps = [3, 6, 10, 18, 30, 50, 80, 120, 180, 250, 315, 400, 500];
    private static readonly double[] Multipliers = [7, 10, 16, 25, 40, 64, 100, 160, 250, 400, 640, 1000, 1600, 2500];

    private static Calculation Tolerance() => new()
    {
        Id = "fit.it",
        Title = "标准公差（IT5～IT18）公式值",
        Source = "GB/T 1800.1 / ISO 286-1 标准公差因子 i = 0.45∛D + 0.001D（μm，D 为尺寸段两端的几何平均），IT = 倍数 × i",
        Scope = "公称尺寸 ≤ 500 mm、IT5～IT18；未按标准圆整规则修约的公式估算；标准表值请用norma.basic.tolerance，二者不互相回退",
        Inputs =
        [
            new Input("size", "D", "mm", "公称尺寸") { Min = 0, MinExclusive = true, Max = 500, Example = 25 },
            new Input("grade", "IT", "", "公差等级 5～18") { Min = 5, Max = 18, Example = 7 },
        ],
        Outputs =
        [
            new("lower", "D_low", "mm", "所在尺寸段下界（不含）"),
            new("upper", "D_up", "mm", "所在尺寸段上界（含）"),
            new("i", "i", "μm", "标准公差因子"),
            new("it", "IT", "μm", "标准公差公式值"),
            new("it_rounded", "IT′", "μm", "修约到 1 μm（≥ 100 μm 时修约到 10 μm 的约值）"),
        ],
        Evaluate = (args, r) =>
        {
            var size = args["size"];
            var grade = args["grade"];
            if (grade != Math.Floor(grade))
                throw new ArgumentException("grade 必须是整数");
            var index = Array.FindIndex(Steps, step => size <= step);
            var upper = Steps[index];
            var lower = index == 0 ? 0 : Steps[index - 1];
            // 第一段（≤ 3 mm）按标准取 1 与 3 的几何平均。
            var mean = Math.Sqrt((index == 0 ? 1 : lower) * upper);
            var i = 0.45 * Math.Cbrt(mean) + 0.001 * mean;
            var it = Multipliers[(int)grade - 5] * i;
            r["lower"] = lower;
            r["upper"] = upper;
            r["i"] = i;
            r["it"] = it;
            r["it_rounded"] = it < 100 ? Math.Round(it) : Math.Round(it / 10) * 10;
            r.Note("本值用于公式估算；手册GB/T1800.3—1998标准表值请用norma.basic.tolerance，不以估算补表");
        },
    };

    private static Calculation Beam() => new()
    {
        Id = "beam.deflection",
        Title = "等截面梁最大挠度、转角与弯矩",
        Source = "材料力学经典解：悬臂端部集中 FL³/(3EI)，悬臂均布 WL³/(8EI)，简支中点集中 FL³/(48EI)，简支均布 5WL³/(384EI)（W 为均布总载荷）",
        Scope = "线弹性小变形、等截面直梁、不计剪切变形",
        Inputs =
        [
            Input.Choice("case", "", "工况：cantilever_point 悬臂端部集中、cantilever_uniform 悬臂均布、simple_point 简支中点集中、simple_uniform 简支均布", null,
                "cantilever_point", "cantilever_uniform", "simple_point", "simple_uniform"),
            Input.Positive("f", "F/W", "N", "集中载荷，或均布载荷的总和", 1000),
            Input.Positive("l", "L", "mm", "梁长（跨度）", 1000),
            new Input("e", "E", "MPa", "弹性模量") { Default = 206000, Min = 1000 },
            Input.Positive("i", "I", "mm⁴", "截面惯性矩", 1e6),
            new Input("w", "W_z", "mm³", "抗弯截面系数，0 表示不算应力") { Default = 0, Min = 0 },
        ],
        Outputs =
        [
            new("deflection", "f_max", "mm", "最大挠度"),
            new("slope", "θ_max", "rad", "最大转角"),
            new("moment", "M_max", "N·m", "最大弯矩"),
            new("stress", "σ_max", "MPa", "最大弯曲正应力（W_z 为 0 时为 0）"),
        ],
        Evaluate = (args, r) =>
        {
            double f = args["f"], l = args["l"], ei = args["e"] * args["i"];
            var (deflection, slope, moment) = args.Choice("case") switch
            {
                "cantilever_point" => (f * l * l * l / (3 * ei), f * l * l / (2 * ei), f * l),
                "cantilever_uniform" => (f * l * l * l / (8 * ei), f * l * l / (6 * ei), f * l / 2),
                "simple_point" => (f * l * l * l / (48 * ei), f * l * l / (16 * ei), f * l / 4),
                _ => (5 * f * l * l * l / (384 * ei), f * l * l / (24 * ei), f * l / 8),
            };
            r["deflection"] = deflection;
            r["slope"] = slope;
            r["moment"] = moment / 1000;
            r["stress"] = args["w"] > 0 ? moment / args["w"] : 0;
        },
    };
}
