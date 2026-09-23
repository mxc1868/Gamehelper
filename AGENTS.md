# Agent handoff

Read [TODO.md](TODO.md) before changing this project. It records the user's objective, completed work, verification limits, and the next investigation.

- Current destination: `https://github.com/mxc1868/Gamehelper`, branch `main`. The user restored this fork on 2026-09-23 and explicitly authorized syncing local work to it. Preserve remote commits; never publish to the upstream repository.
- Active work includes the GameHelper ShowMeWisp migration and the requested PoE2 UniqueLoot asset-based drop identification. Earlier unrelated ExileCore reverse-engineering research was explicitly cleared; do not restart it without a new request.
- ShowMeWisp is the renamed WhereTheWispsAt plugin. Preserve legacy settings/enabled-state migration and loader deduplication. Small/Medium/Big box scaling applies to wisps, not chests; live size-path validation is still pending.
- Wisp size uses the saved Windows implementation's resource ModelPath family (`sml`/`med`/`big`). UniqueLoot now has persisted checkbox selection, highlighted-only drop lists and bundled optional item icons; preserve existing highlight styles when toggling selections. Radar layout changes were canceled after the user confirmed resizing the window fixes visibility.
- This is a compilable prototype with diagnostics, not a Windows/game-validated release. Describe framework additions separately from original public APIs.
- The user now builds on Windows (confirmed 2026-09-23). Commit and push completed source changes to this fork's `main`; do not create more test ZIPs or Releases unless requested. The focused Linux packaging script remains available for later use.
- The API comparison logger is implemented. The question of whether the final plugin can use an unchanged GameHelper core is still open pending live comparisons.
- Upstream `v1.5.11` points to `0d11fe7`, already merged. Core/launcher versions now match 1.5.11; the fork launcher skips online binary updates to preserve patched APIs. Windows headless plugin loading is verified; game rendering is not. See `tests/PluginLoad.Tests` and the latest TODO entry before another sync.
- Do not run the upstream mirror/update/publish scripts for this fork without reviewing their target and overwrite behavior. They can replace local core changes or publish to the upstream project. The focused packaging script does not publish by itself.
- Keep TODO status and validation evidence accurate when handing off. User authorization in the conversation takes precedence over this file.
