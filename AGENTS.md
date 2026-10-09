# Nanally 速查

给以后改这份工具的人看。先读这里，再搜源码。不要把 token、App Secret 写进本文档或回复里。

面向使用者的说明见飞书 Wiki「Nanally 使用文档」与本地 `wiki~/wiki.txt`（根文档 URL）；精简版见 `README.md`。本文仍是开发速查，不要改成用户手册。

根目录：`D:\Tools\Nanally`。和 Client 仓库无关，不要往 Client 里改。

## 这是什么

单文件 WPF 小工具。三件事：

1. 从已连接 Android 手机拉 `com.piegame.bd` 的 Logs，落到本机 logs 目录。
2. 列出手机截图和录屏，勾选后用本机 `lark-cli` 以**机器人**身份发到指定飞书会话。
3. 把本机 APK 拖进「安装 APK」页，装到所有已授权设备；可选按包名先卸载再装。
4. 关于页可从 GitHub Release 自更新（默认 `SodaSolas/Nanally`，有新版本才显示按钮）。

不依赖 Lily Say，也不调用 Linko。飞书只走 `lark-cli`。

版本与更新源：`AppInfo.Version` / `AppInfo.DefaultUpdateRepo`（`Nanally.cs`）。自更新：`GitHubUpdater` 拉 latest release 的 `Nanally.exe`，写 `nanally-apply-update.cmd` 替换后重启。

## 编译和运行

语言锁死在 C# 5（`csc.exe` .NET Framework 4.0）。禁止：字符串插值、`nameof`、`out var`、`?.`、`async/await` 语法糖以外的新语法。含中文的 `.cs` 必须带 UTF-8 BOM。

```bat
cmd /c D:\Tools\Nanally\build.bat
```

PowerShell 必须用 `cmd /c` 包住。源文件只有 `Nanally.cs` + `Nanally.Ui.cs`。图标 `icon\Nanally.ico`。窗口图标读 `icon\Nanally.png`。

exe 正在运行时写不进去。先关掉 Nanally 再编译，编完让用户重新打开 exe。不要把预览图留在工具根目录。

自测：`Nanally.exe --selftest`。结果写 `%TEMP%\nanally-selftest.txt`。自测只覆盖拷贝目录规则，不覆盖 UI。

## 文件和数据

| 路径 | 作用 |
|---|---|
| `Nanally.cs` | 入口、Theme、Acrylic、Adb、拷贝、设备名 |
| `Nanally.Ui.cs` | 窗口、侧栏、工具条、设置、缩略图、飞书发送 |
| `build.bat` | 编译 |
| `icon\` | `Nanally.png` / `Nanally.ico`，不要把无关预览图放根目录 |
| `logs\` | **仅**游戏 Logs：从手机拉 `com.piegame.bd` 的落点根。下面才是 `{设备名}+{yyyy-MM-dd_HH-mm-ss}` |
| `whathappened\` | **Nanally App 自身**运营日志（按天 `nanally-yyyy-MM-dd.log` + 目录内 README）。排查工具问题时找这个文件夹，**不要**和 `logs\` 搞混 |
| `%AppData%\Nanally\settings.json` | 用户设置。不要打印里面的 token |
| `%LocalAppData%\Nanally\preview` | 缩略图缓存 |
| `.nanally-send\` | 发送前的临时拉取目录，发完删 |

`AppSettings` 字段：`LogsDir`、`FeishuAppId`、`FeishuToken`、`FeishuChatId`、`ThemeMode`（默认 `Light`；Dark/Light）、`Accent`（默认 `Blue`；Pink/Blue/Green）、`FollowSystem`、`EnableGlass`（默认 true）、`SidebarExpanded`、`ApkUninstallFirst`（默认 false）、`ApkInstallAllDevices`（默认 false，只装选中设备）、`ApkUseAdbInstall`（默认 false；true 时走 `adb install -r -t`，false 时走 push+pm）。浅色色板按 `D:\tools\GlassWidgetStyle\README.md`（半透明白卡片、冰蓝强调）。空的会话 ID 保持空，不会回填，也不会去飞书查询。

## 界面

窗口约 1100×720，`WindowStyle=None`，可缩放。`WindowChrome` 标题高 36，标题栏要能拖；放进标题栏的按钮必须 `WindowChrome.SetIsHitTestVisibleInChrome`。标题栏背景为 null 时点不中，拖不动。

侧栏是图标轨，不是粉胶囊包住「图标+文字」。选中只染 40×40、圆角 10 的图标方块。展开后文字在方块右边，左对齐（`LeftButtonTemplate`）。收起宽 64，展开宽 176。关于页头像也落在同一条 40 宽的列里。

顶栏只有一行：设备卡（名字和「已连接 · USB」左右并排）｜刷新设备｜当前页工具。

- 提取日志：上级、路径框、浏览、刷新
- 截图录屏：全部/截图/录屏、读取设备、全选、计数
- 安装 APK：拖放 / 选择文件；默认只装顶栏选中设备；「装到全部」时并行安装；页内开关「先卸载同包名再安装」「用 adb install」（关=push+pm，开=adb install）；安装过程有进度条（百分比 + 心跳）
- 切页用 `ShowNav` 切换 `logsTools` / `mediaTools` 的可见性，不要再给页面自己加第二行工具条

底栏：状态在上，主按钮在下。日志页是「拷贝日志」，媒体页是发送。安装 APK 页右下角是进度条 + 百分比。

设置内页：外观 / 目录 / 飞书。左侧页签已标明分类，右侧**不要**再加「外观 / 目录 / 飞书」大标题（`SectionTitle` 已删）。选中项是通栏粉底、文字左对齐（宽度锁成 `nav.ActualWidth - 16`）。开关 `HorizontalAlignment=Left`，不要靠右。

滚动条是全局隐式样式，在 `Theme.InstallScrollBars`。跟 Linko 一样：宽 8、圆角、没有箭头。槽用 `CardSoft`，滑块用 `Thumb`（深色 `#3C3C3C`，浅色 `#99A0B0C0`），悬停 `Label`，拖动跟随强调色。不要改回系统白条。模板用 `DynamicResource`，不要把共享刷子塞进 Setter，否则一切换主题又会冻住。

