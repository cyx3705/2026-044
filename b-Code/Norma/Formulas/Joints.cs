namespace Norma.Formulas;

/// <summary>螺纹连接、螺旋传动、焊缝、过盈配合。</summary>
internal static class Joints
{
    public static IEnumerable<Calculation> All()
    {
        yield return Thread();
        yield return Torque();
        yield return Preload();
        yield return Tension();
        yield return Friction();
        yield return Shear();
        yield return Screw();
        yield return Fillet();
        yield return Press();
    }

    /// <summary>ISO 普通外螺纹（60° 牙型）的中径、小径与应力截面积。</summary>
    internal static (double D2, double D3, double As) ThreadSizes(double d, double p)
    {
        var d2 = d - 0.649519 * p;
        var d3 = d - 1.226869 * p;
        var mean = (d2 + d3) / 2;
        return (d2, d3, Math.PI / 4 * mean * mean);
    }

    private static readonly Input Diameter = Input.Positive("d", "d", "mm", "螺纹公称直径", 10);
    private static readonly Input Pitch = Input.Positive("p", "P", "mm", "螺距（M10 粗牙 1.5）", 1.5);

    private static Calculation Thread() => new()
    {
        CatalogSummary = "查询普通螺纹基本尺寸和应力截面积",
        Id = "bolt.thread",
        Title = "普通螺纹基本尺寸与应力截面积",
        Source = "GB/T 196 / ISO 724 基本尺寸：d₂ = d − 0.6495P，d₃ = d − 1.2269P；GB/T 3098.1 / ISO 898-1 应力截面积 A_s = π/4·((d₂ + d₃)/2)²",
        Scope = "60° 普通外螺纹，粗牙或细牙",
        Inputs = [Diameter, Pitch],
        Outputs =
        [
            new("d2", "d₂", "mm", "中径"),
            new("d3", "d₃", "mm", "外螺纹小径"),
            new("d1", "D₁", "mm", "内螺纹小径 d − 1.0825P"),
            new("as", "A_s", "mm²", "应力截面积"),
        ],
        Evaluate = (args, r) =>
        {
            var (d2, d3, area) = ThreadSizes(args["d"], args["p"]);
            r["d2"] = d2;
            r["d3"] = d3;
            r["d1"] = args["d"] - 1.082532 * args["p"];
            r["as"] = area;
        },
    };

    private static readonly Input TighteningK =
        new("k", "K", "", "拧紧力矩系数；无润滑钢件约 0.2，润滑 0.12～0.15") { Default = 0.2, Min = 0.05, Max = 0.5 };

    private static Calculation Torque() => new()
    {
        CatalogSummary = "由预紧力计算螺栓拧紧力矩",
        Id = "bolt.torque",
        Title = "由预紧力求拧紧力矩",
        Source = "T = K F₀ d（K 为拧紧力矩系数，综合螺纹与支承面摩擦）",
        Scope = "扭矩法拧紧的估算；K 的离散性大，重要连接应试验标定",
        Inputs = [Input.Positive("f0", "F₀", "N", "要求的预紧力", 20000), Diameter, TighteningK],
        Outputs = [new("t", "T", "N·m", "拧紧力矩")],
        Evaluate = (args, r) => r["t"] = args["k"] * args["f0"] * args["d"] / 1000,
    };

    private static Calculation Preload() => new()
    {
        CatalogSummary = "由拧紧力矩计算螺栓预紧力",
        Id = "bolt.preload",
        Title = "由拧紧力矩求预紧力",
        Source = "F₀ = T / (K d)",
        Scope = "扭矩法拧紧的估算；预紧力离散度约 ±25%",
        Inputs = [Input.Positive("t", "T", "N·m", "拧紧力矩", 40), Diameter, TighteningK],
        Outputs = [new("f0", "F₀", "N", "预紧力")],
        Evaluate = (args, r) => r["f0"] = args["t"] * 1000 / (args["k"] * args["d"]),
    };

