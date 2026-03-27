---
name: csharp
description: "Plans, implements, refactors, and debugs C# / .NET projects using the dotnet CLI, Grep, SemanticSearch, sub-agents, and web search. Use when: working with .cs, .csproj, .sln files, C#, ASP.NET, Entity Framework, NuGet packages, or any .NET development task. Compiles with dotnet build — never executes dotnet run or dotnet test (user runs those manually). Compilation success = task complete."
---

# C# 项目开发 Skill（csharp）

适用于在 C# / .NET 项目中进行**需求分析、架构设计、功能开发、重构与调试**。  
智能体应主动利用文件工具、搜索工具、子代理和网络搜索来完成较复杂的分析与 Debug，而不仅仅是单次编辑。

## 一、铁律（必须无条件遵守）

- **`dotnet build` 成功 = 任务完成标志。**
- **禁止** 在任何场景下执行 `dotnet run`、`dotnet test` 或等价命令（包括通过脚本间接运行）。程序运行与测试由用户自行执行。
- **禁止** 为“验证运行”而启动可执行程序或启动服务；只允许通过编译结果和静态分析来判断质量。

## 二、工具使用指南

- **Shell**: always use **PowerShell** syntax; chain commands with `;` not `&&`. Never call `grep`/`rg` via shell — use the built-in Grep tool.
- **Grep**: search C# files with `type: “cs”` — class definitions, interface names, error codes, method references.
- **SemanticSearch**: concept-level search when a single keyword is insufficient (e.g. “how is HTTP wrapped?”).
- **Sub-agents**: use `explore` for large-repo analysis and call chains; `generalPurpose` for multi-step design tasks.
- **WebSearch/WebFetch**: look up unfamiliar .NET APIs, NuGet packages, or obscure compiler errors.

## 三、四步闭环（提高编写质量与成功率）

与 coding skill 一致：**识别分析问题 → 规划路径 → 代码编写 → 检查编译**。C# 场景下具体化为：

### 1. 识别分析问题 — 发生了什么

- **明确现象**：是 Bug、新需求、重构还是配置/依赖问题？复现条件与错误信息（含文件、行号、错误码）完整记录。
- **收集信息**：用 `Glob` / `Grep` / `SemanticSearch` 找到项目入口、相关类/接口、调用链；读 `.csproj`、`*.sln` 确认依赖与目标框架。
- **归纳根因**：类型不匹配、命名空间缺失、API 变更、还是逻辑/边界问题？先有结论再改，避免盲目改。

### 2. 规划路径 — 应该如何做

- **列出改动点**：要改/新增哪些 `.cs`、接口、方法？调用方与测试是否要同步改？
- **拆分步骤**：大改动拆成小步（如先改接口签名、再改实现、再改调用处），每步都可 `dotnet build` 验证。
- **选对工具**：读代码用 `Read`，定位用 `Grep`/`SemanticSearch`，修改用 `ApplyPatch`/`StrReplace` 或 **editing** skill 中的 Shell 方法（ReplaceRangeWithText、ReplaceAll 等），构建用 `exec`（`dotnet build -v normal`）。

### 3. 代码编写 — 执行

- **先读再改**：修改前 `Read` 目标文件及引用处，保持项目现有命名、异常处理、日志与 DI 风格。
- **小步构建**：每完成一小块就 `dotnet build`，避免一次改很多再一起排错。
- **大文件增量编辑**：优先局部修改，避免整文件重写导致误删逻辑；新类/新文件可整体写入。

### 4. 检查编译 — 闭环检查

- **必做编译**：每次修改后执行 `dotnet restore`（如有新包）和 `dotnet build -v normal`，以**编译通过**为完成标志。
- **按错误逐个修**：根据编译器输出的文件/行号用 `Read` 查看，用 `Grep` 找引用与定义，修完再 build，直到零错误。
- **禁止运行**：不执行 `dotnet run`、`dotnet test`；运行与测试由用户自行完成。收尾时简要总结改动与后续建议。

## 四、推荐工作流（从需求到通过编译）

1. **理解需求** — 解析输入/输出、边界条件、多模块时草拟架构与命名空间。
2. **全局扫描与代码定位** — `Glob`/`Grep`/`SemanticSearch` 找入口、领域层、服务与配置。
3. **方案设计** — 规划新增/修改的类与方法；大重构可用子代理 `explore` 生成分步 TODO。
4. **实现 / 修改** — `Read` + `ApplyPatch`，遵循项目风格。
5. **编译验证** — `dotnet restore` + `dotnet build -v normal`，提取并分析错误。
6. **错误处理与再次编译** — 按路径/行号 `Read`，`Grep` 查符号，修复后反复 build 直至通过。
7. **收尾** — 确认未运行程序；总结修改的文件与后续建议。

## 五、调试（Debug）细化流程

### 1. 编译错误调试

- 步骤：
  1. 阅读 `dotnet build -v normal` 输出，记录第一个错误及其上下文（文件、行号、错误码）。
  2. 使用 `Read` 查看对应文件和附近代码。
  3. 如错误与类型/命名空间相关，使用 `Grep` 搜索相关类型或命名空间定义。
  4. 无法从仓库内推断时，使用 `WebSearch` 查询错误码或异常信息，并结合当前 .NET 版本理解原因。
  5. 修改代码后再次 `dotnet build`，重复直至通过。

### 2. 逻辑错误（静态分析）

在不能运行程序的前提下：

- 分析调用链：
  - 用 `Grep` 搜索方法/接口的所有调用点。
  - 如调用关系复杂，可用子代理 `explore` 生成调用链说明。
- 检查：
  - 边界条件（null / 空集合 / 越界 / 溢出）。
  - 异常处理是否吞异常或忽略重要信息。
  - 并发场景下的共享状态与锁。

### 3. 第三方库与 API 问题

- 如果涉及 NuGet 包或外部 API：
  - 用 `WebSearch` + `WebFetch` 查看官方文档与示例。
  - 对照现有用法，识别是否存在已废弃的 API、默认行为变化、配置缺失等问题。

## 六、dotnet CLI Quick Reference

```powershell
dotnet new sln -n <Name>              # Create solution
dotnet new console -n <Name>          # Create console project
dotnet new classlib -n <Name>         # Create class library
dotnet sln add <projPath>             # Add project to solution
dotnet add <proj> reference <refProj> # Add project reference
dotnet add <proj> package <PkgId>     # Add NuGet package
dotnet restore                        # Restore dependencies
dotnet build -v normal                # Build (recommended default)
dotnet build -c Release               # Release build
dotnet package search <keyword>       # Search NuGet packages
```

## 七、注意事项与最佳实践

- **路径约定**：优先使用相对工作区的路径；在命令和代码中避免硬编码绝对路径。
- **批量修改**：大规模重构前，先用 `Glob` / `Grep` 列出受影响文件，再逐个 `Read` / `ApplyPatch`。
- **输出截断**：对于可能产生大量构建输出的操作，仅关注关键错误并在必要时提示用户本地完整查看。
- **风格一致性**：参照项目中已有 C# 代码风格（命名、格式、日志、异常处理），避免在同一项目内引入多套风格。
- **优先静态分析**：在无法运行程序的前提下，充分利用类型系统、编译器诊断、静态分析以及搜索工具完成 Debug。

## 八、智能体总结要求

- 完成任务后，应简要总结：
  - 修改或新增了哪些核心类/方法。
  - 如何解决了主要编译错误或潜在逻辑问题。
  - 用户后续需要执行的命令（如 `dotnet build`、`dotnet test`、`dotnet run`——仅作为建议，不代为执行）。

