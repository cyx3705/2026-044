namespace Norma.Formulas;

/// <summary>渐开线圆柱齿轮（外啮合）：几何、接触与弯曲强度、初估尺寸。</summary>
internal static class Gears
{
    private const string Pair = "外啮合渐开线直齿或斜齿圆柱齿轮副，标准基本齿条（GB/T 1356 / ISO 53）";

    // 几何类输入，几何、接触、弯曲三条计算共用，名字与含义必须一致。
    private static readonly Input[] GeometryInputs =
    [
        Input.Positive("mn", "mₙ", "mm", "法向模数", 2),
        new Input("z1", "z₁", "", "小轮齿数") { Min = 6, Max = 1000, Example = 20 },
        new Input("z2", "z₂", "", "大轮齿数（不小于小轮）") { Min = 6, Max = 10000, Example = 40 },
        new Input("alpha", "αₙ", "°", "法向压力角") { Default = 20, Min = 10, Max = 30 },
        new Input("beta", "β", "°", "分度圆螺旋角，直齿为 0") { Default = 0, Min = 0, Max = 45 },
        new Input("x1", "x₁", "", "小轮法向变位系数") { Default = 0, Min = -1, Max = 2 },
        new Input("x2", "x₂", "", "大轮法向变位系数") { Default = 0, Min = -1, Max = 2 },
        new Input("ha", "hₐ*", "", "齿顶高系数") { Default = 1, Min = 0.5, Max = 1.5 },
        new Input("c", "c*", "", "顶隙系数") { Default = 0.25, Min = 0.1, Max = 0.5 },
    ];

    public static IEnumerable<Calculation> All()
    {
        yield return Geometry();
        yield return Contact();
        yield return Bending();
        yield return Diameter();
        yield return Module();
    }

    /// <summary>齿轮副几何，供几何/接触/弯曲共用。</summary>
    internal sealed class PairGeometry
    {
        public double Mn, Z1, Z2, AlphaN, Beta, X1, X2, Ha, C;
        public double AlphaT, Mt, D1, D2, Db1, Db2, A, Aw, AlphaWt, Y, DeltaY;
        public double Ha1, Ha2, Hf1, Hf2, Da1, Da2, Df1, Df2, BetaB, EpsAlpha, U;

        public static PairGeometry From(Args args)
        {
            var g = new PairGeometry
            {
                Mn = args["mn"], Z1 = args["z1"], Z2 = args["z2"],
                AlphaN = args["alpha"] * Num.Deg, Beta = args["beta"] * Num.Deg,
                X1 = args["x1"], X2 = args["x2"], Ha = args["ha"], C = args["c"],
            };
            if (g.Z2 < g.Z1)
                throw new ArgumentException("z2 必须不小于 z1（z1 是小轮）");
            g.U = g.Z2 / g.Z1;
            g.AlphaT = Math.Atan(Math.Tan(g.AlphaN) / Math.Cos(g.Beta));
            g.Mt = g.Mn / Math.Cos(g.Beta);
            g.D1 = g.Mt * g.Z1;
            g.D2 = g.Mt * g.Z2;
            g.Db1 = g.D1 * Math.Cos(g.AlphaT);
            g.Db2 = g.D2 * Math.Cos(g.AlphaT);
            g.BetaB = Math.Atan(Math.Tan(g.Beta) * Math.Cos(g.AlphaT));
            // 无侧隙啮合方程：inv αwt = 2 tan αn (x1+x2)/(z1+z2) + inv αt
            g.AlphaWt = Num.InvInverse(2 * Math.Tan(g.AlphaN) * (g.X1 + g.X2) / (g.Z1 + g.Z2) + Num.Inv(g.AlphaT));
            g.A = g.Mt * (g.Z1 + g.Z2) / 2;
            g.Aw = g.A * Math.Cos(g.AlphaT) / Math.Cos(g.AlphaWt);
            g.Y = (g.Aw - g.A) / g.Mn;
            g.DeltaY = g.X1 + g.X2 - g.Y;
            g.Ha1 = g.Mn * (g.Ha + g.X1 - g.DeltaY);
            g.Ha2 = g.Mn * (g.Ha + g.X2 - g.DeltaY);
            g.Hf1 = g.Mn * (g.Ha + g.C - g.X1);
            g.Hf2 = g.Mn * (g.Ha + g.C - g.X2);
            g.Da1 = g.D1 + 2 * g.Ha1;
            g.Da2 = g.D2 + 2 * g.Ha2;
            g.Df1 = g.D1 - 2 * g.Hf1;
            g.Df2 = g.D2 - 2 * g.Hf2;
            var tanWt = Math.Tan(g.AlphaWt);
            g.EpsAlpha = (g.Z1 * (Math.Tan(Math.Acos(g.Db1 / g.Da1)) - tanWt)
                          + g.Z2 * (Math.Tan(Math.Acos(g.Db2 / g.Da2)) - tanWt)) / (2 * Math.PI);
            return g;
        }

