using HistoryVulcan.Core.Commands;
using System.Text.Json;
using Norma.Formulas;

namespace Norma;

internal static class BasicCommands
{
    internal static void Register(ICommandRegistrar registry)
    {
        registry.Register(new CommandDescriptor
        {
            Name = "norma.basic.coneangle", Domain = "norma", CommandClass = "basic", Readonly = true,
            Summary = "圆锥角公差：AT1～AT12等级与基本圆锥长度L两个坐标，返回μrad、原表角秒及按实际L计算的直径公差；不混IT与直径",
            Example = "norma.basic.coneangle grade=AT7 length=50",
            Parameters =
            [
                new() { Name = "grade", Type = ParamType.String, Description = "AT1～AT12圆锥角公差等级，严格大写；不是IT。省略一次给齐缺参default=null，*=展开12等级；与length最多一个*，完整结果不分页。手册13-02:55，页面未标标准号" },
                new() { Name = "length", Type = ParamType.String, Description = "基本圆锥长度L，mm，6～630，首段6≤L≤10，其后下开上闭；不是直径或母线长。不插值锥角公差，不外推。省略给分段候选，*=展开10段且只给ATD原范围，不取中点；中间L时ATD=ATα×L×10⁻³μm，按该原范围最大值的有效位数修约（GB/T8170）；列出长度上端点取原表ATD最大值。角秒为原表舍入值，不反算覆盖μrad" }
            ],
            Handler = CommandDescriptor.Sync(c => Execute(() => ConeAngleTolerances.Query(c.GetString("grade"), c.GetString("length"))))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.basic.angular", Domain = "norma", CommandClass = "basic", Readonly = true,
            Summary = "角度与斜度系列卡：以C1:10、α60°、β45°、S1:20等带几何符号的基本值查系列、对应角度/锥度/斜度及用途；支持莫氏/贾氏代号，无表名参数",
            Example = "norma.basic.angular designation=C1:10",
            Parameters =
            [
                new() { Name = "designation", Type = ParamType.String, Description = "完整几何基本值主键：C圆锥锥度（C1:10、C7:24）、α圆锥全角（α60°）、β棱体全角（β45°）、S棱体斜度（S1:20）。保留°、′、″和大小写；C=1:10亦可。接受莫氏0～6/MT0～MT6及贾氏0/1/2/3/6/33或JT代号，返回相应锥度卡；特殊用途适用范围随卡。仅查原表基本值，不按推算角度近似匹配，不作任意值计算。省略给default=null与候选；*=展开71基本值，C*/α*/β*/S*按符号展开，与property最多一个*" },
                new() { Name = "property", Type = ParamType.String, Description = "可选精确返回项：圆锥角、锥度、棱体角、比率、斜度、系列、用途、圆锥换算系数。只筛已列事实；省略完整卡，*=展开属性。C=2tan(α/2)，比率Cp=2tan(β/2)，S=tanβ，勿混全角与半角、Cp与S。角度单位°，保留度分秒与原十进制度；比率值无量纲且保留1:n。基本值与推算值分别标明；原横线null，不补计算值；完整卡不分页" },
                new() { Name = "series", Type = ParamType.String, Description = "可选系列号1或2，精确筛选且先收窄候选；第一系列优先。特殊用途与独立基本斜度S行没有系列号，不伪造优先级；省略不筛选，不支持*" },
                new() { Name = "standard", Type = ParamType.String, Description = "可选来源包含过滤：圆锥GB/T157—1989、棱体GB/T4096—1983；圆锥换算系数来自手册经验查表（用手册），第60～61页，系数为C=1:10基准轴向公差/基本偏差的换算约数；使用157、4096或年份。不是表名分发，不支持*；来源为手册第53～54及60～61页，不冒称现行标准" },
                Offset(), Limit()
            ],
            Handler = CommandDescriptor.Sync(c => Execute(() => AngularSeries.Query(c.GetString("designation"), c.GetString("property"),
                c.GetString("series"), c.GetString("standard"), c.GetInt("offset", 0), c.GetInt("limit", 20))))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.basic.general", Domain = "norma", CommandClass = "basic", Readonly = true,
            Summary = "未注公差的一般公差：以尺寸mm和f/m/c/v等级，返回线性、倒圆/倒角、角度极限偏差，可用property只取一项；角度项尺寸指较短边长，缺参一次给齐",
            Example = "norma.basic.general size=25 grade=m",
            Parameters =
            [
                new() { Name = "size", Type = ParamType.String, Description = "尺寸，单位mm，有限正数。线性项按线性基本尺寸，倒圆/倒角项按半径或高度，角度项按角度较短边长度（不是角度值）分别查表；同一输入一次返回三项，实际设计按对象读取。线性0.5～4000，倒圆/倒角≥0.5，角度较短边>0；各项保留自身下闭首段及下开上闭后续段。超出某项范围仅该项null，不外推。省略给分段候选default=null，*=完整展开三表分段并集，不分页，与grade/property最多一个*" },
                new() { Name = "grade", Type = ParamType.String, Description = "一般公差等级f精密、m中等、c粗糙、v最粗，严格小写；不是IT精度等级，也不是公差带。省略给四级候选default=null；*=展开四级，与size/property最多一个*。两参数均缺时一次给齐；完整卡与显式表不截断。GB/T1804—2000手册第49～51页，公差为对称±值；value是非负幅值，upperDeviation/ lowerDeviation为带符号偏差，线性与倒圆/倒角mm、角度°（保留原°/′文字）。横线返回null，不取邻级估算，无表名参数" },
                new() { Name = "property", Type = ParamType.String, Description = "可选精确返回属性：线性尺寸、倒圆半径和倒角高度、角度尺寸；省略或空白返回完整三项卡。只筛输出属性，不改变size/grade含义或数值边界；size=*仍按三表边界并集展开12段，每行只保留所选项。*=逐项展开三种属性，与size/grade合计最多一个*；缺size或grade仍一次给齐，完整结果不分页。如property=角度尺寸时size是较短边长度，输出单位°并保留原角分" }
            ],
            Handler = CommandDescriptor.Sync(c => Execute(() => GeneralTolerances.Query(c.GetString("size"), c.GetString("grade"), c.GetString("property"))))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.basic.tolerance", Domain = "norma", CommandClass = "basic", Readonly = true,
            Summary = "IT公差等级卡：等级统摄应用范围、选用举例、可达与经济加工方法、加工成本比；给尺寸再查标准公差值；缺条件一次给齐，一个显式*展开",
            Example = "norma.basic.tolerance size=25 grade=IT7",
            Parameters =
            [
                new() { Name = "size", Type = ParamType.String, Description = "可选基本尺寸mm，有限正数，数值表>0～3150下开上闭；仅约束尺寸相关数值，圆锥两项以基本直径索引（>0～500mm），不是圆锥长度；选用/加工资料不要求尺寸。仅给grade返回等级卡及公差所缺size；property=公差时缺size一次列出。*=仅展开数值尺寸段，须指定等级，不重复尺寸无关资料；不插值、不外推" },
                new() { Name = "grade", Type = ParamType.String, Description = "主键IT01、IT0、IT1～IT18，可省IT前缀；IT01/IT0有选用资料及第56页最大圆锥角误差，但无第4页标准公差数值，公差项返回null。省略列按已知条件收窄的等级候选，default=null；*=展开等级，与size/property合计最多一个*。公差带H7应查basic.band，配合H7/g6应查basic.fit" },
                new() { Name = "property", Type = ParamType.String, Description = "可选精确属性：公差、圆锥最大角误差（μrad，固定L=100mm，第56页）、圆锥轴向公差（mm，固定C=1:10，第59页）、应用范围、选用举例、可达加工方法、经济加工方法、加工成本比；省略返回完整等级卡，*=展开属性。等级/加工资料来自13-02:41～45，公差值来自第4页GB/T1800.3—1998。不是表名分发；筛选属性后才判断所需坐标，查加工无需补size；单卡完整返回不分页" },
                new() { Name = "process", Type = ParamType.String, Description = "可选加工方法文字过滤，去排版空格后包含匹配，如研磨、车、铰、普通车削；仅筛带加工方法的事实并收窄等级候选，不选择表。可达加工精度、经济加工描述、成本比仍各自独立；不支持*，无匹配不放宽" },
                new() { Name = "feature", Type = ParamType.String, Description = "可选加工对象：轴、孔、外径、内径、长度；轴与外径、孔与内径分别关联，经济加工保留轴/孔原条件，成本保留外径/内径/长度原条件；过滤只返回有相应对象条件的加工事实，不影响公差计算规则，不支持*" },
                new() { Name = "standard", Type = ParamType.String, Description = "可选事实来源包含过滤，公差值为GB/T1800.3—1998（如1998），选用加工资料及第56/59页圆锥参考值为机械工程师手册（用手册）；圆锥项各带固定长度/锥度条件；各事实独立筛选，不给经验表虚构GB版本。未命中no_match，不支持*" },
                Offset(), Limit()
            ],
            Handler = CommandDescriptor.Sync(c => Execute(() => BasicStandards.QueryTolerance(
                c.GetString("size"), c.GetString("grade"), c.GetString("standard"), c.GetInt("offset", 0), c.GetInt("limit", 20),
                c.GetString("property"), c.GetString("process"), c.GetString("feature"))))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.basic.band", Domain = "norma", CommandClass = "basic", Readonly = true,
            Summary = "单个孔轴公差带卡：以H7、g6等单独代号及基本尺寸查标准公差、基本偏差及孔轴极限偏差、常用级别、基本偏差应用；应用无需尺寸；usage按选用级别筛选；大小写区分孔轴，不接收配合组合，缺条件一次给齐",
            Example = "norma.basic.band band=K7 size=25",
            Parameters =
            [
                new() { Name = "band", Type = ParamType.String, Description = "主键为单个带IT等级的公差带代号，如H7、g6、K7、JS7；严格区分大小写：a/b/c/cd/d/e/ef/f/fg/g/h/js/j/k/m/n/p/r/s/t/u/v/x/y/z/za/zb/zc为轴，对应大写为孔。H7/g6属于配合组合，明确拒绝。裸H缺等级时返回H1～H18候选，不默认H7；省略一次给全部缺失维度。*=展开公差带，H*=展开H的等级，*7=展开7级各代号；与size/property合计最多一个*。本批提供标准公差、基本偏差及孔轴极限偏差（原手册第5～35页），常用级别来自第36～37页，轴基本偏差应用来自第46页；h/js原页为图片表，返回明确标记的图表转录定位，空白组合不推算补齐" },
                Size(),
                new() { Name = "property", Type = ParamType.String, Description = "可选返回项，精确匹配：公差、基本偏差、上偏差、下偏差、圆锥轴向基本偏差、常用级别、基本偏差应用；圆锥轴向项仅外圆锥C=1:10、基本直径≤500mm，单位mm，第58页，js取+Tze/2；第46页轴基本偏差应用无需size，按代号返回推荐范围及应用，不套用到孔，推荐IT范围不是等级限制；上/下偏差覆盖孔和轴的原表组合。省略返回完整单条件卡，*=展开属性表；先按此属性收窄公差带。数值带单位、出处、必要原注，派生值带表达式与依赖格；空白、未定义Δ或异常表达式返回null及原因，不猜改" },
                new() { Name = "usage", Type = ParamType.String, Description = "常用级别精确筛选1～4：1适用尺寸图未列出、2列出但框外、3框内未圈出、4框内且圈出；3不包含4。不是IT精度等级。省略不筛选；不支持*。搭配band=* size=25 property=常用级别直接列选用表；缺参仍返回候选。来源为第36～37页GB/T1801—1999，尺寸≤500与>500～3150分别判定；1级不表示禁用" },
                Standard(), Offset(), Limit(),
            ],
            Handler = CommandDescriptor.Sync(c => Execute(() => BasicStandards.QueryBand(
                c.GetString("band"), c.GetString("size"), c.GetString("property"), c.GetString("standard"), c.GetInt("offset", 0), c.GetInt("limit", 20), c.GetString("usage"))))
        });
        registry.Register(new CommandDescriptor
        {
            Name = "norma.basic.fit", Domain = "norma", CommandClass = "basic", Readonly = true,
            Summary = "公差配合卡：H7/g6等配合代号统摄选用级别、极限间隙/过盈、统计极限、装配方法、特性、应用举例及过盈概率；应用属性无需尺寸，一个显式*展开",
            Example = "norma.basic.fit fit=H7/g6 size=25",
            Parameters =
            [
                new() { Name = "fit", Type = ParamType.String, Description = "配合主键，孔大写/轴小写，双方均含IT等级，如H7/g6、G7/h6、JS7/h6；严格大小写，单个H7应查basic.band。省略给收录配合候选；*=展开已列配合，H7/*固定孔、*/h6固定轴，合计仅一个*。完整合法但未列组合可查1级卡，数值不反算补齐；枚举仅覆盖第38～40页已列102种代号，不做任意孔轴笛卡尔积" },
                new() { Name = "size", Type = ParamType.String, Description = "基本尺寸mm，有限正数，数值及选用卡>0～500下开上闭；完整卡缺size仍一次给全部缺坐标。只查装配方法/配合特性及使用条件/应用举例/过盈概率时无需size；给size也不改变这些尺寸无关资料。*=展开原数值尺寸段，不重复应用资料，与fit/property最多一个*；不默认尺寸、不插值、不外推" },
                new() { Name = "property", Type = ParamType.String, Description = "第52页新增统计最大间隙或最小过盈、统计最小间隙或最大过盈（μm，T:Wc=2:1、Pcmin=86%，JB/T9184—1999），需要size；统计值与绝对极限分别返回。精确返回项：选用级别、配合制、配合类型、最大间隙或最小过盈、最小间隙或最大过盈、装配方法、配合特性及使用条件、应用举例、过盈概率。后四项来自第47页，可只给fit不填size；返回逐来源事实数组，同一配合多个举例完整保留，缺应用不向相邻等级套用。概率用%原单值/上下限，不取中点；无概率数值返回null。省略完整卡，*=展开属性，单卡不分页。两极限μm保留正间隙负过盈的唯一权威值，未列或空白null不反算。例：fit=H7/g6 property=应用举例" },
                new() { Name = "usage", Type = ParamType.String, Description = "配合选用级别精确筛选1～3：1所收录表未列、2常用但非优先、3灰底优先；2不含3。不同于单个公差带的1～4级，不是IT等级。省略不筛选，不支持*；缺参候选先收窄。1不表示禁用。*=枚举仅已列组合，因此usage=1须给完整组合而不能浏览全部任意组合" },
                new() { Name = "standard", Type = ParamType.String, Description = "可选逐事实来源包含过滤：数值和选用为GB/T1801—1999（用1801或1999），统计极限为JB/T9184—1999（用9184），应用为机械工程师手册（经验参考，用手册）。省略同时汇总各自来源；standard=手册只查尺寸无关应用卡。不是表名参数，不支持*，未命中no_match不放宽条件" },
                Offset(), Limit(),
            ],
            Handler = CommandDescriptor.Sync(c => Execute(() => FitStandards.Query(c.GetString("fit"), c.GetString("size"),
                c.GetString("property"), c.GetString("usage"), c.GetString("standard"), c.GetInt("offset", 0), c.GetInt("limit", 20))))
        });
    }

