# GameHelper 插件：TODO 与接手上下文

更新日期：2026-09-26。后续 agent 请先读本文，再读 [插件说明](Plugins/ShowMeWisp/README.md) 和 [Windows 调试说明](Plugins/ShowMeWisp/WINDOWS-DEBUG.zh-CN.md)。

## Follower 贴墙与窄路寻路修复（2026-09-26）

- [x] 用户实测反馈 Follower 很容易无法抵达，尤其贴墙时，要求对照 Radar。已确认原因：原 `Walkable` 按 `Clearance` 扩大禁行区，默认 1 会把墙边可走格当成障碍，同时影响起终点检查、转向和停止距离判断。新增“默认间距允许贴墙起步”回归在旧实现上失败，修复后通过。
- [x] `Clearance` 改为路径代价偏好，靠墙越近代价越高，但不改变真实格子的连通性；允许贴墙起终点、窄道和窄门。有空间时优先离墙，路线前瞻比较同一代价，避免跳过节点后又沿墙直走。实际方向检查和距离停止使用真实障碍；仍禁止穿墙、斜切墙角及跨越已关闭门。菜单改名“期望离墙间距”并说明含义，旧配置数值保留。
- [x] 对照确认 Radar 不扩大墙体、起终点可在半径 75 格内重定位，并简化显示路线；默认 100 万次迭代、2500 格距离上限，门附近无条件覆盖。Follower 保留原有真实阻挡端点拒绝、关门检查、4 万次出队/约 100 毫秒与 600 格上限，不采用可能跨墙的重定位。本轮无 Radar、核心或框架 API 改动。
- [x] 87 项 Follower 合成检查通过（原 62 项基础上增加贴墙、窄道转向、离墙偏好、分数坐标、窄门开关与近距离停止场景）；Windows Release 插件构建 0 错误，仅 2 条既存 NU1900 网络警告；11 项真实插件发现/加载/API 检查通过。未连接游戏或发送按键，未更新 Test；只交付源码。
- [ ] 修复后实机复核：贴墙起步、队长贴墙、窄路转弯、门和坡道。如果仍不可达，需要区分原始地形端点阻挡、门组件状态、搜索预算或方向投影问题；本次合成验证不代表这些实机场景均已解决。

## Follower 前台 WASD 跟随（2026-09-25）

用户要求参考 Radar 新增 Follower，已明确小号运行在另一台电脑或虚拟机，窗口保持前台；队长必须从扫描名单选择，不要手打名字。仍按既有要求仅交付源码，不更新 Test、不制作 ZIP/Release。

