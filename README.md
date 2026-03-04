# OpenLumSharp

[English](#english) | [中文](#中文)

---

## English

**OpenLumSharp** is a personal AI assistant runtime implemented in C#. It is inspired by [OpenClaw](https://github.com/openclaw/openclaw), reimagined in .NET for developers who prefer the C# ecosystem.

> OpenClaw is a personal AI assistant that runs on your own devices. OpenLumSharp brings similar concepts—agent loop, tools, skills, sessions—into a native C# implementation.

### Features

- **Agent loop** — ReAct-style loop with tool calling and streaming responses
- **Tools** — Built-in tools: `read`, `write`, `list_dir`, `exec`, `memory_get`, `memory_search`, `sessions_spawn`
- **Skills** — Load external skills from `Skills/` directory (similar to OpenClaw skills)
- **Session compaction** — Optional context summarization to reduce token usage
- **Model providers** — OpenAI API–compatible providers (OpenAI, DeepSeek, Ollama, etc.)
- **Tool policy** — Allow/deny tool access via configuration

### Projects

| Project | Description |
|---------|-------------|
| **OpenLum.Console** | CLI agent with REPL, supports Native AOT publishing |
| **OpenLum.Browser** | Browser automation companion using Microsoft Playwright |

### Requirements

- .NET 8.0+ (target: .NET 10.0; can fall back to .NET 9.0 if needed)
- `openlum.json` with `apiKey` and model settings

### Quick Start

1. Clone the repository:
   ```bash
   git clone https://github.com/LdotJdot/OpenLum.git
   cd OpenLum
   ```

2. Create `OpenLum.Console/openlum.json` (copy from example below):
   ```json
   {
     "model": {
       "provider": "DeepSeek",
       "model": "deepseek-chat",
       "baseUrl": "https://api.deepseek.com/v1",
       "apiKey": "your-api-key"
     },
     "workspace": ".",
     "compaction": {
       "enabled": true,
       "maxMessagesBeforeCompact": 30,
       "reserveRecent": 10
     }
   }
   ```

3. Run the console agent:
   ```bash
   dotnet run --project OpenLum.Console
   ```

4. (Optional) Publish as a single-file executable with Native AOT:
   ```bash
   dotnet publish OpenLum.Console -c Release -r win-x64
   ```

### Configuration

Config is loaded from (in order): `openlum.json` → `openlum.console.json` → `appsettings.json`.

| Key | Description |
|-----|-------------|
| `model.provider` | Provider name (e.g., OpenAI, DeepSeek, Ollama) |
| `model.model` | Model identifier |
| `model.baseUrl` | API base URL |
| `model.apiKey` | API key |
| `model_backup` | Fallback model when primary fails |
| `workspace` | Workspace root for tools |
| `compaction.enabled` | Enable session compaction |
| `compaction.maxMessagesBeforeCompact` | Messages before compacting |
| `compaction.reserveRecent` | Recent messages to keep |
| `tools.profile` | Tool policy profile (`local`, etc.) |
| `tools.allow` / `tools.deny` | Tool allow/deny lists |

### Comparison with OpenClaw

| Aspect | OpenClaw | OpenLumSharp |
|--------|----------|--------------|
| Language | TypeScript/Node.js | C# / .NET |
| Runtime | Node ≥22 | .NET 8+ |
| Deployment | npm, Docker | `dotnet run`, Native AOT |
| Channels | WhatsApp, Telegram, Slack, etc. | Console REPL (extensible) |
| Tools & Skills | Full ecosystem | Core tools + skills loading |

OpenLumSharp focuses on a minimal, self-contained agent runtime. It does not include multi-channel messaging or companion apps; it is designed for local CLI use and integration into your own .NET workflows.

### License

MIT License. See [LICENSE.txt](LICENSE.txt) for details.

---

## 中文

**OpenLumSharp** 是一个使用 C# 实现的个人 AI 助手运行时。它参考了 [OpenClaw](https://github.com/openclaw/openclaw)，用 .NET 重新实现，面向偏好 C# 生态的开发者。

> OpenClaw 是运行在本地的个人 AI 助手。OpenLumSharp 将类似概念——Agent 循环、工具、技能、会话——以原生 C# 的形式实现。

### 功能特点

- **Agent 循环** — 基于 ReAct 的工具调用与流式响应
- **工具** — 内置：`read`、`write`、`list_dir`、`exec`、`memory_get`、`memory_search`、`sessions_spawn`
- **技能** — 从 `Skills/` 目录加载外部技能（类似 OpenClaw 的 skills）
- **会话压缩** — 可选的上下文摘要以降低 token 消耗
- **模型提供者** — 兼容 OpenAI API 的提供者（OpenAI、DeepSeek、Ollama 等）
- **工具策略** — 通过配置允许/禁止工具访问

### 项目构成

| 项目 | 说明 |
|------|------|
| **OpenLum.Console** | 命令行 Agent，支持 REPL，可发布为 Native AOT |
| **OpenLum.Browser** | 基于 Microsoft Playwright 的浏览器自动化伴侣 |

### 环境要求

- .NET 8.0 或更高（目标框架 .NET 10.0，可回退到 .NET 9.0）
- 在 `openlum.json` 中配置 `apiKey` 和模型参数

### 快速开始

1. 克隆仓库：
   ```bash
   git clone https://github.com/LdotJdot/OpenLum.git
   cd OpenLum
   ```

2. 在 `OpenLum.Console/` 下创建 `openlum.json`（可参考下方示例）：
   ```json
   {
     "model": {
       "provider": "DeepSeek",
       "model": "deepseek-chat",
       "baseUrl": "https://api.deepseek.com/v1",
       "apiKey": "your-api-key"
     },
     "workspace": ".",
     "compaction": {
       "enabled": true,
       "maxMessagesBeforeCompact": 30,
       "reserveRecent": 10
     }
   }
   ```

3. 运行控制台 Agent：
   ```bash
   dotnet run --project OpenLum.Console
   ```

4. （可选）以 Native AOT 单文件可执行形式发布：
   ```bash
   dotnet publish OpenLum.Console -c Release -r win-x64
   ```

### 配置说明

配置按优先级从以下文件中加载：`openlum.json` > `openlum.console.json` > `appsettings.json`。

| 配置项 | 说明 |
|--------|------|
| `model.provider` | 提供者名称（如 OpenAI、DeepSeek、Ollama） |
| `model.model` | 模型标识 |
| `model.baseUrl` | API 基础地址 |
| `model.apiKey` | API 密钥 |
| `model_backup` | 主模型失败时的备用模型 |
| `workspace` | 工具工作区根目录 |
| `compaction.enabled` | 是否启用会话压缩 |
| `compaction.maxMessagesBeforeCompact` | 压缩前的消息数量 |
| `compaction.reserveRecent` | 压缩后保留的最近消息数 |
| `tools.profile` | 工具策略（如 `local`） |
| `tools.allow` / `tools.deny` | 工具白名单 / 黑名单 |

### 与 OpenClaw 的对比

| 方面 | OpenClaw | OpenLumSharp |
|------|----------|--------------|
| 语言 | TypeScript / Node.js | C# / .NET |
| 运行时 | Node ≥22 | .NET 8+ |
| 部署方式 | npm、Docker | `dotnet run`、Native AOT |
| 渠道 | WhatsApp、Telegram、Slack 等 | 控制台 REPL（可扩展） |
| 工具与技能 | 完整生态 | 核心工具 + 技能加载 |

OpenLumSharp 聚焦于轻量、自包含的 Agent 运行时，不包含多渠道消息或配套应用，适合在本地 CLI 使用，或集成到自己的 .NET 工作流中。

### 许可证

MIT License。详见 [LICENSE.txt](LICENSE.txt)。