    private static ParameterSpec Size() => new() { Name = "size", Type = ParamType.String,
        Description = "基本尺寸，单位mm，有限正数（如25）。省略与其他缺参一次返回全部缺失维度及按已知条件收窄的候选，default=null；*=展开原表尺寸段，一次最多一个*，其他未固定坐标仍需补齐。property=基本偏差应用无需尺寸，给size不改变说明；但usage筛选仍需尺寸；size=*只展开数值不重复应用。不插值、不外推、不暗选尺寸/等级/孔轴；尺寸段下开上闭" };
    private static ParameterSpec Standard() => new() { Name = "standard", Type = ParamType.String,
        Description = "可选来源版本过滤，每个返回项按自身来源筛选，含GB/T1800.3—1998、孔轴极限偏差1999及GB/T1801—1999常用级别；省略可同时返回不同版本各自有出处的事实。可用原文完整代号或年份包含过滤；P/Z/ZA/ZB/ZC原文印作GB/T18004—1999，照录并附注；第46页应用及第58页圆锥轴向基本偏差为机械工程师手册；后者仅外圆锥、C=1:10、直径≤500mm，单位mm，js取+Tze/2。standard=手册缺size只取应用，查圆锥轴向基本偏差需size；不选表、不改变参数含义。未命中返回no_match，不另选版本；不支持*，不宣称现行标准" };
    private static ParameterSpec Offset() => new() { Name = "offset", Type = ParamType.Int, Default = "0",
        Description = "各维度候选与显式变化表的分页偏移≥0；完整单条件卡不分页" };
    private static ParameterSpec Limit() => new() { Name = "limit", Type = ParamType.Int, Default = "20",
        Description = "每个维度候选及显式变化表每页1～100，默认20；不截断完整单条件卡" };
    private static CommandResult Execute(Func<object> query)
    {
        try { return CommandResult.Ok("基础标准", JsonSerializer.SerializeToElement(query())); }
        catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }
    }
}