- [x] 检查本地与 MordWraith 上游插件目录、公开搜索，未确认可直接使用的 GameHelper Follower。Radar 的队长标识来自配置名字，不是可靠的组队队长字段；它的门覆盖把所有门附近强制视为可通行，不能直接当作移动依据。
- [x] 新增 `Plugins/Follower` 并加入整套解决方案构建。扫描附近有效 Player，排除小号自己、按名字去重排序后下拉选择，选中立即保存；不提供手动输入框，也不自动选择最近玩家。名单是附近可见玩家，尚不能保证都是队友。
- [x] 复用现有公开 AwakeEntities/Player/Render/Life/TriggerableBlockage/地形/投影接口；独立实现有界可取消 A*、严格边界与墙角检查、离墙间距、已知开门覆盖/关门阻挡、路线更新和屏幕方向到 WASD 转换。无 GameHelper 核心、GameOffsets 或 Radar 改动，无新增框架 API，无 Radar DLL 依赖。
- [x] 默认仅预览、可配置快捷键启停（默认 F6）、Esc 停止、停止/恢复距离滞回、卡住超时；失焦、目标丢失/身份变化、切图、死亡、聊天/大面板/设置、异常时停止。独立按键租期定时器处理 F9/绘制卡顿与焦点变化；正常禁用/退出释放按键。关闭预览才发送前台 SendInput；不支持后台双开、自动开门、传送门或跨区。
- [x] Windows Release 插件及整套解决方案构建成功；Follower 62 项纯逻辑测试通过；真实 PManager 插件发现/加载/API 检查扩展到 Follower，共 11 项通过。首次插件构建有 3 条既存核心警告；整套构建另有 4 条既存 WorldDrawing 警告，NuGet 漏洞元数据网络不可达产生 NU1900 警告。无新插件编译警告；构建时未做游戏输入实测，后续单键验证见下。
- [x] 用户随后要求测试同机客户端的后台输入。2026-09-25 本地时间（UTC 2026-09-26），对当前 PoE2 定向发送 `PostMessage WM_KEYDOWN/WM_KEYUP`：后台多轮、前台对照均返回投递成功，但用户均未观察到移动。不能据此启用后台跟随，也不能将消息投递成功当作游戏接受输入。
- [x] 改用与 Follower 相同的 `SendInput` W 扫描码 0x11，于 UTC 02:15:37 在游戏前台按住约 510 毫秒后松开；按下/松开各接受 1 个输入，系统 W 状态由按下恢复未按下，全程游戏保持前台。用户明确确认“这次动了”。这是独立探针的前台 W 实测，未启动完整 Follower，不代表寻路、其他方向或插件生命周期已通过实测。保持现有 SendInput 实现和前台限制。
- [x] 后续依次测试后台 `SendMessageTimeout`、`AttachThreadInput + SetKeyboardState + 窗口消息`、真实 `SendInput`（临时测试窗前台、游戏后台），每项均发送 3 次约半秒 W；用户逐项确认没有移动。测试已释放 W、恢复线程键盘状态并解除连接，临时测试窗已关闭。API 成功仅表示系统接受调用，不代表角色移动。
- [x] 用户最终选择两台机器，取消继续测试后台输入。虚拟手柄测试未执行，未安装驱动或下载手柄 SDK。已删除本轮 `gamehelper-follower-probe` 临时目录及两张截图（共 17,858,978 字节），核对无测试进程残留。保留 Follower 源码与前台限制，不自动重启后台输入调查；未更新 Test。
- [x] 用户指出 F8 与游戏截图冲突，新增菜单“开始/停止快捷键”下拉选择，选中立即保存；默认改为 F6，旧配置缺少该字段时也使用 F6。读键和中英文提示均使用当前选择；排除 WASD、Esc、Enter、修饰键和鼠标键，改键时同步按住状态，避免凭空产生一次启停。无核心 API 改动。Windows Release 插件构建成功（0 错误，3 条既存核心警告及 2 条 NU1900 网络警告），62 项 Follower 检查、11 项加载/API 检查和中英文格式化检查通过；未发送实机按键，菜单操作与重启保存仍待实测，未更新 Test。
- [ ] 实机验证：附近名字选择与保存；启停快捷键选择、重启保存与状态提示；预览方向；WASD 直行/斜行；拐角/窄道/坡道/开关门；距离启停；手动接管；目标走远、死亡、切区；Alt-Tab、聊天、F9 和禁用后的释放。操作系统强制结束进程不执行清理；同时按住插件已按下的同一个移动键不能可靠识别，使用 Esc 或启停快捷键接管。

说明：[Follower README](Plugins/Follower/README.md)。验证命令：`dotnet run --project tests/Follower.Tests/Follower.Tests.csproj -c Release`；`dotnet run --project tests/PluginLoad.Tests/PluginLoad.Tests.csproj -c Release -- GameHelper/bin/Release/net10.0-windows/win-x64`。当前加载检查要求构建目录包含 Follower，旧 Test 未部署该插件。

## UniqueLoot 默认只显示高亮名称（2026-09-23 UTC）

- [x] 增加 `OnlyShowHighlightedItemNames=true` 及中英文设置项。默认地面名称也只显示勾选且高亮启用的物品，旧配置缺少该字段时默认开启；关闭该项恢复普通名称及原 ShowUnknown 过滤。屏幕列表仍始终只含高亮项。
- [x] 用户明确选择“本次只提交源码，我稍后自行更新”。不再尝试覆盖 Test，也不再等待用户退出程序；后续部署需新请求。
- [x] 此选项加入后 Windows Release 构建成功，0 错误、3 条既存核心警告；现有 UniqueLoot 60 项回归通过，未进行游戏内显示验证。

## UniqueLoot 高亮样式调整（2026-09-23 UTC）

