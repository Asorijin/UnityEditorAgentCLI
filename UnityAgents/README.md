# Claude Code for Unity 2022

本项目为 Unity 2022 开发配置了专用的 Claude Code 智能体系统，构建了简单的 requirement-coding-review-summary workflow。

使用时需要提供尽可能详细的操作对象路径和具体技术细节，以提升对话效率

## 智能体架构

### 根智能体路由规则

根据 `CLAUDE.md` 定义，用户请求会被自动路由到以下专用智能体：

- **codeAgent** - C# 脚本编辑任务
- **analyzeAgent** - 只读分析任务
- **reviewAgent** - 代码审查任务

### 1. codeAgent

**用途**: 修改现有的 Unity 2022 C# 脚本

**功能**:
- 验证目标脚本是否存在
- 安全更新 .cs 文件
- 应用 Unity 规范（SerializeField、RequireComponent、MenuItem 等）
**约束**:
- 可修改已有脚本或创建新的非 MonoBehaviour 类
- 最小化修改范围
- 编译前必须读取 `unity-attributes-guide.md`

### 2. analyzeAgent

**用途**: 分析项目结构和代码组织

**功能**:
- 枚举 C# 脚本、ScriptableObject、程序集定义
- 解析程序集依赖（.asmdef）
- 映射继承关系和序列化状态
- 生成结构化报告（ProjectInfo.json / ProjectInfo.md）

**约束**:
- 只读操作，不修改文件
- 默认仅扫描 `Assets/` 目录
- 不访问 PackageCache 或 Library（除非明确指定）

### 3. reviewAgent

**用途**: 代码审查（由其他智能体调用）

**审查范围**:
- 用户意图正确性
- Unity C# 语法和语义有效性
- 运行时安全性

**约束**:
- 不修改代码
- 不审查命名风格或格式
- 发现问题时返回用户决策

## 可用技能（Skills）

- **update-config** - 配置 settings.json
- **simplify** - 审查和优化代码质量
- **loop** - 定时执行命令
- **schedule** - 创建定时任务
- **claude-api** - 使用 Claude API 开发

---

*本配置遵循最小化原则，仅在必要时调用智能体，避免过度自动化。*

> TODO : 为分析出的工程结构嵌入向量数据库供其他模块查询；为 codeAgent 接入 reviewAgent 代码审查功能；需要定期整理脚本依赖关系；
