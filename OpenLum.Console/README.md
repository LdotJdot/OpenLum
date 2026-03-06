# OpenLum.Console 项目说明

> 基于 .NET 的通用智能体 Shell，原生 AOT 发布、零第三方依赖、纯 BCL 实现。  
> 面向本地/内网部署，支持 OpenAI API 兼容的各类模型（DeepSeek、Ollama、OpenAI 等）。

---

## 一、项目定位与特点

### 1.1 一句话定位

OpenLum.Console 是一个**控制台可交互的通用 AI 智能体 Shell**：  
你输入自然语言，模型调用工具（读文件、执行命令、搜索记忆、 spawning 子智能体等），在既定策略下完成复杂任务。

### 1.2 核心特点

| 特点 | 说明 |
|------|------|
| **Native AOT** | 支持 `PublishAot=true`，`dotnet publish -r win-x64` 产出单一可执行文件，冷启动快、体积可控 |
| **零 NuGet 依赖** | 不引用任何 `PackageReference`，仅用 BCL + `System.Text.Json`，便于内网、离线环境部署 |
| **OpenAI API 兼容** | 通过 `baseUrl` 支持 DeepSeek、Ollama、本地/代理 OpenAI 等一切兼容 Chat Completions 的接口 |
| **工具策略可配置** | 基于 profile（minimal / coding / local / full）+ allow/deny 精细控制可用工具 |
| **Skill 机制** | 从 `skills/*/SKILL.md` 自动发现技能，模型按需 `read` 加载，再用 `exec` 调用技能 exe |
| **会话压缩** | 长对话时自动用模型总结历史，保留最近 N 条，控制 token 消耗 |
| **时间戳注入** | 用户输入前自动加 `[Dow YYYY-MM-DD HH:mm +08:00]`，模型具备“今日”感知 |

这些设计都是为了：**在尽量少依赖、少配置的前提下，让一个本地可执行文件就能跑起完整的 Agent 能力**。

---

## 二、技术架构与搭建思路

### 2.1 整体数据流

```
Program.Main
    └── Application.Run()
        ├── ConfigLoader.Load()        → AppConfig
        ├── ToolRegistry               → 注册 read / write / list_dir / exec / memory_* / sessions_spawn
        ├── ToolPolicyFilter           → 按 profile + allow/deny 过滤工具
        ├── OpenAIModelProvider        → HTTP 调用 Chat Completions
        ├── SystemPromptBuilder       → 构建系统提示（工具列表 + Skills + Workspace）
        ├── ConsoleSession             → 内存会话
        ├── SessionCompactor?          → 可选：超阈值时压缩历史
        └── AgentLoop                  → 主循环：user → model → tools → model → ...
```

用户输入经过 `TimestampInjection` 打上时间戳，送入 `AgentLoop`；  
模型返回的 `tool_calls` 经 `ExecuteToolAsync` 执行，结果再回传模型；  
直到模型不再调用工具，返回最终回复。

### 2.2 核心模块职责

| 模块 | 职责 |
|------|------|
| **ConfigLoader** | 从 `openlum.json` / `openlum.console.json` / `appsettings.json` 加载配置，使用 `JsonDocument` 解析，无额外 JSON 库 |
| **ToolRegistry** | 工具注册与按名称查找；`ToolPolicyFilter` 包装后实现策略过滤 |
| **SystemPromptBuilder** | 拼装系统提示：日期、工具列表、工作区、Skill 元数据（模型通过 `read` 按需加载 SKILL.md） |
| **AgentLoop** | 实现 `IAgent`，负责多轮 tool-call 循环，最多 50 轮，超限时强制 wrap-up |
| **SessionCompactor** | 当 `MessageCount > maxMessagesBeforeCompact` 时，用模型总结旧消息，替换为一条摘要 |
| **SkillLoader** | 扫描 workspace/skills、AppContext.BaseDirectory/skills 等目录下 `SKILL.md`，解析 frontmatter，生成 `<available_skills>` 片段 |

