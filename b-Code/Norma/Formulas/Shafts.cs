namespace Norma.Formulas;

/// <summary>轴、疲劳、键、动力换算。</summary>
internal static class Shafts
{
    public static IEnumerable<Calculation> All()
    {
        yield return Torque();
        yield return Torsion();
        yield return Combined();
        yield return Endurance();
        yield return Safety();
        yield return ShaftFatigue();
        yield return FlatKey();
    }

    private static Calculation Torque() => new()
    {
        CatalogSummary = "按功率和转速换算扭矩",
        Id = "drive.torque",
        Title = "功率转速换算扭矩",
        Source = "T = 9550 P / n（P 单位 kW，n 单位 r/min，T 单位 N·m）",
        Scope = "稳态回转；多级传动时可给总效率，得到输出轴扭矩",
        Inputs =
        [
            Input.Positive("p", "P", "kW", "功率", 7.5),
            Input.Positive("n", "n", "r/min", "转速", 1450),
            new Input("eta", "η", "", "从功率点到本轴的总效率") { Default = 1, Min = 0, MinExclusive = true, Max = 1 },
        ],
        Outputs = [new("t", "T", "N·m", "扭矩"), new("omega", "ω", "rad/s", "角速度")],
        Evaluate = (args, r) =>
        {
            r["t"] = 9550 * args["p"] * args["eta"] / args["n"];
            r["omega"] = 2 * Math.PI * args["n"] / 60;
        },
    };

    private static Calculation Torsion() => new()
    {
        CatalogSummary = "按扭转强度估算轴径",
        Id = "shaft.torsion",
        Title = "按扭转强度估算轴径",
        Source = "d ≥ A₀ ∛(P/n)，A₀ 由许用扭转切应力折算；常用 45 钢 A₀ = 103～126，40Cr 等合金钢 97～112",
        Scope = "初估最小轴径（通常是外伸端），未计弯矩；有键槽时按槽数放大",
        Inputs =
        [
            Input.Positive("p", "P", "kW", "轴传递功率", 7.5),
            Input.Positive("n", "n", "r/min", "轴转速", 480),
            new Input("a0", "A₀", "", "与材料许用切应力相关的系数") { Default = 112, Min = 80, Max = 170 },
            Input.Choice("keyway", "", "该截面键槽数：none 无、one 一个（+5%）、two 两个（+10%）", "none", "none", "one", "two"),
        ],
        Outputs =
        [
            new("d0", "d₀", "mm", "不计键槽的计算轴径"),
            new("dmin", "d_min", "mm", "计入键槽后的最小轴径（未圆整）"),
            new("tau_allow", "[τ_T]", "MPa", "A₀ 对应的许用扭转切应力"),
        ],
        Evaluate = (args, r) =>
        {
            var d0 = args["a0"] * Math.Cbrt(args["p"] / args["n"]);
            var factor = args.Choice("keyway") switch { "one" => 1.05, "two" => 1.10, _ => 1.0 };
            r["d0"] = d0;
            r["dmin"] = d0 * factor;
            // A₀ = ∛(9.55e6 / (0.2[τ]))，反推 [τ]
            r["tau_allow"] = 9.55e6 / (0.2 * Math.Pow(args["a0"], 3));
            r.Note("把 d_min 圆整到标准直径后再按弯扭合成校核");
        },
    };

    private static Calculation Combined() => new()
    {
        CatalogSummary = "按弯扭合成强度校核轴",
        Id = "shaft.combined",
        Title = "按弯扭合成强度校核轴",
        Source = "第三强度理论：σ_ca = √(M² + (αT)²) / W，实心 W = πd³/32，空心乘 (1 − β⁴)",
        Scope = "危险截面的静强度近似校核；α 为扭矩折合系数（不变扭矩 0.3，脉动 0.6，对称循环 1）",
        Inputs =
        [
            new Input("m", "M", "N·m", "危险截面合成弯矩") { Min = 0, Example = 300 },
            new Input("t", "T", "N·m", "危险截面扭矩") { Min = 0, Example = 400 },
            Input.Positive("d", "d", "mm", "截面直径", 45),
            new Input("alpha", "α", "", "扭矩折合系数") { Default = 0.6, Min = 0, Max = 1 },
            new Input("ratio", "β", "", "空心轴内外径比 d_i/d，实心为 0") { Default = 0, Min = 0, Max = 0.95 },
            Input.Positive("sigma_allow", "[σ₋₁]", "MPa", "对称循环许用弯曲应力（45 钢调质约 60）", 60),
        ],
        Outputs = [new("w", "W", "mm³", "抗弯截面系数"), new("sigma_ca", "σ_ca", "MPa", "计算应力")],
        Evaluate = (args, r) =>
        {
            var d = args["d"];
            var w = Math.PI * d * d * d / 32 * (1 - Math.Pow(args["ratio"], 4));
            var sigma = Math.Sqrt(Math.Pow(args["m"], 2) + Math.Pow(args["alpha"] * args["t"], 2)) * 1000 / w;
            r["w"] = w;
            r["sigma_ca"] = sigma;
            r.AtMost("弯扭合成应力", sigma, args["sigma_allow"], "σ_ca ≤ [σ₋₁]");
        },
    };

