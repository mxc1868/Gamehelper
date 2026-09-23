# UniqueLoot：PoE2 未鉴定暗金掉落提示

根据地面物品本身的 `RenderItem.ResourcePath` 匹配完整图标 asset 路径，显示暗金名称。无需鉴定，无需联网查询价格。物品旁文字和屏幕列表可分别关闭；列表按距离排序。此版本是 **Linux 编译通过、尚待 Windows / 当前游戏实测的原型**。

## Windows 使用

1. 从 [本仓库 Releases](https://github.com/mxc1868/Gamehelper/releases) 下载 `GameHelper-unique-debug-win-x64.zip` 和 `.sha256`，将整个 ZIP 解压到新的可写目录。包内包含 GameHelper 核心、UniqueLoot、WhereTheWispsAt、Radar 和 Windows x64 .NET 10 运行时，不需要编译器或另外安装 .NET。
2. 关闭旧 GameHelper，右键 `Start-Debug.cmd`，以管理员身份运行。权限要求来自原有 `app.manifest`。此包直接运行配套核心，不使用指向上游的更新启动器。
3. F12 打开插件管理，启用 `UniqueLoot`。默认显示物品旁名称及左侧掉落列表；在插件设置中调整位置、最多数量或显示开关。设置界面支持中文/英文，内置物品名称为英文。
4. 在战斗区域观察暗金掉落。只有一个映射名称时显示名称；共用贴图时显示“可能为：A / B”；表中未收录时显示“未知暗金”；贴图读取失败另行标明。城镇和藏身处不扫描。

不能只替换 DLL：此实现新增核心 `WorldItem.TryReadItem` 接口。包内保留此前幽火相关核心改动；这不代表原版 GameHelper 已经提供该接口。

## 识别边界

- 本功能只判断暗金名称候选，不读取鉴定后的随机词缀、数值或价格。原有 `Mods` 未公开可靠的 `Identified` 字段，本插件不新增猜测 offsets，也不按鉴定状态过滤，所以已经鉴定后重新丢地的暗金也会显示。
- 内置映射来自 PoE2 的固定数据导出，版本及原理见 [数据来源](Data/SOURCES.md)。同贴图、改名记录仍可能存在多个候选；单候选表示当前表只有一项，不等于未来版本不会共用该贴图。
- 通过已有 `AreaInstance.AwakeEntities` 扫描，原有过滤和读取失败仍可能漏掉实体。没有遍历游戏 UI 标签，文字锚定在地面投影位置；不会跟随游戏标签堆叠，也不会照搬物品过滤器的显示/隐藏规则。
- 每次扫描替换列表；拾取或离开读取范围的掉落在下一次扫描后消失。切区、禁用、关闭游戏时清理状态。默认每 500 毫秒扫描，最多检查 20,000 个实体或累计约 100 毫秒，超过会报告截断；单次底层内存读取的耗时不受此时间检查控制。

## 实机验证和报告

找一件已知暗金，在战斗区域放到地上测试名称和位置，然后用未鉴定暗金验证；再测试拾取、重新掉落、移动、切图和重新启用。拾取未鉴定物品后可自行鉴定，对比真实名称与候选。不要将离线测试当作这一验证已经完成。

插件设置显示地面物品数、暗金数、唯一/多候选/未知数、读取失败、扫描耗时与截断状态。在目标仍在地面附近时点击“导出最近一次扫描”，发送：

- `Plugins/UniqueLoot/diagnostics/latest-scan.json`（覆盖式单文件，最多 100 个暗金样本，包含完整 asset、候选、位置和读取计数）；
- `build-manifest.json`、`host-stdout.log`、`host-stderr.log`；
- 当前游戏版本、实际物品名称和观察结果。

若扫描显示 0，先看地面物品与读取失败计数。零结果不能证明没有掉落。报告保留 `ProcessAllRenderableEntities` 设置值供判断，本插件不会自动修改全局设置。

## 自定义映射

可在 `Plugins/UniqueLoot/uniqueArtMapping.json` 加入精确路径映射，然后点“重新加载映射”。它逐键覆盖内置表；空数组可禁用过期映射；非法文件会保留上一次可用表并显示错误。首次加载自定义文件失败时使用内置表。

```json
{
  "Art/2DItems/Armours/BodyArmours/Uniques/Bramblejack.dds": ["Bramblejack"]
}
```

名称可改成中文。共用贴图必须保留所有已知候选，不使用同文件名、模糊匹配或价格高低来猜名称。

## Linux 构建

```bash
dotnet run --project tests/UniqueLoot.Tests/UniqueLoot.Tests.csproj -c Release
dotnet run --project tests/WhereTheWispsAt.Tests/WhereTheWispsAt.Tests.csproj -c Release
python3 scripts/package-wisps.py --include-unique
```

输出 `artifacts/unique/GameHelper-unique-debug-win-x64.zip` 和 `.sha256`。脚本只构建，不发布；发布目标必须是 `mxc1868/Gamehelper`。离线映射检查与内存读数/Windows 绘制验证分别记录。
