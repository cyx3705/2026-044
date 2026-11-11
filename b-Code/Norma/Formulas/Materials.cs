namespace Norma.Formulas;

/// <summary>REQ-MATERIAL-001：手册 sjzl:7 的材料疲劳极限近似关系。</summary>
internal static class Materials
{
    public static IEnumerable<Calculation> All()
    {
        yield return new Calculation
        {
            CatalogSummary = "换算常用材料极限强度的近似值",
            Id = "material.limits",
            Title = "常用材料极限强度的近似关系",
            Source = "机械工程师手册 sjzl:7《常用材料极限强度的近似关系》；sjzl/sjzl.ysc HTML 条目7，字节偏移145957；条目号不是印刷页码",
            Scope = "由抗拉强度估算材料试样的对称/脉动疲劳极限（MPa），不是零件许用应力或屈服强度；原表未给数值适用区间。铝合金扭转给上下界，脉动弯曲/扭转无定义。材料包括铸铁、结构钢和铝合金，已按用户提供的完整原表截图补齐残缺文字。",
            Inputs =
            [
                Input.Choice("material", "", "材料：structural_steel 结构钢；aluminum 铝合金；cast_iron 铸铁", null,
                    "structural_steel", "aluminum", "cast_iron"),
                Input.Positive("sigma_b", "σ_b", "MPa", "材料抗拉强度；由调用方提供，不是本工具查出的牌号强度", 600),
            ],
            Outputs =
            [
                new("sigma_m1t", "σ₋₁t", "MPa", "对称拉压疲劳极限"),
                new("sigma_m1", "σ₋₁", "MPa", "对称弯曲疲劳极限"),
                new("tau_m1_min", "τ₋₁,min", "MPa", "对称扭转疲劳极限下界"),
                new("tau_m1_max", "τ₋₁,max", "MPa", "对称扭转疲劳极限上界；单值时与下界相同"),
                new("sigma_0t", "σ₀t", "MPa", "脉动拉压疲劳极限（最大应力，不是应力幅）"),
                new("sigma_0", "σ₀", "MPa", "脉动弯曲疲劳极限；铝合金无定义"),
                new("tau_0", "τ₀", "MPa", "脉动扭转疲劳极限；铝合金无定义"),
            ],
            Evaluate = (a, r) =>
            {
                var sb = a["sigma_b"];
                var material = a.Choice("material");
                if (material == "aluminum")
                {
                    r["sigma_m1t"] = r["sigma_m1"] = sb / 6 + 73.5;
                    r["tau_m1_min"] = 0.55 * r["sigma_m1"];
                    r["tau_m1_max"] = 0.58 * r["sigma_m1"];
                    r["sigma_0t"] = 1.5 * r["sigma_m1t"];
                    r["sigma_0"] = r["tau_0"] = double.NaN;
                    r.Note("铝合金 σ₀、τ₀ 原表为—，输出 null；τ₋₁=(0.55～0.58)σ₋₁，不取中值。两条 σ_b/6+73.5 公式均已由完整原表截图核对。");
                }
                else
                {
                    var cast = material == "cast_iron";
                    r["sigma_m1t"] = (cast ? 0.225 : 0.3) * sb;
                    r["sigma_m1"] = (cast ? 0.45 : 0.43) * sb;
                    r["tau_m1_min"] = r["tau_m1_max"] = (cast ? 0.36 : 0.25) * sb;
                    r["sigma_0t"] = 1.42 * r["sigma_m1t"];
                    r["sigma_0"] = (cast ? 1.35 : 1.33) * r["sigma_m1"];
                    r["tau_0"] = (cast ? 1.35 : 1.5) * r["tau_m1_min"];

                }
                r.Note("全部为近似关系；对称循环 R=-1，脉动循环 R=0。未计尺寸、表面、应力集中等零件修正。源 HTML 缺字已按用户提供的完整原表截图核对补齐。");
            },
        };
    }
}