- [x] 用户要求所有高亮默认金色，去掉 `[!]`，图标与文字分别占上、下两行；地面高亮与屏幕列表共用居中布局，列表间距按两行总高度计算，缺图时不保留空白图片行。
- [x] 物品勾选列表增加逐项颜色选择，修改文字和边框颜色并立即保存；保留启用状态，同名称不同外观一起改色，取消/重选和重启保留配色。
- [x] 新增高亮配置 `DefaultColorVersion=1`，仅把完全匹配旧魔血默认紫色方案的未版本化配置迁移为金色；保留其他自定义颜色，后续主动选紫色不会反复迁移。
- [x] Windows Release 插件构建成功，0 错误、3 条既存核心警告；UniqueLoot 60 项回归和构建目录的 10 项真实插件发现/加载/API 检查通过。
- [x] 高亮样式源码已提交并推送 `32f25b8`。Test 更新首次因运行中的 GameHelper 占用 `UniqueLoot.dll` 而中止（尚未替换文件）；用户随后取消本次部署，选择自行更新。备份为 `artifacts/unique/before-gold-stacked-20260922-235517/UniqueLoot`。游戏内视觉效果仍待用户确认。

## ShowMeWisp 改名与幽火三档方框（2026-09-23 UTC）

用户要求整体改名为 `ShowMeWisp`，并确认 Small / Medium / Big 指幽火自身大小，不是宝箱。本 fork 缺少三档逻辑；后续在 `D:\PoE Trade\WhereTheWispsAt` 的旧源码和交接记录中找到资源模型规则及三档倍率说明，已按旧规则恢复。

- [x] 项目、目录、DLL、命名空间、Core/Settings 类、测试项目、解决方案、构建/打包引用与当前文档使用新名称。上游参考仓库链接保留原名。历史章节的源码路径已按新名称更新，当时实际插件名为 `WhereTheWispsAt`。
- [x] 内部 `PluginRenames` 让插件管理器在新 DLL 存在时跳过旧目录，首次建立新元数据时继承旧 Enable 值；新插件优先读取自己的配置，没有时读取旧 `WhereTheWispsAt/config/settings.txt`。保存使用新目录，旧配置/诊断不删除。未新增公共框架 API。
- [x] 严格限定资源模型目录 `Metadata/Effects/Spells/monsters_effects/League_Azmeri/resources/wisp_doodads/wisp_`，四类颜色模型的 `_sml` / `_med` / `_big.ao` 对应三档大小；无后缀、未知模型不猜大小。Animated 路径已有颜色时优先使用，否则回退到上述 ModelPath 家族。宝箱和补给不采用幽火倍率。
- [x] 默认 Small / Medium / Big 倍率为 0.6 / 1 / 1.6，地图基础 5 像素对应 3 / 5 / 8 像素；相同倍率作用于大地图、小地图和启用后的地面框。双语设置提供总开关、三档倍率及大小计数；API 对比和绘制诊断记录大小。
- [x] Windows 回归 114 项通过，包含颜色/三档模型、未知后缀、非幽火排除、倍率和旧配置/启用状态继承；真实核心插件发现、加载与新增 API 检查共 10 项通过。
- [ ] 旧目录交接记录报告 2026-09-14 蓝/黄三档实机样本，紫/神圣仍为规则与合成测试。本次未重新采集当前游戏数据，视觉效果仍待验证。旧记录还报告过 API 等价样本；当前 fork 尚未独立复核或迁移为无核心修改插件，不应自动开展该重构。

## UniqueLoot 可选高亮清单与物品图标（2026-09-23 UTC）

- [x] 用户确认 Radar 是窗口宽度问题，取消修复；本次 Radar 源码无改动。
- [x] UniqueLoot 设置增加可搜索、可勾选的清单和“仅查看已勾选”过滤；同名称多外观归为一项，共用贴图仍保留全部候选。旧高亮规则初始化选择，取消/重新勾选保留自定义颜色和字号；通过临时文件替换保存到 `config/highlights.json`，立即生效。
- [x] 屏幕掉落列表只显示选中的高亮物品，独立于普通地面文字名额；高亮文字默认至少 1.6 倍，可调 1.3–2.5 倍，保留颜色、边框和背景。普通地面文字仍由原设置控制。
- [x] 内置 439 张 WebP 图标（约 5.7 MiB），来自公开 PoE2DB 图标 CDN，439/446 条 asset 有图，7 条缺图回退大字；设置清单、地面高亮和屏幕列表支持图片。运行时仅加载本地图像、不联网；纹理缓存有上限，禁用时释放。
- [x] `Icons/sources.json` 保留 URL、文件名、SHA-256、大小及缺图情况；`scripts/fetch-unique-icons.ps1` 可复现下载。所有 439 图像均用部署版本 ImageSharp 解码成功。无新框架 API，无 ZIP/Release。
- [x] UniqueLoot 53 项离线检查通过：全清单选择/取消/保存重载、旧样式保留、候选/外观分组、列表资格与字号；Windows 整套 Release 编译成功。实际游戏读取、位置和 UI 点击仍需用户验证。
- [x] 本机 Test 已覆盖 452 个核心/插件/图标文件，34 个既存配置及诊断文件哈希保持不变；覆盖前完整备份为 `artifacts/wisps/before-showmewisp-unique-20260922-234638/Test`。部署后 10 项真实插件发现/加载/API 检查全通过，439 张图标部署齐全。此前 `test-runtime-backup` 被用户构建流程重新生成，旧备份已不在该位置；本次备份位于不会被该流程重置的 artifacts。