    private static Calculation Tension() => new()
    {
        CatalogSummary = "校核轴向载荷下紧螺栓的强度",
        Id = "bolt.tension",
        Title = "受轴向载荷的紧螺栓强度",
        Source = "总拉力 F₂ = F₀ + C_b F（C_b = c_b/(c_b + c_m)），残余预紧力 F₁ = F₀ − (1 − C_b)F；σ_ca = 1.3 F₂ / A_s（1.3 计入拧紧扭转切应力）",
        Scope = "单个螺栓、轴向工作载荷；静强度校核；变载荷还须另做应力幅校核",
        Inputs =
        [
            Diameter,
            Pitch,
            Input.Positive("f0", "F₀", "N", "预紧力", 20000),
            new Input("f", "F", "N", "单个螺栓的轴向工作载荷") { Default = 0, Min = 0 },
            new Input("cb", "C_b", "", "螺栓相对刚度；金属垫片 0.2～0.3，铜皮石棉 0.8，橡胶 0.9") { Default = 0.25, Min = 0, Max = 1 },
            Input.Positive("sigma_s", "σ_s", "MPa", "螺栓屈服强度（8.8 级 640，10.9 级 900，12.9 级 1080）", 640),
            new Input("s", "S", "", "安全系数；控制预紧力 1.2～1.5") { Default = 1.5, Min = 1, Max = 5 },
            new Input("f1_min", "F₁min", "N", "要求的最小残余预紧力（密封或防松）") { Default = 0, Min = 0 },
        ],
        Outputs =
        [
            new("as", "A_s", "mm²", "应力截面积"),
            new("f2", "F₂", "N", "螺栓总拉力"),
            new("f1", "F₁", "N", "残余预紧力"),
            new("sigma_ca", "σ_ca", "MPa", "计算应力"),
            new("sigma_allow", "[σ]", "MPa", "许用拉应力 σ_s/S"),
        ],
        Evaluate = (args, r) =>
        {
            var (_, _, area) = ThreadSizes(args["d"], args["p"]);
            var f2 = args["f0"] + args["cb"] * args["f"];
            var f1 = args["f0"] - (1 - args["cb"]) * args["f"];
            var sigma = 1.3 * f2 / area;
            var allow = args["sigma_s"] / args["s"];
            r["as"] = area;
            r["f2"] = f2;
            r["f1"] = f1;
            r["sigma_ca"] = sigma;
            r["sigma_allow"] = allow;
            r.AtMost("螺栓拉应力", sigma, allow, "σ_ca ≤ σ_s/S");
            r.AtLeast("残余预紧力", f1, Math.Max(args["f1_min"], 0), "F₁ ≥ F₁min（且不得分离）");
        },
    };

    private static Calculation Friction() => new()
    {
        CatalogSummary = "计算横向载荷下摩擦传力螺栓的预紧力",
        Id = "bolt.friction",
        Title = "受横向载荷的普通螺栓所需预紧力",
        Source = "靠摩擦传力：F₀ ≥ K_s F / (f i z)",
        Scope = "普通（有间隙）螺栓组受横向载荷、各螺栓均载；求出 F₀ 后再用 bolt.tension（f=0）校核螺栓",
        Inputs =
        [
            Input.Positive("f", "F", "N", "螺栓组承受的总横向载荷", 10000),
            new Input("z", "z", "", "螺栓数") { Min = 1, Example = 4 },
            new Input("i", "i", "", "结合面数") { Default = 1, Min = 1, Max = 10 },
            new Input("mu", "f", "", "结合面摩擦系数；干燥钢面 0.1～0.16") { Default = 0.15, Min = 0.02, Max = 0.6 },
            new Input("ks", "K_s", "", "防滑系数") { Default = 1.2, Min = 1, Max = 2 },
        ],
        Outputs = [new("f0", "F₀", "N", "每个螺栓所需的预紧力")],
        Evaluate = (args, r) => r["f0"] = args["ks"] * args["f"] / (args["mu"] * args["i"] * args["z"]),
    };