### 2.3 接口抽象

核心契约集中在 `Interfaces` 下，便于替换实现：

```csharp
// 工具
public interface ITool
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<ToolParameter> Parameters { get; }
    Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);
}

// 模型
public interface IModelProvider
{
    Task<ModelResponse> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        IProgress<string>? contentProgress,
        CancellationToken ct = default);
}

// 会话
public interface ISession
{
    IReadOnlyList<ChatMessage> Messages { get; }
    void Add(ChatMessage msg);
    void Clear();
}
```

这样设计的好处是：  
- 换模型只需实现 `IModelProvider`；  
- 换存储只需实现 `ISession`；  
- 新增工具实现 `ITool` 并注册即可。

---

## 三、智能体设计思路

### 3.1 AgentLoop 的核心循环

智能体本质是一个 **“用户输入 → 模型推理 → 工具执行 → 模型推理 → … → 最终回复”** 的循环。  
`AgentLoop.RunAsync` 的简化逻辑如下：

```csharp
// 1. 用户消息入会话
_session.Add(new ChatMessage { Role = MessageRole.User, Content = userPrompt });

// 2. 多轮循环（最多 maxTurns = 50）
for (var turn = 0; turn < maxTurns; turn++)
{
    // 2.1 可选：会话压缩
    if (_compactor is { } c && _session is ICompactableSession cs)
        await c.CompactIfNeededAsync(cs, ct);

    // 2.2 构建消息 + 工具定义，调用模型
    var messages = BuildMessages(...);
    var toolDefs = _tools.All.Select(t => new ToolDefinition(...)).ToList();
    var response = await _model.ChatAsync(messages, toolDefs, contentProgress, ct);

    // 2.3 无 tool_calls → 直接返回
    if (response.ToolCalls.Count == 0)
    {
        _session.AddAssistant(response.Content, null);
        return new AgentTurnResult(true, null);
    }

    // 2.4 有 tool_calls → 执行工具，结果入会话，继续下一轮
    _session.AddAssistant(response.Content, response.ToolCalls);
    foreach (var tc in response.ToolCalls)
    {
        var result = await ExecuteToolAsync(tc, ct);
        results.Add(result);
    }
    _session.AddToolResults(results);
}
```

### 3.2 工具调用与错误处理

- 模型返回的 `arguments` 可能是空字符串（如 DeepSeek），代码会规范为 `"{}"`。
- 未知工具名返回 `Error: unknown tool 'xxx'`，模型有机会调整策略。
- `ExecTool` 在执行前会校验 skill exe 是否存在，避免 LLM 幻觉调用不存在的文件，并提示“先 read SKILL.md 确认路径”。

### 3.3 强制收尾（Force Wrap-Up）

当达到 `maxTurns` 但模型仍请求 tool_calls 时，不再执行工具，而是向会话中注入一条系统提示：

```
[System: 本轮工具调用次数已达上限。请根据目前已有的信息，给用户一个简洁的总结和回答。不要再调用任何工具。]
```

然后做一次无工具调用的模型请求，得到总结后返回，避免无限循环。

### 3.4 子智能体（sessions_spawn）

`sessions_spawn` 工具会创建一个**独立会话**和**排除自身的工具集**，用同样的 `AgentLoop` 跑一个子任务，最终返回子智能体的最后一条 assistant 回复。  
这样可以把复杂任务拆成子任务，隔离上下文，降低主会话长度。

---

## 四、工具与 Skill 机制

### 4.1 内置工具一览

| 工具 | 作用 | 说明 |
|------|------|------|
| `read` | 读文件 | 支持 workspace 相对路径、`~`、skill 目录；纯文本限 200–2000 行；PDF/Office 通过 exec + read-*.exe |
| `write` | 写文件 | 限制在工作区下 |
| `list_dir` | 列目录 | PowerShell 风格 |
| `exec` | 执行命令 | PowerShell（Windows）/ sh（非 Windows）；支持 stdin、timeout；skill exe 校验 |
| `memory_get` | 读记忆 | `MEMORY.md` / `memory/*.md`，支持行范围 |
| `memory_search` | 搜记忆 | 关键词匹配，无向量 |
| `sessions_spawn` | 子智能体 | 独立会话执行子任务 |

