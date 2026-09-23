# Asset 映射来源

- 数据源：[RePoE2 `data/uniques.json`](https://github.com/repoe-fork/poe2/blob/b818b843337cae43b090b272fd98bbc0fd3a34f3/data/uniques.json)。固定提交 `b818b843337cae43b090b272fd98bbc0fd3a34f3`（2026-09-15）。该导出声明版本为 `4.5.5.2`；不代表已验证用户当前客户端版本。
- 本表提取 `visual_identity.dds_file` → `name`，保留所有同贴图候选、不同外观路径和改名前后的名称。449 条源记录生成 446 条完整路径、441 个去重名称，3 条路径有多个候选。没有根据上市交易量或价格筛掉物品。
- 复现：下载上述固定文件到本地，执行 `python3 scripts/build-unique-art-map.py /path/to/uniques.json`。更新时同时更新这里、插件版本说明和测试快照，不能声称静态表自动覆盖未来更新。
- 参考行为：[Ground Items With Linq](https://github.com/exApiTools/Ground-Items-With-Linq/tree/568297fe1c99eed754a1b6f6fe08b51820b95da2)。其 `GetGameFileUniqueArtMapping` 联结 `ItemVisualIdentities` 与 `UniqueItemDescriptions`，按完整 `ArtPath` 分组；`CustomItemData.UniqueNameCandidates` 用物品 `RenderItem.ResourcePath` 查候选。

此实现为 GameHelper 单独编写，没有引入 ExileCore 二进制、原插件 LINQ 规则引擎或其 PoE1 默认映射。GameHelper 没有相同的两张表读取 API，因此这里使用内嵌 PoE2 导出和用户可覆盖的 JSON。

## 物品图标（2026-09-23 UTC）

图像为 Grinding Gear Games 的游戏物品美术，通过 PoE2DB 的公开 CDN 获取，例如 [Headhunter](https://cdn.poe2db.tw/image/Art/2DItems/Belts/Uniques/Headhunter.webp) 和 [Mageblood](https://cdn.poe2db.tw/image/Art/2DItems/Belts/Uniques/Mageblood.webp)。按已有完整 asset 路径对应 `.webp`，未使用同文件名猜测或图像生成。来源不改变图像原有权利归属。

`Icons/sources.json` 记录每条 URL、文件名、SHA-256、字节数或下载失败原因。439/446 条路径取得图像，共 5,984,758 字节；缺图回退文字。全部图像已用当前部署的 ImageSharp 解码验证。文件名为规范化 asset 路径（小写 UTF-8）的 SHA-256，避免同名不同目录碰撞。

Windows 复现：`powershell -NoProfile -File scripts/fetch-unique-icons.ps1`。下载是维护操作，插件运行期间不联网；普通编译只复制已保存资源。

## 默认高亮核对（2026-09-23）

按用户指定的 PoE2DB 核对两个腰带的 Icon 字段，并与上述固定 RePoE2 导出的 `.dds` 路径交叉确认：

| 物品 | 完整 asset 路径 | 核对页面 |
| --- | --- | --- |
| Headhunter | `Art/2DItems/Belts/Uniques/Headhunter.dds` | [PoE2DB Headhunter](https://poe2db.tw/us/Headhunter) |
| Mageblood | `Art/2DItems/Belts/Uniques/Mageblood.dds` | [PoE2DB Mageblood](https://poe2db.tw/us/Mageblood) |

规则写在 `highlights.default.json`。这里只使用该物品自身的完整 art 路径；通用腰带的 `Metadata/Items/...` 路径、底材名或文件名片段不能唯一识别这两件暗金。PoE2DB 的 Icon 字段不带扩展名，`.dds` 来自固定 RePoE2 导出。