    private static Calculation Endurance() => new()
    {
        CatalogSummary = "用 Marin 修正计算零件疲劳极限",
        Id = "fatigue.endurance",
        Title = "零件疲劳极限（Marin 修正）",
        Source = "Shigley《机械工程设计》Marin 方程：S_e = k_a k_b k_c k_d k_e S′_e，S′_e = 0.5 S_ut（S_ut ≤ 1400 MPa，否则 700 MPa）",
        Scope = "钢制零件旋转弯曲的高周疲劳估算；k_a 按表面处理、k_b 按回转直径、k_c 按载荷类型、k_e 按可靠度取公认的拟合值",
        Inputs =
        [
            Input.Positive("sut", "S_ut", "MPa", "材料抗拉强度", 600),
            Input.Choice("surface", "", "表面：ground 磨削、machined 机加工或冷拉、hotrolled 热轧、forged 锻造", "machined", "ground", "machined", "hotrolled", "forged"),
            new Input("d", "d", "mm", "回转零件直径；0 表示尺寸系数取 1（如轴向载荷）") { Default = 0, Min = 0, Max = 254 },
            Input.Choice("load", "", "载荷类型：bending 弯曲、axial 轴向、torsion 扭转", "bending", "bending", "axial", "torsion"),
            new Input("kd", "k_d", "", "温度系数") { Default = 1, Min = 0.1, Max = 1.1 },
            Input.Choice("reliability", "", "可靠度（%）", "50", "50", "90", "95", "99", "99.9", "99.99"),
        ],
        Outputs =
        [
            new("se_prime", "S′_e", "MPa", "旋转梁试样疲劳极限"),
            new("ka", "k_a", "", "表面系数"),
            new("kb", "k_b", "", "尺寸系数"),
            new("kc", "k_c", "", "载荷系数"),
            new("ke", "k_e", "", "可靠度系数"),
            new("se", "S_e", "MPa", "零件疲劳极限"),
        ],
        Evaluate = (args, r) =>
        {
            var sut = args["sut"];
            var sePrime = sut <= 1400 ? 0.5 * sut : 700;
            var (a, b) = args.Choice("surface") switch
            {
                "ground" => (1.58, -0.085),
                "hotrolled" => (57.7, -0.718),
                "forged" => (272.0, -0.995),
                _ => (4.51, -0.265),
            };
            var ka = a * Math.Pow(sut, b);
            var d = args["d"];
            double kb;
            if (d == 0 || args.Choice("load") == "axial")
                kb = 1;
            else if (d < 2.79)
                throw new ArgumentException("d 小于 2.79 mm 超出尺寸系数拟合范围；不计尺寸影响请给 d=0");
            else
                kb = d <= 51 ? 1.24 * Math.Pow(d, -0.107) : 1.51 * Math.Pow(d, -0.157);
            var kc = args.Choice("load") switch { "axial" => 0.85, "torsion" => 0.59, _ => 1.0 };
            var ke = args.Choice("reliability") switch
            {
                "90" => 0.897,
                "95" => 0.868,
                "99" => 0.814,
                "99.9" => 0.753,
                "99.99" => 0.702,
                _ => 1.0,
            };
            r["se_prime"] = sePrime;
            r["ka"] = ka;
            r["kb"] = kb;
            r["kc"] = kc;
            r["ke"] = ke;
            r["se"] = ka * kb * kc * args["kd"] * ke * sePrime;
            r.Note("应力集中不在 S_e 里，按名义应力乘 K_f 计入 fatigue.safety 的应力幅");
        },
    };