        public double EpsBeta(double b) => b * Math.Sin(Beta) / (Math.PI * Mn);

        /// <summary>当量齿数 zn = z / (cos²βb cos β)（ISO 6336-3）。</summary>
        public double Zn(double z) => z / (Math.Pow(Math.Cos(BetaB), 2) * Math.Cos(Beta));

        /// <summary>不根切的最小变位系数（按当量齿数 z/cos³β 的近似式）。</summary>
        public double XMin(double z) => Ha - z / Math.Pow(Math.Cos(Beta), 3) * Math.Pow(Math.Sin(AlphaN), 2) / 2;
    }

    private static Calculation Geometry() => new()
    {
        Id = "gear.geometry",
        Title = "齿轮副几何尺寸",
        Source = "GB/T 3374 / ISO 21771 渐开线圆柱齿轮几何：无侧隙啮合方程、中心距变动系数、齿顶高变动系数、端面重合度",
        Scope = Pair + "；齿顶按齿顶高变动系数削减，未计侧隙与齿顶倒棱",
        Inputs = [.. GeometryInputs, new Input("b", "b", "mm", "齿宽，只用于纵向重合度；0 表示不计") { Default = 0, Min = 0 }],
        Outputs =
        [
            new("u", "u", "", "齿数比 z₂/z₁"),
            new("mt", "mₜ", "mm", "端面模数"),
            new("alpha_t", "αₜ", "°", "端面压力角"),
            new("d1", "d₁", "mm", "小轮分度圆直径"),
            new("d2", "d₂", "mm", "大轮分度圆直径"),
            new("db1", "d_b1", "mm", "小轮基圆直径"),
            new("db2", "d_b2", "mm", "大轮基圆直径"),
            new("da1", "dₐ₁", "mm", "小轮齿顶圆直径"),
            new("da2", "dₐ₂", "mm", "大轮齿顶圆直径"),
            new("df1", "d_f1", "mm", "小轮齿根圆直径"),
            new("df2", "d_f2", "mm", "大轮齿根圆直径"),
            new("a", "a", "mm", "标准中心距"),
            new("aw", "a_w", "mm", "实际（变位后）中心距"),
            new("alpha_wt", "α_wt", "°", "端面啮合角"),
            new("y", "y", "", "中心距变动系数"),
            new("dy", "Δy", "", "齿顶高变动系数"),
            new("eps_alpha", "ε_α", "", "端面重合度"),
            new("eps_beta", "ε_β", "", "纵向重合度"),
            new("eps_gamma", "ε_γ", "", "总重合度"),
            new("xmin1", "x_min1", "", "小轮不根切最小变位系数"),
            new("xmin2", "x_min2", "", "大轮不根切最小变位系数"),
        ],
        Evaluate = (args, r) =>
        {
            var g = PairGeometry.From(args);
            r["u"] = g.U;
            r["mt"] = g.Mt;
            r["alpha_t"] = g.AlphaT / Num.Deg;
            r["d1"] = g.D1;
            r["d2"] = g.D2;
            r["db1"] = g.Db1;
            r["db2"] = g.Db2;
            r["da1"] = g.Da1;
            r["da2"] = g.Da2;
            r["df1"] = g.Df1;
            r["df2"] = g.Df2;
            r["a"] = g.A;
            r["aw"] = g.Aw;
            r["alpha_wt"] = g.AlphaWt / Num.Deg;
            r["y"] = g.Y;
            r["dy"] = g.DeltaY;
            r["eps_alpha"] = g.EpsAlpha;
            var epsBeta = g.EpsBeta(args["b"]);
            r["eps_beta"] = epsBeta;
            r["eps_gamma"] = g.EpsAlpha + epsBeta;
            r["xmin1"] = g.XMin(g.Z1);
            r["xmin2"] = g.XMin(g.Z2);
            r.AtLeast("端面重合度", g.EpsAlpha, 1.2, "一般传动建议 ε_α ≥ 1.2");
            r.AtLeast("小轮不根切", g.X1, g.XMin(g.Z1), "x₁ ≥ x_min1");
            r.AtLeast("大轮不根切", g.X2, g.XMin(g.Z2), "x₂ ≥ x_min2");
            if (args["b"] == 0 && g.Beta > 0)
                r.Note("斜齿未给齿宽 b，纵向重合度按 0 计");
        },
    };

