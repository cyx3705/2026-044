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

## 能力

每条计算是一条只读指令 `norma.<类>.<方法>`，全部投影到 MCP。

| 类 | 指令 |
| --- | --- |
| 齿轮 | `gear.geometry` 几何、`gear.contact` 接触强度、`gear.bending` 弯曲强度（ISO 6336 方法 B）、`gear.diameter` / `gear.module` 初估 |
| 轴与疲劳 | `drive.torque`、`shaft.torsion`、`shaft.combined`、`fatigue.endurance`、`fatigue.safety`、`fatigue.shaft` |
| 连接 | `key.flat`、`bolt.thread` / `torque` / `preload` / `tension` / `friction` / `shear`、`weld.fillet`、`fit.press` |
| 传动 | `screw.drive`、`belt.geometry`、`chain.geometry`；`norma.drive.efficiency q=` 按类型/工况查53条机械传动效率概略值，省略q分页浏览 |
| 支承与弹性件 | `bearing.load` / `life` / `required` / `static`、`spring.compression` / `wire` |
| 其他 | `fit.it` 标准公差、`beam.deflection` 梁 |
| 目录 | `norma.formula.list [topic=]`、`norma.formula.show id=` |
| 手册查询 | `norma.manual.search q= [scope=title/text]` 搜索/浏览目录，`norma.manual.read id=` 分页读正文或 HTML；`norma.manual.open name= [scroll_y=0]` 按页名打开并可滚动长页，`norma.manual.directory` 仅以文字查看左侧目录；`norma.manual.screenshot` 仅截右侧正文，x/y/width/height相对正文局部裁切；`mode=html id=`才渲染提取HTML |
| 标准代号 | `norma.std.lookup q=` 按代号或名称互查（机械工程师手册「国内/国外部分标准代号」两页） |
| 机械材料 | `norma.material.limits material= sigma_b=` 从抗拉强度估算材料对称/脉动疲劳极限，保留区间与无定义项 |
| 机械材料 | `norma.metal.strength family= value= scale= grade=` 钢/变形铝合金/铜合金的硬度互转与强度换算（PCHIP 保形插值，含肖氏 HS、表面洛氏、维氏、布氏） |

| 机械材料 | norma.material.elastic 弹性模量/泊松比、norma.material.density 密度、norma.material.thermal 熔点/热导率/比热容、norma.material.expansion 线膨胀、norma.material.bulk 堆密度/安息角；material.friction 材料滑动摩擦（静/动及润滑四列）、material.object_friction 物体摩擦、material.rolling_arm 滚动摩擦力臂(mm)；q= 查询，省略浏览整表 |

铸铁数据：`norma.material.grade` 牌号及化学成分，`norma.material.strength` 材料力学性能（材料强度）；覆盖铸铁整节28页30表。支持q/family/grade/table与offset/limit，牌号工具还可按section筛选命名、热处理、化学成分与牌号目录。试样、壁厚、铸型、温度和热处理条件随表头返回。

页面有一张目录表，加两个操作面板：标准代号查表、强度换算（保留命令`norma.metal.strength`）。

指令说明就是注册时的自描述（`diana.docs.read domain=norma`，宿主 6.1.0 起没有消费文档）；每条都带出处、适用范围、参数的符号/单位/范围。自描述装不下的取舍写进 [现行约定](./b-Office/现行约定.md)。

指令说明就是注册时的自描述（`diana.docs.read domain=norma`，宿主 6.1.0 起没有消费文档）；自描述装不下的取舍写进 [现行约定](./b-Office/现行约定.md)。

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
