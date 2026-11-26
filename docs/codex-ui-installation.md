# 只用 Codex 界面安装桌宠插件（Windows）

这篇教程适合不想手动打开 PowerShell 或逐条复制命令的用户。你只需在
ChatGPT 桌面版的 **Codex** 界面中发送一段安装请求，由 Codex 完成下载、构建、
注册 marketplace、安装插件和启动 Windows 助手。

> **当前限制：** 插件还没有携带签名的 Windows EXE，因此首次安装仍需在本机运行
> Git、.NET 和 Codex 命令。这些命令由 Codex 执行，你不需要自己打开终端；当界面询问
> 是否允许命令或 Hook 时，仍需要你审核并确认。

## 一、准备 Codex 本地聊天

1. 在 Windows 上打开 ChatGPT 桌面版并登录。
2. 在顶部产品切换器中选择 **Codex**。
3. 创建新聊天，将运行位置设为 **This computer / 这台电脑**。
4. 选择 **Local / 本地** 模式，并选一个允许写入的工作目录。

不要使用 Cloud 任务安装这个插件：桌宠需要在你当前的 Windows 电脑上注册全局快捷键
并运行本地进程。

## 二、把这段话发给 Codex

将下面整段内容复制到 Codex 聊天中发送：

```text
请在这台 Windows 电脑上安装 Codex Desktop Pet 本地插件。

仓库：https://github.com/JYYMVP/codex-desktop-pet.git
稳定插件分支：plugin

请按顺序完成：
1. 先检查 Windows、Git、.NET 8 SDK、codex.exe 和 Codex 登录状态；如果缺少依赖，先解释，得到我确认后再安装。
2. 如果当前没有仓库，把 plugin 分支克隆到一个明确的本地目录；如果已有仓库，不要覆盖未提交的更改。
3. 用 Release、win-x64、framework-dependent 方式发布 src/CodexDesktopPet/CodexDesktopPet.csproj 到 artifacts/win-x64。
4. 使用仓库根目录注册 marketplace，确认名称为 jyymvp-desktop-tools。
5. 安装 codex-desktop-pet@jyymvp-desktop-tools 插件。
6. 执行 plugins/codex-desktop-pet/scripts/install-pet.ps1，将发布产物安装到 %LOCALAPPDATA%/CodexDesktopPet/app。
7. 执行 start-pet.ps1 启动助手。
8. 验证插件已安装且启用、CodexDesktopPet.exe 进程存在，并告诉我日志路径。

不要删除我现有的设置、日志或宠物库。遇到需要系统级权限、下载依赖或信任 Hook 的步骤时，请在 Codex 界面中让我审批。
```

### 测试官方 Pets 增强分支

在该功能合并进 `plugin` 前，如果 GitHub 上已经存在
`codex/official-pets-companion` 远程分支，可以把上面的“稳定插件分支”改为：

```text
测试分支：codex/official-pets-companion
```

如果 GitHub 上还找不到该分支，不要让 Codex 猜测或创建同名远程分支，继续安装
`plugin` 稳定分支。

## 三、审核 Codex 中的授权请求

安装过程中，Codex 可能会请求权限。核对命令的目标后再批准：

- `git clone` 或 `git pull`：获取本项目源码；
- `dotnet publish`：在本机构建 Windows 程序；
- `codex plugin marketplace add`：注册项目的 marketplace；
- `codex plugin add`：安装插件；
- `install-pet.ps1`：复制程序到 `%LOCALAPPDATA%\CodexDesktopPet\app`；
- `start-pet.ps1`：启动已安装的助手。

如果请求删除整个仓库、重置不相关项目、上传私密文件或执行与上述流程无关的命令，
请拒绝并让 Codex 解释原因。

## 四、在 Codex 界面中检查插件

Codex 报告安装完成后：

1. 打开 **Plugins** 页面，或在 Codex 输入框中输入 `/plugins`。
2. 选择 `JYYMVP Desktop Tools` / `jyymvp-desktop-tools` marketplace。
3. 找到 **Codex Desktop Pet**，确认它处于已安装和已启用状态。
4. 如果列表没有刷新，完全退出并重新打开 ChatGPT 桌面版。
5. 在一个新的 Codex 本地聊天中输入：

```text
使用 Codex Desktop Pet 检查桌宠是否已正确安装和运行。
```

本地 marketplace 插件更新后，需要重新加载或重启桌面应用，因为 Codex 使用安装到缓存目录的副本，
而不是每次直接读取仓库文件。

## 五、在 Codex 界面中审核启动与活动 Hooks

插件包含 `SessionStart` 启动 Hook，以及把多个 Codex 聊天状态同步给桌宠的活动 Hooks。
活动脚本只写入状态元数据，不读取或保存提示词、回答和工具参数。

