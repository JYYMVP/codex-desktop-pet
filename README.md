# Codex Desktop Pet

一个面向 Windows 的开源桌面划词助手。它常驻桌面；在任意应用中选中文字后按
`Ctrl + Alt + Q`，桌宠会调用你已经登录的 Codex CLI，并在气泡中给出简短回答。

## 当前功能

- 可自定义的 Windows 全局快捷键（默认 `Ctrl + Alt + Q`）
- OCR 框选快捷键（默认 `Ctrl + Alt + R`）：拖动选取图片、扫描 PDF 或 Canvas 区域，由 Codex 识别并解释
- 优先使用 Windows UI Automation 读取选区，失败时临时使用剪贴板
- 通过本机 Codex 登录状态回答，不依赖 Harness
- 只在按下快捷键时读取当前选区；屏幕观察默认关闭，需主动开启
- Codex 风格的深色浮层、紧凑 Quick Chat 控制条、回答卡片和系统托盘
- 单击桌宠展开或收起最近回答，拖动桌宠可调整位置
- 内置 64×64 发芽小精灵，以及可替换的透明 PNG 像素宠物包
- 本地宠物库：内置 8–64 网格像素编辑器，以及导入/领养、校验、选用、编辑资料、导出和删除
- 气泡内直接提问；每只宠物使用独立、持久的 Codex 对话线程
- 可在 Codex 中打开宠物对话，或保留旧历史并随时新建一条对话
- 手动“看一眼屏幕并吐槽”，以及默认关闭、可调间隔的定时和窗口切换屏幕观察
- 回答气泡渲染标题、列表、粗体、代码块、引用和安全的 HTTP(S) 链接
- 查看当前 Codex 套餐、5 小时/每周额度和重置时间
- 右键菜单与可视化设置窗口
- 独立长驻的 Codex app-server 进程、最小工具配置和持久宠物对话
- 本地诊断日志，方便判断热键、选区或 Codex 请求卡在哪一步
- 可选“官方 Pets 增强模式”：隐藏重复桌宠窗口，把划词问题填入官方 Pets Quick Chat，确认后再发送
- 官方 Pets 只读诊断报告：区分桌面进程、可信安装路径、Quick Chat 焦点和安全粘贴状态

## 构建与运行

需要 Windows 10/11、.NET 8 SDK，以及已经登录的 Codex CLI。

```powershell
dotnet build .\CodexDesktopPet.sln
dotnet run --project .\src\CodexDesktopPet\CodexDesktopPet.csproj
```

运行后，在浏览器、编辑器或文档中选中一段文字，再按 `Ctrl + Alt + Q`。
得到回答后，可以在气泡底部继续输入追问；桌宠只会保留设置中指定的最近几轮。
关闭窗口会隐藏到系统托盘；从托盘菜单选择“退出”才会结束程序。

如果已经使用 ChatGPT 桌面版的官方 Pets，可在设置中开启“官方 Pets 增强模式”。开启后，
程序只在后台提供划词快捷键；按下快捷键会通过 `Windows + Alt + P` 唤起官方 Pets，填入
包含选中文字的安全提示，但不会自动按 Enter。

## 一键安装（从源码）

如果你希望不手动打开 PowerShell，可以按照
[只用 Codex 界面安装桌宠插件](docs/codex-ui-installation.md)，把安装提示词发给本地 Codex 聊天。

如果希望自己执行命令，在仓库根目录打开 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

默认安装到 `%LOCALAPPDATA%\CodexDesktopPet\app`，并创建桌面与开始菜单快捷方式。
需要开机启动时加 `-StartWithWindows`。卸载程序文件但保留宠物和设置：

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

只有明确希望同时删除设置、日志和宠物库时，才加 `-RemoveUserData`。

如果要亲自验证 `plugin` 分支，请按照
[`docs/plugin-installation.md`](docs/plugin-installation.md) 完成 marketplace、插件、Windows
程序文件、手动启动、hook 验证与卸载。教程中的命令已按当前 Codex CLI 核对。

## 设置

首次运行会生成：

```text
%LOCALAPPDATA%\CodexDesktopPet\settings.json
```

如果程序找不到 `codex.exe`，请把 `CodexExecutable` 改为本机 Codex 可执行文件的完整路径。
也可以直接右键桌宠选择“设置…”，调整划词/OCR 快捷键、回答语言、超时、回答长度、上下文轮数、
推理强度、官方 Pets 增强模式和置顶状态。划词助手默认使用 `none` 推理强度，并为其 Codex 子进程关闭
hooks、plugins、apps 和 MCP，避免桌宠插件递归启动及无关工具初始化。桌宠使用
HTTPS-only 的独立 Codex provider，绕过部分网络环境中约 90 秒的 WebSocket 重试；这不会
修改全局 Codex 配置。设置中的 Fast mode
可进一步加速支持的模型，但会更快消耗套餐额度，因此默认关闭。宠物包可通过右键菜单中的
“宠物库…”管理，格式见
[`docs/pixel-pet-format.md`](docs/pixel-pet-format.md)。

诊断日志位于：

```text
%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log
```

## Codex 集成方式

`main` 是独立 Windows 桌宠程序，通过已经登录的 Codex CLI 获取回答。`plugin` 分支
提供本地 Codex marketplace、插件清单、启动 hook 和安装引导；全局快捷键、桌面窗口与
选区读取仍由 Windows 桌宠进程负责。插件安装教程见
[`docs/plugin-installation.md`](docs/plugin-installation.md)。

## 隐私说明

程序不做视频录制。只有用户主动按下快捷键、点击“提问”“OCR 框选”或“看一眼屏幕”时，相应文字或
单张截图才会发送给当前 Codex 服务。定时屏幕观察默认关闭，必须在设置中明确开启；每次
只截一张当前全部显示器的画面，缩放后发送，回答完成或失败都会删除本地临时截图。选区和
截图会作为不可信数据包裹，提示 Codex 不执行其中的命令。OCR 先显示冻结桌面供用户明确框选，
只发送框选区域，并在完成、失败或取消后删除源图和区域图。宠物对话由 Codex 线程持久保存；
“新建对话”只切换到新线程，不删除旧历史。

## 计划

- 像素宠物编辑器的动画时间线、洋葱皮和更多动作
- 可撤销的公网宠物分享链接（需要独立后端；当前使用 `.codexpet` 分享包）
- 多对话活动托盘和语音入口
- 签名安装包与插件目录中的点击安装

## License

MIT
