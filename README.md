# GameHelper — WhereTheWispsAt / UniqueLoot 调试分支

当前仓库：**[mxc1868/Gamehelper](https://github.com/mxc1868/Gamehelper)**，分支 `main`，包含幽火和 UniqueLoot 插件。此 fork 已恢复并保留上游更新；Windows / 当前 PoE2 实机验证仍待完成。

- **[完整 Windows x64 包（含 UniqueLoot、幽火和 Radar）](https://github.com/mxc1868/Gamehelper/releases/download/unique-debug-2026-09-23/GameHelper-unique-debug-win-x64.zip)**，内含 .NET 10 运行时，无需编译环境。
- [Release 与 SHA-256 校验文件](https://github.com/mxc1868/Gamehelper/releases/tag/unique-debug-2026-09-23)。
- [UniqueLoot 使用说明](Plugins/UniqueLoot/README.md)；仅幽火的构建输出为 `artifacts/wisps/WhereTheWispsAt-debug-win-x64.zip`。
- [Windows 启动与实机调试](Plugins/WhereTheWispsAt/WINDOWS-DEBUG.zh-CN.md)
- **[TODO / 后续 agent 接手上下文](TODO.md)**

完整解压到新目录，右键 `Start-Debug.cmd`，以管理员身份运行；F12 启用 `WhereTheWispsAt`。本包包含修改后的核心，不能只把插件 DLL 放进原版 GameHelper。录制 60 秒会同时对比原有 API 与新增扫描，帮助决定正式版能否只维护插件。

Linux 构建：`python3 scripts/package-wisps.py`。完整 ZIP 和 checksum 发布到本 fork 的 GitHub Releases；下面保留的上游安装器、更新器和发布脚本说明属于原项目，不用于下载或发布这里的调试包。

新增 **[UniqueLoot 暗金掉落识别](Plugins/UniqueLoot/README.md)**：根据完整 asset 路径显示未鉴定暗金的名称候选，独立于价格数据。内置 PoE2 映射包含 446 条路径、441 个名称；已通过 Linux 编译和 25 项离线检查，仍待实机验证。

构建完整测试包：`python3 scripts/package-wisps.py --include-unique`，输出 `artifacts/unique/GameHelper-unique-debug-win-x64.zip` 及 SHA-256 文件。GitHub 发布状态见 [TODO](TODO.md)。

原项目与作者信息保留如下。

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
