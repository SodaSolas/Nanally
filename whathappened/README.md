# whathappened

这里是 **Nanally 工具自己的运营日志**（排查「安装失败 / 发飞书失败 / 找不到设备」等）。

## 和 `logs` 的区别（重要）

| 目录 | 存什么 | 谁用 |
|---|---|---|
| **`whathappened\`** | Nanally **App 自身**运行记录（按天 `nanally-yyyy-MM-dd.log`） | 同事排查工具问题时，把这个文件夹打包发过来 |
| **`logs\`** | 从手机拉下来的 **游戏** `com.piegame.bd` Logs | 游戏侧问题；不是 Nanally 的运行日志 |

不要把 Nanally 的运行日志写进 `logs\`，也不要指望在 `logs\` 里找到本工具自己的报错。

## 文件

- `nanally-yyyy-MM-dd.log`：当天追加写入，UTF-8
- 可能含设备序列号、APK 路径、adb 摘要；**不应**含飞书 Token / App Secret

本 README 也可由 Nanally 首次启动时自动创建（已存在则不覆盖）。
