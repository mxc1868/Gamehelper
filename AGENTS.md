# Agent handoff

Read [TODO.md](TODO.md) before changing this project. It records the user's objective, completed work, verification limits, and the next investigation.

- Current destination: `https://github.com/mxc1868/Gamehelper`, branch `main`. The user restored this fork on 2026-09-23 and explicitly authorized syncing local work to it. Preserve remote commits; never publish to the upstream repository.
- Active work includes the GameHelper ShowMeWisp migration and the requested PoE2 UniqueLoot asset-based drop identification. Earlier unrelated ExileCore reverse-engineering research was explicitly cleared; do not restart it without a new request.
- Bloodybot2 uses Chrome local Web configuration: General contains common settings and a required mode selection; Follow is currently the only mode and has its own settings tab. Skills and start/stop keys use click-to-record controls. Preserve the Bloodybot1 base stylesheet and stable/deferred datalist updates. Native settings only open Chrome / copy the URL. No Edge or Simulacrum.
- Bloodybot2 controller chat compatibility is automatic; the old chat-permission switch is retired. Accept its legacy config field without rewriting user files on load. Draw status/routes on every host frame, independently of the 75 ms observation/navigation cadence; rendering must not send input or refresh the watchdog.
- The user authorized integrating Follower into Bloodybot2 and retiring the standalone plugin. Navigation lives in `Plugins/Bloodybot2/Navigation`, combat remains independent in `Modules/Combat`. Preserve legacy settings/enabled-state migration and loader deduplication, preview, key-release protections, shared WASD/P2 correction and unstuck priority. Never delete old Follower configuration files; monitoring P1/P2 does not route skill keys, the user's mapping does.
- ShowMeWisp is the renamed WhereTheWispsAt plugin. Preserve legacy settings/enabled-state migration and loader deduplication. Small/Medium/Big box scaling applies to wisps, not chests; live size-path validation is still pending.
- Wisp size uses the saved Windows implementation's resource ModelPath family (`sml`/`med`/`big`). UniqueLoot now has persisted checkbox selection, highlighted-only drop lists and bundled optional item icons; preserve existing highlight styles when toggling selections. Radar layout changes were canceled after the user confirmed resizing the window fixes visibility.
- This is a compilable prototype with diagnostics, not a Windows/game-validated release. Describe framework additions separately from original public APIs.
- The user now builds on Windows (confirmed 2026-09-23). Commit and push completed source changes to this fork's `main`; do not create more test ZIPs or Releases unless requested. The focused Linux packaging script remains available for later use.
- Updated user instruction (2026-09-28): before every commit, rebuild and update the `Test` runtime/plugins using `rebuild-test.ps1` so the user does not have to compile it manually. This explicitly replaces the earlier source-only restriction. Complete relevant tests and a successful Test build/deployment before committing. Preserve configuration backups and running-process/file-lock checks; never force-kill GameHelper or delete/overwrite settings to bypass a lock. If deployment is blocked, finish reviewable source/build work and report the concrete blocker before asking the user to close the running instance. No ZIPs/Releases unless requested. UniqueLoot ground names default to selected highlights only; preserve the opt-out.
- The API comparison logger is implemented. The question of whether the final plugin can use an unchanged GameHelper core is still open pending live comparisons.
- Upstream `v1.5.11` points to `0d11fe7`, already merged. Core/launcher versions now match 1.5.11; the fork launcher skips online binary updates to preserve patched APIs. Windows headless plugin loading is verified; game rendering is not. See `tests/PluginLoad.Tests` and the latest TODO entry before another sync.
- Do not run the upstream mirror/update/publish scripts for this fork without reviewing their target and overwrite behavior. They can replace local core changes or publish to the upstream project. The focused packaging script does not publish by itself.
- Keep TODO status and validation evidence accurate when handing off. User authorization in the conversation takes precedence over this file.

## 提交前必须更新 Test（用户要求，2026-09-28）

1. 完成源码修改和相关检查后，执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\rebuild-test.ps1`，重新编译整套核心/插件并更新 `Test`，不能只编译到 `bin`。
2. 确认脚本成功，并通过 `tests/PluginLoad.Tests` 检查 `Test` 中的实际插件，再提交和推送。用户无需再手动运行测试编译脚本。
3. 保留个人配置、配置备份和占用保护；GameHelper 正在运行或 DLL 被锁时停止部署，明确报告阻塞，不强制终止进程、不清空配置，也不声称 Test 已更新。