### 4.2 工具策略（profile）

策略由 `ToolProfiles` 和 `ToolPolicyFilter` 实现：

- **Profile**：`minimal` / `coding` / `messaging` / `local` / `full`，映射到不同的工具/组。
- **组**：`group:fs`、`group:web`、`group:runtime`、`group:memory`、`group:sessions`。
- **allow / deny**：在 profile 基础上追加或排除工具。

例如 `profile: "local"` 等价于允许：`group:fs` + `group:web` + `group:runtime` + `group:memory`。

### 4.3 Skill 加载与使用

Skill 目录结构：

```
skills/
  webbrowser/
    SKILL.md          # 通过 shell 调用 agent-browser，见 agent-browser skill
  agent-browser/
    SKILL.md          # agent-browser CLI 命令参考（npm 安装）
  read/
    SKILL.md
    pdf/read-pdf.exe
    docx/read-docx.exe
    ...
```

**SKILL.md 基础格式要求**

为正确生成元数据并注入到系统提示，每个 SKILL.md 必须满足：

- **YAML frontmatter**：以 `---` 开头和结尾，包裹 YAML 块
- **`name`**（可选）：skill 的显示名称；缺省时使用目录名（如 `webbrowser`）
- **`description`**（可选）：简短说明，供模型判断何时加载该 skill；缺省时为 `"Skill: {name}"`

```yaml
---
name: webbrowser
description: "浏览网页。与 read skill 风格一致：直接 exec 调用 exe，传参执行，stdout 为结果。"
---
```

`description` 支持带引号或不带引号；`name` 和 `description` 建议都填写，以便模型准确选用。

`SkillLoader` 扫描 `skills/*/SKILL.md`，解析上述 frontmatter。

系统提示中会注入 `<available_skills>` 片段，包含 name、description、location。  
模型**不直接获得完整 SKILL.md**，而是需要时用 `read` 工具加载，这样既节省 token，又保证指令是最新的。

系统提示中的指引：

> Use the read tool to load a skill's SKILL.md at the listed location when needed.  
> Before exec with a skill exe: always read that skill's SKILL.md first to get the exact exe path.

### 4.4 Skill 动态注入逻辑

Skill 的注入发生在**应用启动时**，每次 `Application.Run()` 都会重新扫描目录并构建系统提示。流程如下：

```
Application.Run()
    └── SystemPromptBuilder.Build(workspaceDir, tools)
            └── BuildSkillsSection(workspaceDir)
                    ├── SkillLoader.Load(workspaceDir)     // 1. 扫描目录
                    └── SkillLoader.FormatForPrompt(...)   // 2. 生成 prompt 片段
```

**1. 目录扫描（SkillLoader.Load）**

按优先级扫描三个目录，取并集（同名 skill 以先发现的为准）：

| 优先级 | 目录 |
|--------|------|
| 1 | `{workspaceDir}/skills` |
| 2 | `AppContext.BaseDirectory/skills`（exe 同目录） |
| 3 | `{父级目录}/skills` |

对每个目录下的子目录 `X`，若存在 `X/SKILL.md`，则视为一个 skill。用 `Path.GetFileName(sub)` 得到默认名，再通过 frontmatter 的 `name:` 覆盖。

```csharp
// SkillLoader.Load 扫描逻辑
var dirs = new[] {
    Path.Combine(workspaceDir, "skills"),
    Path.Combine(AppContext.BaseDirectory, "skills"),
    Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory) ?? ".", "skills")
};
foreach (var dir in dirs)
{
    foreach (var sub in Directory.GetDirectories(dir))
    {
        var skillPath = Path.Combine(sub, "SKILL.md");
        if (!File.Exists(skillPath)) continue;
        var (desc, parsedName) = ParseFrontmatter(skillPath);
        var skillName = !string.IsNullOrWhiteSpace(parsedName) ? parsedName : Path.GetFileName(sub);
        results.Add(new SkillEntry(skillName, desc, skillPath));
    }
}
```