    private static Calculation Shear() => new()
    {
        CatalogSummary = "校核铰制孔螺栓的剪切和挤压强度",
        Id = "bolt.shear",
        Title = "铰制孔用螺栓剪切与挤压强度",
        Source = "τ = F / (m π d₀²/4)，σ_p = F / (d₀ L_min)",
        Scope = "单个铰制孔螺栓承受横向载荷，螺杆与孔壁无间隙",
        Inputs =
        [
            Input.Positive("f", "F", "N", "单个螺栓承受的横向载荷", 10000),
            Input.Positive("d0", "d₀", "mm", "螺栓受剪处直径", 11),
            new Input("m", "m", "", "受剪面数") { Default = 1, Min = 1, Max = 10 },
            Input.Positive("lmin", "L_min", "mm", "孔壁最小受挤压高度", 12),
            Input.Positive("tau_allow", "[τ]", "MPa", "许用切应力（静载钢约 σ_s/2.5）", 256),
            Input.Positive("sigma_p_allow", "[σ_p]", "MPa", "较弱被连接件的许用挤压应力", 250),
        ],
        Outputs = [new("tau", "τ", "MPa", "切应力"), new("sigma_p", "σ_p", "MPa", "挤压应力")],
        Evaluate = (args, r) =>
        {
            var d0 = args["d0"];
            var tau = args["f"] / (args["m"] * Math.PI * d0 * d0 / 4);
            var sigma = args["f"] / (d0 * args["lmin"]);
            r["tau"] = tau;
            r["sigma_p"] = sigma;
            r.AtMost("切应力", tau, args["tau_allow"], "τ ≤ [τ]");
            r.AtMost("挤压应力", sigma, args["sigma_p_allow"], "σ_p ≤ [σ_p]");
        },
    };

    private static Calculation Screw() => new()
    {
        CatalogSummary = "计算螺旋传动的力矩、效率和自锁",
        Id = "screw.drive",
        Title = "螺旋传动的力矩、效率与自锁",
        Source = "导程角 ψ = arctan(nP/(πd₂))，当量摩擦角 ρ_v = arctan(f/cos(α/2))；举升力矩 T = F d₂/2·tan(ψ + ρ_v)，效率 η = tanψ/tan(ψ + ρ_v)，自锁条件 ψ ≤ ρ_v",
        Scope = "滑动螺旋（梯形、锯齿、矩形、三角形螺纹），不计支承面摩擦",
        Inputs =
        [
            Input.Positive("f", "F", "N", "轴向载荷", 10000),
            Input.Positive("d2", "d₂", "mm", "螺纹中径（Tr24×5 为 21.5）", 21.5),
            Input.Positive("p", "P", "mm", "螺距", 5),
            new Input("n", "n", "", "线数") { Default = 1, Min = 1, Max = 8 },
            new Input("alpha", "α", "°", "牙型角：梯形 30，锯齿 3（工作面），矩形 0，普通 60") { Default = 30, Min = 0, Max = 60 },
            new Input("mu", "f", "", "螺纹副摩擦系数；钢对青铜润滑 0.08～0.10") { Default = 0.1, Min = 0.01, Max = 0.5 },
        ],
        Outputs =
        [
            new("psi", "ψ", "°", "导程角"),
            new("rho_v", "ρ_v", "°", "当量摩擦角"),
            new("t_raise", "T", "N·m", "举升（逆载荷旋进）所需力矩"),
            new("t_lower", "T′", "N·m", "下降（顺载荷）所需力矩，负值表示会自行下滑"),
            new("eta", "η", "", "举升效率"),
        ],
        Evaluate = (args, r) =>
        {
            var psi = Math.Atan(args["n"] * args["p"] / (Math.PI * args["d2"]));
            var rhoV = Math.Atan(args["mu"] / Math.Cos(args["alpha"] / 2 * Num.Deg));
            var radius = args["d2"] / 2 / 1000;
            r["psi"] = psi / Num.Deg;
            r["rho_v"] = rhoV / Num.Deg;
            r["t_raise"] = args["f"] * radius * Math.Tan(psi + rhoV);
            r["t_lower"] = args["f"] * radius * Math.Tan(rhoV - psi);
            r["eta"] = Math.Tan(psi) / Math.Tan(psi + rhoV);
            r.AtMost("自锁", psi / Num.Deg, rhoV / Num.Deg, "ψ ≤ ρ_v 时自锁（要求自锁的场合才看这条）");
        },
    };

