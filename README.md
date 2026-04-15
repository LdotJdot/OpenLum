# OpenLumSharp

[English](#english) | [中文](#中文)

---

## English

**OpenLumSharp** — C# personal AI assistant runtime. Inspired by [OpenClaw](https://github.com/openclaw/openclaw), implemented in .NET.

### Features

- **Agent loop** — ReAct-style loop, tool calling, streaming, with configurable tool-turn limits and “model decide at limit”.
- **Native tools (Tier-1)** — Optimized for code/text workflows:
  - File & edit: `read`, `read_many`, `write`, `str_replace`, `text_edit`, `list_dir`, `todo`, `submit_plan`
  - Search: `grep`, `glob`
  - Runtime: `exec`
  - Memory: `memory_get`, `memory_search`
  - Sub-agents: `sessions_spawn`
- **Search → Read → Edit workflow** — Encourages `glob` → `grep` → `read(offset/limit)` → `str_replace` / `text_edit` / `write`, with read-like tools executed in parallel for speed.
- **Workflow phases (optional)** — Observable / Act / Verify phases with per-phase tool allowlists and optional “plan required before writes”.
- **Skills** — External skills from `Skills/` (see [Skill execution](#skill-execution-extension))
- **Session compaction** — Optional context summarization
- **Model** — OpenAI API–compatible (OpenAI, DeepSeek, Ollama, etc.)
- **Tool policy** — Allow/deny by profile and allow/deny lists

### Projects

| Project | Description |
|---------|-------------|
| **OpenLum.Console** | CLI agent, REPL, Native AOT publish |
| **OpenLum.Browser** | Legacy Playwright browser (optional). **Browser automation is now via the agent-browser skill** (see below). |
| **OpenLum.Core** | Shared library |
| **OpenLum.Tests** | Unit tests |

### Documentation

- **[Agent efficiency & tooling roadmap](docs/AGENT_EFFICIENCY_ROADMAP.md)** — Architecture and implementation plan for native tools (grep, str_replace, path resolution, Skill fusion, parallel execution).

### Browser automation

Browser actions (open, snapshot, click, type, screenshot, etc.) are provided by the **agent-browser** skill: the agent uses the `exec` tool to run the [agent-browser](https://agent-browser.dev/commands) CLI. Install it locally (`npm` global or per-project) and ensure `agent-browser` is on PATH. The old **OpenLum.Browser** (Playwright) is no longer used by the console; it remains in the repo as an optional/legacy component.

### Skill execution extension

Skills extend the agent without new C# tools:

1. **Discovery** — The app scans for skills in:
   - `workspace/skills/*/`
   - `AppContext.BaseDirectory/skills/*/` (under the console build output when copied)
   - Parent directory `skills/*/`
   Each subfolder that contains a `SKILL.md` is one skill. Priority: workspace > app base > parent; same name skips lower priority.

2. **Metadata** — From each `SKILL.md`, frontmatter is parsed:
   - `name:` — Skill name (default: folder name)
   - `description:` — Short description for the model

3. **Prompt** — Skill list (name, description, path to `SKILL.md`) is injected into the system prompt. The model is told to use the **read** tool to load a skill’s `SKILL.md` when needed, and to use **exec** to run commands from that documentation. Never infer exe names from skill names; always read `SKILL.md` first.

4. **Execution** — The model reads `SKILL.md` for usage and paths, then runs:
   - Bundled executables under `InternalTools/` (including extractors under `InternalTools/read/`), or skill-scoped exes under `Skills/`, or
   - Shell commands as documented in the skill.

Adding a skill: add a folder under `OpenLum.Console/Skills/<SkillName>/` with `SKILL.md` (and optional exes). It is copied to output/publish automatically.

### Requirements

- .NET 10.0 (or .NET 9.0 fallback)
- `openlum.json` with `apiKey` and model settings

### Quick Start

1. Clone and enter:
   ```bash
   git clone https://github.com/LdotJdot/OpenLumSharp.git
   cd OpenLumSharp
   ```

2. Create `OpenLum.Console/openlum.json`:
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

3. Run:
   ```bash
   dotnet run --project OpenLum.Console
   ```

4. Optional — Native AOT single-file publish:
   ```bash
   dotnet publish OpenLum.Console -c Release -r win-x64
   ```

### Configuration

Load order: `openlum.json` → `openlum.console.json` → `appsettings.json`.

| Key | Description |
|-----|-------------|
| `model.provider` | Provider name (OpenAI, DeepSeek, Ollama, …) |
| `model.model` | Model id |
| `model.baseUrl` | API base URL |
| `model.apiKey` | API key. If empty, read from env `OPENLUM_API_KEY`. |
| `workspace` | Workspace root for tools |
| `compaction.enabled` | Enable compaction |
| `compaction.maxMessagesBeforeCompact` | Messages before compact |
| `compaction.reserveRecent` | Messages to keep after compact |
| `compaction.maxToolResultChars` | Max chars from a single tool result kept in history |
| `compaction.maxFailedToolResultChars` | Max chars for failed tool results when sending to model |
| `tools.profile` | `minimal` \| `coding` \| `messaging` \| `local` \| `full` |
| `tools.allow` / `tools.deny` | Tool or group names |
| `workflow.enabled` | Enable Observe/Act/Verify workflow phases |
| `workflow.requirePlanForWrite` | When true, writes are gated behind a submitted plan or TODOs in Observe phase |
| `workflow.autoVerifyAfterFirstWrite` | Auto-switch to Verify phase after first write-like tool |
| `search.skipDirs` | Extra directory names to skip when scanning (on top of defaults like `.git`, `bin`, `obj`) |
| `search.skipGlobs` | Globs of paths to skip when scanning (matched against relative path) |

Env overrides: `OPENLUM_PROVIDER`, `OPENLUM_MODEL`, `OPENLUM_BASE_URL`, `OPENLUM_API_KEY`. Strict config: `OPENLUM_STRICT_CONFIG=1`.

#### Tool profiles

| Profile | Base tools |
|---------|------------|
| `minimal` | `list_dir` only |
| `coding` | group:fs, group:web, group:runtime |
| `messaging` | `list_dir` only |
| `local` / `full` | group:fs, group:web, group:runtime, group:memory, group:sessions |

Refine with `allow`/`deny`. Deny all tools: `"deny": ["*"]`.

### Comparison with OpenClaw

| Aspect | OpenClaw | OpenLumSharp |
|--------|----------|--------------|
| Language / runtime | TypeScript/Node.js, Node ≥22 | C# / .NET 10  |
| Deployment | npm, Docker | `dotnet run`, Native AOT single-file |
| Channels | WhatsApp, Telegram, Slack, etc. | Console REPL (extensible) |
| Tools & skills | Full ecosystem, skills + tools | Core tools + Skills dir (read/exec–driven) |
| Browser | Built-in or companion browser | agent-browser skill (local CLI) |
| Focus | Multi-channel messaging, companion apps | Local CLI, self-contained runtime, .NET integration |

OpenClaw focuses on multi-channel messaging and companion UX; OpenLumSharp on a minimal, local agent runtime and C# ecosystem integration.

### License

MIT. See [LICENSE.txt](LICENSE.txt).

---

## 中文

**OpenLumSharp** — 使用 C# 实现的个人 AI 助手运行时。参考 [OpenClaw](https://github.com/openclaw/openclaw)，在 .NET 中实现。

### 功能

- **Agent 循环** — ReAct 风格循环、工具调用、流式输出，支持可配置的工具调用轮数与“由模型在上限时决策”。
- **原生工具（Tier-1）** — 针对代码/文本场景做了窄接口优化：
  - 文件与编辑：`read`、`read_many`、`write`、`str_replace`、`text_edit`、`list_dir`、`todo`、`submit_plan`
  - 搜索：`grep`、`glob`
  - 运行：`exec`
  - 记忆：`memory_get`、`memory_search`
  - 子会话：`sessions_spawn`
- **Search → Read → Edit 工作流** — 鼓励 `glob` → `grep` → `read(offset/limit)` → `str_replace` / `text_edit` / `write` 的模式，read 类工具一轮内可并行执行以降低总耗时。
- **可选阶段工作流** — Observe / Act / Verify 阶段化暴露工具，可配置“写操作前必须先提交计划/维护 TODO 列表”。
- **技能（Skills）** — 从 `Skills/` 加载外部能力（见 [Skill 执行扩展](#skill-执行扩展)）
- **会话压缩** — 可选上下文摘要
- **模型** — 兼容 OpenAI API（OpenAI、DeepSeek、Ollama 等）
- **工具策略** — 按 profile 与 allow/deny 控制

### 项目

| 项目 | 说明 |
|------|------|
| **OpenLum.Console** | 命令行 Agent，REPL，可 Native AOT 发布 |
| **OpenLum.Browser** | 旧版 Playwright 浏览器（可选）。**浏览器自动化已改为 agent-browser 技能**（见下）。 |
| **OpenLum.Core** | 公共库 |
| **OpenLum.Tests** | 单元测试 |

### 文档

- **[智能体效率与工具面路线图](docs/AGENT_EFFICIENCY_ROADMAP.md)** — 原生工具、路径统一解析、Skill 融合与并行调度等设计与实施清单。

### 浏览器自动化

浏览器操作（打开、快照、点击、输入、截图等）由 **agent-browser** 技能提供：Agent 通过 `exec` 调用本机 [agent-browser](https://agent-browser.dev/commands) CLI。请在本机安装（如 `npm i -g @agent-browser/cli` 或项目内安装），并保证 `agent-browser` 在 PATH 中。原 **OpenLum.Browser**（Playwright）已不再被控制台使用，仅作为可选/遗留保留在仓库中。

### Skill 执行扩展

技能在不新增 C# 工具的前提下扩展 Agent 能力：

1. **发现** — 仅从 **宿主根目录（host root）** 下的 `skills/` 或 `Skills/` 扫描（与用户 **workspace** 分离）。宿主根目录默认与 `openlum-console` 同目录，可通过 `openlum.json` 的 `hostRoot` 或环境变量 `OPENLUM_HOST_ROOT` 指定。与 `skills` 同级应有 `InternalTools/`（内置提取器等）。源码中技能位于 `OpenLum.Core/Skills/`，构建时复制到宿主输出目录。

2. **元数据** — 从每个 `SKILL.md` 解析 frontmatter：
   - `name:` — 技能名（默认取目录名）
   - `description:` — 给模型看的简短说明

3. **注入** — 技能列表（name、description、SKILL.md 路径）写入 system prompt。模型被要求：需要时用 **read** 工具读取该技能的 `SKILL.md`，用 **exec** 执行命令（如技能目录下的 exe 或 `agent-browser ...`）。禁止根据技能名猜测 exe 名，必须先读 `SKILL.md`。

4. **执行** — 模型按 `SKILL.md` 的用法与路径执行：
   - 宿主内置目录 `InternalTools/` 下的可执行文件（如 `InternalTools/read/...` 的提取器），或 `Skills/` 下的技能专用 exe，或
   - Shell 命令（如 `agent-browser open --headed "https://example.com"`）。

新增技能：在 `OpenLum.Core/Skills/<技能名>/` 下放 `SKILL.md`（及可选 exe），随 Core 项目复制到宿主目录。

### 环境要求

- .NET 10.0（或 .NET 9.0 回退）
- `openlum.json` 中配置 `apiKey` 与模型

### 快速开始

1. 克隆并进入：
   ```bash
   git clone https://github.com/LdotJdot/OpenLumSharp.git
   cd OpenLumSharp
   ```

2. 在 `OpenLum.Console/` 下创建 `openlum.json`：
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

3. 运行：
   ```bash
   dotnet run --project OpenLum.Console
   ```

4. 可选 — Native AOT 单文件发布：
   ```bash
   dotnet publish OpenLum.Console -c Release -r win-x64
   ```

### 配置

加载顺序：`openlum.json` → `openlum.console.json` → `appsettings.json`。

| 配置项 | 说明 |
|--------|------|
| `model.provider` | 如 OpenAI、DeepSeek、Ollama |
| `model.model` | 模型 id |
| `model.baseUrl` | API 基础地址 |
| `model.apiKey` | API 密钥。为空时从环境变量 `OPENLUM_API_KEY` 读取。 |
| `workspace` | 工具工作区根目录 |
| `compaction.enabled` | 是否启用压缩 |
| `compaction.maxMessagesBeforeCompact` | 压缩前消息数 |
| `compaction.reserveRecent` | 压缩后保留消息数 |
| `compaction.maxToolResultChars` | 单次工具结果在会话中保留的最大字符数 |
| `compaction.maxFailedToolResultChars` | 发送给模型时，失败工具结果的最大字符数 |
| `tools.profile` | `minimal` / `coding` / `messaging` / `local` / `full` |
| `tools.allow` / `tools.deny` | 工具名或组名 |
| `workflow.enabled` | 是否启用 Observe/Act/Verify 阶段化工作流 |
| `workflow.requirePlanForWrite` | 为 true 时，在 Observe 阶段写操作需先提交计划或维护 TODO 列表 |
| `workflow.autoVerifyAfterFirstWrite` | 为 true 时，首次写操作后自动进入 Verify 阶段 |
| `search.skipDirs` | 额外需要在扫描时跳过的目录名（叠加 `.git`、`bin`、`obj` 等默认值） |
| `search.skipGlobs` | 扫描时需要跳过的相对路径 glob |

环境变量覆盖：`OPENLUM_PROVIDER`、`OPENLUM_MODEL`、`OPENLUM_BASE_URL`、`OPENLUM_API_KEY`。严格模式：`OPENLUM_STRICT_CONFIG=1`。

#### 工具 Profile

| Profile | 默认工具 |
|---------|----------|
| `minimal` | 仅 `list_dir` |
| `coding` | group:fs, group:web, group:runtime |
| `messaging` | 仅 `list_dir` |
| `local` / `full` | group:fs, group:web, group:runtime, group:memory, group:sessions |

可用 `allow`/`deny` 微调。禁用全部工具：`"deny": ["*"]`。

### 与 OpenClaw 对比

| 方面 | OpenClaw | OpenLumSharp |
|------|----------|--------------|
| 语言 / 运行时 | TypeScript/Node.js，Node ≥22 | C# / .NET 10 |
| 部署方式 | npm、Docker | `dotnet run`、Native AOT 单文件 |
| 渠道 | WhatsApp、Telegram、Slack 等消息渠道 | 控制台 REPL，可扩展 |
| 工具与技能 | 完整生态，技能与工具深度集成 | 核心工具 + Skills 目录扩展（read/exec 驱动） |
| 浏览器 | 内置或配套浏览器能力 | 通过 agent-browser 技能（本机 CLI） |
| 定位 | 多端消息、伴侣应用 | 本地 CLI、自包含运行时、嵌入 .NET 工作流 |

OpenClaw 侧重多通道消息与端到端伴侣体验；OpenLumSharp 侧重轻量、单机 Agent 运行时与 C# 生态集成。

### 许可证

MIT。见 [LICENSE.txt](LICENSE.txt)。