**2. Frontmatter 解析**

仅解析 `---` 块内的 `name:` 和 `description:`，用于生成元数据；完整 SKILL.md 不在此阶段读入。

```csharp
// ParseFrontmatter 使用正则
var match = Regex.Match(text, @"^---\s*\r?\n(.*?)\r?\n---", RegexOptions.Singleline);
// name: webbrowser
// description: "浏览网页。..."
```

**3. 注入到系统提示**

`FormatForPrompt` 只输出 **name / description / location** 三样，不输出 SKILL.md 正文，以减少 token 并让模型按需加载：

```xml
<available_skills>
  <skill>
    <name>webbrowser</name>
    <description>浏览网页。与 read skill 风格一致：直接 exec 调用 exe，传参执行，stdout 为结果。</description>
    <location>D:/app/skills/webbrowser/SKILL.md</location>
  </skill>
  ...
</available_skills>

Use the read tool to load a skill's SKILL.md at the listed location when needed.
Before exec with a skill exe: always read that skill's SKILL.md first to get the exact exe path.
```

路径中的用户主目录会 compact 成 `~`，以节省 token。

**“动态”的含义**：每次启动应用时都会重新扫描上述目录。新增 `skills/新技能名/SKILL.md` 后，重启即可被自动发现，无需改代码。

### 4.5 基于 Skill 学会技能并调用第三方

Skill 的本质是：**用 SKILL.md 教会模型如何通过 exec 调用第三方 exe**。模型不预训练技能，而是运行时“按需学习”。

**整体流程**

```
用户任务 → 模型看 <available_skills> 元数据 → 按 description 选 skill
    → read(SKILL.md) 加载完整说明 → 按文档构造 exec 命令 → 执行 exe → 解析 stdout → 继续推理
```

**1. 从元数据选技能**

模型只看到 name、description、location。例如用户说“帮我打开 Bing 搜索”，description 里出现“浏览网页”，模型会选 `webbrowser`，并知道要 read 对应 location 的 SKILL.md。

**2. read 加载 SKILL.md**

SKILL.md 是给模型看的说明书，一般包含：

- 执行方式（如 webbrowser 通过 shell 调用 `agent-browser`；read 使用 `skills/read/.../xxx.exe`）
- 命令格式、子命令、参数
- 示例命令
- 错误处理与注意事项

`ReadTool` 通过 `SkillLoader.GetSkillRoots` 拿到的 `_extraReadRoots`，允许读取 skills 目录下的文件；读到 SKILL.md 时还会在控制台打 `[skill] Loaded: webbrowser` 日志。

**3. exec 调用 exe**

模型根据 SKILL.md 构造 PowerShell 命令，例如：

```powershell
agent-browser --headed open "https://cn.bing.com"
```

`ExecTool` 在执行前会做一次校验：若命令中涉及 skills 目录下的 exe，则检查该 exe 是否存在；不存在则返回错误，并提示“请先 read 该 skill 的 SKILL.md 确认正确的 exe 路径”，避免模型幻觉出错误路径。

**4. 第三方 exe 的契约**

Skill 下的 exe 通常遵循统一风格：

- **输入**：命令行参数
- **输出**：stdout 文本或 JSON
- **工作目录**：若 exe 在 skills 下，`ExecTool` 会将工作目录设为 exe 所在目录，方便加载同目录的 DLL（如 pdfium）

模型从 stdout 解析结果，再决定下一步（如解析 snapshot 中的 ref，继续调用 type、click 等）。

**示例：webbrowser skill 的典型调用链（通过 agent-browser）**