## Windows：同步 1.5.11、恢复插件加载（2026-09-23 UTC）

用户报告插件列表没有幽火，并要求同步 1.5.11、保留本 fork 补丁后重新编译。

- [x] 联网核对 `MordWraith/Gamehelper` 的 `v1.5.11` 标签和 main：均为 `0d11fe76014862c45a21d759fefd28129eeddfc5`，发布页也指向该提交。已抓取到 `upstream/v1.5.11`；它早已是本地 main 的祖先，无待合并的上游提交。该标签源码中的核心版本仍写着 1.5.10，不能仅凭版本字段认定未同步。未运行覆盖式 `sync-gordin.ps1`。
- [x] 实际复现：`Test/launcher.log` 记录启动器安装上游 1.5.11；上游核心在 `PManager.LoadPlugin` 的类型枚举阶段抛出 `ReflectionTypeLoadException`，明确缺少 `GameHelper.Utils.MapProjection`。同一个幽火 DLL 在配套核心成功加载。上游核心同时缺少实体扫描接口及 `WorldItem.TryReadItem`；UniqueLoot 虽可实例化，仍不能据此认定扫描可用。
- [x] 保留全部现有幽火/UniqueLoot 核心与插件补丁，将核心和启动器版本统一为 1.5.11。本 fork 的 `Launcher/Program.cs` 跳过在线更新，继续正常启动 overlay；后续从 fork 拉取源码并编译更新。原有上游下载器与维护/发布工具未改造，不要用于本 fork 的更新或发布。
- [x] Windows .NET SDK 10.0.302 整套 `GameOverlay.sln` Release 编译成功，0 错误。首次整套重编译有 7 条既存警告（核心 3 条、WorldDrawing 4 条）；最后增量编译有 3 条核心警告。
- [x] Windows 离线检查：幽火 67 项、UniqueLoot 40 项通过。修正幽火测试读取正在写入的日志时的 Windows 共享模式（测试读取端使用 `FileShare.ReadWrite`），未更改插件日志写入行为。
- [x] 新增 `tests/PluginLoad.Tests`：直接调用部署核心的实际 `PluginAssemblyLoadContext` / `PManager.LoadPlugin`，验证 ShowMeWisp、UniqueLoot、Radar 的加载与实例化，并检查 6 项新增核心类型/接口。已在原版 Test 核心复现失败，配套 1.5.11 编译目录和更新后的 Test 目录均通过全部 9 项检查。不启动 overlay、不调用 OnEnable、不读取游戏、不写设置。
- [x] 已更新本机 `D:\PoE Trade\Gamehelper\Test` 的程序文件；覆盖前完整备份到 `test-runtime-backup/before-patched-1.5.11-20260922-224142/Test`，哈希确认原有 33 个配置/诊断文件保持不变。用户可直接运行 `Test/GameHelper.exe`，F12 启用 `ShowMeWisp`。未制作新 ZIP 或 Release。
- [ ] 当前 PoE2 场景中的幽火分类、地图显示、UniqueLoot 掉落读取和 API 对比仍待用户实测；加载检查通过不等于这些功能已验证。

后续重现：`dotnet build GameOverlay.sln -c Release`；`dotnet run --project tests/PluginLoad.Tests/PluginLoad.Tests.csproj -c Release -- Test`。后者最后一个参数应指向要验证的实际运行目录。

## 新功能：UniqueLoot 暗金 asset 识别（2026-09-23）

