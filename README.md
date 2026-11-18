# Codex Desktop Pet

一个面向 Windows 的开源桌面划词助手。它常驻桌面；在任意应用中选中文字后按
`Ctrl + Alt + Q`，桌宠会调用你已经登录的 Codex CLI，并在气泡中给出简短回答。

## 当前功能

- Windows 全局快捷键：`Ctrl + Alt + Q`
- 优先使用 Windows UI Automation 读取选区，失败时临时使用剪贴板
- 通过本机 Codex 登录状态回答，不依赖 Harness
- 只在按下快捷键时读取当前选区，不持续截图或监控桌面
- 透明置顶桌宠、思考动画、回答气泡、复制按钮和系统托盘
- 单击桌宠展开或收起最近回答，拖动桌宠可调整位置
- 内置 64×64 像素鱼，以及可替换的透明 PNG 像素宠物包
- 气泡内直接提问，并在内存中保留最近几轮上下文；可随时一键清空
- 查看当前 Codex 套餐、5 小时/每周额度和重置时间
- 右键菜单与可视化设置窗口
- Codex 进程采用只读沙箱和临时会话
- 本地诊断日志，方便判断热键、选区或 Codex 请求卡在哪一步

## 构建与运行

需要 Windows 10/11、.NET 8 SDK，以及已经登录的 Codex CLI。

```powershell
dotnet build .\CodexDesktopPet.sln
dotnet run --project .\src\CodexDesktopPet\CodexDesktopPet.csproj
```

运行后，在浏览器、编辑器或文档中选中一段文字，再按 `Ctrl + Alt + Q`。
得到回答后，可以在气泡底部继续输入追问；桌宠只会保留设置中指定的最近几轮。
关闭窗口会隐藏到系统托盘；从托盘菜单选择“退出”才会结束程序。

当前测试版也会显示在任务栏，便于发现窗口和诊断快捷键问题。

## 设置

首次运行会生成：

```text
%LOCALAPPDATA%\CodexDesktopPet\settings.json
```

如果程序找不到 `codex.exe`，请把 `CodexExecutable` 改为本机 Codex 可执行文件的完整路径。
也可以直接右键桌宠选择“设置…”，调整回答语言、超时、回答长度、上下文轮数、
置顶状态和像素宠物包路径。宠物包格式见
[`docs/pixel-pet-format.md`](docs/pixel-pet-format.md)。

诊断日志位于：

```text
%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log
```

## Codex 集成方式

`main` 是独立 Windows 桌宠程序，通过已经登录的 Codex CLI 获取回答。`plugin` 分支
提供本地 Codex marketplace、插件清单、启动 hook 和安装引导；全局快捷键、桌面窗口与
选区读取仍由 Windows 桌宠进程负责。

## 隐私说明

程序不做持续录屏。只有用户主动按下快捷键或点击“提问”时，当前选中的文字或输入的
问题才会被发送给当前 Codex 服务。选区会作为不可信数据包裹，提示 Codex 不执行其中
的命令。连续对话上下文只保存在内存，退出或点击“清空上下文”后消失。

## 计划

- 自定义全局快捷键
- 像素宠物包编辑器和更多动作
- OCR 截图选区（明确授权后触发，不做持续监控）
- 安装包与自动启动选项
- 插件分支的一键安装/卸载脚本

## License

MIT