| 步骤 | 模型动作 | 结果 |
|------|----------|------|
| 1 | 根据 description 选 webbrowser | - |
| 2 | read(skills/webbrowser/SKILL.md) 或 skills/agent-browser/SKILL.md | 获得 agent-browser 命令格式 |
| 3 | exec: agent-browser --headed open "https://cn.bing.com" | 浏览器打开，返回 snapshot |
| 4 | 从 snapshot 找到搜索框 ref（如 @e2） | - |
| 5 | exec: agent-browser fill @e2 "关键词" 与 press Enter 或 click 提交 | 输入并搜索 |
| 6 | 解析新页面 snapshot，提炼答案 | 回复用户 |

**扩展新 Skill 的步骤**

1. 在 `skills/` 下新建目录，如 `my-tool/`
2. 编写 `my-tool/SKILL.md`（含 frontmatter、exe 路径、命令格式、示例）
3. 将 exe 放入 `my-tool/` 或其子目录
4. 重启应用，skill 自动被扫描并注入 `<available_skills>`
5. 模型在需要时 read SKILL.md，再通过 exec 调用你的 exe

无需修改 Agent 代码，只需遵循“exe + SKILL.md”的约定即可接入任意第三方能力。

### 4.6 自建 Skill 详细指南

**是的，用户只要在 `skills` 文件夹里新建目录、写好 SKILL.md 和可执行程序，重启应用即可使用。** 无需改 Agent 源码、无需重新编译。

#### 4.6.1 目录结构

```
skills/
  你的技能名/           ← 文件夹名会作为默认 skill 名，也可用 frontmatter 的 name 覆盖
    SKILL.md            ← 必需：模型按需加载的“说明书”
    你的程序.exe        ← 或其他可执行文件，python基本等，可放在子目录,skill文档要写调用方式
    其他依赖.dll        ← 可选，exe 同目录可被加载
```

#### 4.6.2 SKILL.md 必需内容

**1. Frontmatter（必须在文件开头）**

```yaml
---
name: 技能显示名
description: "一句话描述，用于模型判断何时选用此技能。尽量包含关键词。"
---
```

- `name`：可选，不写则用文件夹名
- `description`：**务必写好**，模型根据它决定是否选用该 skill

**2. exe 路径**

用表格或列表明确写出 exe 的相对路径（相对应用根目录或 workspace）。模型会根据这里构造 exec 命令，路径写错会导致调用失败。

```markdown
## exe 路径

| exe | 说明 |
|-----|------|
| skills/我的技能/run.exe | 主程序 |
```

**3. 命令格式与示例**

说明子命令、参数、典型用法。模型会按文档构造命令行。

```markdown
## 命令

run.exe <action> [选项]

- action: search | fetch | ...

## 示例

```powershell
& "skills/我的技能/run.exe" search --keyword "test"
```
```

#### 4.6.3 完整示例：新建一个「天气查询」skill

```
skills/weather/
  SKILL.md
  weather.exe
```

**SKILL.md**：

```markdown
---
name: weather
description: "查询指定城市天气。调用 weather.exe，传入城市名，stdout 返回 JSON。"
---

# 天气查询 Skill

## exe 路径

| exe | 说明 |
|-----|------|
| skills/weather/weather.exe | 天气查询程序 |

## 命令

```
weather.exe --city <城市名>
```

## 输出

stdout 为 JSON：`{"city":"北京","temp":15,"desc":"晴"}`

## 示例

```powershell
& "skills/weather/weather.exe" --city 北京
```
```

**weather.exe**：任意语言编写，只需读取命令行参数、输出到 stdout 即可。例如 C# 控制台程序、Python 脚本打包的 exe 等。

完成后，将 `skills/weather/` 放到应用同级的 `skills` 目录（或 workspace 下的 `skills`），**重启 OpenLum.Console**，新 skill 即被扫描并出现在 `<available_skills>` 中。用户问「北京今天天气怎么样」时，模型会先 read 该 SKILL.md，再 exec 调用 weather.exe。

#### 4.6.4 路径与工作目录