用户要求参考 [exApiTools/Ground-Items-With-Linq](https://github.com/exApiTools/Ground-Items-With-Linq)，在 GameHelper 中无需鉴定即可提示暗金掉落名称。按当前项目 PoE2 实现；这是新增任务，幽火实机验证仍未完成。

- [x] 核对原插件：`ItemVisualIdentities` 与 `UniqueItemDescriptions` 联结并按完整 ArtPath 分组，`RenderItem.ResourcePath` 查询多个名称候选。没有移植 LINQ 规则引擎。
- [x] 新增 `Plugins/UniqueLoot`：物品旁文字和屏幕列表、全路径匹配、多候选/未知/贴图读取失败状态、中英文设置、自定义映射覆盖、扫描上限和有界诊断导出。
- [x] 复用本仓库原有 `AwakeEntities`、`WorldItem`、`RenderItem`、`Mods`、`Render`。**本次新增核心 API 是 `WorldItem.TryReadItem(out Item)`**：重建内部物品并校验读取前后指针；未新增或改动 offsets 数值。插件不依赖此前幽火 `ScanEntities` API。
- [x] 内置 PoE2 导出版本 `4.5.5.2`，源提交 `repoe-fork/poe2@b818b843337cae43b090b272fd98bbc0fd3a34f3`，446 条路径、441 个名称、3 条共用路径；来源及更新方式见 [数据说明](Plugins/UniqueLoot/Data/SOURCES.md)。与价格数据无关。
- [x] Linux 编译成功。UniqueLoot **40 项离线检查通过**；ShowMeWisp **67 项回归检查通过**。前者检验映射/覆盖/高亮配置，不是游戏内存和绘制测试；后者包含原 63 项以及 4 项 Sacred 颜色默认值/迁移检查。干净编译有 3 条既存核心警告。
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
- 历史 fork 删除后于 2026-09-23 恢复。旧 `wisps-debug-2026-09-14` Release 未恢复，不再作为下载入口；使用新的 `unique-debug-2026-09-23` 完整包（包含 UniqueLoot、ShowMeWisp、Radar）。
- 源码同步到本 fork 的 main；如用户另行要求打包，完整 ZIP 和 checksum 放 GitHub Releases。不把 DLL 和运行时逐个提交进源码历史。
- 初始基础源码来自 `MordWraith/Gamehelper` 提交 `5e581b16c834bbdee831e28910f4786f9e22ab94`；本次同步保留恢复后 fork 中的 `0d11fe7`（核心版本 1.5.10、部署实体记录及地形容量等更新）。与 Gordin/GameHelper2 共用大量核心及 offsets 源码，但不能推断未来版本始终兼容。
- 用户已要求清理此前的 ExileCore2 / ExileApi 逆向研究，并转向 GameHelper；此前研究文档和临时目录已经清理。当前不做付费授权绕过或相关研究。

## 已完成

- [x] 原生 `PCore<ShowMeWispSettings>` 插件，幽火颜色、地图标记、相邻 ID 连线、宝箱和事件、可选地面框、中英文设置。
- [x] 显式标注未知颜色与不可用状态；没有凭空补燃料百分比或精确模型旋转。
- [x] 新增核心定向扫描 API，按 metadata 过滤后建立新实体/组件对象；正常绘制读 awake，避免共享对象缓存影响该路线。
- [x] 地图投影提取为 `GameHelper/Utils/MapProjection.cs`，Radar 复用相同公式。没有修改 `GameOffsets` 数值。
- [x] 诊断日志：各扫描阶段计数、失败样本、组件父指针验证、路径、坐标、StateMachine 名称/值、渲染条件、投影参数、实体变化。
- [x] 一次性 awake / sleeping 实体源对比。它与“原有 API / 新增 API 对比”是两个不同的功能。
- [x] **完成三路 API 对照日志**：`public_lookup`、`public_component_recreate`、`fresh_entity_scan`；使用同一分类/状态读取代码，按实体 ID + 地址 + metadata 比较。
- [x] API 对照记录缺失实体、不可用观测、组件/路径/分类/状态/坐标差异、过滤开关、时间/区域、耗时、截断标记、有限样本。空结果或两边都失败不计为可用观测一致。
- [x] 60 秒录制内以最短 2 秒间隔进行 API 对照，也有手动单次按钮。公开集合读取使用 `shouldCache:false` 避免额外填充共享缓存；不会自动修改全局实体过滤设置。
- [x] 日志轮换最多 3 × 2 MiB；自动停止、报告导出、写入失败展示。独立定时协程确保 F9 跳过 DrawUI 时仍可记录心跳和停止。
- [x] Linux 可构建 Windows x64 自包含包，包含配套核心、ShowMeWisp、Radar、.NET 10 运行时、字体、语言文件、启动脚本及构建清单。
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
- [ ] 在当前游戏复核 Animated/ModelPath 的四类颜色及 `_sml` / `_med` / `_big` 大小分类；旧目录证据与本次离线测试见顶部记录。
- [ ] 检查地图缩放、拖动、窗口尺寸、高度差、小地图裁剪和地面框位置。
- [ ] 用开箱、激活前后样本确认 `Chest.IsOpened` 与 `StateMachine` 的 `activated=1` 语义。
- [ ] 测试切图、回城、失焦、面板遮挡、F9、关闭游戏、禁用/重新启用，确认显示和日志没有跨区域误判。
- [ ] 比较普通扫描与诊断开启的耗时、帧率、内存。对照读数不是逐帧执行；慢扫描会自动延长间隔。
- [ ] 燃料百分比与精确旋转暂不实现；取得当前版本真实字段证据后再评估。

## 关键文件与维护边界

| 文件 | 职责 |
| --- | --- |
| `Plugins/ShowMeWisp/ShowMeWispCore.cs` | 插件生命周期、扫描、共同的组件读取和状态解释 |
| `ShowMeWispCore.ApiComparison.cs`（同目录） | 原有 API 与新增扫描的采集调度 |
| `WispApiComparison.cs`（同目录） | 有界快照、实体身份匹配、差异字段和样本 |
| `ShowMeWispCore.Diagnostics.cs`、`WispDiagnostics.cs` | 限时录制、报告、心跳、版本信息 |
| `WispModel.cs`、`WispRenderer.cs` | 分类/连线与 ImGui 绘制 |
| `GameHelper/RemoteObjects/States/InGameStateObjects/AreaInstance.cs` | 新增 `ScanEntities` / `ScanAwakeEntities`；升级核心时需保留或移除的改动 |
| `EntityScanDiagnostics.cs`（同目录） | 扫描阶段计数和有限失败样本 |
| `GameHelper/Utils/MapProjection.cs`、`Plugins/Radar/Helper.cs` | 共用投影计算；最终插件独立化时可调整 |
| `scripts/package-wisps.py` | Linux 构建和打包，不自动推送或发布 |
| `tests/ShowMeWisp.Tests` | Linux 合成数据回归检查 |

## 复现构建与发布

一般 Linux 环境：

```bash
dotnet run --project tests/ShowMeWisp.Tests/ShowMeWisp.Tests.csproj -c Release
python3 scripts/package-wisps.py
```

当前工作区已还原依赖的离线方式（路径是开发缓存，不是项目依赖）：

```bash
DOTNET_CLI_HOME=/tmp/gamehelper-unique/dotnet-home dotnet build tests/ShowMeWisp.Tests/ShowMeWisp.Tests.csproj -c Release --no-restore -m:1 -nr:false --disable-build-servers
DOTNET_CLI_HOME=/tmp/gamehelper-unique/dotnet-home dotnet tests/ShowMeWisp.Tests/bin/Release/net10.0/ShowMeWisp.Tests.dll
DOTNET_CLI_HOME=/tmp/gamehelper-unique/dotnet-home python3 scripts/package-wisps.py --packages /tmp/gamehelper-unique/nuget --source /tmp/gamehelper-unique/nuget
```

交付前先提交源码，再重新打包，让清单中的 BaseCommit 对应本地提交。默认输出 `artifacts/wisps/ShowMeWisp-debug-win-x64.zip` 及 `.sha256`；加 `--include-unique` 输出 `artifacts/unique/GameHelper-unique-debug-win-x64.zip` 及 `.sha256`。提供完整包，单独更新插件 DLL 目前不够。当前源码同步目标为 `mxc1868/Gamehelper` 的 main；用户现自行在 Windows 编译，未经新请求不执行上述打包/Release 步骤。将来需要打包时建议使用干净提交/临时 worktree，避免混入其他尚未提交的评估文档。

原仓库的 `scripts/sync-gordin.ps1` 使用覆盖式同步，其他维护/启动器脚本也可能仍指向上游。不要直接运行它们更新当前分支或发布本 fork；先检查目标和差异，避免丢掉新增 API 或拿错编译包。
