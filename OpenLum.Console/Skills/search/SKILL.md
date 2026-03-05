---
name: search
description: "高效纯文本 / 代码搜索：优先用 Grep 做精确 / 正则匹配，配合 SemanticSearch 做语义级定位。只针对工作区内的文本与代码文件。"
---

# Search Skill（纯文本 / 代码搜索）

适用于在**工作区文件**中快速定位字符串、正则模式、类名 / 方法名、错误信息等。  
只处理**文本 / 代码**文件（如 .cs/.ts/.js/.json/.md 等），不负责 PDF/Office/CAD（这些由 `read` skill 处理）。

## 何时使用

- 想知道「某个类 / 方法 / 接口 / 常量定义在哪里」；
- 想找「某个错误码 / 日志片段 / 配置键」在代码中的所有出现位置；
- 在大仓库中，先用搜索快速缩小范围，再进入具体文件阅读或重构；
- 对某个功能「大概在哪一块代码里实现」不确定时，可配合 SemanticSearch 做语义级定位。

## 使用的工具

- **Grep 工具**（基于 ripgrep）：高效、精确 / 正则匹配，是首选的纯文本搜索方式。  
  - 支持参数：`pattern`、`path`、`glob`、`type`、`output_mode`、`-i` 等。
- **SemanticSearch 工具**：当问题更偏「语义 / 概念」，而不是一个固定字符串时，用来找「某一类逻辑」所在的文件或函数。

## 推荐工作流

1. **精确定位：优先 Grep**
   - 已知类名 / 方法名 / 错误码 / 日志文本：
     - 例如查找类定义：`pattern: "class\\s+OrderService"`，`type: "cs"`；
     - 查找错误码：`pattern: "ERR_1234"`，`output_mode: "files_with_matches"`；
     - 查找日志关键字：`pattern: "Payment failed"`, `glob: "**/*.cs" 或 "**/*.ts"`.
   - 先用 `output_mode: "files_with_matches"` 获取受影响文件列表，需要查看更多上下文时再切换成 `content`。

2. **语义级定位：配合 SemanticSearch**
   - 当你在问：「用户登录是在哪里做的？」「HTTP 请求是在哪一层封装的？」「缓存策略怎么实现？」时：
     - 先用 SemanticSearch 在整个仓库或某个目录内搜索，例如：
       - query: "Where are HTTP API calls wrapped?"
       - target_directories: ["backend/"] 或 ["OpenLum.Console/"]
     - 得到候选文件后，再在这些文件内部用 Grep 做更精确的字符串 / 正则搜索。

3. **与其他技能的协作**
   - 做 C#/.NET 重构或 Debug 时：优先组合 `csharp` + `search` + `coding`；
   - 缺少专用工具，需要通过 Python 脚本处理数据前：用 `search` 找到相关输入文件 / 模板，再交给 `python` skill 规划脚本。

## 使用技巧

- **限制范围**：
  - `path` 指向子目录（如 `src/`、`OpenLum.Console/`）以减少噪音；
  - `glob`（如 `"**/*.cs"`, `"**/*.{ts,tsx}"`）避免搜索无关文件。
- **输出模式**：
  - 快速罗列文件：`output_mode: "files_with_matches"`；
  - 需要上下文：`output_mode: "content"` 并设置 `-C`/`-A`/`-B` 控制上下文行数。
- **大小写与正则**：
  - 不确定大小写时使用 `-i: true`；
  - 复杂匹配使用正则（如 `"HttpClient\\s*\\("`、`"ILogger<[^>]+>"` 等）。

## 不负责的内容

- 二进制文件、PDF、Office、CAD 等富文档的内容提取 → 使用 `read` skill 的相关 exe（`skills/read/...`）。
- 跨系统 / 网络搜索（如 GitHub / Web）→ 使用 `github` 或 `webbrowser` / `web_search` 相关技能。

> 总体原则：**能用 Grep 就尽量用 Grep，能先缩小文件范围再看完整文件，减少无目的扫描。** SemanticSearch 主要用来在概念层面「大致锁定区域」，然后继续回到 Grep 与文件阅读。