- **exe 路径**：推荐用 `skills/技能名/xxx.exe` 这种相对路径，跨机器可移植
- **工作目录**：ExecTool 会检测 skills 下的 exe，并将进程工作目录设为 exe 所在目录，因此 exe 同目录的 DLL、配置文件可直接加载
- **读文件**：若 exe 需要读 workspace 下的文件，路径可用相对 workspace 的写法，因为 exec 的默认工作目录是 workspace（非 skill exe 时）

#### 4.6.5  checklist

| 步骤 | 检查项 |
|------|--------|
| 1 | 在 `skills/` 下创建子目录 |
| 2 | 编写 `SKILL.md`，含 frontmatter（name、description） |
| 3 | 在 SKILL.md 中写明 exe 的准确路径 |
| 4 | 在 SKILL.md 中写明命令格式和示例 |
| 5 | 将 exe 放到 SKILL.md 中声明的路径 |
| 6 | 重启 OpenLum.Console |
| 7 | 用自然语言测试（如「用天气技能查一下上海天气」） |

---

### 4.7 这样实现的好处

采用「目录扫描 + SKILL.md + exec 调用」这种设计，带来以下好处：

| 好处 | 说明 |
|------|------|
| **零代码扩展** | 用户不需要改 C# 源码、不重新编译 Agent。新增能力 = 新增一个文件夹，改的是文档和 exe，而不是框架 |
| **任意语言实现** | exe 可用 C#、Python、Go、Rust 等任意语言编写，只要遵循「参数进、stdout 出」的契约即可。技能实现与 Agent 解耦 |
| **自然语言即配置** | SKILL.md 是给人看的文档，也是给模型看的指令。改说明即可改行为，无需改配置格式 |
| **按需加载，省 token** | 系统提示只注入 name、description、location，不注入 SKILL.md 全文。模型只有在需要时才 read 加载，避免把所有技能文档塞进上下文 |
| **版本与部署解耦** | 技能可单独更新：替换 exe、改 SKILL.md 即可，不必动主程序。内网环境可以只同步 skills 目录 |
| **本地优先、可审计** | 技能逻辑在本地 exe 中，行为可审计、可调试。不依赖外部 API 时，可完全离线运行 |
| **统一契约** | 所有 skill 都通过 exec 调用，ExecTool 统一处理超时、stdin、工作目录、exe 存在性校验，减少重复逻辑 |
| **易分发** | 把 `skills/` 打成 zip 分享，别人解压到同目录即可使用，无需安装依赖（exe 自带运行时除外） |

核心思想是：**把 Agent 做成“壳”，把能力做成“可插拔的 skill”**。壳只负责调度（read、exec、会话管理），具体能力由用户用「SKILL.md + exe」自行扩展，既降低门槛，又保持架构清晰。

---

## 五、实用说明与配置

### 5.1 配置文件

配置文件按优先级查找：`openlum.json` > `openlum.console.json` > `appsettings.json`。  
放在可执行文件同目录即可。

示例 `openlum.json`（本地开发推荐）：

```json
{
  "tools": { "profile": "local", "allow": [], "deny": [] },
  "model": {
    "provider": "DeepSeek",
    "model": "deepseek-chat",
    "baseUrl": "https://api.deepseek.com/v1",
    "apiKey": "sk-xxx"
  },
  "compaction": {
    "enabled": true,
    "maxMessagesBeforeCompact": 30,
    "reserveRecent": 10
  },
  "workspace": ".",
  "userTimezone": "Asia/Shanghai"
}
```

其他常见配置模板：

- **安全 coding（无 exec / 无浏览器，偏只读）**：

  ```json
  {
    "tools": {
      "profile": "minimal",
      "allow": [ "read", "group:memory" ],
      "deny": []
    }
  }
  ```

- **全禁工具（只聊天，不调用任何工具）**：

  ```json
  {
    "tools": {
      "profile": "local",
      "allow": [],
      "deny": [ "*" ]
    }
  }
  ```

### 5.2 配置项说明

