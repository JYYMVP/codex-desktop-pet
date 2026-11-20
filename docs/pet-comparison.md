# 桌宠方案对比与开发优先级

更新时间：2026-11-20。这里比较的是公开可见能力；官方 Codex Pets 的内部实现并未开放，
本项目不会复制其私有代码或素材。

| 方面 | 官方 Codex Pets | Harness / DSH Pet | Codex Desktop Pet |
| --- | --- | --- | --- |
| 与宿主融合 | 原生集成，无额外辅助程序 | 插件负责事件，通常由透明桌面窗口承载 | 插件 Hook + 无控制台 Windows 辅助进程；默认与 Codex 同启同退 |
| 工作状态 | 原生思考、等待和完成动画 | 思考、工具、等待确认、完成、失败，部分实现含真实进度 | 接收任务、思考、工具、整理、子任务、确认、完成和阻塞；不读取聊天内容 |
| 桌面互动 | 轻量点击和动画 | 随机动作、游走、拖拽物理、悬停反馈、多宠物 | 悬停跳跃、点击挥手、方向拖动和手动动作；物理与多宠物尚未实现 |
| 提问能力 | Codex / Work 内原生对话 | 依实现支持文字、语音、记忆 | 划词、OCR、快速提问、Markdown 回答、每只宠物独立 Codex 对话 |
| 屏幕感知 | 由宿主能力和权限决定 | 常见为可选截图评论，默认关闭 | 手动、定时和窗口切换评论；自动模式默认关闭并删除临时截图 |
| 自定义 | v1/v2 精灵表和宠物库 | 宠物包、Live2D、Workshop 等 | v1/v2 导入、本地宠物包、像素动画时间线与洋葱皮编辑器 |

## 当前优先级

1. **宿主融合**：无控制台启动、退出后下一次 Codex 提问自动恢复、Codex 结束后自动退出。
2. **真实状态反馈**：只使用生命周期事件显示任务阶段和多聊天数量，不伪造进度。
3. **自然互动**：点击、悬停、拖动方向、空闲动作和减少动态效果。
4. **下一阶段**：宠物尺寸、空闲行为权重、更多内置动画和可选语音播报。

公网宠物领养与分享暂不作为近期开发主线；本地 `.codexpet` 导入/导出继续保留。

参考：

- [OpenAI：ChatGPT Work and Codex](https://help.openai.com/en/articles/20001275-chatgpt-work-and-codex)
- [DSH Pet 插件说明](https://deepseek-harness-plugin-hub.toddzheng024.workers.dev/en/plugins/dsh-pet)
- [Harness 大肥鱼状态与设置说明](https://deepseekharnessplugins.com/learn/desktop-pet)
- [dsh-blue-fat-fish-pet](https://github.com/ShiZhenan98/dsh-blue-fat-fish-pet)
