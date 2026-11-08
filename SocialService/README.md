# 本机云上邮局

这是最后阶段的可运行本机服务，使用 Node.js 内置模块，没有外部依赖。默认仅监听 `127.0.0.1:18747`，不会发布到互联网，也不自动传送私人日记。

双击项目根目录的 `开始白日梦手作房.cmd` 启动服务与原有手账，`打开第二位玩家.cmd` 使用独立PlayerB存档；也可单独运行 `Tools/Start-AtelierSocialService.ps1`。在游戏“我的手账 / 手作房 → 云上朋友”注册本机匿名身份，使用好友码互相邀请。普通游戏身份不是管理员。

所有页面照片、好友信件、贴纸和玩家委托均先进入待审核队列。审核密钥在 `SocialService/data/moderator-key.txt`，可手动填到“本机审核”，刷新后逐份通过或拒绝。举报会立即暂时下架；可暂停违规账号。运行数据和审核密钥不会随项目源码交付。

收礼协议：客户端先把领取账本与有限实例一起原子保存并Flush，成功后才发送ack。HTTP失败可重试；重复领取、删除和重启均不会重新生成该份礼物。自制贴纸模板可以发行给不同好友，每封信发行一份新的有限实例；不会移动寄信者页面上的实体。

命令：

```powershell
D:\node.exe SocialService/server.mjs --port 18747 --data SocialService/data/social-store.json
D:\node.exe --test SocialService/server.test.mjs
```

接口均在 `/v1/` 下：`register`、`me`、`gallery`、`mine`、`friends`、`friends/accept`、`letters`、`inbox`、`commissions`、`reports`、`block`、`moderation`。除register外普通操作使用 `Authorization: Bearer ...`；审核只接受独立 `X-Admin-Key`。不设评论接口。

服务端有限body/文字/PNG尺寸和解压尺寸，逐块CRC校验，拒绝远程图片URL；仅GET/POST，校验loopback Host与同源Origin。数据上限64MB，持久快照写临时文件后原子替换。测试使用临时独立数据，不会操作真实玩家。

公网发布仍需另行配置TLS、真实账号恢复、反滥用和人工审核运营；本机联调通过不能视为这些生产条件已完成。
