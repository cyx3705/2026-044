# Norma

> 机械设计经验公式库：按标准条款自描述的公式与查表，供 AI 调用与扩展

## 定位

把机械设计里的经验公式和查表做成一条条可调用的指令，每条都带着自己的出处条款、变量、单位和适用范围，AI 通过自描述就能找到、调用和核对。目标是取代 R 机械设计这类老软件：切面统一，扩展只需按同一格式加一条公式。

- 不做建模、出图和标注，那是 Strenua（SolidWorks）与 SolidEdgeMCP 的事；本模块只算数、查表、给判据。
- 不搬没有许可证或限制非商业的第三方代码和表格数据；公式按标准原文自己实现，第三方项目只当核对样例。

## 概况

| 项 | 值 |
| --- | --- |
| 角色 | 宿主模块（`kind=module`） |
| 指令域 | `norma` |
| 界面 | Aurora 描述式页面（场景 `HistoryNorma`） |
| MCP 投影 | `readonly` |
| 版本与宿主下限 | [`NormaVersion.props`](./b-Code/Norma/NormaVersion.props) |

0.12.0 已完成材料卷911个叶页的目录核对，补充牌号物性/工艺及产品指标；计算数据与未整理范围见 `b-Office/现行约定.md` 和 `b-Code/Norma/Formulas/materials-volume-audit.json`。页级核对不表示整卷表格全部入库。

## 能力

每条计算是一条只读指令 `norma.<类>.<方法>`，全部投影到 MCP。0.13.0的“基础标准”类提供六个独立查询入口，覆盖极限与配合第2～47、49～62页（含孔轴极限偏差、公差带常用级别、优先/常用配合卡、IT等级加工资料及配合应用），术语定义暂存Usus，第48页工作状况影响整表纳入Usus单条U0041，整章62页的逐页归属与验收见现行约定及`limits-fits-audit.json`。

| 类 | 指令 |
| --- | --- |
| 齿轮 | `gear.geometry` 几何、`gear.contact` 接触强度、`gear.bending` 弯曲强度（ISO 6336 方法 B）、`gear.diameter` / `gear.module` 初估 |
| 轴与疲劳 | `drive.torque`、`shaft.torsion`、`shaft.combined`、`fatigue.endurance`、`fatigue.safety`、`fatigue.shaft` |
| 连接 | `key.flat`、`bolt.thread` / `torque` / `preload` / `tension` / `friction` / `shear`、`weld.fillet`、`fit.press` |
| 传动 | `screw.drive`、`belt.geometry`、`chain.geometry`；`norma.drive.efficiency q=` 按类型/工况查53条机械传动效率概略值，省略q分页浏览 |
| 支承与弹性件 | `bearing.load` / `life` / `required` / `static`、`spring.compression` / `wire` |
| 基础标准 | `norma.basic.tolerance grade=IT7` 查IT等级卡（应用范围、选用举例、可达/经济加工方法、成本比），追加property=圆锥最大角误差或圆锥轴向公差可查第56/59页固定条件参考值；加size=25查标准公差，property/process/feature正交筛选；`norma.basic.band band=H7 size=25` 查单个公差带卡（标准公差、基本偏差、孔轴上/下偏差和常用级别、基本偏差应用、C=1:10圆锥轴向基本偏差）；`band=g6 property=基本偏差应用`无需尺寸查第46页应用说明，`band=* size=25 property=常用级别 usage=4`列圈选公差带；不接收H7/g6配合组合；无表名参数，缺条件一次给齐，一个显式*才展开，未定义不回退估算 |
| 一般公差 | `norma.basic.general size=25 grade=m`：尺寸mm、f/m/c/v等级两个坐标，增加可选property=线性尺寸/倒圆半径和倒角高度/角度尺寸；省略返回线性尺寸、倒圆半径/倒角高度、角度尺寸的极限偏差；角度按较短边长度查询，返回°并保留原角分文字。缺参一次给齐，一个显式*展开整表；无表名参数，横线/各项超范围返回null |
| 基础标准配合 | `norma.basic.fit fit=H7/g6 size=25` 查配合卡及原表带符号极限间隙/过盈；`fit=* size=25 usage=3 property=选用级别` 列优先配合，usage=2列常用非优先；`fit=H7/g6 property=应用举例`无需尺寸查应用，可选装配方法/配合特性及使用条件/过盈概率；`property=统计最大间隙或最小过盈`或`统计最小间隙或最大过盈`查统计极限，条件T:Wc=2:1、Pcmin=86%；来源38～40、47及52页，未列数值仍为null |
| 角度与斜度系列 | `norma.basic.angular designation=C1:10` 查圆锥锥度卡，`α60°`查圆锥角，`β45°`查棱体角，`S1:20`查棱体斜度；支持`MT0`及`贾氏2`别名，返回系列、原表推算值及用途。`property=圆锥换算系数`查以1:10为基准的原表系数，支持36个已有圆锥基本值；可选property/series/standard，无表名参数；一个显式*展开 |
| 圆锥计算与角公差 | `norma.cone.fundamental taper=0.1 deviation=-7` 由直径基本偏差μm算轴向基本偏差mm；`norma.cone.limits taper=0.1 upper=-7 lower=-20` 算轴向上下偏差和公差，自动反号并交换端点；`norma.basic.coneangle grade=AT7 length=50` 按AT等级、圆锥长度mm查角公差，返回315μrad、1′05″及按实际L计算的ATD=15.8μm。详见REQ-BASIC-025/026 |
| 其他 | `fit.it` 标准公差公式估算、`beam.deflection` 梁 |
| 目录 | `norma.formula.list [topic=]`、`norma.formula.show id=` |
| 手册查询 | `norma.manual.search q= [scope=title/text]` 搜索/浏览目录，`norma.manual.read id=` 分页读正文或 HTML；`norma.manual.open name= [scroll_y=0]` 按页名打开并可滚动长页，`norma.manual.directory` 仅以文字查看左侧目录；`norma.manual.screenshot full_page=true` 双向滚动拼接整页正文含脚注并恢复位置，默认仅截可见视口；x/y/width/height相对正文局部裁切；`mode=html id=`才渲染提取HTML |
| 标准代号 | `norma.std.lookup q=` 按代号或名称互查（机械工程师手册「国内/国外部分标准代号」两页） |
| 机械材料 | `norma.material.limits material= sigma_b=` 从抗拉强度估算材料对称/脉动疲劳极限，保留区间与无定义项 |
| 机械材料 | `norma.metal.strength family= value= scale= grade=` 钢/变形铝合金/铜合金的硬度互转与强度换算（PCHIP 保形插值，含肖氏 HS、表面洛氏、维氏、布氏） |

