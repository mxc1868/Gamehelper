# GameHelper 插件：TODO 与接手上下文

更新日期：2026-09-23。后续 agent 请先读本文，再读 [插件说明](Plugins/WhereTheWispsAt/README.md) 和 [Windows 调试说明](Plugins/WhereTheWispsAt/WINDOWS-DEBUG.zh-CN.md)。

## Windows：同步 1.5.11、恢复插件加载（2026-09-23 UTC）

用户报告插件列表没有幽火，并要求同步 1.5.11、保留本 fork 补丁后重新编译。

- [x] 联网核对 `MordWraith/Gamehelper` 的 `v1.5.11` 标签和 main：均为 `0d11fe76014862c45a21d759fefd28129eeddfc5`，发布页也指向该提交。已抓取到 `upstream/v1.5.11`；它早已是本地 main 的祖先，无待合并的上游提交。该标签源码中的核心版本仍写着 1.5.10，不能仅凭版本字段认定未同步。未运行覆盖式 `sync-gordin.ps1`。
- [x] 实际复现：`Test/launcher.log` 记录启动器安装上游 1.5.11；上游核心在 `PManager.LoadPlugin` 的类型枚举阶段抛出 `ReflectionTypeLoadException`，明确缺少 `GameHelper.Utils.MapProjection`。同一个幽火 DLL 在配套核心成功加载。上游核心同时缺少实体扫描接口及 `WorldItem.TryReadItem`；UniqueLoot 虽可实例化，仍不能据此认定扫描可用。
- [x] 保留全部现有幽火/UniqueLoot 核心与插件补丁，将核心和启动器版本统一为 1.5.11。本 fork 的 `Launcher/Program.cs` 跳过在线更新，继续正常启动 overlay；后续从 fork 拉取源码并编译更新。原有上游下载器与维护/发布工具未改造，不要用于本 fork 的更新或发布。
- [x] Windows .NET SDK 10.0.302 整套 `GameOverlay.sln` Release 编译成功，0 错误。首次整套重编译有 7 条既存警告（核心 3 条、WorldDrawing 4 条）；最后增量编译有 3 条核心警告。
- [x] Windows 离线检查：幽火 67 项、UniqueLoot 40 项通过。修正幽火测试读取正在写入的日志时的 Windows 共享模式（测试读取端使用 `FileShare.ReadWrite`），未更改插件日志写入行为。
- [x] 新增 `tests/PluginLoad.Tests`：直接调用部署核心的实际 `PluginAssemblyLoadContext` / `PManager.LoadPlugin`，验证 WhereTheWispsAt、UniqueLoot、Radar 的加载与实例化，并检查 6 项新增核心类型/接口。已在原版 Test 核心复现失败，配套 1.5.11 编译目录和更新后的 Test 目录均通过全部 9 项检查。不启动 overlay、不调用 OnEnable、不读取游戏、不写设置。
- [x] 已更新本机 `D:\PoE Trade\Gamehelper\Test` 的程序文件；覆盖前完整备份到 `test-runtime-backup/before-patched-1.5.11-20260922-224142/Test`，哈希确认原有 33 个配置/诊断文件保持不变。用户可直接运行 `Test/GameHelper.exe`，F12 启用 `WhereTheWispsAt`。未制作新 ZIP 或 Release。
- [ ] 当前 PoE2 场景中的幽火分类、地图显示、UniqueLoot 掉落读取和 API 对比仍待用户实测；加载检查通过不等于这些功能已验证。

后续重现：`dotnet build GameOverlay.sln -c Release`；`dotnet run --project tests/PluginLoad.Tests/PluginLoad.Tests.csproj -c Release -- Test`。后者最后一个参数应指向要验证的实际运行目录。

## 新功能：UniqueLoot 暗金 asset 识别（2026-09-23）