    private static Calculation Safety() => new()
    {
        CatalogSummary = "计算变应力下的疲劳安全系数",
        Id = "fatigue.safety",
        Title = "变应力下的疲劳安全系数",
        Source = "Shigley 平均应力修正准则：Goodman σ_a/S_e + σ_m/S_ut = 1/n；Gerber；Soderberg；ASME 椭圆；首次屈服 Langer n_y = S_y/(σ_a + σ_m)",
        Scope = "单轴或已合成的等效应力；σ_m ≥ 0；应力幅与均值应已乘疲劳缺口系数 K_f",
        Inputs =
        [
            new Input("sigma_a", "σ_a", "MPa", "应力幅") { Min = 0, Example = 80 },
            new Input("sigma_m", "σ_m", "MPa", "平均应力") { Min = 0, Example = 120 },
            Input.Positive("se", "S_e", "MPa", "零件疲劳极限（见 fatigue.endurance）", 200),
            Input.Positive("sut", "S_ut", "MPa", "抗拉强度", 600),
            Input.Positive("sy", "S_y", "MPa", "屈服强度", 355),
            Input.Choice("criterion", "", "准则：goodman、gerber、soderberg、asme", "goodman", "goodman", "gerber", "soderberg", "asme"),
            new Input("n_req", "[n]", "", "要求的安全系数") { Default = 1.5, Min = 1, Max = 5 },
        ],
        Outputs = [new("n", "n_f", "", "疲劳安全系数"), new("ny", "n_y", "", "首次屈服安全系数")],
        Evaluate = (args, r) =>
        {
            double sa = args["sigma_a"], sm = args["sigma_m"], se = args["se"], sut = args["sut"], sy = args["sy"];
            if (sa == 0 && sm == 0)
                throw new ArgumentException("σ_a 与 σ_m 不能同时为 0");
            var n = args.Choice("criterion") switch
            {
                "soderberg" => 1 / (sa / se + sm / sy),
                "asme" => 1 / Math.Sqrt(Math.Pow(sa / se, 2) + Math.Pow(sm / sy, 2)),
                "gerber" => sm == 0
                    ? se / sa
                    : sa == 0
                        ? sut / sm
                        : 0.5 * Math.Pow(sut / sm, 2) * sa / se * (-1 + Math.Sqrt(1 + Math.Pow(2 * sm * se / (sut * sa), 2))),
                _ => 1 / (sa / se + sm / sut),
            };
            var ny = sy / (sa + sm);
            r["n"] = n;
            r["ny"] = ny;
            r.AtLeast("疲劳安全系数", n, args["n_req"], "n_f ≥ [n]");
            r.AtLeast("首次屈服安全系数", ny, 1, "n_y = S_y/(σ_a + σ_m) ≥ 1");
        },
    };

