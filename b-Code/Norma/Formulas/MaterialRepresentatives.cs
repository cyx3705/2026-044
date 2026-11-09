namespace Norma.Formulas;

// REQ-MATERIAL-008: editorial baseline selections requested by the user.
// These are purpose-level comparisons, never chemical equivalence or approved substitutions.
internal static class MaterialRepresentatives
{
    internal sealed record Selection(string Family, string Material, string Grade, string Reason, string Source);
    internal const string GraySource = "https://www.mechtool.cn/metalmaterial/metalmaterial_castiron.html";
    internal const string RingSource = "https://www.dongya.cn/dy-piston-ring-material-introduction/";
    internal const string WhiteSource = "https://www.mechtool.cn/metalmaterial/metalmaterial_whitecastiron.html";
    internal static readonly Selection[] Selections = Build();
    private static Selection[] Build()
    {
        var result = new List<Selection>();
        void Add(string family, string grade, string reason, string source, params string[] names)
            => result.AddRange(names.Select(n => new Selection(family, n, grade, reason, source)));
        Add("机床零件用耐磨铸铁", "HT250", "珠光体灰铁机床零件用途基准；未包含原配方特定合金要求", GraySource,
            "磷钒铸铁", "稀土钒钛铸铁", "磷铜钒钛铸铁", "锑铜铸铁", "锑铜稀土铸铁", "锑铸铁");
        Add("机床零件用耐磨铸铁", "HT350", "高强度灰铁机床用途基准；不代表稀土处理或原配方350～500MPa性能范围", GraySource,
            "稀土铸铁", "磷稀土铸铁");
        Add("汽缸套用耐磨铸铁", "HT250", "灰铁气缸与衬筒用途基准；原配方合金含量及缸套耐磨要求仍需另核", GraySource,
            "磷铬铸铁", "磷铬铜铸铁", "磷钒铸铁", "磷铬钼铸铁", "磷锑铸铁", "硼铸铁");
        Add("活塞环用耐磨铸铁", "DH33", "东亚活塞环合金铸铁企业牌号，ISO材料细类MC13；厂家未公开成分，不能认定含原配方合金元素", RingSource,
            "钨铸铁", "钨钒钛铸铁", "钨铬钼铸铁", "钼铬铸铁", "钼铬铜铸铁", "钼铬铜钛铸铁", "镍铬钼铸铁", "镍铬铸铁", "铜钒钛铸铁", "磷稀土铸铁");
        Add("冷硬铸铁", "KmTBCr2", "低铬抗磨白口铁比较基准；不代表冷硬工艺、白口层深度或原配方的稀土/硼处理", WhiteSource,
            "普通冷硬铸铁", "铬钼稀土冷硬铸铁", "硼冷硬铸铁", "稀土冷硬铸铁");
        Add("冷硬铸铁", "KmTBNi4Cr2-GT", "镍铬抗磨白口铁比较基准；不代表含铜配方或冷硬工艺等同", WhiteSource,
            "镍铬铜冷硬铸铁");
        return result.ToArray();
    }
    // Manufacturer's visible table, retrieved 2026-11-09. E's unit is absent: do not infer it.
    internal static readonly CastIronLookup.Fact[] AdditionalFacts =
    [
        new("grade", "活塞环用合金铸铁（东亚企业牌号）", "DH33", "化学成分", "原表条件", "厂家未公开", RingSource, "dongya:DH33"),
        new("strength", "活塞环用合金铸铁（东亚企业牌号）", "DH33", "抗弯强度", "厂家公布值", "≥450 MPa", RingSource, "dongya:DH33"),
        new("strength", "活塞环用合金铸铁（东亚企业牌号）", "DH33", "硬度", "厂家公布值", "240～330 HV30", RingSource, "dongya:DH33"),
    ];
}