用户要求参考 [exApiTools/Ground-Items-With-Linq](https://github.com/exApiTools/Ground-Items-With-Linq)，在 GameHelper 中无需鉴定即可提示暗金掉落名称。按当前项目 PoE2 实现；这是新增任务，幽火实机验证仍未完成。

- [x] 核对原插件：`ItemVisualIdentities` 与 `UniqueItemDescriptions` 联结并按完整 ArtPath 分组，`RenderItem.ResourcePath` 查询多个名称候选。没有移植 LINQ 规则引擎。
- [x] 新增 `Plugins/UniqueLoot`：物品旁文字和屏幕列表、全路径匹配、多候选/未知/贴图读取失败状态、中英文设置、自定义映射覆盖、扫描上限和有界诊断导出。
- [x] 复用本仓库原有 `AwakeEntities`、`WorldItem`、`RenderItem`、`Mods`、`Render`。**本次新增核心 API 是 `WorldItem.TryReadItem(out Item)`**：重建内部物品并校验读取前后指针；未新增或改动 offsets 数值。插件不依赖此前幽火 `ScanEntities` API。
- [x] 内置 PoE2 导出版本 `4.5.5.2`，源提交 `repoe-fork/poe2@b818b843337cae43b090b272fd98bbc0fd3a34f3`，446 条路径、441 个名称、3 条共用路径；来源及更新方式见 [数据说明](Plugins/UniqueLoot/Data/SOURCES.md)。与价格数据无关。
- [x] Linux 编译成功。UniqueLoot **40 项离线检查通过**；WhereTheWispsAt **67 项回归检查通过**。前者检验映射/覆盖/高亮配置，不是游戏内存和绘制测试；后者包含原 63 项以及 4 项 Sacred 颜色默认值/迁移检查。干净编译有 3 条既存核心警告。
- [x] 初版 `scripts/package-wisps.py --include-unique` 生成完整自包含 Windows x64 ZIP + SHA-256（约 45.4 MiB），包含三个插件和 .NET 10.0.12。检查必要文件、x64 PE、自包含运行时配置、ZIP CRC、252 个包内文件哈希与双语资源键。
- [x] **远程目标已恢复并获授权**：2026-09-23 用户重新建立 `mxc1868/Gamehelper` fork，并明确要求同步本地工作。已核对仓库所有者、main 分支和写入权限，保留远端 `0d11fe7` 上游更新并合并本地幽火/UniqueLoot 历史。完整包与 checksum 的发布入口：[UniqueLoot + 幽火调试版](https://github.com/mxc1868/Gamehelper/releases/tag/unique-debug-2026-09-23)；发布前须确认源码、构建清单与 Release 指向同一提交。
- [x] 默认高亮猎首（金色）和魔血（紫红色）：PoE2DB Icon 与内置 `.dds` asset 交叉确认，完整路径匹配；1.3 倍字号、描边及列表优先显示，默认规则在 `Plugins/UniqueLoot/highlights.default.json`，首次启用生成 `config/highlights.json`，可编辑并重新加载。
- [x] Sacred Wisp 默认改为橙色；旧默认白色一次性迁移，保留其他自定义颜色。
- [x] **最新交付要求**：用户于 2026-09-23 明确表示自己在 Windows 编译，不再制作测试包。后续仅提交并推送源码到本 fork 的 `main`，除非用户再次要求打包。已发布的初版 `unique-debug-2026-09-23` 对应 `5e66127`，不含后续腰带高亮/橙色幽火修改；没有发布 r2 包。
- [ ] Windows 实测：未鉴定暗金名称与鉴定后对照；地面投影；拾取/丢回；切区、禁用/重启；过滤设置、扫描截断与性能。读取失败时交回 `Plugins/UniqueLoot/diagnostics/latest-scan.json`、host 日志及构建清单。

初版构建：`artifacts/unique/GameHelper-unique-debug-win-x64.zip`，不含后续高亮与 Sacred 色彩修改；当前使用 main 源码编译。使用和限制详见 [UniqueLoot 说明](Plugins/UniqueLoot/README.md)。没有可靠的原有 `Identified` 属性，所以显示地面所有暗金的 asset 候选（包括已鉴定的），不猜测该字段；不读取随机词缀/数值。仅扫描公开 awake 集合，是否漏掉当前客户端地面实体仍待实机证据；静态映射不会自动覆盖未来更新。新增说明不代表原版框架已经提供新接口。

## 用户目标与当前交付

把 [exCore2/WhereTheWispsAt](https://github.com/exCore2/WhereTheWispsAt) 的幽火标记功能迁移到 GameHelper，最终希望尽量只维护插件。用户在 Windows 玩游戏；现已明确可自行编译（2026-09-23），当前交付 main 源码，先前 Linux 打包流程保留但不再默认执行。

- 当前仓库：<https://github.com/mxc1868/Gamehelper>，本地 Windows `D:\PoE Trade\Gamehelper`，分支 `main`（历史 Linux 工作区为 `/home/ubuntu/Gamehelper`）。幽火实现提交 `05d1116`，UniqueLoot 实现提交 `dfee696`，均在同一分支上；恢复后的 fork 已有更新 `0d11fe7`，通过 merge 保留，现已确认它也是上游 `v1.5.11` 标签指向的提交。
- 历史 fork 删除后于 2026-09-23 恢复。旧 `wisps-debug-2026-09-14` Release 未恢复，不再作为下载入口；使用新的 `unique-debug-2026-09-23` 完整包（包含 UniqueLoot、WhereTheWispsAt、Radar）。
- 源码同步到本 fork 的 main；如用户另行要求打包，完整 ZIP 和 checksum 放 GitHub Releases。不把 DLL 和运行时逐个提交进源码历史。
- 初始基础源码来自 `MordWraith/Gamehelper` 提交 `5e581b16c834bbdee831e28910f4786f9e22ab94`；本次同步保留恢复后 fork 中的 `0d11fe7`（核心版本 1.5.10、部署实体记录及地形容量等更新）。与 Gordin/GameHelper2 共用大量核心及 offsets 源码，但不能推断未来版本始终兼容。
- 用户已要求清理此前的 ExileCore2 / ExileApi 逆向研究，并转向 GameHelper；此前研究文档和临时目录已经清理。当前不做付费授权绕过或相关研究。

## 已完成

- [x] 原生 `PCore<WhereTheWispsAtSettings>` 插件，幽火颜色、地图标记、相邻 ID 连线、宝箱和事件、可选地面框、中英文设置。
- [x] 显式标注未知颜色与不可用状态；没有凭空补燃料百分比或精确模型旋转。
- [x] 新增核心定向扫描 API，按 metadata 过滤后建立新实体/组件对象；正常绘制读 awake，避免共享对象缓存影响该路线。
- [x] 地图投影提取为 `GameHelper/Utils/MapProjection.cs`，Radar 复用相同公式。没有修改 `GameOffsets` 数值。
- [x] 诊断日志：各扫描阶段计数、失败样本、组件父指针验证、路径、坐标、StateMachine 名称/值、渲染条件、投影参数、实体变化。
- [x] 一次性 awake / sleeping 实体源对比。它与“原有 API / 新增 API 对比”是两个不同的功能。
- [x] **完成三路 API 对照日志**：`public_lookup`、`public_component_recreate`、`fresh_entity_scan`；使用同一分类/状态读取代码，按实体 ID + 地址 + metadata 比较。
- [x] API 对照记录缺失实体、不可用观测、组件/路径/分类/状态/坐标差异、过滤开关、时间/区域、耗时、截断标记、有限样本。空结果或两边都失败不计为可用观测一致。
- [x] 60 秒录制内以最短 2 秒间隔进行 API 对照，也有手动单次按钮。公开集合读取使用 `shouldCache:false` 避免额外填充共享缓存；不会自动修改全局实体过滤设置。
- [x] 日志轮换最多 3 × 2 MiB；自动停止、报告导出、写入失败展示。独立定时协程确保 F9 跳过 DrawUI 时仍可记录心跳和停止。
- [x] Linux 可构建 Windows x64 自包含包，包含配套核心、WhereTheWispsAt、Radar、.NET 10 运行时、字体、语言文件、启动脚本及构建清单。
- [x] 根目录 `AGENTS.md` 指向此接手文档；仓库首页提供本 fork 的完整包下载与调试入口。

## 已验证与尚未验证

已完成 Linux 编译，当前 **67 项**不依赖游戏的幽火回归检查通过（原 63 项 + Sacred 色彩默认值/迁移 4 项）。测试覆盖分类、连线、地图数学、配置边界、并发采样上限、日志轮换/超时/I/O 错误、API 差异比较。**测试输入是合成数据，不是 Windows 实机样本。** 后续版本以测试运行输出和 Release 说明为准。

历史打包检查包括必要文件、自包含 runtimeconfig、Windows x64 PE、ZIP CRC 和 SHA-256 清单。本次已在 Windows 通过实际插件管理器的加载/实例化检查；尚未验证 overlay 启动与游戏内读取、最终显示，详见本页最新 Windows 验证记录。

## P0：用实机证据决定是否保留新核心 API

- [ ] 用户运行已更新的 Windows `Test/GameHelper.exe`，接受现有 `app.manifest` 要求的管理员权限，F12 启用插件并进行游戏内测试。
- [ ] 在确实有幽火/宝箱/井的区域录制 60 秒，覆盖静止、移动、采集、开启/激活、切图；收集 `diagnostics/`、host 日志、`build-manifest.json` 和实际观察说明。
- [ ] 检查 `api_comparison`：公开集合是否缺少新增扫描能读到的目标？是否仅因 `ProcessAllRenderableEntities=false`？报告只记录设置，不自动切换。
- [ ] 若要排除过滤设置影响，可在 GH 原有设置中手动短时开启“处理所有可渲染实体”后再录制，并核对开关值与耗时。该设置会扩大整个框架处理范围，需要观察实际性能；测试后恢复自己的配置。
- [ ] 比较公开读取与公开构造函数重读组件：同实体路径/颜色/状态是否只有重读路线更新？重复差异是否跨多个稳定场景采样持续？
- [ ] 比较公开构造函数路线与新实体扫描：是否仍存在目标缺失、组件地址表过期、父指针不符或激活状态不一致？不能把单次先后读取的差异当作缓存失效定论。
- [ ] 只在 `CompleteWindow=true`、未截断、同一区域且有可用目标的样本上讨论覆盖率；一致的未知颜色/未知状态也不代表功能已确认。
- [ ] **做架构决定并写下依据**：若原有 API 覆盖和更新都足够，则把投影移回插件、去掉对新扫描 API 的生产依赖，并在未修改核心上验证；若不足，列出失败样本、最小核心改动和每次升级需要合并的位置。

重要源码事实：

1. `AreaInstance.AwakeEntities` 原本就是公开集合；`ProcessAllRenderableEntities` 会影响视觉实体是否进入集合。
2. `Entity.TryGetComponent<T>(..., shouldCache:false)` **先查已有缓存**；false 只控制未命中时是否写入缓存，不保证强制重读。
3. `Animated`、`Render`、`Chest`、`StateMachine` 的带地址构造函数原本就是 public。对已找到组件的地址重新构造组件，是本次加入对照的零新 API 候选方案；但它不能补回根本未进入公开集合的实体。
4. `Entity` 构造函数以及底层进程 Handle/部分事件是 internal，不能当作公开插件 API。当前新 `ScanEntities` 正是放在核心内实现实体重建。
5. `Animated.UpdateData(false)` 不重新加载路径；`Entity` 对某些无用实体会跳过组件更新。这是需要实测的风险来源，尚不是当前幽火必然受影响的证据。

## P1：功能与性能验收

- [ ] 验证当前 PoE2 的 Awake / Sleeping 布局和目标 metadata；当前日志存在并不意味着 offsets 正确。
- [ ] 验证 `_primal`、`_warden`、`_vodoo`、`_sacred` 分类；`ModelPath` 仍只是样本，不作为未经证明的颜色替代。
- [ ] 检查地图缩放、拖动、窗口尺寸、高度差、小地图裁剪和地面框位置。
- [ ] 用开箱、激活前后样本确认 `Chest.IsOpened` 与 `StateMachine` 的 `activated=1` 语义。
- [ ] 测试切图、回城、失焦、面板遮挡、F9、关闭游戏、禁用/重新启用，确认显示和日志没有跨区域误判。
- [ ] 比较普通扫描与诊断开启的耗时、帧率、内存。对照读数不是逐帧执行；慢扫描会自动延长间隔。
- [ ] 燃料百分比与精确旋转暂不实现；取得当前版本真实字段证据后再评估。

## 关键文件与维护边界

| 文件 | 职责 |
| --- | --- |
| `Plugins/WhereTheWispsAt/WhereTheWispsAtCore.cs` | 插件生命周期、扫描、共同的组件读取和状态解释 |
| `WhereTheWispsAtCore.ApiComparison.cs`（同目录） | 原有 API 与新增扫描的采集调度 |
| `WispApiComparison.cs`（同目录） | 有界快照、实体身份匹配、差异字段和样本 |
| `WhereTheWispsAtCore.Diagnostics.cs`、`WispDiagnostics.cs` | 限时录制、报告、心跳、版本信息 |
| `WispModel.cs`、`WispRenderer.cs` | 分类/连线与 ImGui 绘制 |
| `GameHelper/RemoteObjects/States/InGameStateObjects/AreaInstance.cs` | 新增 `ScanEntities` / `ScanAwakeEntities`；升级核心时需保留或移除的改动 |
| `EntityScanDiagnostics.cs`（同目录） | 扫描阶段计数和有限失败样本 |
| `GameHelper/Utils/MapProjection.cs`、`Plugins/Radar/Helper.cs` | 共用投影计算；最终插件独立化时可调整 |
| `scripts/package-wisps.py` | Linux 构建和打包，不自动推送或发布 |
| `tests/WhereTheWispsAt.Tests` | Linux 合成数据回归检查 |

## 复现构建与发布

一般 Linux 环境：

```bash
dotnet run --project tests/WhereTheWispsAt.Tests/WhereTheWispsAt.Tests.csproj -c Release
python3 scripts/package-wisps.py
```

当前工作区已还原依赖的离线方式（路径是开发缓存，不是项目依赖）：

```bash
DOTNET_CLI_HOME=/tmp/gamehelper-unique/dotnet-home dotnet build tests/WhereTheWispsAt.Tests/WhereTheWispsAt.Tests.csproj -c Release --no-restore -m:1 -nr:false --disable-build-servers
DOTNET_CLI_HOME=/tmp/gamehelper-unique/dotnet-home dotnet tests/WhereTheWispsAt.Tests/bin/Release/net10.0/WhereTheWispsAt.Tests.dll
DOTNET_CLI_HOME=/tmp/gamehelper-unique/dotnet-home python3 scripts/package-wisps.py --packages /tmp/gamehelper-unique/nuget --source /tmp/gamehelper-unique/nuget
```

交付前先提交源码，再重新打包，让清单中的 BaseCommit 对应本地提交。默认输出 `artifacts/wisps/WhereTheWispsAt-debug-win-x64.zip` 及 `.sha256`；加 `--include-unique` 输出 `artifacts/unique/GameHelper-unique-debug-win-x64.zip` 及 `.sha256`。提供完整包，单独更新插件 DLL 目前不够。当前源码同步目标为 `mxc1868/Gamehelper` 的 main；用户现自行在 Windows 编译，未经新请求不执行上述打包/Release 步骤。将来需要打包时建议使用干净提交/临时 worktree，避免混入其他尚未提交的评估文档。

原仓库的 `scripts/sync-gordin.ps1` 使用覆盖式同步，其他维护/启动器脚本也可能仍指向上游。不要直接运行它们更新当前分支或发布本 fork；先检查目标和差异，避免丢掉新增 API 或拿错编译包。