    private static Calculation ShaftFatigue() => new()
    {
        CatalogSummary = "校核弯扭复合轴截面的疲劳安全系数",
        Id = "fatigue.shaft",
        Title = "轴危险截面疲劳安全系数（弯扭复合）",
        Source = "S_σ = σ₋₁/(K_σ σ_a + ψ_σ σ_m)，S_τ = τ₋₁/(K_τ τ_a + ψ_τ τ_m)，S_ca = S_σ S_τ/√(S_σ² + S_τ²)；国内机械设计教材的轴精确校核式",
        Scope = "K_σ、K_τ 为综合影响系数（应力集中、尺寸、表面合并），由调用方查取；弯曲通常对称循环（σ_m = 0），扭转常按脉动循环（τ_a = τ_m = τ/2）",
        Inputs =
        [
            Input.Positive("sigma_1", "σ₋₁", "MPa", "材料对称循环弯曲疲劳极限（45 钢调质约 275）", 275),
            Input.Positive("tau_1", "τ₋₁", "MPa", "材料对称循环扭转疲劳极限（45 钢调质约 155）", 155),
            Input.Positive("k_sigma", "K_σ", "", "弯曲综合影响系数", 2.2),
            Input.Positive("k_tau", "K_τ", "", "扭转综合影响系数", 1.8),
            new Input("psi_sigma", "ψ_σ", "", "弯曲平均应力折算系数（碳钢 0.1～0.2）") { Default = 0.2, Min = 0, Max = 0.5 },
            new Input("psi_tau", "ψ_τ", "", "扭转平均应力折算系数（碳钢 0.05～0.1）") { Default = 0.1, Min = 0, Max = 0.5 },
            new Input("sigma_a", "σ_a", "MPa", "弯曲应力幅") { Min = 0, Example = 40 },
            new Input("sigma_m", "σ_m", "MPa", "弯曲平均应力") { Default = 0, Min = 0 },
            new Input("tau_a", "τ_a", "MPa", "扭转应力幅") { Min = 0, Example = 10 },
            new Input("tau_m", "τ_m", "MPa", "扭转平均应力") { Min = 0, Example = 10 },
            new Input("s_req", "[S]", "", "要求的安全系数（材料均匀、计算精确 1.3～1.5）") { Default = 1.5, Min = 1, Max = 5 },
        ],
        Outputs = [new("s_sigma", "S_σ", "", "只计弯矩的安全系数"), new("s_tau", "S_τ", "", "只计扭矩的安全系数"), new("s_ca", "S_ca", "", "计算安全系数")],
        Evaluate = (args, r) =>
        {
            var bendingLoad = args["k_sigma"] * args["sigma_a"] + args["psi_sigma"] * args["sigma_m"];
            var torsionLoad = args["k_tau"] * args["tau_a"] + args["psi_tau"] * args["tau_m"];
            if (bendingLoad == 0 && torsionLoad == 0)
                throw new ArgumentException("弯曲与扭转应力不能同时为 0");
            var sSigma = bendingLoad == 0 ? double.PositiveInfinity : args["sigma_1"] / bendingLoad;
            var sTau = torsionLoad == 0 ? double.PositiveInfinity : args["tau_1"] / torsionLoad;
            var sca = double.IsPositiveInfinity(sSigma) ? sTau
                : double.IsPositiveInfinity(sTau) ? sSigma
                : sSigma * sTau / Math.Sqrt(sSigma * sSigma + sTau * sTau);
            r["s_sigma"] = sSigma;
            r["s_tau"] = sTau;
            r["s_ca"] = sca;
            r.AtLeast("轴疲劳安全系数", sca, args["s_req"], "S_ca ≥ [S]");
        },
    };

    private static Calculation FlatKey() => new()
    {
        CatalogSummary = "校核普通平键连接的挤压强度",
        Id = "key.flat",
        Title = "普通平键连接挤压强度",
        Source = "σ_p = 2T / (k l d)，k ≈ 0.5h，工作长度 A 型 l = L − b、B 型 l = L、C 型 l = L − b/2",
        Scope = "静连接，键与轮毂中最弱材料的挤压校核；双键按 1.5 个键计",
        Inputs =
        [
            Input.Positive("t", "T", "N·m", "传递扭矩", 200),
            Input.Positive("d", "d", "mm", "轴径", 40),
            Input.Positive("b", "b", "mm", "键宽", 12),
            Input.Positive("h", "h", "mm", "键高", 8),
            Input.Positive("l", "L", "mm", "键长", 50),
            Input.Choice("type", "", "键型：a 圆头、b 平头、c 单圆头", "a", "a", "b", "c"),
            Input.Choice("count", "", "键数：1 或 2（两键相隔 180°，按 1.5 个计）", "1", "1", "2"),
            new Input("sigma_p_allow", "[σ_p]", "MPa", "许用挤压应力；钢轮毂静连接轻微冲击约 100～120，铸铁减半") { Default = 110, Min = 10, Max = 300 },
        ],
        Outputs =
        [
            new("lw", "l", "mm", "键的工作长度"),
            new("sigma_p", "σ_p", "MPa", "挤压应力"),
            new("l_req", "L_req", "mm", "满足许用挤压应力所需的最短键长"),
        ],
        Evaluate = (args, r) =>
        {
            double b = args["b"], len = args["l"];
            var offset = args.Choice("type") switch { "a" => b, "c" => b / 2, _ => 0 };
            var lw = len - offset;
            if (lw <= 0)
                throw new ArgumentException("键长 L 不足以扣除圆头，工作长度 ≤ 0");
            var keys = args.Choice("count") == "2" ? 1.5 : 1;
            var k = 0.5 * args["h"];
            var twoT = 2 * args["t"] * 1000;
            var sigma = twoT / (k * lw * args["d"] * keys);
            r["lw"] = lw;
            r["sigma_p"] = sigma;
            r["l_req"] = twoT / (k * args["d"] * keys * args["sigma_p_allow"]) + offset;
            r.AtMost("挤压应力", sigma, args["sigma_p_allow"], "σ_p ≤ [σ_p]");
            r.Note("键宽、键高按轴径查 GB/T 1095/1096；键长取标准系列且不超过轮毂长度");
        },
    };
}
