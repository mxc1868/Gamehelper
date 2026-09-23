# UniqueLoot：PoE2 未鉴定暗金掉落提示

根据地面物品本身的 `RenderItem.ResourcePath` 匹配完整图标 asset 路径，显示暗金名称。无需鉴定，无需联网查询价格。物品旁文字和屏幕列表可分别关闭；重要掉落优先，其余按距离排序。此版本是 **Linux 编译通过、尚待 Windows / 当前游戏实测的原型**。

## Windows 构建与使用

用户已确认自行在 Windows 编译。安装 .NET 10 SDK，在仓库根目录执行：

```powershell
git pull --ff-only
dotnet build Plugins/UniqueLoot/UniqueLoot.csproj -c Release
dotnet build Plugins/WhereTheWispsAt/WhereTheWispsAt.csproj -c Release
```

运行 `GameHelper/bin/Release/net10.0-windows/win-x64/GameHelper.exe`，接受原有 manifest 的管理员权限提示，F12 启用 `UniqueLoot`。默认显示物品旁名称及左侧掉落列表；设置界面支持中文/英文，内置物品名称为英文。

项目同时编译引用的配套核心，并把插件 DLL、语言资源和默认高亮配置复制到输出目录。不能只换 DLL 到原版核心：本实现新增 `WorldItem.TryReadItem` 接口。此前的初版测试 ZIP 不含本次高亮修改；当前按用户要求只提交源码，不制作新版测试包。

在战斗区域观察暗金掉落。单候选显示名称，共用贴图显示“可能为：A / B”，未收录显示“未知暗金”，贴图读取失败另行标明。城镇和藏身处不扫描。

## 默认高亮

- **Headhunter：金色**；**Mageblood：紫红色**。两者在地面文字和屏幕列表都带 `[!]`、描边、深色背景和 1.3 倍字号，并优先占用显示名额。
- 默认规则文件为 `Plugins/UniqueLoot/highlights.default.json`；首次启用会在运行目录生成可编辑的 `Plugins/UniqueLoot/config/highlights.json`；已有规则不会被自动覆盖。
- 修改 `config/highlights.json` 后，在设置里点“重新加载高亮配置”。可改每条规则的 `Enabled`、完整 `AssetPath`、`#RRGGBB` / `#RRGGBBAA` 颜色和 `FontScale`（1–2 倍），也可通过“高亮重要掉落”统一开关。
- 高亮依据两个物品的完整 asset 路径，已核对 PoE2DB 及内置 PoE2 导出。不会把相同底材的所有腰带高亮，规则名称也不会覆盖识别出的名称/候选。

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

输出 `artifacts/unique/GameHelper-unique-debug-win-x64.zip` 和 `.sha256`。脚本只构建，不发布；用户已恢复并授权同步到 `mxc1868/Gamehelper` 的 `main`。用户目前自行在 Windows 编译，除非再次要求，不执行打包或发布 Release。离线映射检查与内存读数/Windows 绘制验证分别记录。
