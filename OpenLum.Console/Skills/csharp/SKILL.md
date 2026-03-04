---
name: csharp
description: 用于在 C# / .NET 项目中进行规划、实现、重构和调试。优先使用 dotnet CLI 完成项目管理与编译，结合 Grep / SemanticSearch / 子代理 / 网络搜索进行代码定位与 Debug。禁止执行 dotnet run / dotnet test，编译通过即视为任务完成。
---

# C# 项目开发 Skill（csharp）

适用于在 C# / .NET 项目中进行**需求分析、架构设计、功能开发、重构与调试**。  
智能体应主动利用文件工具、搜索工具、子代理和网络搜索来完成较复杂的分析与 Debug，而不仅仅是单次编辑。

## 一、铁律（必须无条件遵守）

- **`dotnet build` 成功 = 任务完成标志。**
- **禁止** 在任何场景下执行 `dotnet run`、`dotnet test` 或等价命令（包括通过脚本间接运行）。程序运行与测试由用户自行执行。
- **禁止** 为“验证运行”而启动可执行程序或启动服务；只允许通过编译结果和静态分析来判断质量。

## 二、可用工具与职责分工

### 1. 文件与代码操作

- 使用 `Read`：读取 `.cs`、`.csproj`、`.sln`、配置文件等内容。
- 使用 `ApplyPatch`：对单个文件进行结构化修改（新增类/方法、重构逻辑、修复 Bug）。
- 使用 `Glob`：按模式列出 C# 相关文件（如 `**/*.cs`、`**/*.csproj`），避免硬编码文件列表。
- 使用 `ReadLints`：在修改后检查被编辑文件是否存在新的编译/分析错误，并尽量修复。

### 2. 代码搜索与理解

- 使用 `Grep`（rg）：
  - 精确或正则搜索：类名、方法名、接口、错误消息、日志文本。
  - 典型用法：搜索 `class <Name>`、`interface <Name>`、`MethodName\(` 或错误码。
  - 通过 `glob` 或 `type=cs` 仅在 C# 文件中搜索。
- 使用 `SemanticSearch`：
  - 在功能/概念级别查找相关实现，例如“HTTP 调用是如何封装的”、“日志是在哪个层写入的”。
  - 适合单个关键字不足以准确匹配的场景。

### 3. 终端命令（Shell / exec）

- 始终按 **PowerShell** 语法构造命令；多命令串联使用 `;` 而不是 `&&`。
- 合法典型命令：
  - `dotnet new sln -n MyApp`
  - `dotnet new console -n MyApp`
  - `dotnet new classlib -n MyLib`
  - `dotnet sln add MyApp/MyApp.csproj`
  - `dotnet add MyApp reference MyLib/MyLib.csproj`
  - `dotnet add MyApp package <PackageId> [-v <Version>]`
  - `dotnet restore`
  - `dotnet build -v normal`（推荐，用于捕获完整编译输出）
  - `dotnet build -c Release`
- 不得通过 Shell 调用 `grep` / `rg`，优先使用内置 `Grep` 工具完成搜索。

### 4. 子代理（Task 工具）

当单次对话难以完成复杂任务（如大规模重构、全仓库架构梳理、复杂调试）时：

- 使用子代理：
  - `subagent_type: "explore"`：需要在大仓库中系统梳理结构、找调用链、罗列相关文件时使用。
  - `subagent_type: "generalPurpose"`：需要多轮推理、综合文档/网络信息来设计方案或长流程任务时使用。
- 在 `prompt` 中明确：
  - 目标（例如：“重构所有同步 IO 调用为异步”）。
  - 需要返回的结果形式（文件列表、改造建议、TODO 列表等）。
  - 是否允许写入（单纯分析用 `readonly: true`）。

### 5. 网络信息（WebSearch / WebFetch）

在以下场景主动使用网络：

- 不确定某个 .NET API / NuGet 包的最新用法或推荐实践。
- 碰到晦涩异常或编译错误，仓库内代码与注释无法解释原因。
- 需要确认 2026 年左右的最佳实践或重大 Breaking Change。