    // 强度计算共用的载荷与系数输入。
    private static readonly Input[] LoadInputs =
    [
        Input.Positive("t1", "T₁", "N·m", "小轮传递的名义扭矩", 100),
        Input.Positive("b", "b", "mm", "工作齿宽（两轮啮合宽度）", 40),
        new Input("ka", "K_A", "", "使用系数（原动机与工作机冲击）；缺省 1.25 为电机驱动中等冲击的粗估") { Default = 1.25, Min = 1, Max = 3 },
        new Input("kv", "K_v", "", "动载系数；缺省 1.1 为中等精度中低速的粗估") { Default = 1.1, Min = 1, Max = 3 },
    ];

    private static Calculation Contact() => new()
    {
        Id = "gear.contact",
        Title = "齿面接触强度校核",
        Source = "GB/T 3480.2 / ISO 6336-2 方法 B 基本式：σ_H = Z_H Z_E Z_ε Z_β √(F_t/(d₁b)·(u+1)/u)·√(K_A K_v K_Hβ K_Hα)",
        Scope = Pair + "；按节点计算（不计单对齿啮合系数 Z_B/Z_D），寿命、润滑、速度、粗糙度等影响合并为 z_n 由调用方给定",
        Inputs =
        [
            .. GeometryInputs,
            .. LoadInputs,
            new Input("khb", "K_Hβ", "", "接触齿向载荷分布系数；缺省 1.15 为对称布置刚性轴的粗估") { Default = 1.15, Min = 1, Max = 3 },
            new Input("kha", "K_Hα", "", "接触齿间载荷分配系数；缺省 1.1") { Default = 1.1, Min = 1, Max = 3 },
            new Input("e1", "E₁", "MPa", "小轮弹性模量，钢 206000") { Default = 206000, Min = 1000 },
            new Input("e2", "E₂", "MPa", "大轮弹性模量，钢 206000") { Default = 206000, Min = 1000 },
            new Input("nu1", "ν₁", "", "小轮泊松比") { Default = 0.3, Min = 0, Max = 0.5 },
            new Input("nu2", "ν₂", "", "大轮泊松比") { Default = 0.3, Min = 0, Max = 0.5 },
            Input.Positive("sigma_hlim", "σ_Hlim", "MPa", "较弱一轮的接触疲劳极限（按材料与热处理查取）", 1100),
            new Input("zn", "Z_N", "", "接触寿命系数（含润滑、速度、粗糙度等修正的合并系数）；缺省 1") { Default = 1, Min = 0.5, Max = 1.8 },
            new Input("sh_min", "S_Hmin", "", "最小接触安全系数") { Default = 1, Min = 0.5, Max = 3 },
        ],
        Outputs =
        [
            new("ft", "F_t", "N", "分度圆切向力"),
            new("d1", "d₁", "mm", "小轮分度圆直径"),
            new("u", "u", "", "齿数比"),
            new("zh", "Z_H", "", "节点区域系数"),
            new("ze", "Z_E", "√MPa", "弹性系数"),
            new("zeps", "Z_ε", "", "接触重合度系数"),
            new("zbeta", "Z_β", "", "接触螺旋角系数"),
            new("eps_alpha", "ε_α", "", "端面重合度"),
            new("eps_beta", "ε_β", "", "纵向重合度"),
            new("sigma_h", "σ_H", "MPa", "计算接触应力"),
            new("sigma_hp", "σ_HP", "MPa", "许用接触应力"),
            new("sh", "S_H", "", "接触安全系数"),
        ],
        Evaluate = (args, r) =>
        {
            var g = PairGeometry.From(args);
            var b = args["b"];
            var ft = 2000 * args["t1"] / g.D1;
            var zh = Math.Sqrt(2 * Math.Cos(g.BetaB) * Math.Cos(g.AlphaWt)
                               / (Math.Pow(Math.Cos(g.AlphaT), 2) * Math.Sin(g.AlphaWt)));
            var ze = Math.Sqrt(1 / (Math.PI * ((1 - Math.Pow(args["nu1"], 2)) / args["e1"]
                                               + (1 - Math.Pow(args["nu2"], 2)) / args["e2"])));
            var epsBeta = g.EpsBeta(b);
            var zeps = epsBeta < 1
                ? Math.Sqrt((4 - g.EpsAlpha) / 3 * (1 - epsBeta) + epsBeta / g.EpsAlpha)
                : Math.Sqrt(1 / g.EpsAlpha);
            var zbeta = Math.Sqrt(Math.Cos(g.Beta));
            var k = args["ka"] * args["kv"] * args["khb"] * args["kha"];
            var sigmaH = zh * ze * zeps * zbeta * Math.Sqrt(ft / (g.D1 * b) * (g.U + 1) / g.U) * Math.Sqrt(k);
            var limit = args["sigma_hlim"] * args["zn"];
            r["ft"] = ft;
            r["d1"] = g.D1;
            r["u"] = g.U;
            r["zh"] = zh;
            r["ze"] = ze;
            r["zeps"] = zeps;
            r["zbeta"] = zbeta;
            r["eps_alpha"] = g.EpsAlpha;
            r["eps_beta"] = epsBeta;
            r["sigma_h"] = sigmaH;
            r["sigma_hp"] = limit / args["sh_min"];
            r["sh"] = limit / sigmaH;
            r.AtLeast("接触安全系数", limit / sigmaH, args["sh_min"], "S_H = σ_Hlim Z_N / σ_H ≥ S_Hmin");
        },
    };

