# 白日梦手作房 · daydream-handjournal

在悬浮书桌前整理胶带、贴纸与便签，把想法留在自己的手账里。这个 Unity Demo 使用鼠标操作，提供实体收纳、页面拼贴、离型本、TN 本册、来信创作和昼夜素材收集。

## 下载试玩

1. 前往 [Releases](https://github.com/y04380017-hue/daydream-handjournal/releases)，下载 Windows 完整 ZIP 包。
2. 将整个压缩包解压到一个文件夹；保持 EXE、`VintageJournalDesk_Data`、`UnityPlayer.dll` 等文件在原来的相对位置。
3. 双击包内的“开始离线手账.cmd”或 `VintageJournalDesk.exe`，在开始页选择“坐到桌前”。如果包内保留了项目目录结构，EXE 位于 `Build/Windows/`。

桌面创作可以离线运行，不需要 Unity 编辑器或 Node.js。GitHub 的“Download ZIP”是项目源码；试玩请下载 Releases 中的 Windows 包。退出前完成或取消手里的取放操作，进度会保存在本机。

## 常用操作

| 操作 | 方法 |
| --- | --- |
| 选择素材 | 左键单击，素材保持原位置 |
| 移动或粘贴素材 | 按住右键拖动，松开放置；放到无效位置会返回 |
| 使用离型本 | 左键拖到手账旁的桌面空位，放稳后右键单击打开 |
| 拉取胶带 | 按住左键从卷体或图案样条向桌前拉，调整长度，再右键确认；默认 5 cm，可选 2–30 cm |
| 开合抽屉 | 点击把手，或按住左键前后拖动；叠层材料可从“抽屉素材目录”取出 |
| 查看与翻页 | 选中本子按 `E`，或右键单击 TN 本；使用界面按钮、方向键或 `Page Up` / `Page Down` 翻页 |
| 归位与取消 | `R` 归回收纳位；`ESC` 取消当前操作或退出查看 |
| 撤销与重做 | `Ctrl+Z` / `Ctrl+Y` 用于允许撤销的创作操作 |
| 环视 | 开启左侧“环视”，将鼠标停在画面左右边缘持续转头，移回中间停止；“回到桌面”返回正前方 |

“视角”菜单提供手账、胶带架、本册和抽屉特写；视点保持坐姿。选中正面素材会显示光晕、图案与介绍，正面可领取的漂浮物会给出额外素材；远处与身后的背景装饰仅供观赏。

本轮布局包括八兜布艺便签收纳、缩小的双抽屉、30 卷陈列胶带与对应图案样条、柜底两本离型本、七本侧插本册和双轮点点胶。中央手账、TN 与离型本各自保存页面；有限素材的转移保留同一实例。

## 可选的本机邮局

好友礼物、画廊和玩家委托使用可选的本机服务。需要安装可运行 `SocialService/server.mjs` 的 Node.js；项目工具要求 Node.js 20 或以上。可在项目根目录执行：

```powershell
node SocialService/server.mjs --port 18747 --data SocialService/data/social-store.json
```

然后打开游戏“我的手账 / 手作房 → 云上朋友”。服务只监听 `127.0.0.1:18747`，用于同一台电脑上的多客户端试玩；它没有部署为公网服务。审核密钥和运行数据在首次启动后于本机生成，不随仓库或下载包提供。接口与审核流程见 [SocialService/README.md](SocialService/README.md)。

## 打开源码

使用 Unity Hub 添加仓库目录，以 **Unity 6000.3.27f1** 打开，加载 `Assets/Scenes/VintageJournalDesk.unity` 后进入 Play Mode。项目保留 `Assets`、对应 `.meta`、`Packages` 和 `ProjectSettings`；编辑器首次打开会生成缓存。源码仓库不包含 Windows 构建产物。

不要运行旧阶段的全场景生成器来打开项目；当前桌面场景已保存，生成器属于主动重建工具。

## 发布检查与范围

版本：`v0.1.0-mouse`。本次发布前复验结果：**本轮复验通过：Unity Play 156 项；Windows 156 项及重启 8 项；完整阶段 154 项及重启 11 项；真实鼠标环视 6 项、桌面操作 21 项；本机 HTTP 14 项；最终解压包正常离线启动 7 项。**。

详细状态见 [PUBLICATION_NOTES.md](PUBLICATION_NOTES.md)。当前 Demo 提供鼠标创作与本机流程；公网账号、跨设备同步、完整 48 章的长期真人游玩，以及所有屏幕比例、DPI 和硬件组合仍需后续验证。浏览器内试玩与 macOS / Linux 构建尚未提供。
