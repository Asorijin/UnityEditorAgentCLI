# UnityEditorAgentCLI — Claude Code 驱动的 Unity 编辑器智能助手

将 [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) 集成到 Unity Editor 中，同时通过多智能体配置实现结构化的需求-编码-审查-总结工作流。

项目包含两个协作组件：

| 组件 | 路径 | 用途 |
|------|------|------|
| **AgentCLI** (编辑器插件) | `AgentCLI/` | Unity Editor 内置聊天窗口，流式对话 UI |
| **UnityAgents** (智能体配置) | `UnityAgents/` | Claude Code 多智能体路由规则与工作流 |

---

## AgentCLI — 编辑器内聊天窗口

将 Claude Code CLI 嵌入 Unity Editor，提供流式输出、对话上下文管理、气泡消息 UI，可直接在编辑器中与 Claude 进行对话。

### Features

- **Native Editor Window** — 通过 `Tools > Claude Code Chat` 打开，与 Unity 原生窗口一致的操作体验
- **Streaming Response** — 实时解析 Claude CLI 的 `stream-json` 输出，逐字显示回复内容
- **Context Management** — 自动携带历史对话上下文，超出限制时裁剪最早的消息对（默认 100KB）
- **Bubble-style UI** — 用户（蓝色）、Claude（绿色）、系统消息（灰色）角色区分，带时间戳
- **Thinking & Tool Call Details** — 可折叠的 Details 面板展示 Claude 的思考过程和工具调用
- **Persistent Settings** — 通过 `EditorPrefs` 持久化 Claude CLI 路径和上下文模式
- **Process Lifecycle** — 异步进程管理，支持超时控制、手动终止、资源自动清理

### Requirements

- Unity 2022.3 or later
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code/overview) installed and available in PATH

### Installation

1. 将项目复制到你的 Unity 项目的 `Assets/Editor/{项目代码所在目录}/` 目录下
2. 确保 `claude` 命令可在终端中直接调用，或通过 `claude --version` 验证安装
3. 在 Unity 菜单栏点击 `Tools > Claude Code Chat` 打开窗口即可使用

> 如果你的 `claude` 不在 PATH 中，可在**后续版本**中通过设置面板指定绝对路径（设置持久化到 `EditorPrefs`）。

### Usage

1. 打开 `Tools > Claude Code Chat`
2. 在底部输入框中输入问题
3. 按 `Enter` 或点击 **Send** 发送
4. Claude 的回复将流式显示在对话区域中
5. 点击 **Clear** 清空对话历史；点击 **Stop** 中断当前请求

#### Context Modes

- **提供上下文 (With Context)** — 将历史对话拼接后一并发送，适合连续对话（目前效果不是很好，不建议开，后续会继续优化）
- **不提供上下文 (Without Context)** — 仅发送当前问题，每次为独立会话

通过窗口标题栏右侧下拉菜单切换。

### Architecture

```
AgentCLI/
├── AgentCLI.cs          # EditorWindow 主窗口，UI 绘制与事件协调
├── ChatMessage.cs       # 对话消息数据模型（User / Claude / System）
├── ContextManager.cs    # 上下文管理与自动裁剪
├── StreamJsonParser.cs  # Claude CLI stream-json 事件解析
└── ThreadWorker.cs      # 异步进程执行器（stdout/stderr 读取、超时、清理）
```

### How It Works

1. 用户在输入框中输入问题并按 Enter
2. `AgentCLI.SendMessage()` 构建 prompt（含历史上下文），启动 `claude -p "<prompt>" --verbose --output-format stream-json --include-partial-messages`
3. `ThreadWorker` 异步读取进程 stdout，逐行触发 `OnMessageReceived` 事件
4. `StreamJsonParser` 解析每行 JSON，提取 `text_delta`（正文）和 `thinking_delta` / `input_json_delta`（Details）
5. UI 通过 `delayCall` + 节流重绘实时更新流式文本
6. 进程完成后，流式文本固化为 `ChatMessage` 加入消息列表

---

## UnityAgents — 多智能体工作流配置

为 Unity 2022 项目配置的 Claude Code 专用智能体系统，构建 **Requirement → Coding → Review → Summary** 结构化工作流。使用时提供尽可能详细的操作对象路径和技术细节以提升效率。

### 智能体架构

根智能体 (`CLAUDE.md`) 根据用户请求自动路由到对应专用智能体：

#### code-agent — C# 脚本编辑

- 验证目标脚本是否存在
- 安全更新 .cs 文件或创建新的非 MonoBehaviour 类
- 自动应用 Unity 规范（`[SerializeField]`、`[RequireComponent]`、`[MenuItem]` 等）
- 编译前必须读取 `unity-attributes-guide.md` 参考手册
- 最小化修改范围，保留原有代码结构

#### analyze-agent — 项目只读分析

- 枚举 C# 脚本、ScriptableObject、程序集定义
- 解析 .asmdef 依赖关系
- 映射继承关系和序列化状态
- 生成结构化报告（`ProjectInfo.json` / `ProjectInfo.md`）
- 仅扫描 `Assets/`，不访问 PackageCache 或 Library

#### review-agent — 代码审查

审查维度：
- 用户意图正确性
- Unity C# 语法和语义有效性
- 运行时安全性（空引用、生命周期、事件清理等）

不审查命名风格或格式。发现问题时返回结构化建议，不直接修改代码。

### 工作流

```
用户需求 → 根智能体路由
              ├─ code-agent     → 编辑/创建 C# 脚本
              ├─ analyze-agent  → 分析项目结构
              └─ review-agent   → 审查代码变更（由其他智能体调用）
                                       ↓
                               根智能体转发审查结果
```

### 可用技能（Skills）

- `unity-attributes-guide` — Unity C# 属性与最佳实践参考手册
- `update-config` — 配置 settings.json
- `simplify` — 审查和优化代码质量
- `claude-api` — Claude API / SDK 开发

### 使用方式

将 `UnityAgents/` 下的内容放入目标 Unity 项目的 `.claude/` 目录，Claude Code 将自动加载路由规则和智能体配置。

> 根智能体仅负责路由，不执行具体任务 —— 所有工作由专用子智能体完成。

---

## TODO

- 可选CLI：自由切换claude code/codex，项目仅作为中转框架，提高灵活度
- 面向非技术开发者：提供Unity对象插槽，通过拖拽等方式，在编辑器内一键读取需要操作的对象数据
- review-agent 结果自动反馈至 code-agent 形成审查闭环
- 定期整理脚本依赖关系

## License

MIT