    private static Calculation Bending() => new()
    {
        Id = "gear.bending",
        Title = "齿根弯曲强度校核",
        Source = "GB/T 3480.3 / ISO 6336-3 方法 B：σ_F = F_t/(b mₙ)·Y_F Y_S Y_β Y_ε·K_A K_v K_Fβ K_Fα；Y_F、Y_S 按载荷作用于单对齿啮合外界点求齿形",
        Scope = Pair + "，滚刀加工无凸台；Y_ε 取 0.25 + 0.75/ε_αn；许用应力 σ_FP = σ_FE Y_NT / S_Fmin（σ_FE = 2σ_Flim，相对齿根敏感/表面系数取 1）",
        Inputs =
        [
            .. GeometryInputs,
            .. LoadInputs,
            new Input("kfb", "K_Fβ", "", "弯曲齿向载荷分布系数；缺省 1.12") { Default = 1.12, Min = 1, Max = 3 },
            new Input("kfa", "K_Fα", "", "弯曲齿间载荷分配系数；缺省 1.1") { Default = 1.1, Min = 1, Max = 3 },
            new Input("hfp", "h_fP*", "", "基本齿条齿根高系数（刀具齿顶高）") { Default = 1.25, Min = 0.8, Max = 1.6 },
            new Input("rhofp", "ρ_fP*", "", "基本齿条齿根圆角半径系数") { Default = 0.38, Min = 0, Max = 0.6 },
            Input.Positive("sigma_flim1", "σ_Flim1", "MPa", "小轮弯曲疲劳极限（名义值，σ_FE = 2σ_Flim）", 350),
            Input.Positive("sigma_flim2", "σ_Flim2", "MPa", "大轮弯曲疲劳极限", 300),
            new Input("yn", "Y_NT", "", "弯曲寿命系数；缺省 1") { Default = 1, Min = 0.5, Max = 2.5 },
            new Input("sf_min", "S_Fmin", "", "最小弯曲安全系数") { Default = 1.25, Min = 0.5, Max = 3 },
        ],
        Outputs =
        [
            new("ft", "F_t", "N", "分度圆切向力"),
            new("yf1", "Y_F1", "", "小轮齿形系数"),
            new("yf2", "Y_F2", "", "大轮齿形系数"),
            new("ys1", "Y_S1", "", "小轮应力修正系数"),
            new("ys2", "Y_S2", "", "大轮应力修正系数"),
            new("yeps", "Y_ε", "", "弯曲重合度系数"),
            new("ybeta", "Y_β", "", "弯曲螺旋角系数"),
            new("sigma_f1", "σ_F1", "MPa", "小轮计算齿根应力"),
            new("sigma_f2", "σ_F2", "MPa", "大轮计算齿根应力"),
            new("sigma_fp1", "σ_FP1", "MPa", "小轮许用齿根应力"),
            new("sigma_fp2", "σ_FP2", "MPa", "大轮许用齿根应力"),
            new("sf1", "S_F1", "", "小轮弯曲安全系数"),
            new("sf2", "S_F2", "", "大轮弯曲安全系数"),
        ],
        Evaluate = (args, r) =>
        {
            var g = PairGeometry.From(args);
            var b = args["b"];
            var ft = 2000 * args["t1"] / g.D1;
            var epsAlphaN = g.EpsAlpha / Math.Pow(Math.Cos(g.BetaB), 2);
            var yeps = 0.25 + 0.75 / epsAlphaN;
            var epsBeta = Math.Min(1, g.EpsBeta(b));
            var ybeta = 1 - epsBeta * Math.Min(30, g.Beta / Num.Deg) / 120;
            var k = args["ka"] * args["kv"] * args["kfb"] * args["kfa"];
            var (yf1, ys1) = FormFactors(g, g.Z1, g.X1, g.D1, g.Da1, epsAlphaN, args["hfp"], args["rhofp"]);
            var (yf2, ys2) = FormFactors(g, g.Z2, g.X2, g.D2, g.Da2, epsAlphaN, args["hfp"], args["rhofp"]);
            var nominal = ft / (b * g.Mn) * yeps * ybeta * k;
            var sigma1 = nominal * yf1 * ys1;
            var sigma2 = nominal * yf2 * ys2;
            var limit1 = 2 * args["sigma_flim1"] * args["yn"];
            var limit2 = 2 * args["sigma_flim2"] * args["yn"];
            r["ft"] = ft;
            r["yf1"] = yf1;
            r["yf2"] = yf2;
            r["ys1"] = ys1;
            r["ys2"] = ys2;
            r["yeps"] = yeps;
            r["ybeta"] = ybeta;
            r["sigma_f1"] = sigma1;
            r["sigma_f2"] = sigma2;
            r["sigma_fp1"] = limit1 / args["sf_min"];
            r["sigma_fp2"] = limit2 / args["sf_min"];
            r["sf1"] = limit1 / sigma1;
            r["sf2"] = limit2 / sigma2;
            r.AtLeast("小轮弯曲安全系数", limit1 / sigma1, args["sf_min"], "S_F1 = σ_FE1 Y_NT / σ_F1 ≥ S_Fmin");
            r.AtLeast("大轮弯曲安全系数", limit2 / sigma2, args["sf_min"], "S_F2 = σ_FE2 Y_NT / σ_F2 ≥ S_Fmin");
        },
    };

