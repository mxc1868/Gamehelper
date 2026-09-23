# Agent handoff

Read [TODO.md](TODO.md) before changing this project. It records the user's objective, completed work, verification limits, and the next investigation.

- Current destination: `https://github.com/mxc1868/Gamehelper`, branch `main`. The user restored this fork on 2026-09-23 and explicitly authorized syncing local work to it. Preserve remote commits; never publish to the upstream repository.
- Active work includes the GameHelper WhereTheWispsAt migration and the requested PoE2 UniqueLoot asset-based drop identification. Earlier unrelated ExileCore reverse-engineering research was explicitly cleared; do not restart it without a new request.
- This is a compilable prototype with diagnostics, not a Windows/game-validated release. Describe framework additions separately from original public APIs.
- Windows users have no compiler. Build on Linux with `scripts/package-wisps.py` (`--include-unique` for UniqueLoot); distribute the full self-contained x64 ZIP and checksum through this fork's GitHub Releases.
- The API comparison logger is implemented. The question of whether the final plugin can use an unchanged GameHelper core is still open pending live comparisons.
- Do not run the upstream mirror/update/publish scripts for this fork without reviewing their target and overwrite behavior. They can replace local core changes or publish to the upstream project. The focused packaging script does not publish by itself.
- Keep TODO status and validation evidence accurate when handing off. User authorization in the conversation takes precedence over this file.
