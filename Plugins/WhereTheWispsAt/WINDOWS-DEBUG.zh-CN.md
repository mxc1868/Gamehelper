# Windows 实机调试

## 当前 Windows 源码构建

在仓库根目录编译 `dotnet build GameOverlay.sln -c Release`，运行 `GameHelper/bin/Release/net10.0-windows/win-x64/GameHelper.exe`，F12 查找 `WhereTheWispsAt`。核心和启动器现为 1.5.11，保留本 fork 新增 API；启动器跳过在线二进制更新，后续更新请拉取源码后重新编译。已有 `Test` 安装可用 `rebuild-test.ps1` 重建，需关闭该目录运行中的程序。

已在 Windows 通过加载检查：`dotnet run --project tests/PluginLoad.Tests/PluginLoad.Tests.csproj -c Release -- Test`（将最后的目录改为自己的部署目录）。它调用实际插件加载器并检查核心依赖，不启用插件或读取游戏。原版上游 1.5.11 缺少 `MapProjection`，会使幽火在进入列表前被跳过；版本号相同也需要核对配套核心。

## 历史 Linux 自包含调试包

此包是可编译原型的实机验证包，尚未在 Windows / 当前 PoE2 中验证读取和显示。包含 Windows x64 的 .NET 10 运行时；这台 Windows 机器无需安装 SDK、Visual Studio 或单独安装 .NET。运行仍依赖 Windows 与其图形环境。

## 启动

1. 将整个 ZIP 传到 Windows，完整解压到一个新的可写目录，例如 `D:\Tools\WispsDebug`。不要只复制插件 DLL：本包使用了修改后的 GameHelper 核心。保留所有 DLL、运行时、字体和语言文件。
2. 关闭其他 GameHelper 实例，右键 `Start-Debug.cmd`，选择“以管理员身份运行”。这是现有核心 `app.manifest` 的启动要求。脚本使用 Windows PowerShell 启动本目录的 `GameHelper.exe`，并把原有框架的输出保存为 `host-stdout.log`、`host-stderr.log`。也可以直接运行 EXE 并接受其权限提示，但启动排错时建议使用脚本。
3. 默认按 F12 打开设置，在插件管理中启用 `WhereTheWispsAt`，再进入它的设置页。包中另有 Radar，可选启用以对比地图显示。首次配置独立于原安装；不要把整套旧配置覆盖进来。

本包没有更新启动器；直接启动配套核心。上游更新可能覆盖这里新增的 API，因此实机调试期间请使用完整的同一构建包。`build-manifest.json` 保存基础提交、源文件和包内文件 SHA-256，用于核对版本。

## 录制一次问题现场

1. 进入确实能在游戏里看到幽火、井或宝箱的区域。设置页填写游戏版本和简短场景备注，点击“录制 60 秒”。
2. 返回游戏，打开大地图，先停留几秒，再移动、缩放或拖动地图；随后采集一组幽火、开箱或激活一个目标。记录实际看到的行为。诊断期间切到设置页仍会采集有效区域的数据，绘制继续遵循“失焦隐藏”等设置。
3. 如果始终匹配 0 个目标，在目标仍在附近时点击“一次性对比 awake / sleeping”。默认关键字 `Azmeri`，可按已有路径证据修改。它依次探测两个实体源，可能造成短暂停顿；报告带有各自时间和区域，不是原子快照，也不会把 sleeping 对象加入显示。区域或玩家数据无效时会显示等待。
4. 等待自动停止并导出，或点击“停止并导出”。如已关闭插件，会先导出再清空显示。不要在收集完日志后立即反复启动新录制，以免轮换覆盖现场。
5. 把 `Plugins\WhereTheWispsAt\diagnostics` 整个目录打包，连同 `build-manifest.json`、两份 host 日志和“游戏实际发生了什么”的文字说明交回分析。每次启动调试脚本会覆盖旧 host 日志，请先保留问题现场。

录制文件是 `capture-0.jsonl`（最新）至 `capture-2.jsonl`，每个最多 2 MiB；事件含 UTC 时间和 Session。每次扫描记阶段计数，每两秒记心跳和最近渲染样本；渲染诊断最多每 500 ms 采样一次。超大单条记录会留下 `record_omitted` 提示。

`latest-report.json` 是最近导出的报告，`previous-report.json` 是上一次。包含最多 2,000 个当前目标、每阶段最多 4 个扫描样本、最近 64 条变化事件、最近扫描/实体源探测报告、设置和加载的程序集版本/MVID。投影阶段每项最多 8 个样本。切图后保留旧报告及其时间、区域标签，当前显示单独清空。

60 秒计时由框架中的定时协程驱动，每 250 ms 检查一次截止时间，即使 F9 暂停插件 DrawUI 也能停止录制。若整个框架卡死或进程崩溃，需依赖此前已刷入文件的日志；同步实体扫描运行期间，停止会延迟至框架恢复执行。

## 怎样根据证据定位