    /// <summary>
    /// ISO 6336-3 方法 B 的齿形系数 Y_F 与应力修正系数 Y_S（外齿轮、滚刀无凸台）。
    /// 先迭代求 30° 切线点的角度 θ，再求危险截面弦齿厚 s_Fn、圆角半径 ρ_F 与弯曲力臂 h_Fe。
    /// </summary>
    internal static (double YF, double YS) FormFactors(
        PairGeometry g, double z, double x, double d, double da, double epsAlphaN, double hfpStar, double rhofpStar)
    {
        var mn = g.Mn;
        var alphaN = g.AlphaN;
        var hfP = hfpStar * mn;
        var rhofP = rhofpStar * mn;
        var zn = g.Zn(z);

        var e = Math.PI / 4 * mn - hfP * Math.Tan(alphaN) - (1 - Math.Sin(alphaN)) * rhofP / Math.Cos(alphaN);
        var gg = rhofP / mn - hfP / mn + x;
        var h = 2 / zn * (Math.PI / 2 - e / mn) - Math.PI / 3;
        var theta = Math.PI / 6;
        for (var i = 0; i < 100; i++)
        {
            var next = 2 * gg / zn * Math.Tan(theta) - h;
            if (Math.Abs(next - theta) < 1e-12)
            {
                theta = next;
                break;
            }

            theta = next;
        }

        var sFn = mn * (zn * Math.Sin(Math.PI / 3 - theta) + Math.Sqrt(3) * (gg / Math.Cos(theta) - rhofP / mn));
        var rhoF = rhofP + mn * 2 * gg * gg / (Math.Cos(theta) * (zn * Math.Pow(Math.Cos(theta), 2) - 2 * gg));

        var dn = mn * zn;
        var dbn = dn * Math.Cos(alphaN);
        var dan = dn + da - d;
        var inner = Math.Sqrt(Math.Pow(dan / 2, 2) - Math.Pow(dbn / 2, 2))
                    - Math.PI * d * Math.Cos(g.Beta) * Math.Cos(alphaN) / z * (epsAlphaN - 1);
        var den = 2 * Math.Sqrt(inner * inner + Math.Pow(dbn / 2, 2));
        var alphaEn = Math.Acos(dbn / den);
        var gammaE = (0.5 * Math.PI + 2 * Math.Tan(alphaN) * x) / zn + Num.Inv(alphaN) - Num.Inv(alphaEn);
        var alphaFen = alphaEn - gammaE;
        var hFe = 0.5 * mn * ((Math.Cos(gammaE) - Math.Sin(gammaE) * Math.Tan(alphaFen)) * den / mn
                              - zn * Math.Cos(Math.PI / 3 - theta) - gg / Math.Cos(theta) + rhofP / mn);

        var yf = 6 * hFe / mn * Math.Cos(alphaFen) / (Math.Pow(sFn / mn, 2) * Math.Cos(alphaN));
        var l = sFn / hFe;
        var qs = sFn / (2 * rhoF);
        var ys = (1.2 + 0.13 * l) * Math.Pow(qs, 1 / (1.21 + 2.3 / l));
        return (yf, ys);
    }

