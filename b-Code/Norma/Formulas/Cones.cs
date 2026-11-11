namespace Norma.Formulas;

// REQ-BASIC-025: signed diameter deviations map to axial deviations with reversed endpoints.
internal static class Cones
{
    internal static IEnumerable<Calculation> All()
    {
        yield return new Calculation
        {
            CatalogSummary = "换算圆锥轴向基本偏差",
            Id = "cone.fundamental", Title = "圆锥轴向基本偏差换算",
            Source = "机械工程师手册13-02:57（字节5340705）：ez=−e/C；Ez=−E/C",
            Scope = "内外圆锥通用；输入直径基本偏差（不是半径偏差），保留其符号。C=(D−d)/L=2tan(α/2)，α为全角；输出遵循原手册轴向坐标。第58页为C=0.1的外圆锥参考值，第60～61页系数为约数，本计算使用输入的实际C。",
            Inputs = [Input.Positive("taper", "C", "1", "实际锥度数值，例如1:10输入0.1；不是锥角或换算系数", .1),
                new Input("deviation", "e/E", "μm", "带符号的直径基本偏差；从单公差带卡取得，不能填轴向偏差") { Example = -7 }],
            Outputs = [new("axial", "ez/Ez", "mm", "带符号的轴向基本偏差")],
            Evaluate = (a,r) => { r["axial"] = -a["deviation"] / a["taper"] / 1000; }
        };
        yield return new Calculation
        {
            CatalogSummary = "换算圆锥轴向极限偏差和公差",
            Id = "cone.limits", Title = "圆锥轴向极限偏差及公差换算",
            Source = "机械工程师手册13-02:57、62（字节5340705、5418939）：esz=−ei/C，eiz=−es/C，Tz=(es−ei)/C；内圆锥同理",
            Scope = "内外圆锥通用，输入直径上、下偏差μm；先反号并交换上下端点，再除以实际锥度。第62页基孔制H给ESz=0、EIz=−Tzi，h给esz=Tze、eiz=0，js给±Tze/2，均是同一关系的特例。无公差带等级或表名参数，不隐选公差值。",
            Inputs = [Input.Positive("taper", "C", "1", "实际锥度数值，C=(D−d)/L=2tan(α/2)；1:10输入0.1", .1),
                new Input("upper", "es/ES", "μm", "带符号的直径上偏差，须≥下偏差；不是半径偏差") { Example = -7 },
                new Input("lower", "ei/EI", "μm", "带符号的直径下偏差；与上偏差取自同一公差带、尺寸及来源") { Example = -20 }],
            Outputs = [new("upper", "esz/ESz", "mm", "轴向上偏差，对应负的直径下偏差除以C"),
                new("lower", "eiz/EIz", "mm", "轴向下偏差，对应负的直径上偏差除以C"), new("tolerance", "Tz", "mm", "非负轴向公差宽度")],
            Evaluate = (a,r) =>
            {
                if (a["upper"] < a["lower"]) throw new ArgumentException("直径上偏差必须≥下偏差，不自动交换输入");
                r["upper"] = -a["lower"] / a["taper"] / 1000;
                r["lower"] = -a["upper"] / a["taper"] / 1000;
                r["tolerance"] = r["upper"] - r["lower"];
            }
        };
    }
}
