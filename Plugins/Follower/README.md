# Follower 已整合到 Bloodybot2

独立 Follower 插件已移除，原寻路、双人协调和脱困代码迁入 Plugins/Bloodybot2/Navigation。现在在 Chrome 的 **General → Follow** 中选择模式，然后配置 Follow 页面。详见 [Bloodybot2 使用说明](../Bloodybot2/README.md)。

运行目录中的 Plugins/Follower/config/settings.txt 保留用于首次迁移，**不要删除**。Bloodybot2 首次读取时导入旧导航设置；保存到新目录后不再覆盖新配置。旧 DLL 与新插件并存时，核心只发现 Bloodybot2，首次建立其启用元数据时继承旧 Follower 的开关。

tests/Follower.Tests 保留原测试项目名，验证的源码已指向 Bloodybot2 的 Follow 导航。