| 材料牌号库 | `norma.material.grade grade=HT250`：牌号是主键；成分、力学、物理、工艺合一，category/property/condition/family可选 |
| 材料库 | `norma.material.library name=PTFE`：非金属/复合材料按名称或型号查卡；通用物性、堆积、摩擦及无明确牌号的产品性能也在此，以category分类 |
| 产品规格库 | `norma.product.spec name=结构用热轧无缝钢管 spec='{"外径":32,"壁厚":3}' property=理论重量`：尺寸、重量、截面参数、供应组合及公差；产品全名/坐标见工具自描述 |

0.12.1 合并了9个重复入口：`material.strength`改为牌号库`category=力学`；8个物性/摩擦专用入口改为材料库可选分类。Norma总注册指令57→48，对MCP公开54→45。材料数据查询固定为牌号库、材料库、规格库三个入口；硬度换算和疲劳极限估算保留独立计算。

材料库支持category=非金属/复合材料/弹性/密度/热学/线膨胀/堆积/滑动摩擦/物体摩擦/滚动摩擦力臂/产品性能。例如`name=铅 category=密度 property=密度`或`name=钢-钢 category=滑动摩擦`。通用参考值不归给具体牌号，原表温区、载荷、配方和单位保持独立；缺值保留null及原因。类别/属性均不需要新增工具。

原产品数据中的791格纯力学/试验性能转由材料库`category=产品性能`查询；不推定钢带/钢丝的牌号。规格入口当前为99页150表、9126条规则、22018格、106个产品/专表；存储仍保留全部原表用于追溯，整卷未整理范围不变。

牌号库仍以材料牌号为主键，保留ZL101、LY12等明确代号映射；category=成分/力学/物理/工艺过滤，省略返回完整牌号卡。非金属和复合材料使用独立材料库的name主键。缺变量返回default=null及选项，一个显式*才返回变化表；完整卡片不受limit截断。原有用途代表选型和basis=source仍保留，并明确非等同或替代认证。

