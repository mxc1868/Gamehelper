# Follower

GameHelper 原生跟随插件：在小号端扫描附近玩家，从下拉列表选择队长，按地形路线发送 WASD。适用于另一台电脑或虚拟机内、小号游戏窗口保持前台的用法。当前是可编译、通过离线检查的原型，尚未在当前 PoE2 中验证实际移动。

## 使用

1. 在仓库根目录执行 `dotnet build GameOverlay.sln -c Release`，使用该构建目录的 GameHelper。插件自动复制到 `GameHelper/bin/Release/net10.0-windows/win-x64/Plugins/Follower/`。
2. 在小号游戏中选择 **WASD 移动**。GameHelper 必须连接小号进程。
3. F12 启用 Follower，打开“选择附近玩家”，从扫描出的名字中选队长。排除小号自己，名字去重并排序；选中后立即保存，不需要手动输入。没有候选时，让两人进入同一区域并靠近后再展开列表。
4. 默认勾选“只预览路线”。关闭设置、回到游戏，按 **F8** 查看路线、距离和拟发送的按键。
5. 确认定位和路线后，在设置里取消“只预览路线”，回到游戏再按 **F8** 开始移动。**F8 切换启停，Esc 停止**。

默认停止距离 18、重新跟随距离 25，单位为游戏地形网格；间隔用于避免在距离边缘反复启停。离墙间距默认 1；狭窄道路找不到路线时可尝试 0。配置保存在插件的 `config/settings.txt`，重启保留选中的名字与参数，但不会自动开始移动。

## 行为与边界

- 从原有 `AwakeEntities`、`Player.Name`、`Render.GridPosition` 读取目标。当前没有使用经过验证的队伍成员列表，候选是**附近可见玩家**，不保证每个候选都在队伍中；由用户选择目标，不自动选择陌生人或最近玩家。
- 读取原有 `GridWalkableData` / `TerrainMetadata.BytesPerRow`。沿用 Radar 的半字节地形解码与八方向 A* 思路，在插件内实现坐标边界、墙体间距、禁止斜穿墙角、节点/耗时预算，以及可取消的后台寻路。无需启用或依赖 Radar DLL。
- Radar 的 `BuildDoorOverrideMap` 会无条件把门附近设为可通行。Follower 只为已知 `TriggerableBlockage.IsBlocked == false` 的门提供通行修正；关闭或无法确认状态的门当作障碍。门的组件状态与修正范围仍需实机验证。**不会点击开门、使用传送门或自动跨区。**
- 通过原有 `WorldToScreen` 计算当前位置的投影方向，将下一段路线转换成 WASD/斜向组合，并检查该方向的短距离地形。路线预览按玩家当前高度绘制，坡道上可能与地面不完全贴合；真实键盘方向、走速、碰撞和窄道表现待实测。
- 仅使用标准 Windows `SendInput` 扫描码；调用前核对前台窗口 PID。此 API 受 Windows 完整性级别限制，输入是否被当前游戏接受需要现场确认，参考 [Microsoft SendInput 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)。不提供后台双开输入。
- 死亡、目标不可见/身份变化、切区、游戏失焦、聊天或大面板打开、设置打开、异常均停止；恢复后需要 F8。卡住约 2.5 秒停止，默认不反复撞墙。客户端看不到队长时，没有远距离坐标可用，也不会追逐旧坐标。
- 不持久化运行状态。独立 25 毫秒定时器检查按键租期：没有有效绘制帧续期 200 毫秒后释放按键，因此 F9 暂停绘制或主循环卡顿不会一直保持 WASD；重新绘制时超过 300 毫秒间隔会停止跟随。正常禁用和进程退出也释放已发送的键。操作系统强制终止进程不执行清理回调。
- 检测到额外的手动 WASD 或 Ctrl/Alt/Windows 键时停止；无法区分用户是否物理按住了插件已经按下的同一个键，手动接管请用 Esc/F8。

本次**没有修改 GameHelper 核心、GameOffsets 或 Radar**，复用当前仓库已有公开接口；没有重新验证此插件与其他历史或原版核心的二进制兼容性。

## 检查与来源

```powershell
dotnet run --project tests/Follower.Tests/Follower.Tests.csproj -c Release
dotnet run --project tests/PluginLoad.Tests/PluginLoad.Tests.csproj -c Release -- GameHelper/bin/Release/net10.0-windows/win-x64
```

Follower 离线检查覆盖障碍绕行、门覆盖、边界、墙角、寻路取消/预算、投影方向、按键释放/发送失败、跟随距离和卡住检测；不会连接游戏或发送真实按键。插件加载检查调用真实 `PManager`，包括 Follower，但不调用 `OnEnable`，不等于实机测试。

2026-09-25 检查本地插件目录与 [MordWraith 上游插件目录](https://github.com/MordWraith/Gamehelper/tree/main/Plugins)，未发现可直接复用的 Follower；公开搜索也未确认兼容此框架的现成版本。实现参考本仓库 `Plugins/Radar/Pathfinder.cs`、`LineWalker.cs`、`GameHelper/RemoteObjects/States/InGameStateObjects/Entity.cs`。