主题刷子是未冻结的，`Theme.Apply` 原地改颜色，已有控件会跟着变。浅色默认走 GlassWidgetStyle；深色为半透明炭黑毛玻璃色板（`#B81C1C1C` 卡片等），配 `Acrylic dark:true`。开启毛玻璃时 `shellBody` 不铺底，侧栏用半透明 `nn.Sidebar` 让 Acrylic 透过来；关毛玻璃时 `shellBody` 仍绑 `nn.Bg`。PrintWindow 截图常把毛玻璃拍成实色，不能据此说透明失败。

## 两个日志目录（别搞反）

| 目录 | 存什么 | 给谁 |
|---|---|---|
| `whathappened\` | Nanally **工具自己**的运行记录 | 别人遇到安装失败 / 发飞书失败 / 找不到设备时，让他把这个文件夹打包发来 |
| `logs\` | 从手机拉下来的 **游戏** Logs | 游戏侧问题；**不是** Nanally 的 app 日志 |

启动时会自动创建 `whathappened\`，并写入目录内 `README.md`（已存在不覆盖）。按天追加 `nanally-yyyy-MM-dd.log`。不要把 App 日志写进 `logs\`。

## 提取日志

远程目录：`/sdcard/Android/data/com.piegame.bd/files/Logs`（`Adb.RemoteLogs`）。

`adb pull` 会在本地再包一层 `Logs`。拷贝时取这层里面的内容，不要把 `Logs` 外壳再套进去。

落点必须是「用户选的 logs 根」下面的 `{SafeName(设备名)}+{yyyy-MM-dd_HH-mm-ss}`。禁止在工具根建这个文件夹，禁止把文件平铺进 logs 根。logs 根拷贝前不清空。同名则加 `_2`。

底栏「拷贝日志」左边是「打开文件夹」。打开路径栏当前目录（`logsPathBox`，不行再 `logsBrowsePath`，再不行 `LogsDir`）。只在提取日志页显示。拷贝成功后不再弹出「打开文件夹 / 打开目录」那一行。

设备名顺序：`settings get global device_name`，否则 `ro.product.marketname`，否则 `ro.product.model`。多台时自定义下拉（MDL2 `E70D`/`E70E`），250ms 防连开关。状态不要再重复模型号。

设备列表**只保留 USB**（`adb devices` 里序列号带 `:` 的无线调试直接丢弃，界面也不再显示「无线」）。

adb 查找顺序：正在跑的 `adb.exe` → 环境变量 `ADB` → PATH → `ANDROID_HOME` / `ANDROID_SDK_ROOT` → `%LocalAppData%\Android\Sdk\platform-tools` → `C:\Workspace\onepiece\Tools\CITools\SDK\platform-tools\adb.exe`。Client 下那份通常不存在，不要只认它。

已知设备（只是对照，不写死逻辑）：`e66ba196` REDMI K80；`10ACCC12US000YH` V2241A（vivo，截图在 `Pictures/Screenshots`）；`3B15AS00EGL00000` PKB110。小米截图 `DCIM/Screenshots`，录屏 `DCIM/ScreenRecorder`。

## 截图和录屏

扫描根在 `MediaRoots`。列表按修改时间从新到旧（`DateTicks`，必要时从文件名猜时间）。切设备或进入截图录屏页会自动 `RefreshMediaAsync`。

单击切换勾选，双击开预览（图放大，视频 `MediaElement`）。提示：类型 / 大小 / 修改日期。左上角是 12px 圆点，不要用系统白方块勾选框。未选是半透明黑底加浅色环，选中填强调色。缩略图：先 adb pull，图用 `System.Drawing` 缩小，视频用 PATH 上的 ffmpeg 抽帧，再不行用 Shell `IShellItemImageFactory`。

## 飞书发给自己

### 登录与配置

- 「登录飞书 CLI」：`EnsureLarkConfigured` → 若 `not_configured` 则 `config init --new --brand feishu --force-init`（读输出里的 URL 开浏览器）；再 `auth login --scope "im:message offline_access" --no-wait --json`。
- 授权 URL 字段兼容：`verification_url` / `verification_uri_complete` / `verification_uri`，以及正文里的 `https://…`。
- **不要**申请 `im:message.send_as_user`（以用户身份发送消息）；**不要**用 `--domain im`（权限面过大）。
- 调用 CLI 时清掉子进程环境里的 `HERMES_HOME` / `OPENCLAW_HOME` / `LARK_CHANNEL`，避免被 Agent 工作区绑死。
- 「登录飞书 CLI」只检查**用户**是否 ready；发送不依赖用户代发。

