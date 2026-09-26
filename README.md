# GameHelper — ShowMeWisp / UniqueLoot / Follower

当前仓库：**[mxc1868/Gamehelper](https://github.com/mxc1868/Gamehelper)**，分支 `main`。基于上游 `v1.5.11` 标签的 `0d11fe7`，保留幽火与 UniqueLoot 补丁；核心和启动器版本统一为 1.5.11。Windows 编译和插件加载已验证，当前 PoE2 游戏读取与绘制仍待实测。

- **UniqueLoot**：按完整 asset 路径识别未鉴定暗金，默认猎首金色高亮、魔血紫红色高亮；可编辑 `Plugins/UniqueLoot/highlights.default.json`。详见 [插件说明](Plugins/UniqueLoot/README.md)。
- **Sacred Wisp**：默认橙色；旧配置的默认白色会迁移为橙色。详见 [幽火说明](Plugins/ShowMeWisp/README.md)。
- **ShowMeWisp**（原名 WhereTheWispsAt）：按幽火自身的 Small / Medium / Big 大小缩放方框，默认地图边长 3 / 5 / 8 像素；支持三档倍率调整，并继承旧设置和启用状态。
- **[TODO / 后续 agent 接手上下文](TODO.md)**。
- **Follower**：扫描附近玩家并下拉选择队长，前台 WASD 跟随与地形寻路，默认只预览；F8 启停、Esc 停止。适用于另一台电脑/虚拟机的小号前台窗口。尚未实测移动，详见 [使用与限制](Plugins/Follower/README.md)。

用户现已在 Windows 自行编译，后续按要求提交源码到 `main`，不再自动制作测试包。安装 .NET 10 SDK 后，在仓库根目录执行：

```powershell
git pull --ff-only
dotnet build GameOverlay.sln -c Release
```

运行 `GameHelper/bin/Release/net10.0-windows/win-x64/GameHelper.exe`，F12 启用插件。两个项目均引用配套核心；插件和语言文件会复制到该目录的 `Plugins` 下。首次启用 UniqueLoot 会生成可编辑的 `Plugins/UniqueLoot/config/highlights.json`；修改后点击“重新加载高亮配置”。

**本 fork 的启动器已跳过在线二进制更新**，后续使用源码更新与编译。上游原版 1.5.11 缺少本 fork 新增的 `MapProjection`、实体扫描接口和 `WorldItem.TryReadItem`；覆盖核心会导致幽火在加载阶段消失、UniqueLoot 扫描接口不可用。已有 `Test` 目录可用 `rebuild-test.ps1` 重建，需一起更新启动器、核心与插件，保留自己的配置。

Windows 验证：整套 Release 编译成功，UniqueLoot 40 项、ShowMeWisp 98 项离线检查通过。[插件加载检查](tests/PluginLoad.Tests/Program.cs) 调用实际 `PManager` 加载器，检查新旧插件去重与核心接口：

```powershell
dotnet run --project tests/PluginLoad.Tests/PluginLoad.Tests.csproj -c Release -- GameHelper/bin/Release/net10.0-windows/win-x64
```

11 项发现/加载/接口检查通过（含 Follower）；不启动 overlay、不启用插件、不读取游戏或改写设置。此前的 [初版测试 Release](https://github.com/mxc1868/Gamehelper/releases/tag/unique-debug-2026-09-23) 保留，但不包含改名、尺寸和 Follower 功能，请从 `main` 编译当前版本。

原项目与作者信息保留如下；其中上游下载与自动更新说明不适用于本 fork 的配套构建。

---

# GameHelper

GameHelper is a Windows x64 .NET overlay for **Path of Exile 2** with a plugin architecture. The launcher (`GameHelper.exe`) checks for updates, starts the overlay (`GameHelper.App.exe`), reads data from the running game process, and loads plugins from the `Plugins` folder.

**Open-source fork maintained by** [MordWraith](https://github.com/MordWraith) — basis **Lafko / Gordin** ([GameHelper2](https://github.com/MordWraith/Gamehelper)).

## Source + binaries (same project)

| Channel | For whom | Link |
|---------|----------|------|
| **`main` branch** | Developers, auditors, no-auto-update users | https://github.com/MordWraith/Gamehelper |
| **Releases** | Players (installer / ZIP) | https://github.com/MordWraith/Gamehelper/releases |
| **Auto-update** | Optional convenience (signed ZIP from Releases) | Built into `GameHelper.exe` |

- **Do not want auto-update?** Use the [full ZIP](https://github.com/MordWraith/Gamehelper/releases/latest) or [build from source](#build-from-source).
- **Trust / security:** [SECURITY.md](SECURITY.md) — signed manifests, what gets updated, what stays local.
- **Windows Defender blocked GameHelper?** See [SECURITY.md → false positives](SECURITY.md#windows-defender-and-antivirus-false-positives) — common with auto-update and unsigned DLLs; usually not a real trojan.
- **Attribution:** [CREDITS.md](CREDITS.md) and in-app **Plugins → Author** column.

## Download (players)

| What | Link |
|------|------|
| **Installer (recommended)** | https://github.com/MordWraith/Gamehelper/releases/latest/download/GameHelperDownloader.exe |
| Full ZIP (manual install) | https://github.com/MordWraith/Gamehelper/releases/latest/download/GameHelper-*-full.zip |
| All releases | https://github.com/MordWraith/Gamehelper/releases/latest |

Run `GameHelperDownloader.exe` in an **empty folder**. Requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### Release assets

| Asset | Purpose |
|-------|---------|
| `GameHelperDownloader.exe` | One-file fresh install |
| `GameHelper-*-full.zip` | Full install / auto-update package |
| `manifest.json` + `manifest.sig` | Signed update metadata (SHA256 per file) |
| `changelog-history.json` | In-app changelog history |

User settings (`configs/`, `Plugins/*/config/`) are **not** included in update packages and are **not** overwritten by auto-update.

## Build from source

### Required tools

- [Visual Studio](https://visualstudio.microsoft.com/downloads/) with **.NET desktop development**
- [.NET 10 SDK for Windows x64](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
git clone https://github.com/MordWraith/Gamehelper
cd Gamehelper
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

Output: `publish\` (run `GameHelper.exe` from there).

Open [`GameOverlay.sln`](GameOverlay.sln) for IDE development — not a single `.csproj` only.

### Maintainer: publish

```powershell
# Release-Binaries hochladen (Quellcode wird NICHT automatisch committed):
powershell -ExecutionPolicy Bypass -File rebuild-and-publish.ps1

# Optional: alten Auto-Push-Quellcode-Flow (nicht empfohlen):
powershell -ExecutionPolicy Bypass -File rebuild-and-publish.ps1 -PushSource

# Quellcode manuell committen (bevorzugt): normales git add/commit/push mit Messages wie [Core] ...
```

Source commits should be descriptive (`[Core] …`, `[Radar] …`), not bulk `Release vX source` snapshots.

## Project layout

| Path | Role |
|------|------|
| `GameHelper/` | Overlay (`GameHelper.App.exe`) |
| `Launcher/` | Launcher & updater (`GameHelper.exe`) |
| `Downloader/` | Standalone installer EXE |
| `GameOffsets/` | Game structure offsets |
| `Plugins/` | Atlas, Radar, AutoPot, … |
| `scripts/` | Build & publish automation |
| `CREDITS.md` | Attribution |
| `SECURITY.md` | Auto-update & trust |

Target: `net10.0-windows`, `win-x64`. License: [GPLv3](LICENSE).

## Solution projects

- `GameHelper` — main overlay
- `Launcher` — updater entry point
- `Downloader` — public installer
- `GameOffsets` — offsets
- Plugins: `Atlas`, `AuraTracker`, `AutoPot`, `AutoHotKeyTrigger`, `HealthBars`, `SimpleBars`, `MapKillCounter`, `PreloadAlert`, `Radar`, `RitualHelper`, `RuneforgeHelper`, `SekhemaHelper`, `PlayerBuffBar`, `AmanamuVoidAlert`, …

## Run

1. Open `publish\` or `GameHelper\bin\Release\net10.0-windows\win-x64\`
2. Start **`GameHelper.exe`** (not `GameHelper.App.exe` directly)
3. Match admin elevation with the game if needed

## Runtime data (not in git)

```
configs\core_settings.json
configs\plugins.json
Plugins\<Name>\config\
```

## Troubleshooting

**`net10.0-windows` not supported** — Install .NET 10 SDK and update Visual Studio.

**Plugins missing after build** — Use **Rebuild Solution**, not Build Project.

**Update pulled wrong version** — Install into an **empty** folder; check `VERSION.txt`.

**Overlay does not attach** — Match admin elevation with the game.

**Windows Defender / helper won't start** — Open Protection history, allow blocked `GameHelper` / `GameHelperUpdate` entries, or install from the [full ZIP](https://github.com/MordWraith/Gamehelper/releases/latest) into a new folder. Details: [SECURITY.md](SECURITY.md#windows-defender-and-antivirus-false-positives).

## Links

- [Upstream GameHelper2](https://github.com/MordWraith/Gamehelper)
- [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0)