    private static Calculation Diameter() => new()
    {
        Id = "gear.diameter",
        Title = "按接触强度初估小轮直径",
        Source = "由接触强度基本式反解：d₁ ≥ ∛(2K T₁/φ_d·(u+1)/u·(Z_H Z_E Z_ε/σ_HP)²)，常见于机械设计教材的设计式",
        Scope = "闭式软齿面或硬齿面初步设计，得到的 d₁ 还须按 gear.contact 精确校核",
        Inputs =
        [
            Input.Positive("t1", "T₁", "N·m", "小轮扭矩", 100),
            new Input("u", "u", "", "齿数比") { Min = 1, Example = 3 },
            new Input("phid", "φ_d", "", "齿宽系数 b/d₁；对称布置软齿面常取 0.8～1.4") { Default = 1, Min = 0.1, Max = 2 },
            new Input("k", "K", "", "载荷系数（K_A K_v K_Hβ K_Hα 的初估）") { Default = 1.3, Min = 1, Max = 3 },
            new Input("zh", "Z_H", "", "节点区域系数，标准直齿 2.5") { Default = 2.5, Min = 1.5, Max = 3 },
            new Input("ze", "Z_E", "√MPa", "弹性系数，钢对钢 189.8") { Default = 189.8, Min = 50, Max = 300 },
            new Input("zeps", "Z_ε", "", "重合度系数；不确定取 1 偏安全") { Default = 1, Min = 0.6, Max = 1 },
            Input.Positive("sigma_hp", "σ_HP", "MPa", "许用接触应力", 600),
        ],
        Outputs =
        [
            new("d1", "d₁", "mm", "小轮分度圆直径最小值"),
            new("b", "b", "mm", "对应齿宽 φ_d d₁"),
        ],
        Evaluate = (args, r) =>
        {
            var u = args["u"];
            var t = args["t1"] * 1000;
            var d1 = Math.Cbrt(2 * args["k"] * t / args["phid"] * (u + 1) / u
                               * Math.Pow(args["zh"] * args["ze"] * args["zeps"] / args["sigma_hp"], 2));
            r["d1"] = d1;
            r["b"] = args["phid"] * d1;
        },
    };