### 发送

- 固定 `--as bot`。`EnsureLarkIdentity`：bot ready 才发；否则尝试把设置里的 Token 写入 CLI 后再查；仍不行就提示填 App ID / Token，不回退 `--as user`。
- **发送时不要**设 `LARKSUITE_CLI_TENANT_ACCESS_TOKEN_SOURCE=credential-store`（设置里旧 Token 会盖掉可用机器人）。保存设置时仍可用 `config tenant-access-token set` 写入。
- 失败时把 CLI 的 `message` / `code` 提到状态栏（`FormatLarkError`），不要只写「没登录」。

发送命令（工作目录必须是文件所在目录，参数只用文件名，`--image`/`--file`/`--video` 拒绝绝对路径和 `..`）：

```text
im +messages-send --as bot --chat-id "<会话>" --image "<文件名>"
im +messages-send --as bot --chat-id "<会话>" --video "<视频>" --video-cover "<封面jpg>"
```

图片用 `--image`。视频必须用 `--video` + `--video-cover`（先 ffmpeg / Shell 抽帧成 jpg）；**不要**把 mp4 当 `--file` 发，飞书会报 type of file upload does not match。封面与视频放同一工作目录。已有 `LocalPath` 缓存就直接拷，否则再 `adb pull`。视频发送超时 10 分钟。

发给飞书的封面必须是视频原始分辨率（`ExtractVideoFrame(..., nativeSize: true)`），不要 `scale=360`。飞书播放器按封面像素摆画面，封面被缩小后，正片会缩在黑色播放器中间。列表缩略图仍可缩到 360。

`lark-cli` 查找：PATH 里的 `lark-cli.exe` / `lark-cli.cmd`，否则 `%LocalAppData%\hermes\node\node.exe` + `node_modules\@larksuite\cli\scripts\run.js`。Hermes 不用开着。找不到时提示安装，不会自动下载安装包。

会话 ID 不会自动识别，也不要再写死默认值。空着就提示去设置里填。设置页有「怎么拿到自己的会话 ID」。说明正文不要出现具体某个人的 `oc_`。机器人必须已在目标会话中。

查 ID：

- 飞书桌面端打开会话，复制链接，取 `openChatId=` 后面的 `oc_...`
- `lark-cli im +chat-search --query "会话名" --as bot`
- `lark-cli im +chat-list --as bot --sort active_time --page-size 20`

不要拿这条功能做真实发送测试。

## 已知问题

主题按 Linko `ThemeManager`：启动 `Theme.Bootstrap` 先造可变刷子进 `Application.Resources`；`Theme.Apply` 能改就改 `Color`，冻住了就换新刷子写回 Resources 和静态字段。壳层用 `SetResourceReference` / `Theme.BindElement`。跟随系统监听 `SystemEvents.UserPreferenceChanged`。模板只用 `Theme.Dyn` / `DynamicResource`，不要把共享刷子塞进 Setter。
