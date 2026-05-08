# AgentCLI — Claude Code Chat for Unity Editor

将 [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) 集成到 Unity Editor 中的聊天窗口插件。支持流式输出、对话上下文管理、气泡消息 UI，可直接在编辑器中与 Claude 进行对话。

## Features

- **Native Editor Window** — 通过 `Tools > Claude Code Chat` 打开，与 Unity 原生窗口一致的操作体验
- **Streaming Response** — 实时解析 Claude CLI 的 `stream-json` 输出，逐字显示回复内容
- **Context Management** — 自动携带历史对话上下文，超出限制时裁剪最早的消息对（默认 100KB）
- **Bubble-style UI** — 用户（蓝色）、Claude（绿色）、系统消息（灰色）角色区分，带时间戳
- **Thinking & Tool Call Details** — 可折叠的 Details 面板展示 Claude 的思考过程和工具调用
- **Persistent Settings** — 通过 `EditorPrefs` 持久化 Claude CLI 路径和上下文模式
- **Process Lifecycle** — 异步进程管理，支持超时控制、手动终止、资源自动清理

## Requirements

- Unity 2022.3 or later
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code/overview) installed and available in PATH

## Installation

1. 将项目复制到你的 Unity 项目的 `Assets/Editor/{项目代码所在目录}/` 目录下
2. 确保 `claude` 命令可在终端中直接调用，或通过 `claude --version` 验证安装
3. 在 Unity 菜单栏点击 `Tools > Claude Code Chat` 打开窗口即可使用

> 如果你的 `claude` 不在 PATH 中，可在**后续版本**中通过设置面板指定绝对路径（设置持久化到 `EditorPrefs`）。

## Usage

1. 打开 `Tools > Claude Code Chat`
2. 在底部输入框中输入问题
3. 按 `Enter` 或点击 **Send** 发送
4. Claude 的回复将流式显示在对话区域中
5. 点击 **Clear** 清空对话历史；点击 **Stop** 中断当前请求

### Context Modes

- **提供上下文 (With Context)** — 将历史对话拼接后一并发送，适合连续对话（目前效果不是很好，不建议开，后续会继续优化）
- **不提供上下文 (Without Context)** — 仅发送当前问题，每次为独立会话

通过窗口标题栏右侧下拉菜单切换。

## Architecture

```
AgentCLI/
├── AgentCLI.cs          # EditorWindow 主窗口，UI 绘制与事件协调
├── ChatMessage.cs       # 对话消息数据模型（User / Claude / System）
├── ContextManager.cs    # 上下文管理与自动裁剪
├── StreamJsonParser.cs  # Claude CLI stream-json 事件解析
└── ThreadWorker.cs      # 异步进程执行器（stdout/stderr 读取、超时、清理）
```

## How It Works

1. 用户在输入框中输入问题并按 Enter
2. `AgentCLI.SendMessage()` 构建 prompt（含历史上下文），启动 `claude -p "<prompt>" --verbose --output-format stream-json --include-partial-messages`
3. `ThreadWorker` 异步读取进程 stdout，逐行触发 `OnMessageReceived` 事件
4. `StreamJsonParser` 解析每行 JSON，提取 `text_delta`（正文）和 `thinking_delta` / `input_json_delta`（Details）
5. UI 通过 `delayCall` + 节流重绘实时更新流式文本
6. 进程完成后，流式文本固化为 `ChatMessage` 加入消息列表

## TODO

- 可选CLI：自由切换claude code/codex，项目仅作为中转框架，提高灵活度
- 面向非技术开发者：提供Unity对象插槽，通过拖拽等方式，在编辑器内一键读取需要操作的对象数据

## License

MIT