    private static Calculation Module() => new()
    {
        Id = "gear.module",
        Title = "按弯曲强度初估模数",
        Source = "由齿根弯曲基本式反解：mₙ ≥ ∛(2K T₁ Y_ε cos²β/(φ_d z₁²)·Y_Fa Y_Sa/σ_FP)，常见于机械设计教材的设计式",
        Scope = "初步设计；Y_Fa Y_Sa 取两轮中 Y_Fa Y_Sa/σ_FP 较大者，得到的模数须圆整为标准值再按 gear.bending 校核",
        Inputs =
        [
            Input.Positive("t1", "T₁", "N·m", "小轮扭矩", 100),
            new Input("z1", "z₁", "", "小轮齿数") { Min = 6, Example = 20 },
            new Input("beta", "β", "°", "螺旋角") { Default = 0, Min = 0, Max = 45 },
            new Input("phid", "φ_d", "", "齿宽系数 b/d₁") { Default = 1, Min = 0.1, Max = 2 },
            new Input("k", "K", "", "载荷系数初估") { Default = 1.3, Min = 1, Max = 3 },
            new Input("yeps", "Y_ε", "", "弯曲重合度系数；不确定取 1 偏安全") { Default = 1, Min = 0.5, Max = 1 },
            new Input("yfs", "Y_Fa Y_Sa", "", "齿形系数与应力修正系数之积（载荷作用于齿顶），z=20 标准齿约 4.3") { Min = 2, Max = 7, Example = 4.3 },
            Input.Positive("sigma_fp", "σ_FP", "MPa", "许用齿根应力", 300),
        ],
        Outputs =
        [
            new("mn", "mₙ", "mm", "法向模数最小值（未圆整）"),
        ],
        Evaluate = (args, r) =>
        {
            var beta = args["beta"] * Num.Deg;
            var z1 = args["z1"];
            r["mn"] = Math.Cbrt(2 * args["k"] * args["t1"] * 1000 * args["yeps"] * Math.Pow(Math.Cos(beta), 2)
                                / (args["phid"] * z1 * z1) * args["yfs"] / args["sigma_fp"]);
            r.Note("把 mₙ 圆整到 GB/T 1357 标准模数系列后，用 gear.geometry 与 gear.bending 复算");
        },
    };
}
