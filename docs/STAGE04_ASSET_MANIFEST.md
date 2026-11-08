# 第四阶段资产与 Prefab 清单

本清单描述 `PhysicalWorkspaceSetup.Build()` 的资产定义和场景生成规则，不代表这些功能已经通过运行测试。实际测试结果见 `STAGE04_TEST_REPORT.md`。

## 场景与尺寸

- 场景沿用 `Assets/Scenes/VintageJournalDesk.unity`，保留第三阶段的手账、页面视图、素材库和存档组件。
- 单位：1 Unity Unit = 1 米。桌面宽 1.35 米、深 0.72 米，桌面上表面高 0.76 米。
- 原手账左右页面通过独立配置适配为每页 0.148 × 0.210 米；页面原始网格及归一化装饰坐标不变。
- 原有超大桌面和摆件的显示适配配置：`Assets/PhysicalWorkspace/LegacyArtAdaptation.json`。原始模型、纹理和旧 Prefab 不改写。
- 镜头配置：`Assets/Data/Physical/DeskCameraProfile.asset`，由既有 `DeskCamera` 统一应用。
- 新场景对象根：`PhysicalWorkspace`；桌面坐标根：`DeskOperationSurface`；查看位置：`SharedInspectAnchor`。

## 独立物品定义

表中尺寸为物品根局部轴 X/Y/Z 的米数。纸类沿 X/Z 展开，厚度为 Y；TN 沿 X/Y 展开，厚度为 Z。

| 类型 | 稳定素材 ID | 素材种类 / 场景实体数 | 物理尺寸（米） | 资源来源 |
|---|---|---:|---|---|
| 和纸胶带卷 | `tape_01`—`tape_12` | 12 / 12 | 0.045 × 0.015 × 0.045 | 复用原 `TapeRoll_01`—`TapeRoll_12` 网格、原胶带纹样；新增独立视觉适配与材质副本 |
| 贴纸 | `sticker_01`—`sticker_12` | 12 / 13 | 0.050 × 0.0006 × 0.050 | 第三阶段 `DecorationCatalog` 对应透明贴图 |
| 单张便签 | `note_01`—`note_06` | 6 / 6 | 0.075 × 0.0006 × 0.075 | 第三阶段 `DecorationCatalog` 对应便签贴图；每格保留独立静态便签叠 |
| A5 活页 | `page_01` | 1 / 1 | 0.148 × 0.0006 × 0.210 | 原 `JournalPaperCatalog`、原 PAGE_01 贴图 |
| A6 活页 | `page_02` | 1 / 1 | 0.105 × 0.0006 × 0.148 | 原 `JournalPaperCatalog`、原 PAGE_02 贴图 |
| 标准 TN | `tn_01`、`tn_02` | 2 / 2 | 0.110 × 0.210 × 0.018 | 新增独立结构，复用原皮革纹理；深棕、焦糖棕材质 |
| Passport TN | `tn_03`、`tn_04` | 2 / 2 | 0.090 × 0.134 × 0.018 | 新增独立结构，复用原皮革纹理；墨绿、酒红材质 |

合计 36 种定义、37 个场景实体。`sticker_01` 的两个实体共用只读素材定义，实例编号分别为 `physical-sticker_01-001` 和 `physical-sticker_01-002`，占用不同收纳格，状态互相独立。其余实例编号为 `physical-{AssetId}-001`。

## Prefab 与绑定

生成资源均位于 `Assets/PhysicalWorkspace/`：

- `Prefabs/{AssetId}.prefab`：36 个物品 Prefab。根保持单位缩放，绑定 `PhysicalInteractable`、`BoxCollider`、物理尺寸 Profile 与独立 `Visual` 子节点。
- `Prefabs/WashiTray.prefab`：左后方 12 格胶带架，6 列 × 2 行。
- `Prefabs/StickerChest.prefab`：左侧可开合贴纸盒，13 个独立收纳格。
- `Prefabs/NoteChest.prefab`：右侧可开合便签盒，6 个独立收纳格与静态便签叠。
- `Prefabs/RefillRack.prefab`：后方 A5/A6 内页架，储存时倾斜展示；拿到桌面后平放。
- `Prefabs/TravelersShelf.prefab`：右后方 4 本 TN 陈列架。
- `Dimensions/{AssetId}.asset`：36 份可配置真实尺寸、Collider 尺寸、抓取偏移及有效交互距离。
- `Materials/`：新增独立木材、黄铜、纸张、TN 皮革、纸类图像材质，以及旧胶带材质副本。
- `Meshes/{AssetId}_surface.asset`：纸类独立平面网格；替换图像不改变业务定义或收纳状态。

贴纸盒与便签盒采用两片刚性折盖：关闭时覆盖完整盒面，打开时分别转动两个独立铰链，在盒后方低位叠放。胶带架和 TN 架前方保留空间，避免竖起的大盒盖遮挡后排收纳物品。

每个场景实体具有 `GrabAnchor`、外置收纳格 `HomeAnchor`、共享静态 `InspectAnchor` 和 `PlacementAnchor`。物品 Prefab 不保存跨场景的收纳格引用；安装场景时单独绑定。共享查看锚点不会随被查看的物品移动。

TN 的 `TravelersNotebookBinding` 绑定前封面、后封面、书脊、松紧绳、内芯锚点、开合轴及展开锚点。标准与 Passport 类型独立配置。本阶段不实现开合、内芯更换或页面编辑。

## 原有资产保留

原 `TapeRack`、`StickerBox`、`NoteBox`、`PageHolder`、两卷旧展示胶带和部分占位摆件在第四阶段场景中停用，源 GameObject 与 Prefab 保留。原 `OpenJournal`、数字装饰目录、纸张目录、图像文件及素材 ID 保留。实体物品与页面装饰不会共用可变实例，也不改变旧存档版本。

生成后的逐实体机器清单位于 `docs/stage4-logs/asset-manifest.json`，包括实例 ID、素材 ID、类型、尺寸、Prefab 路径及各类型数量；该文件须由实际场景安装执行产生。
