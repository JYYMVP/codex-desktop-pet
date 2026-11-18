# 插件版安装与验证（Windows）

本教程用于亲自验证 `plugin` 分支。插件负责在 Codex 中提供发现、安装引导和
`SessionStart` 启动 hook；透明窗口、全局快捷键和选区读取仍由独立的 Windows
桌宠进程完成。

> 当前状态：插件清单、marketplace、安装脚本和插件技能已经验证可用。当前测试环境中的
> Codex CLI `0.169.0-alpha.11.1` 没有实际派发插件的 `SessionStart` hook，因此请先按本文的
> “手动启动验证”确认桌宠本体可用。这个限制不会影响普通版或手动启动。

## 1. 准备环境

需要：

- Windows 10/11；
- Git；
- .NET 8 SDK；
- 已安装并登录的 Codex 桌面版/CLI。

在 PowerShell 中检查：

```powershell
git --version
dotnet --version
codex --version
codex login status
```

## 2. 获取 `plugin` 分支

第一次下载：

```powershell
Set-Location D:\JYYMVP_Workspace
git clone --branch plugin --single-branch `
  https://github.com/JYYMVP/codex-desktop-pet.git `
  codex-desktop-pet-plugin
Set-Location .\codex-desktop-pet-plugin
```

仓库是私有仓库时，GitHub 可能要求使用已经登录的 Git Credential Manager 或 GitHub CLI。

如果目录已经存在：

```powershell
Set-Location D:\JYYMVP_Workspace\codex-desktop-pet-plugin
git switch plugin
git pull --ff-only origin plugin
```

后面的命令都在该仓库根目录执行。

## 3. 发布 Windows 桌宠

```powershell
dotnet publish .\src\CodexDesktopPet\CodexDesktopPet.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\artifacts\win-x64

Test-Path .\artifacts\win-x64\CodexDesktopPet.exe
```

最后一条命令应返回 `True`。

## 4. 添加本地 marketplace

```powershell
codex plugin marketplace add . --json
codex plugin marketplace list --json
```

列表中应出现：

```text
jyymvp-desktop-tools
```

这里的 `.` 指向当前仓库；Codex 会读取
`.agents/plugins/marketplace.json`。

## 5. 安装插件

```powershell
codex plugin add codex-desktop-pet@jyymvp-desktop-tools --json
codex plugin list --json
```

确认列表中存在以下插件，并且 `installed`、`enabled` 都是 `true`：

```text
codex-desktop-pet@jyymvp-desktop-tools
```

当前 CLI 的安装子命令是 `plugin add`，不是 `plugin install`。

## 6. 安装桌宠程序文件

插件本身不携带编译后的 EXE，需要把第 3 步的发布结果复制到稳定位置：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\plugins\codex-desktop-pet\scripts\install-pet.ps1 `
  -SourceDirectory .\artifacts\win-x64

Test-Path "$env:LOCALAPPDATA\CodexDesktopPet\app\CodexDesktopPet.exe"
```

最后一条命令应返回 `True`。程序文件位于：

```text
%LOCALAPPDATA%\CodexDesktopPet\app
```

设置和日志位于该目录的上一级，不会在普通卸载时删除。

## 7. 手动启动验证

如果普通独立版桌宠正在运行，请先从系统托盘菜单选择“退出”。启动脚本检测到任意同名
进程后不会再启动第二份，因此必须先确保下面的命令没有输出：

```powershell
Get-Process CodexDesktopPet -ErrorAction SilentlyContinue
```

然后直接执行插件使用的同一份启动脚本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\plugins\codex-desktop-pet\scripts\start-pet.ps1

Get-Process CodexDesktopPet | Select-Object Id, Path
```

应看到“Codex 桌宠”窗口，而且 `Path` 应指向：

```text
%LOCALAPPDATA%\CodexDesktopPet\app\CodexDesktopPet.exe
```

然后依次验证：

1. 在浏览器或编辑器中选中一段普通文字；
2. 按 `Ctrl + Alt + Q`；
3. 等待气泡显示回答；
4. 单击桌宠收起/展开气泡；
5. 在输入框继续追问；
6. 点击“Codex 用量”查看套餐额度。

关闭窗口只会隐藏到托盘；需要从托盘菜单选择“退出”才能完全结束。

## 8. 验证 Codex 自动启动 hook

先从托盘退出第 7 步启动的桌宠，确保不存在 `CodexDesktopPet` 进程。然后完全退出并重新打开
Codex，或启动一个新的 CLI 会话。首次遇到插件 hook 审核时，请先核对
命令确实指向本仓库的 `scripts/start-pet.ps1`，然后由你决定是否信任。Codex 不会因为插件
已安装就自动信任其中的 hook。

如果新会话没有自动出现桌宠：

- 先用第 7 步确认启动脚本本身可用；
- 再运行 `codex plugin list --json`，确认插件仍处于启用状态；
- 当前已知的 Codex CLI alpha 版本可能不派发插件 `SessionStart` hook，此时继续手动启动即可；
- 不要反复安装插件来解决 hook 不派发的问题。

hook 若被执行，会尝试在插件数据目录写入 `desktop-pet-hook.log`。可以这样查找：

```powershell
Get-ChildItem "$env:USERPROFILE\.codex\plugins" `
  -Recurse `
  -Filter desktop-pet-hook.log `
  -ErrorAction SilentlyContinue
```

桌宠本体日志始终位于：

```text
%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log
```

## 9. 更新插件分支

```powershell
git pull --ff-only origin plugin
codex plugin remove codex-desktop-pet@jyymvp-desktop-tools --json
codex plugin add codex-desktop-pet@jyymvp-desktop-tools --json
```

如果桌宠程序也有更新，请重新执行第 3 步和第 6 步。

## 10. 完整卸载

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\plugins\codex-desktop-pet\scripts\uninstall-pet.ps1

codex plugin remove codex-desktop-pet@jyymvp-desktop-tools --json
codex plugin marketplace remove jyymvp-desktop-tools --json
```

验证：

```powershell
codex plugin list --json
codex plugin marketplace list --json
Test-Path "$env:LOCALAPPDATA\CodexDesktopPet\app"
```

最后一条应返回 `False`。普通卸载会保留：

```text
%LOCALAPPDATA%\CodexDesktopPet\settings.json
%LOCALAPPDATA%\CodexDesktopPet\desktop-pet.log
```

如需连个人设置和日志一起清除，请在确认不再需要后手动删除
`%LOCALAPPDATA%\CodexDesktopPet`。

## 常见问题

### `codex plugin` 不存在

当前 Codex 版本过旧或该功能尚未包含在你的客户端中。先更新 Codex，再运行
`codex plugin --help` 检查是否有 `add`、`list`、`marketplace` 和 `remove`。

### 快捷键有反应，但读不到浏览器选区

先单击网页正文并重新选中文字。程序优先通过 Windows UI Automation 读取选区，失败时才
短暂使用剪贴板。查看 `desktop-pet.log` 可以区分热键注册、选区读取和 Codex 调用问题。

### 回答很慢

设置中的推理强度建议保持 `low`。桌宠子进程默认关闭 hooks 和 MCP，并使用临时会话，
以避免插件递归启动以及无关工具初始化；网络连接和模型响应时间仍会造成波动。

## 参考

- [OpenAI：Package your plugin](https://developers.openai.com/plugins/build/plugins)
- [项目仓库](https://github.com/JYYMVP/codex-desktop-pet)