1. 在 Codex 输入框输入 `/hooks`。
2. 打开 Codex Desktop Pet 的各个处理器。
3. 确认命令只指向插件目录中的 `scripts/start-pet.ps1` 和 `hooks/pet-status.ps1`。
4. 选择信任或启用。
5. 新建一个 Codex 本地聊天来验证启动。

插件安装并不等于自动信任 Hook。你应在 Codex 界面中核对命令后自己做决定。

> 如果不信任这些 Hooks，手动启动、划词、OCR、问答和屏幕吐槽仍然可用，只是桌宠不会
> 跟随其他 Codex 聊天显示工作状态。

## 六、在插件详情页配置功能

打开 **Plugins → Codex Desktop Pet → Settings**，可以直接调整：

- 划词快捷键（默认 `Ctrl+Alt+Q`）；
- OCR 框选快捷键（默认 `Ctrl+Alt+R`）；
- 显示/隐藏桌宠快捷键（默认 `Ctrl+Alt+H`）和启动后隐藏开关；
- Codex 活动状态跟随；
- 定时或窗口切换屏幕评论；
- 碎碎念停留时间；
- 独立桌宠置顶与 Fast mode。

官方 Pets 兼容模式目前保留用于回归测试，但该实验路线已暂停；默认使用独立桌宠模式。

保存后会自动重启并应用设置；只有在插件外手动改过配置时，才需要执行详情页中的
**Restart desktop pet**。三个快捷键必须彼此不同；如果某个组合被其他程序占用，其余快捷键仍会继续工作，
托盘通知会指出需要更换的组合。

如果当前 Codex 版本还没有显示原生设置，也可以使用下面的文字请求。

## 七、只通过 Codex 回归测试官方 Pets 兼容模式（已暂停）

如果当前版本已包含该功能，可以直接向 Codex 发送：

```text
请为 Codex Desktop Pet 开启官方 Pets 增强模式，保留默认划词快捷键 Ctrl+Alt+Q。
修改前先读取现有设置，只更改 OfficialPetsCompanionMode，然后重启助手并验证日志。
```

然后在 ChatGPT 桌面版的 **Settings > Pets** 中选择一只官方或自定义宠物。测试时：

1. 在浏览器、文档或编辑器中选中一段文字；
2. 按 `Ctrl + Alt + Q`；
3. 官方 Pets Quick Chat 应当出现并填入提问草稿；
4. 检查草稿后手动按 Enter 发送。

助手不会自动按 Enter。如果它无法确认 ChatGPT/Codex 窗口已获得焦点，它也不会粘贴选中内容。

## 八、常用管理请求

后续管理时，可以继续用普通语句让 Codex 处理。

### 检查状态

```text
请检查 Codex Desktop Pet 插件、Windows 进程、全局快捷键和最近日志，只做读取检查，不要重新安装。
```

### 更新

```text
请安全更新 Codex Desktop Pet plugin 分支，保留现有设置和日志。更新源码后重新发布、安装程序文件、刷新插件并验证版本。
```

### 卸载但保留个人设置

```text
请卸载 Codex Desktop Pet 的程序文件、插件和 marketplace，但保留 settings.json、日志和宠物库。执行前先列出将要删除和保留的路径。
```

## 九、遇到问题时

可以在同一个 Codex 聊天中发送：

```text
请读取 %LOCALAPPDATA%/CodexDesktopPet/desktop-pet.log，区分是插件安装、Hook、快捷键注册、选区读取、官方 Pets 焦点还是 Codex 连接失败。先告诉我原因，不要立即重装。
```

常见情况：

- **Plugins 中看不到项目**：先让 Codex 检查 marketplace 是否已注册，再重启桌面应用。
- **插件已安装，但桌宠没启动**：让 Codex 手动执行 `start-pet.ps1`，不要反复安装插件。
- **快捷键无反应**：检查热键是否被其他程序占用。
- **读不到选中文字**：让 Codex 检查 UI Automation 与剪贴板回退路径的日志。
- **OCR 框选没有出现**：检查 `Ctrl+Alt+R` 是否被占用；也可以从桌宠菜单点击“OCR 框选识别”。
- **官方 Pets 没有被唤起**：先在应用中手动验证 `Windows + Alt + P`，再检查焦点判定。

## 官方参考

- [OpenAI：安装和使用 Plugins](https://learn.chatgpt.com/docs/plugins)
- [OpenAI：ChatGPT 桌面版命令和深层链接](https://learn.chatgpt.com/docs/reference/commands)
- [OpenAI：打包插件与本地 marketplace](https://developers.openai.com/plugins/build/plugins)
- [完整 PowerShell 安装与验证教程](plugin-installation.md)