### 验证是否能去掉新增核心 API

之前只有新扫描自身的日志和 awake / sleeping 对比，无法直接回答原有 API 是否足够。现在新增“三路 API 对照”，点击“录制 60 秒”即可自动记录，最短间隔 2 秒；也可点击“一次性对比原有 API / 新增扫描”后直接导出。

| 路线 | 具体读取方式 |
| --- | --- |
| `public_lookup` | 原有 `AreaInstance.AwakeEntities` + `TryGetComponent(..., shouldCache:false)`；存在缓存时仍读取缓存，没有缓存时不填入共享缓存 |
| `public_component_recreate` | 同一个公开集合，通过原有 API 找组件地址，再调用现有 public 构造函数重读 Animated / Render / Chest / StateMachine |
| `fresh_entity_scan` | 当前新增 `ScanEntities(Awake, ...)`，从实体树筛选并建立新实体 |

日志中的 `api_comparison` 和最终报告的 `ApiComparison` 包含这三路的阶段样本、目标数、未知颜色/状态数、耗时和两两差异。匹配键是 **实体 ID + 地址 + metadata**，不会把复用的 ID 当成同一实体。差异包含是否缺失、路径、分类、组件地址/父指针、原始状态与坐标。

每路最多保留 2,000 个实体用于内存中对照，日志只导出每路前 8 个样本，以及每对路线每类最多 8 个差异/一致样本。`Truncated` 或 `Discarded` 时不能据此下覆盖率结论；`CompleteWindow` 只表示区域、数量上限和显式读取检查通过，不是游戏实测通过标志。两边均失败、没有目标、或者一致但颜色/状态仍未知，都不能说明原有 API 已够用。

报告记录 `ProcessAllRenderableEntities`，不会自动改这个全局设置。先用自己的原配置录制；若公开集合缺实体，可在 GH 原有设置里手动短时开启“处理所有可渲染实体”后再次录制，观察耗时并恢复原配置。该开关影响整个框架的处理范围。比较公开重读组件与新增扫描是否仍存在持续差异，再决定是否保留核心改动。

三路依次读取，游戏在此期间仍会变化；需要在静止场景以及采集/激活前后重复观察。坐标比较容差为 World 各轴 0.5、Grid 各轴 0.05、TerrainHeight 0.1、Bounds 各轴 0.1。不要把一次位置差异或消失直接判为缓存/offset 错误。metadata 关键字输入框仅用于 awake / sleeping 探测，不改变本对照采用的幽火候选规则。

### 其他故障

| 证据 | 优先检查 |
| --- | --- |
| 插件不出现在列表中，完全没有自己的日志 | host 日志中的缺 DLL、加载失败或类型解析异常；包是否完整 |
| 心跳在继续，DrawUI 调用次数不增加 | 框架是否通过 F9 暂停了插件绘制；插件是否仍启用 |
| `area_read_failed`、`map_head_unreadable`、`map_root_unreadable` | 进程/区域是否可读，实体树布局和当前游戏版本是否相符 |
| `DeclaredCount` 有值而 `entry_seen` 为 0 或明显少 | 树遍历是否提前中断、区域是否变化；两者不是严格一致性断言 |
| `path_read` 有值，`candidate` 为 0 | 是否在正确场景，metadata 规则或实体来源是否变化；查看过滤样本和一次性探测 |
| `entity_invalid`、`id_mismatch`、`entry_exception` | 读取期间实体是否变动、有效性标志或组件布局是否需要核对 |
| `animated_missing`、`animated_parent_invalid`、`animated_path_empty`、`classification_unknown` | 组件查找、Animated 子实体路径和颜色后缀；ModelPath 只作样本 |
| `render_missing`、`render_parent_invalid`、`position_nonfinite` | Render 组件读取与位置；导出的原始坐标和 bounds |
| `activation_state_unavailable` | StateMachine 实际名称和值；“activated=1”含义需由激活前后样本确认 |
| 有 `observation_ready`，屏幕没有标记 | Render gate、地图启用/可见状态、窗口大小、投影参数、`outside_clip` 和绘制提交计数 |
| `observation_changes` 中出现 Missing | 仅表示下次快照未再读到；不能单凭这一项认定已采集 |

组件底层部分读接口在失败时返回默认值，字段非空或数值有限也不能单独证明 offsets 正确。`marker_submitted` / `ground_box_submitted` 表示发出了绘制调用，并不证明像素已在正确位置出现。仍需用游戏画面和前后样本核对语义与投影。

## 改动范围

- 新插件 `Plugins/WhereTheWispsAt`。
- 核心 `AreaInstance` 新增可选 awake / sleeping 的定向扫描接口，以及阶段诊断对象。
- 核心新增 `MapProjection`，Radar 改为复用同一公式。
- 解决方案、回归检查和 Linux 打包脚本。

这次迁移没有修改 `GameOffsets` 中的 offset 数值。框架 API 编译可用与当前游戏内存布局正确是两件需要分别验证的事。
