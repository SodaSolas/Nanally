# Nanally

Windows 桌面小工具：从已连接的 Android 手机提取 `com.piegame.bd` 的 Logs，并可把手机截图 / 录屏通过本机 `lark-cli` 发到指定飞书会话（常见用法是发给自己）。

不依赖 Lily Say，也不依赖 Linko。

完整说明见飞书：[Nanally 使用文档](https://rcnc3bm8lc5c.feishu.cn/wiki/B2KuwC57Ii7mp8kVwWucbSPunRd)（地址也写在 `wiki~/wiki.txt`）。

## 适合谁

需要从真机取游戏 Logs，或把截图 / 录屏发到飞书的同事。

## 环境准备

- Windows
- 手机开启 USB 调试，数据线连接；首次连接时在手机上允许本机调试
- 本机可找到 `adb`（PATH、环境变量 `ADB`，或常见 Android SDK `platform-tools`）
- 使用「发给自己」时：本机可运行 `lark-cli`，并已配置可用的飞书应用（机器人）

## 启动

```text
D:\Tools\Nanally\Nanally.exe
```

## 提取日志

1. 连接手机，确认顶栏设备已连接；需要时点「刷新设备」。
2. 侧栏进入「提取日志」，确认日志根目录（默认工具旁的 `logs`）。
3. 点底栏「拷贝日志」。

落点：

```text
logs\{设备名}+{yyyy-MM-dd_HH-mm-ss}\
```

可用「打开文件夹」打开当前路径栏目录。改日志根目录后，后续拷贝写到新根下；不会清空整个 logs 根。

> **注意：** `logs\` 只存从手机拉下来的**游戏** Logs。Nanally **工具自己**的运行日志在旁边的 `whathappened\`，不要搞混。别人排查「安装失败 / 发飞书失败」时，请他打包 `whathappened` 文件夹。

## 截图与录屏

1. 侧栏进入「截图录屏」（进入页或切换设备会自动读取；也可点「读取设备」）。
2. 可按全部 / 截图 / 录屏筛选；列表按时间从新到旧。
3. 单击左上角圆点勾选；双击预览。
4. 点底栏「发给自己」。

发送前须在设置中填写会话 ID；未填写不会发送。发送走**机器人**身份，请确认该会话里已有对应机器人、且机器人能发言。

## 飞书设置

设置页左侧为「外观 / 目录 / 飞书」，右侧直接显示该项内容（不再重复大标题）。

| 项 | 含义 |
|---|---|
| App ID | 可选；与 Token 一并写入本机飞书 CLI 安全存储时使用 |
| Token | 可选；写入 CLI 的 tenant token。发送时优先用 CLI 默认机器人，不强制覆盖 |
| 发给自己的会话 | 飞书会话 ID（`oc_…`）；不会自动探测 |
| 登录飞书 CLI | 未配置时会先引导应用初始化；再做用户登录（仅基础消息权限，不含「以用户身份发送消息」） |

### 新用户建议顺序

1. 安装 `lark-cli` 并加入 PATH。
2. 打开 Nanally → 设置 → 飞书 →「登录飞书 CLI」，按浏览器完成应用初始化 / 用户授权。
3. （推荐）填写 App ID、Token 并「保存飞书」，保证机器人身份可用。
4. 填写自己的会话 ID 并保存。
5. 在飞书里确认该会话已添加本应用机器人。

自行查询会话 ID：

1. 飞书桌面端打开目标会话 → 更多 → 复制链接
2. 取 `openChatId=` 后面整段（以 `oc_` 开头）

或：

```text
lark-cli im +chat-search --query "会话名" --as bot
```

在结果中取 `chat_id`。不要填写他人的会话；不要把 Token 写进文档或外发截图。

## 安装 APK

1. 侧栏进入「安装 APK」。
2. 把 `.apk` 拖进页面，或点「选择 APK」。
3. 默认只装**顶栏选中**的已授权手机；打开「装到全部已连接手机」则对所有已授权设备**并行**安装。
4. 开关「先卸载同包名再安装」：开启则用 `aapt` 读包名、卸旧再装；关闭则覆盖安装。默认关闭，写入本地设置。
5. 开关「用 adb install」：开启则走 `adb install -r -t`；关闭（默认）则走 `adb push` + `pm install`（大包通常更稳更快一点）。写入本地设置。
6. 安装页会显示进度条：推送 / `adb install` 阶段跟 adb 百分比；`pm install` 阶段走不确定进度，并每 1.5s 刷新「已等 Xs」（很多机型此时无输出，不等于卡死；也可能在等手机确认框）。

需要本机有 `adb`；开启先卸载时还需要 Android SDK `build-tools` 里的 `aapt`。

## 外观

默认浅色（白色毛玻璃，色板对齐 GlassWidgetStyle：冰蓝强调 `#4A7FE5`）。设置 → 外观可开「暗色毛玻璃」与「毛玻璃」；暗色为半透明炭黑色板 + Acrylic 深色模式。不影响提取与发送流程。

## 更新

关于页会检查 GitHub Release（默认仓库 `SodaSolas/Nanally`）。远端版本比本机新时显示「从 GitHub 更新」按钮；否则隐藏。点下去会下载 Release 里的 `Nanally.exe`，退出后由脚本替换并重启。

本地版本号写在 `AppInfo.Version`（当前 `1.0.2`）。发新版时：抬版本 → 编译 → 推代码 → `gh release create vX.Y.Z Nanally.exe`。

## 常见问题

| 现象 | 处理方向 |
|---|---|
| 找不到 adb / 设备为空 | 检查 USB 调试、数据线、adb PATH 或 `ADB`；手机授权后刷新 |
| 手机未授权 | 看手机弹窗并允许，再刷新 |
| 「发给自己」提示未填会话 | 到设置填写会话 ID 并保存 |
| 已登录仍发不出去 | 看状态栏具体报错：会话 ID、机器人是否在会话中、应用是否有发消息权限 |
| 登录权限过多 | 当前用户登录只申请 `im:message` 与 `offline_access`；发送不走「以用户身份发送」 |
| 是否依赖 Lily Say | 否；只依赖 adb，以及可选的 `lark-cli` |
| 工具自己的报错记在哪 | `D:\Tools\Nanally\whathappened\`（按天 `.log`），**不是** `logs\` |

## Nanally 自身日志（whathappened）

路径：工具根下的 `whathappened\`。

- 存 Nanally **App 自身**运行记录（启动、刷设备、拷游戏 Logs、装 APK、发飞书、未处理异常等）
- **不是**游戏 Logs；游戏 Logs 仍在 `logs\`
- 首次启动会自动建目录，并写一份 `whathappened\README.md` 说明二者区别
- 文件名形如 `nanally-2026-09-18.log`

## 相关链接

- 飞书使用文档：https://rcnc3bm8lc5c.feishu.cn/wiki/B2KuwC57Ii7mp8kVwWucbSPunRd
- 本地 Wiki 地址文件：`wiki~/wiki.txt`
- 给改工具的人看的速查：`AGENTS.md`