| 配置块 | 字段 | 说明 |
|--------|------|------|
| `tools` | `profile` | minimal / coding / messaging / local / full |
| `tools` | `allow` / `deny` | 工具名或组名列表 |
| `model` | `provider` | 仅作标识，实际请求由 `baseUrl` 决定，可被 `OPENLUM_PROVIDER` 覆写 |
| `model` | `model` | 模型名，如 `deepseek-chat`、`qwen3:8b`，可被 `OPENLUM_MODEL` 覆写 |
| `model` | `baseUrl` | API 地址，如 Ollama `http://localhost:11434/v1`，可被 `OPENLUM_BASE_URL` 覆写 |
| `model` | `apiKey` | 密钥，推荐通过环境变量 `OPENLUM_API_KEY` 提供 |
| `compaction` | `enabled` | 是否启用会话压缩 |
| `compaction` | `maxMessagesBeforeCompact` | 超过此条数触发压缩 |
| `compaction` | `reserveRecent` | 压缩后保留最近 N 条 |
| `workspace` | - | 工作区根目录，支持环境变量展开 |
| `userTimezone` | - | 时间戳时区，如 `Asia/Shanghai` |

> 高级：将环境变量 `OPENLUM_STRICT_CONFIG` 设为 `1` / `true` / `yes` 时，若配置文件 JSON 格式错误，会在启动时报错退出，而不是静默回退到默认配置。

### 5.3 构建与运行

```bash
# 调试运行
dotnet run -p OpenLum.Console

# Native AOT 发布（Windows x64）
dotnet publish -c Release -r win-x64 -p:PublishAot=true
# 输出在 bin/Release/net10.0/win-x64/publish/
```

发布后需将 `Skills/**` 一并拷贝到运行目录，项目已通过 `CopySkillsToPublish` 目标处理。

### 5.4 REPL 命令

| 命令 | 作用 |
|------|------|
| `/help` | 显示帮助 |
| `/clear` | 清空会话 |
| `/quit` | 退出 |

---

## 六、代码示例片段

### 6.1 时间戳注入

```csharp
// TimestampInjection.Inject
return $"[{dow} {dateTime} {offsetStr}] {message}";
// 例如: [Sat 2026-02-28 14:30 +08:00] 今天有什么新闻？
```

### 6.2 ReadTool 路径与权限

```csharp
// 支持 ~ 展开、workspace 相对路径、skill 根目录
var expandedPath = ExpandPath(path);  // ~ → 用户目录
// 仅允许 workspace 或 _extraReadRoots（skill 目录）下的文件
if (!IsPathAllowed(fullPath))
    return "Error: path is outside workspace or skill directories";
```

### 6.3 ExecTool 的 skill exe 校验

```csharp
if (TryExtractSkillExePath(command, out var exePath) && !File.Exists(exePath))
{
    return $"Error: 技能 exe 不存在: {exePath} 请先 read 该 skill 的 SKILL.md 确认正确的 exe 路径...";
}
```

### 6.4 会话压缩流程

```csharp
// SessionCompactor.CompactIfNeededAsync
if (session.MessageCount <= _maxMessagesBeforeCompact) return false;
var toSummarize = session.GetMessagesToCompact(_reserveRecent);
var summary = await SummarizeAsync(toSummarize, ct);
session.CompactWithSummary(_reserveRecent, summary);
```

---

## 七、总结

OpenLum.Console 的设计目标可以概括为：

1. **轻量**：Native AOT、无 NuGet 依赖、配置即用。
2. **通用**：兼容 OpenAI API，适配 DeepSeek、Ollama、本地代理等。
3. **可控**：工具策略、会话压缩、时间戳、Skill 按需加载，都服务于可控的 token 与行为。
4. **可扩展**：通过接口和 Tool/Skill 机制，可以方便地增加新工具、新模型、新技能。

如果你在做本地/内网 AI 助手、自动化脚本编排或需要“模型 + 工具”的交互式 Shell，这个项目提供了一个清晰、可维护的起点。  
在此基础上按业务需求扩展工具和 Skill，就能快速落地实际场景。
