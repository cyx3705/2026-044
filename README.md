<!-- template:head -->
# ModuleReady

> 宿主模块起步模板：派生、跑一条脚本，就是一个可构建、可测试、可热装的 HistoryVulcan 模块仓
<!-- /template:head -->

<!-- template:usage -->
## 从模板建立模块

本模板从 `0000-001-AIReady` 派生，内含一个能直接跑通的示范模块 `HistoryExemplum`（域 `exemplum`，
一页 Aurora 页面、三条示范指令、离线测试含宿主装载冒烟）。新模块不必再去别的模块仓里复制。

1. 建仓：`janus.proj.create name=2026-0xx-History<神名> base=0000-002-ModuleReady`。
2. 在新仓主树实例化（机械改名、改身份，跑完脚本删掉自己）：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\New-ModuleFromTemplate.ps1 `
       -Name History<神名> -Description '<一句话描述>' [-Domain <域>] [-PageTitle <页面标题>]
   ```

   构建、测试通过后把这次实例化提交成一个脚手架提交，让之后的开发工作区只装业务改动。
3. 不用改宿主仓：发布描述已在 `project.manifest.json` 的 `publish` 节（宿主 5.8.0 起模块自带）。
4. `vulcan.dev.start` 开工作区，在里面把 `hello.*` 示范换成真实业务、填完 `{{...}}`，
   跑严格合同检查，再 `submit`（候选构建并热装）→ 审过 → `finish`。

模板自身的验证（模板模式，不带 `-Instantiation`）：

```powershell
dotnet run --project .\b-Code\HistoryExemplum.Tests\HistoryExemplum.Tests.csproj -c Release -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1
```

<!-- /template:usage -->
## 定位

{{这个模块做什么、为谁做；两三句话}}

- {{明确不做的事，以及它属于哪个模块}}

## 概况

| 项 | 值 |
| --- | --- |
| 角色 | 宿主模块（`kind=module`） |
| 指令域 | `exemplum` |
| 界面 | Aurora 描述式页面（场景 `HistoryExemplum`） |
| MCP 投影 | `readonly` |
| 版本与宿主下限 | [`HistoryExemplumVersion.props`](./b-Code/HistoryExemplum/HistoryExemplumVersion.props) |

## 能力

| 按钮 | 指令 | 用途 |
| --- | --- | --- |
| 打招呼 | `exemplum.hello.ping` | 示范：改状态的无参指令，刷新页面表格 |
| — | `exemplum.hello.echo text=…` | 示范：带参数的只读指令 |
| — | `exemplum.hello.list` | 示范：只读列表，投影到 MCP |

指令说明就是注册时的自描述（`diana.docs.read domain=exemplum`，宿主 6.1.0 起没有消费文档）；自描述装不下的取舍写进 [现行约定](./b-Office/现行约定.md)。

## 入口

| 入口 | 用途 |
| --- | --- |
| [`AGENTS.md`](./AGENTS.md) | AI 工作合同：读取顺序、真值判定、边界 |
| [`project.manifest.json`](./project.manifest.json) | 项目身份、活动目录、文档与命令 |
| [现行约定](./b-Office/现行约定.md) | 唯一的长期文档：模板规则与模块起步约定 |

## 目录

| 路径 | 职责 |
| --- | --- |
| `b-Code/HistoryExemplum/` | 模块源码、manifest 与 `eng/` 构建脚本 |
| `b-Code/HistoryExemplum.Tests/` | 离线自动验证（含宿主装载冒烟） |
| `b-Code/` | 项目合同检查 |
| `b-Office/` | 唯一长期文档 `current/现行约定.md` |
| `z-Publish/` | 正式快照与 `history/` 归档，由宿主管线写入 |

## 构建与验证

```powershell
dotnet build .\b-Code\HistoryExemplum\HistoryExemplum.csproj -c Release -p:NuGetAudit=false
dotnet run --project .\b-Code\HistoryExemplum.Tests\HistoryExemplum.Tests.csproj -c Release -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```

离线测试最后一组用宿主真正的 `ModuleHost` 装一遍打好的包：版本漂移、manifest 写错在宿主那边
都是静默跳过整个模块，这组用例在出包前拦住它们。

## 开发与发布

改动只进 `vulcan.dev.start` 创建的工作区，经宿主 Console CLI 走
`vulcan.dev.start` → `vulcan.dev.submit`（候选构建并热装送审）→ `vulcan.dev.finish`（批准后并回并写入 `z-Publish`）。
本仓不自行发布；`eng/Build-HistoryExemplumPackage.ps1` 只用于本地候选构建。

## 要点

- 页面 owner 由指令域推出（`exemplum` → `HistoryExemplum`），合同检查与离线测试都守着这条。
- 版本只改 `HistoryExemplumVersion.props`，再同步两份 manifest；三处不一致构建前就失败。
- {{本模块特有的要点}}

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