    private static Calculation Fillet() => new()
    {
        CatalogSummary = "校核角焊缝受剪强度",
        Id = "weld.fillet",
        Title = "角焊缝受剪强度",
        Source = "τ = F / (h_e Σl)，有效厚度 h_e = 0.7k（直角角焊缝）",
        Scope = "角焊缝承受通过焊缝形心的力（正面、侧面焊缝统一按切应力偏安全计）；弯矩、扭矩须另行合成",
        Inputs =
        [
            Input.Positive("f", "F", "N", "焊缝承受的力", 50000),
            Input.Positive("k", "k", "mm", "焊脚尺寸", 6),
            Input.Positive("l", "Σl", "mm", "焊缝计算总长（每段扣去起落弧约 2k）", 200),
            Input.Positive("tau_allow", "[τ′]", "MPa", "焊缝许用切应力（Q235 手工焊静载约 140）", 140),
        ],
        Outputs = [new("he", "h_e", "mm", "有效厚度"), new("tau", "τ", "MPa", "焊缝切应力")],
        Evaluate = (args, r) =>
        {
            var he = 0.7 * args["k"];
            var tau = args["f"] / (he * args["l"]);
            r["he"] = he;
            r["tau"] = tau;
            r.AtMost("焊缝切应力", tau, args["tau_allow"], "τ ≤ [τ′]");
        },
    };

    private static Calculation Press() => new()
    {
        CatalogSummary = "计算圆柱面过盈连接",
        Id = "fit.press",
        Title = "圆柱面过盈连接",
        Source = "厚壁圆筒 Lamé 解：p = δ / (d (C₁/E₁ + C₂/E₂))，C₁ = (d² + d₁²)/(d² − d₁²) − ν₁，C₂ = (d₂² + d²)/(d₂² − d²) + ν₂；T = p π d l f d/2，F_a = p π d l f",
        Scope = "弹性范围、等长包容件与被包容件；δ 为计入表面压平后的有效过盈量（测量过盈减去约 0.8(Rz₁ + Rz₂)）",
        Inputs =
        [
            Input.Positive("d", "d", "mm", "配合直径", 50),
            Input.Positive("l", "l", "mm", "配合长度", 60),
            Input.Positive("delta", "δ", "μm", "有效过盈量（直径上）", 40),
            new Input("d1", "d₁", "mm", "轴的内径，实心为 0") { Default = 0, Min = 0 },
            Input.Positive("d2", "d₂", "mm", "轮毂外径", 100),
            new Input("e1", "E₁", "MPa", "轴的弹性模量") { Default = 206000, Min = 1000 },
            new Input("e2", "E₂", "MPa", "轮毂的弹性模量") { Default = 206000, Min = 1000 },
            new Input("nu1", "ν₁", "", "轴的泊松比") { Default = 0.3, Min = 0, Max = 0.5 },
            new Input("nu2", "ν₂", "", "轮毂的泊松比") { Default = 0.3, Min = 0, Max = 0.5 },
            new Input("mu", "f", "", "配合面摩擦系数；钢对钢压入无润滑 0.07～0.16，温差法更高") { Default = 0.12, Min = 0.01, Max = 0.5 },
        ],
        Outputs =
        [
            new("p", "p", "MPa", "配合面压强"),
            new("t", "T", "N·m", "可传递扭矩"),
            new("fa", "F_a", "N", "可承受轴向力（也约等于压入力）"),
            new("sigma_hub", "σ_t2", "MPa", "轮毂内表面周向拉应力"),
            new("sigma_shaft", "σ_t1", "MPa", "轴外表面周向压应力（实心轴等于 −p）"),
        ],
        Evaluate = (args, r) =>
        {
            double d = args["d"], d1 = args["d1"], d2 = args["d2"];
            if (d1 >= d)
                throw new ArgumentException("轴内径 d₁ 必须小于配合直径 d");
            if (d2 <= d)
                throw new ArgumentException("轮毂外径 d₂ 必须大于配合直径 d");
            var c1 = (d * d + d1 * d1) / (d * d - d1 * d1) - args["nu1"];
            var c2 = (d2 * d2 + d * d) / (d2 * d2 - d * d) + args["nu2"];
            var p = args["delta"] / 1000 / (d * (c1 / args["e1"] + c2 / args["e2"]));
            var area = Math.PI * d * args["l"];
            r["p"] = p;
            r["fa"] = p * area * args["mu"];
            r["t"] = p * area * args["mu"] * d / 2 / 1000;
            r["sigma_hub"] = p * (d2 * d2 + d * d) / (d2 * d2 - d * d);
            r["sigma_shaft"] = -p * (d * d + d1 * d1) / (d * d - d1 * d1);
        },
    };
}
