# Asset 映射来源

- 数据源：[RePoE2 `data/uniques.json`](https://github.com/repoe-fork/poe2/blob/b818b843337cae43b090b272fd98bbc0fd3a34f3/data/uniques.json)。固定提交 `b818b843337cae43b090b272fd98bbc0fd3a34f3`（2026-09-15）。该导出声明版本为 `4.5.5.2`；不代表已验证用户当前客户端版本。
- 本表提取 `visual_identity.dds_file` → `name`，保留所有同贴图候选、不同外观路径和改名前后的名称。449 条源记录生成 446 条完整路径、441 个去重名称，3 条路径有多个候选。没有根据上市交易量或价格筛掉物品。
- 复现：下载上述固定文件到本地，执行 `python3 scripts/build-unique-art-map.py /path/to/uniques.json`。更新时同时更新这里、插件版本说明和测试快照，不能声称静态表自动覆盖未来更新。
- 参考行为：[Ground Items With Linq](https://github.com/exApiTools/Ground-Items-With-Linq/tree/568297fe1c99eed754a1b6f6fe08b51820b95da2)。其 `GetGameFileUniqueArtMapping` 联结 `ItemVisualIdentities` 与 `UniqueItemDescriptions`，按完整 `ArtPath` 分组；`CustomItemData.UniqueNameCandidates` 用物品 `RenderItem.ResourcePath` 查候选。

此实现为 GameHelper 单独编写，没有引入 ExileCore 二进制、原插件 LINQ 规则引擎或其 PoE1 默认映射。GameHelper 没有相同的两张表读取 API，因此这里使用内嵌 PoE2 导出和用户可覆盖的 JSON。图标路径与名称是游戏数据；未打包图像文件。
