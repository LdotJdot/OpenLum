---
name: csharp
description: "创建、编辑、规划、编写 C# 项目。支持 dotnet CLI 创建/编译。铁律：dotnet build 通过=完成，禁止 dotnet run/test，用户自行运行。需查找代码时用 grep skill。"
---

# C# 项目 Skill（csharp）

用于创建、编辑、规划、编写 C# 项目程序。Agent 可自主规划任务、实现功能、编译、调试、迭代。工具：`read`、`write`、`list_dir`、`exec`。**查找代码时使用 grep skill**（加载 grep 的 SKILL.md 获取 rg 调用方式）。

## 铁律（必须遵守）

- **`dotnet build` 通过 = 任务完成，立即结束。**
- **禁止** 执行 `dotnet run`、`dotnet test` 或任何运行程序的命令。用户会自己运行。
- **禁止** 为「验证运行」而 exec 运行程序。编译成功即够。

## 工作流程l

1. **规划** — 确定项目结构、命名空间、类/接口拆分
2. **创建** — `dotnet new` 初始化项目
3. **编写/编辑** — `read`、`write` 操作 .cs、.csproj
4. **编译** — `dotnet build` 检查语法与依赖
5. **调试** — 根据编译错误定位问题，修改后重试
6. **迭代** — 直到通过编译、满足需求

## 常用命令（exec）

**exec 一律使用 PowerShell 语法**：链式用 `;` 不用 `&&`。**禁止 exec 执行 `dotnet run` 或 `dotnet test`。**

| 命令 | 说明 |
|------|------|
| `dotnet new sln -n MyApp` | 创建解决方案 |
| `dotnet new console -n MyApp` | 创建控制台项目 |
| `dotnet new classlib -n MyLib` | 创建类库 |
| `dotnet sln add MyApp/MyApp.csproj` | 添加项目到解决方案 |
| `dotnet add MyApp reference MyLib/MyLib.csproj` | 添加项目引用 |
| `dotnet build` / `dotnet build -v normal` | 编译（-v normal 输出完整构建结果到控制台） |
| `dotnet build -c Release` | Release 编译 |
| `dotnet restore` | 还原包 |

### NuGet 包搜索与添加

| 命令 | 说明 |
|------|------|
| `dotnet package search <关键词>` | 搜索 NuGet 包（需 .NET 8.0.2xx+ SDK） |
| `dotnet package search <关键词> --take 10` | 限制返回条数 |
| `dotnet add <项目> package <PackageId>` | 添加包到项目 |
| `dotnet add package Newtonsoft.Json -v 13.0.3` | 添加指定版本 |

包搜索可选参数：`--exact-match`、`--prerelease`、`--source <url>`、`--format json`。

## 代码查找

当需要**查找类名、方法名、引用、错误信息**等时，使用 **grep** skill：先 read grep 的 SKILL.md，再按其中说明通过 exec 调用 rg。例如仅搜 .cs 文件、显示行号、按目录过滤。避免硬编码路径，按 grep skill 的文档执行。

## Debug 流程

1. **编译错误**：根据错误信息定位文件与行号，read 该文件，修改后 write 回
2. **逻辑错误**：读相关代码，梳理调用链，修正逻辑

## 注意事项

- 路径相对于 workspace
- 批量修改时先 list_dir 确认结构，再逐个 read/write
- 编译前可先 `dotnet restore`
- 大输出 exec 会截断，可建议用户本地执行
- 不硬编码 grep 路径：通过 grep skill 文档获取 rg 调用方式

## 智能体须知（重要）

- **成功编译即视为任务完成**，即可结束。不运行程序，不执行测试。
- 运行程序由用户自行执行，智能体不做演示或验证运行。
- 内部调用 exec 时用户看不到界面，只需确保 `dotnet build` 通过。
- 编译时使用 `dotnet build -v normal`，将构建结果输出到控制台。
