# ShowMeWisp for GameHelper

GameHelper 原生幽火插件，功能参考 [exCore2/WhereTheWispsAt 的 PoE2 分支](https://github.com/exCore2/WhereTheWispsAt/tree/49d26cd31a282de388cf796582e21d807f0144ff)，按本仓库的插件接口重新实现。构建和运行不依赖 ExileCore2、其 Loader 或授权服务。

本插件原名 `WhereTheWispsAt`，现整体改名为 **ShowMeWisp**（项目、DLL、命名空间、设置类和插件列表名称）。新配置目录为 `Plugins/ShowMeWisp/config`；首次读取时可继承相邻旧目录的 `config/settings.txt`，已有新配置优先。配套核心继承旧插件启用状态，检测到新 DLL 时跳过旧插件目录，避免重复加载；旧配置和诊断不删除。

## 当前功能

- 蓝、黄、紫、神圣幽火的大地图标记，以及可选的小地图和地面标记。
- Small / Medium / Big 幽火使用不同大小的方框，大地图、小地图与开启后的地面框共用三档倍率。
- 同色、连续实体 ID 且距离小于阈值的连线；这是局部轨迹提示，不是完整寻路。
- 照明弹、燃料补给、井、祭坛、转换器、商人、宝箱和事件标签。
- 已确认开启的宝箱、已确认激活的井/祭坛/转换器隐藏。
- 切图、离开游戏和禁用插件时清理；采集结果完全替换旧结果，不保留已消失实体。
- 中英文设置、颜色与位置校正、可选双人地图中心、样本导出。
- 原有公开集合、公开构造函数重读组件、新增实体扫描三路对照日志，用于实测是否能去掉核心 API 依赖；尚未取得实机结论。

尚未验证或未提供：当前 PoE2 版本的实机数据与显示对齐；燃料百分比；宝箱精确旋转。宝箱地面框使用现有 Render 边界绘制轴对齐框。神圣幽火沿用参考插件的路径分类规则，仍需要实机样本确认。

## 构建与启用

构建机需要 .NET 10 SDK；运行环境为 Windows x64。Windows 使用者可直接运行 Linux 生成的独立包，无需安装编译环境。**需要同时更新这次修改后的 GameHelper 核心**，因为插件使用了新加入的定向实体采集接口。

在仓库根目录构建：

```powershell
dotnet build Plugins/ShowMeWisp/ShowMeWisp.csproj -c Release
```

插件及语言文件会自动复制到 `GameHelper/bin/Release/net10.0-windows/win-x64/Plugins/ShowMeWisp/`。运行同一构建目录中的 GameHelper，在插件管理中启用 `ShowMeWisp`。也可以使用已包含本插件的 `GameOverlay.sln` 和仓库 `scripts/build.ps1` 构建整套程序。

当前基于上游 `v1.5.11` / `0d11fe7` 加本 fork 补丁。该上游提交此前已合并，但其源码版本号仍为 1.5.10；本 fork 已将核心和启动器统一为 1.5.11。首次更新此修复请构建整个 `GameOverlay.sln`，确保启动器也更新：新版启动器跳过在线更新，防止上游原版 DLL 覆盖新增接口。仅凭核心版本号不能判断是否包含补丁。

若插件不出现在列表中，可以从仓库根目录执行 `dotnet run --project tests/PluginLoad.Tests/PluginLoad.Tests.csproj -c Release -- Test` 检查实际部署目录。上游原版核心已复现 `Could not load type 'GameHelper.Utils.MapProjection'`；配套 1.5.11 核心通过实际 `PManager` 加载和实例化检查。此检查不启动图形界面，也不代表游戏内功能已验证。

Linux 打包（需要 Python 3 和 .NET 10 SDK，首次还原需要访问 NuGet）：

```bash
python3 scripts/package-wisps.py
```

输出 `artifacts/wisps/ShowMeWisp-debug-win-x64.zip` 和 SHA-256 校验文件。包内含配套核心、ShowMeWisp、Radar、字体/语言资源和 Windows x64 .NET 运行时；在独立目录解压后运行。具体操作见 [Windows 调试说明](WINDOWS-DEBUG.zh-CN.md)。打包脚本会检查运行时配置、必要文件、x64 PE 和 ZIP 完整性，但不在 Linux 模拟 Windows 实机运行。

设置保存在插件目录的 `config/settings.txt`。默认开启大地图、连线、事件文字和附近宝箱框；小地图与幽火地面标记可单独开启。默认每 500 毫秒采集一次；慢采集会自动延长间隔，仍需在实际场景观察耗时。

## 数据读取与限制

### 幽火方框大小

当前 fork 原先只有统一的 `MarkerSize` / `GroundWidth` / `GroundHeight`。本次从用户保留的旧 Windows 插件源码和交接记录恢复模型规则：只识别 `Metadata/Effects/Spells/monsters_effects/League_Azmeri/resources/wisp_doodads/wisp_` 下四种颜色的 `.ao` 模型，`_sml` / `_med` / `_big` 对应 Small / Medium / Big。无后缀保持 Unknown 和基础尺寸；其他目录、未知颜色、未知后缀不猜测大小。旧记录报告蓝/黄三档实机样本，紫/神圣仍仅有规则与合成测试；本次未在当前游戏重新验证。

| 幽火大小 | 默认倍率 | 默认地图边长 | 默认地面框（宽 × 深 × 高） |
| --- | --- | --- | --- |
| Small | 0.6 | 3 像素 | 18 × 18 × 6 |
| Medium | 1.0 | 5 像素 | 30 × 30 × 10 |
| Big / Large | 1.6 | 8 像素 | 48 × 48 × 16 |
| 未知 | 1.0 | 5 像素 | 30 × 30 × 10 |

可在设置中关闭“按幽火大小缩放方框”，或调整 Small / Medium / Big 各自的倍率。基础地图尺寸或地面宽高的自定义值继续生效。宝箱、补给和其他事件沿用原尺寸。大小计数、诊断报告、绘制样本和三路 API 对照均包含大小证据。以上路径规则通过合成测试；当前游戏实际路径与视觉尺寸对应关系仍需现场样本验证。

### 实体读取

插件通过新增的 `AreaInstance.ScanEntities(EntityScanSource.Awake, ...)` 遍历当前 awake 实体树，不受默认视觉 ID 过滤影响。先按 metadata 过滤，再为目标建立新的实体与组件对象；不会修改全局 `ProcessAllRenderableEntities` 设置，也不会沿用停更的组件缓存。定时采集会重新尝试读取首次未就绪的 Animated 路径。

Sacred Wisp（神圣幽火）默认颜色为橙色（RGBA `1, 0.5, 0, 1`）。旧配置中的默认白色在首次加载新版时迁移为橙色，其他自定义颜色保留；迁移后仍可在设置中调整。

颜色优先读取 Animated 子实体路径的 `_primal`、`_warden`、`_vodoo`、`_sacred`；共享 `AzmeriResourceBase` 无颜色时，使用上述严格限定的 ModelPath 家族。未知路径显示灰色 `?`。颜色与大小分别记录，不从颜色推断大小。

只展示本次读到的有效目标，不推断未加载区域，不自动扫描 sleeping 实体树；只有明确点击一次性诊断按钮才进行 awake / sleeping 对比，探测结果不进入渲染。若游戏将目标移至其他实体源，需要先用实际样本验证再扩展。读取失败导致目标暂时消失时，不将其记录为已采集。对象状态读取不可用时不会假定其已使用，设置页面会统计这类样本。

地图投影与 Radar 共用无状态的系数实现，保持 Radar 原有公式。插件使用自身投影实例与设置，两个插件的缩放不会相互覆盖。地面框使用各目标自己的高度。

## Windows 实机验收

1. 进入有幽火的区域，打开大地图，检查四种颜色和灰色未知标记；比较实际目标数和匹配数。
2. 分别缩放、拖动地图、改变窗口尺寸，检查标记位置；需要时调整插件的地图校正值。
3. 采集幽火、开箱、激活井或祭坛，检查下一次采集后相应标记消失；离开范围后不应留下伪造的历史连线。
4. 切图、回到同一区域、关闭游戏、禁用/重新启用插件，检查没有跨场景残留。
5. 在问题现场点击“录制 60 秒”，必要时进行一次性实体源对比，结束后取插件 `diagnostics/` 目录中的 JSON / JSONL。报告包含成功与失败阶段样本、组件信息、位置、状态、渲染条件、程序集身份和设置，最多 2,000 个当前目标。具体排查步骤和计数解释见 [Windows 调试说明](WINDOWS-DEBUG.zh-CN.md)。

燃料接口的后续实现需要当前游戏版本中进入、消耗、补充和耗尽时的数据。当前版本不会显示估算百分比。

## 不依赖游戏的验证

```powershell
dotnet run --project tests/ShowMeWisp.Tests/ShowMeWisp.Tests.csproj -c Release
```

此测试入口直接编译分类、连线、投影、设置规范化和诊断文件代码，可在 Linux 或 Windows 运行；不加载游戏、图形库或内存访问库。通过这些检查不代表当前游戏 offsets 或绘制效果已经实机验证。
