# WhereTheWispsAt for GameHelper

GameHelper 原生幽火插件，功能参考 [exCore2/WhereTheWispsAt 的 PoE2 分支](https://github.com/exCore2/WhereTheWispsAt/tree/49d26cd31a282de388cf796582e21d807f0144ff)，按本仓库的插件接口重新实现。构建和运行不依赖 ExileCore2、其 Loader 或授权服务。

## 当前功能

- 蓝、黄、紫、神圣幽火的大地图标记，以及可选的小地图和地面标记。
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
dotnet build Plugins/WhereTheWispsAt/WhereTheWispsAt.csproj -c Release
```

插件及语言文件会自动复制到 `GameHelper/bin/Release/net10.0-windows/win-x64/Plugins/WhereTheWispsAt/`。运行同一构建目录中的 GameHelper，在插件管理中启用 `WhereTheWispsAt`。也可以使用已包含本插件的 `GameOverlay.sln` 和仓库 `scripts/build.ps1` 构建整套程序。

Linux 打包（需要 Python 3 和 .NET 10 SDK，首次还原需要访问 NuGet）：

```bash
python3 scripts/package-wisps.py
```

输出 `artifacts/wisps/WhereTheWispsAt-debug-win-x64.zip` 和 SHA-256 校验文件。包内含配套核心、WhereTheWispsAt、Radar、字体/语言资源和 Windows x64 .NET 运行时；在独立目录解压后运行。具体操作见 [Windows 调试说明](WINDOWS-DEBUG.zh-CN.md)。打包脚本会检查运行时配置、必要文件、x64 PE 和 ZIP 完整性，但不在 Linux 模拟 Windows 实机运行。

设置保存在插件目录的 `config/settings.txt`。默认开启大地图、连线、事件文字和附近宝箱框；小地图与幽火地面标记可单独开启。默认每 500 毫秒采集一次；慢采集会自动延长间隔，仍需在实际场景观察耗时。

## 数据读取与限制

插件通过新增的 `AreaInstance.ScanEntities(EntityScanSource.Awake, ...)` 遍历当前 awake 实体树，不受默认视觉 ID 过滤影响。先按 metadata 过滤，再为目标建立新的实体与组件对象；不会修改全局 `ProcessAllRenderableEntities` 设置，也不会沿用停更的组件缓存。定时采集会重新尝试读取首次未就绪的 Animated 路径。

颜色依赖 Animated 的子实体路径：`_primal`、`_warden`、`_vodoo`、`_sacred`。缺失或未知路径显示灰色 `?`，不会根据名字猜测颜色。ModelPath 只进入诊断样本，尚未用作替代分类依据。

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
dotnet run --project tests/WhereTheWispsAt.Tests/WhereTheWispsAt.Tests.csproj -c Release
```

此测试入口直接编译分类、连线、投影、设置规范化和诊断文件代码，可在 Linux 或 Windows 运行；不加载游戏、图形库或内存访问库。通过这些检查不代表当前游戏 offsets 或绘制效果已经实机验证。