使用建议：

- `WebSearch`：根据错误消息 / API 名 + `c#` + `2026` 搜索最新资料。
- `WebFetch`：对于具体的文档链接（如 `learn.microsoft.com`、GitHub README）拉取并阅读关键片段。

## 三、推荐工作流（从需求到通过编译）

1. **理解需求**
   - 解析用户需求，明确输入/输出、边界条件、性能/扩展性要求。
   - 如果涉及多个模块，先草拟架构与命名空间划分。

2. **全局扫描与代码定位**
   - 使用 `Glob` / `Grep` / `SemanticSearch` 找到相关：
     - 项目入口（如 `Program.cs`、`Main`）。
     - 领域模型、服务接口、数据访问层、配置和启动代码。

3. **方案设计**
   - 在现有架构基础上，规划需要新增或修改的类、接口、方法。
   - 大范围重构时，考虑用子代理 `explore` 生成分步骤 TODO 列表。

4. **实现 / 修改**
   - 使用 `Read` 读取目标文件，`ApplyPatch` 局部修改。
   - 遵循项目现有风格（命名规则、异常处理、日志风格、依赖注入模式等）。

5. **编译验证**
   - 用 `Shell` 执行 `dotnet restore`（如新增包或首次构建）。
   - 用 `Shell` 执行 `dotnet build -v normal`。
   - 把关键错误信息提取出来用于分析。

6. **错误处理与再次编译**
   - 对每个错误：
     - 根据输出中的路径/行号使用 `Read` 查看代码。
     - 如错误上下文不清楚，用 `Grep` 在仓库中搜索相关符号或错误码。
     - 修复后再次 `dotnet build`，直到无编译错误。

7. **收尾**
   - 再次确认：**未运行程序 / 测试，仅通过编译**。
   - 总结本次修改涉及的文件、关键点以及后续建议（如测试建议、潜在重构方向）。

## 四、调试（Debug）细化流程

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

## 五、dotnet CLI 速查表（Shell 命令）

> 所有命令均以工作区根目录或解决方案根目录为基准，注意相对路径。

| 命令 | 说明 |
|------|------|
| `dotnet new sln -n <Name>` | 创建解决方案 |
| `dotnet new console -n <Name>` | 创建控制台项目 |
| `dotnet new classlib -n <Name>` | 创建类库项目 |
| `dotnet sln add <projPath>` | 将项目加入解决方案 |
| `dotnet add <projPath> reference <refProj>` | 添加项目引用 |
| `dotnet add <projPath> package <PackageId> [-v <Version>]` | 向项目添加 NuGet 包 |
| `dotnet restore` | 还原所有依赖包 |
| `dotnet build -v normal` | Debug 编译并输出详细信息（推荐默认） |
| `dotnet build -c Release` | Release 编译 |

NuGet 包搜索（如本地 SDK 支持）：

- `dotnet package search <关键词> [--take 10] [--exact-match] [--prerelease] [--source <url>] [--format json]`

## 六、注意事项与最佳实践

- **路径约定**：优先使用相对工作区的路径；在命令和代码中避免硬编码绝对路径。
- **批量修改**：大规模重构前，先用 `Glob` / `Grep` 列出受影响文件，再逐个 `Read` / `ApplyPatch`。
- **输出截断**：对于可能产生大量构建输出的操作，仅关注关键错误并在必要时提示用户本地完整查看。
- **风格一致性**：参照项目中已有 C# 代码风格（命名、格式、日志、异常处理），避免在同一项目内引入多套风格。
- **优先静态分析**：在无法运行程序的前提下，充分利用类型系统、编译器诊断、静态分析以及搜索工具完成 Debug。

## 七、智能体总结要求

- 完成任务后，应简要总结：
  - 修改或新增了哪些核心类/方法。
  - 如何解决了主要编译错误或潜在逻辑问题。
  - 用户后续需要执行的命令（如 `dotnet build`、`dotnet test`、`dotnet run`——仅作为建议，不代为执行）。

