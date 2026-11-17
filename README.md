# Codex Desktop Pet

一个面向 Windows 的开源桌面划词助手。它常驻桌面；在任意应用中选中文字后按
`Ctrl + Alt + Q`，桌宠会调用你已经登录的 Codex CLI，并在气泡中给出简短回答。

## 当前功能

- Windows 全局快捷键：`Ctrl + Alt + Q`
- 优先使用 Windows UI Automation 读取选区，失败时临时使用剪贴板
- 通过本机 Codex 登录状态回答，不依赖 Harness
- 只在按下快捷键时读取当前选区，不持续截图或监控桌面
- 透明置顶桌宠、思考动画、回答气泡、复制按钮和系统托盘
- Codex 进程采用只读沙箱和临时会话
- 本地诊断日志，方便判断热键、选区或 Codex 请求卡在哪一步

## 构建与运行

需要 Windows 10/11、.NET 8 SDK，以及已经登录的 Codex CLI。

```powershell
dotnet build .\CodexDesktopPet.sln
dotnet run --project .\src\CodexDesktopPet\CodexDesktopPet.csproj
```

运行后，在浏览器、编辑器或文档中选中一段文字，再按 `Ctrl + Alt + Q`。
关闭窗口会隐藏到系统托盘；从托盘菜单选择“退出”才会结束程序。

当前测试版也会显示在任务栏，便于发现窗口和诊断快捷键问题。

## 设置

首次运行会生成：

```text
%LOCALAPPDATA%\CodexDesktopPet\settings.json
```

如果程序找不到 `codex.exe`，请把 `CodexExecutable` 改为本机 Codex 可执行文件的完整路径。
还可调整回答超时、选区长度和回答长度。

诊断日志位于：

```text
%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log
```

## Codex 集成方式

当前版本是独立的 Windows 桌宠程序，通过已经登录的 Codex CLI 获取回答，尚未作为
Codex 插件安装。后续插件包装会提供 Codex 内的安装入口和启动流程；全局快捷键、桌面
窗口与选区读取仍由 Windows 桌宠进程负责。

## 隐私说明

程序不做持续录屏。只有用户主动按下快捷键时，当前选中的文字才会被发送给当前 Codex
服务以生成回答。选区会作为不可信数据包裹，提示 Codex 不执行其中的命令。

## 计划

- 可视化设置窗口与自定义快捷键
- 更多桌宠外观和动作
- 安装包与自动启动选项
- Codex 本地插件包装与一键安装说明

## License

MIT