```text
norma.material.grade grade=HT250
norma.material.grade grade=QSn6.5-0.1
norma.material.grade grade=6061
norma.material.grade grade=ZL101
norma.material.grade grade=MTCrMoCu35
norma.material.grade grade=钨铸铁
norma.material.grade grade=钨铸铁 basis=source
norma.material.grade grade=KmTBCr15Mo property=Cr
norma.material.grade grade=HT250 category=力学 property=抗拉强度 condition=单铸试棒
```

6个合金体系名称关联到17个导轨牌号；各用途原数据写入对应卡片“同族参考配方（非本牌号标准指标）”，不覆盖牌号标准成分/性能，不宣称跨用途等同。其余29个分支名称条目（27个名称）依REQ-MATERIAL-008选用5个用途基准：HT250、HT350、东亚DH33、KmTBCr2、KmTBNi4Cr2-GT。名称默认返回代表卡及原配方；basis=source只查原数据。DH33新增厂家公开抗弯强度与HV30硬度，化学成分未公开；其他牌号沿用手册原数据。

页面有一张目录表，加两个操作面板：标准代号查表、强度换算（保留命令`norma.metal.strength`）。

指令说明就是注册时的自描述（`diana.docs.read domain=norma`，宿主 6.1.0 起没有消费文档）；每条都带出处、适用范围、参数的符号/单位/范围。自描述装不下的取舍写进 [现行约定](./b-Office/现行约定.md)。


## 入口

| 入口 | 用途 |
| --- | --- |
| [`AGENTS.md`](./AGENTS.md) | AI 工作合同：读取顺序、真值判定、边界 |
| [`project.manifest.json`](./project.manifest.json) | 项目身份、活动目录、文档与命令 |
| [现行约定](./b-Office/现行约定.md) | 唯一的长期文档：模板规则与模块起步约定 |

## 目录

| 路径 | 职责 |
| --- | --- |
| `b-Code/Norma/` | 模块源码、manifest 与 `eng/` 构建脚本 |
| `b-Code/Norma.Tests/` | 离线自动验证（含宿主装载冒烟） |
| `b-Code/` | 项目合同检查 |
| `b-Office/` | 唯一长期文档 `current/现行约定.md` |
| `z-Publish/` | 正式快照与 `history/` 归档，由宿主管线写入 |

## 构建与验证

```powershell
dotnet build .\b-Code\Norma\Norma.csproj -c Release -p:NuGetAudit=false
dotnet run --project .\b-Code\Norma.Tests\Norma.Tests.csproj -c Release -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```

离线测试最后一组用宿主真正的 `ModuleHost` 装一遍打好的包：版本漂移、manifest 写错在宿主那边
都是静默跳过整个模块，这组用例在出包前拦住它们。

## 开发与发布

改动只进 `vulcan.dev.start` 创建的工作区，经宿主 Console CLI 走
`vulcan.dev.start` → `vulcan.dev.submit`（候选构建并热装送审）→ `vulcan.dev.finish`（批准后并回并写入 `z-Publish`）。
本仓不自行发布；`eng/Build-NormaPackage.ps1` 只用于本地候选构建。

## 要点

- 页面 owner 由指令域推出（`norma` → `HistoryNorma`），合同检查与离线测试都守着这条。
- 版本只改 `NormaVersion.props`，再同步两份 manifest；三处不一致构建前就失败。
- 每条公式都要有编号、出处条款、输入输出变量与单位、适用范围；缺一项就不算登记完成。

## 保留内容

### logo与作者

- 作者：pinavia
- ![logo](./Logo.png)

### 学习许可与权利保留

- 本项目允许个人、学校和研究者进行非商业性的学习、研究和引用。
- 未经著作权人书面许可，不得将项目内容用于商业开发、产品制造、商业服务、商业宣传、投标交付、毕业设计成品交付或再授权。
- 不得删除作者、来源、版本历史和第三方许可证声明，不得将本项目或其衍生成果宣称为与作者无关的原创项目。
- 软件源码适用 [PolyForm Noncommercial 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0/)；机械模型、PCB、原理图、图片、文档、Unity 工程和其他非软件资料适用 [CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/)。
- SE、SW、STEP/STP 及其他三维模型仅允许个人或学校进行非商业学习、评审和研究；未经著作权人书面许可，不得用于加工、打样、制造、销售、量产、投标、产品交付、商业宣传、改作或再授权。
- 第三方组件、模板、元件库、外购件模型和工具仍受其原始许可证约束；本声明不扩大作者对第三方内容的权利。

商业使用、产品化、公司内部长期使用、转让或排他性授权需另行取得著作权人的书面许可。
