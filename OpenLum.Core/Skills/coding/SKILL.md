---
name: coding
description: "Local coding workflow: read/write/list_dir/exec，加上 Grep + Glob 的纯文本搜索。Use when: editing code, building, refactoring, or exploring a codebase. Success = compile passes; for C#/.NET follow csharp skill (may run tests when needed)."
---

# Coding Skill

工作区内进行通用编码工作的基础 Skill：使用 `read`、`write`、`list_dir`、`exec`，并结合 **Grep + Glob** 做代码 / 文本搜索。

**铁律**：编译通过 = 完成。**禁止**无目的地 exec 运行程序（如 `python xxx.py`、长时间服务）。**C# / .NET** 项目不适用本条的绝对禁止：凡涉及 C#，必须遵循 **`csharp` Skill**（可在必要时按该 Skill 执行 `dotnet test` / `dotnet run`）。

## 与其他 Skill 的关系

- 做 **C# / .NET** 开发与 Debug：**必须先完整加载并遵守 `csharp` Skill**（含 TFM 识别、分版本与运行/测试策略），再结合本 `coding` 与 `search` skill。
- 需要写 **临时 Python 脚本** 补充工具能力时：配合 `python` skill，在 `script/任务目录` 下组织脚本。
- 只需做「查找 / 导航 / 定位代码位置」时：使用原生 **Grep** 与 **Glob**。
- 需要**按行号或按内容做增量编辑**（ReplaceRangeWithText、ReplaceAll、InsertLines、DeleteRange 等）时：配合 `editing` skill，通过 exec 执行其提供的 PowerShell 片段。

## When to Use

- Editing or creating files
- Building, linters
- Exploring project structure
- Refactoring or fixing bugs

Success = compile passes. Non-C# stacks: do not run the app unless the user asks; C#/.NET defers to `csharp` skill.

## 四步闭环：提高编写质量与成功率

按以下顺序执行，形成「分析 → 规划 → 执行 → 验证」的闭环，减少返工、提高一次通过率。

### 1. 识别分析问题 — 发生了什么

- **明确现象**：用户描述的是 Bug、新需求、重构还是配置问题？复现条件是什么？
- **收集信息**：用 `list_dir`、`read`、`Grep`、`Glob` 找到相关文件与调用关系；若有编译/运行错误，记录完整错误信息与行号。
- **归纳根因**：是缺逻辑、类型不匹配、依赖缺失，还是风格/架构不一致？先下结论再动手，避免盲目改。

### 2. 规划路径 — 应该如何做

- **列出改动点**：需要改/新增哪些文件、类、方法？依赖关系与调用方是否要一起改？
- **拆分步骤**：大改动拆成小步（例如先改接口、再改实现、再改调用方），每步都可单独编译验证。
- **选对工具**：读/写用 `read`/`write`，定位用 `Grep`/`Glob`，构建用 `exec`；涉及 C# 时**必须先读 `csharp` skill** 并按其执行（含是否运行测试）。

### 3. 代码编写 — 执行

- **先读再写**：修改前务必 `read` 目标文件及周边上下文，保持命名、格式、异常处理与项目一致。
- **小步提交**：每完成一小块逻辑就执行一次构建（如 `dotnet build`），避免一次改太多再排查。
- **少做大段重写**：大文件优先增量编辑，避免误删已有逻辑；新文件可整体 `write`。

### 4. 检查编译 — 闭环检查

- **必做编译**：修改后立即 `exec` 执行 build（如 `dotnet build -v normal`），以编译通过为完成标志。
- **处理错误**：按编译器报错的文件与行号去读代码，结合 Grep 找引用关系，修完再 build，直到零错误。
- **运行**：非 C# 项目一般不执行 `python xxx.py` 等；**C# / .NET** 按 `csharp` Skill 决定是否执行 `dotnet test` / `dotnet run`。

## Workflow

1. **list_dir(path)** — Explore structure. Use "." for workspace root.
2. **read(path)** — Read files. Use limit for large files.
3. **搜索代码 / 文本（强烈推荐）** — 结合 `search` skill：  
   - 精确 / 正则查找：优先使用 **Grep** 工具（而不是 shell `rg`/`grep`）；  
   - 模糊定位：先用 **Glob** 缩小文件范围，再用 **Grep** 精确定位。  
4. **write(path, content)** — Create or overwrite files.
5. **exec(command)** — Run shell commands (build, git). Working dir is workspace.
   - **直接使用 PowerShell 语法**：链式用 `;`，禁止 `&&`、`cmd /c`、bash 风格。含空格路径用 `& "path"`。

## 切块读取与增量编辑（exec + PowerShell / editing skill）

当前没有单独的「按行号读」「按片段替换」工具，可用 **exec** 执行 PowerShell 实现；更完整的编辑方法（ReadRange、ReplaceRangeWithText、ReplaceAll、InsertLines、DeleteRange、AppendLines 等）见 **editing** skill，均用 Shell 命令行实现，可直接在编程时复用。

### 切块读取：只读第 S 行～第 E 行

Grep 得到行号后，用下面命令只读出该区间（避免 read 只读前 N 行、读不到后面）：

```powershell
$path = '.\src\Foo.cs'; $s = 101; $e = 150; (Get-Content $path)[$s-1..$e-1]
```

- `$path`：文件路径，工作区相对（如 `.\src\Foo.cs`）或绝对均可。
- `$s`、`$e`：起始行、结束行（1-based）；PowerShell 数组 0-based，故用 `[$s-1..$e-1]`。
- 输出即为该行范围内容，可直接用于后续修改决策。

### 增量编辑（一）：按行号替换某一段

只改第 S 行～第 E 行，其余行不动（整文件会重写，但逻辑上是“按段改”）：

```powershell
$path = '.\src\Foo.cs'; $s = 101; $e = 110
$lines = Get-Content $path
$new = @('        // 新代码行1', '        // 新代码行2')
$lines[$s-1..$e-1] = $new
$lines | Set-Content $path -Encoding UTF8
```

- `$new` 为要替换成的若干行；行数可与原段不同。
- 含空格或特殊字符的路径给 `$path` 时用单引号；若路径在变量中，注意转义或引号。

### 增量编辑（二）：按内容替换（-replace）

已知要改的字符串或简单模式时，可直接整文件替换：

```powershell
$path = '.\src\Foo.cs'
(Get-Content $path) -replace '旧片段', '新片段' | Set-Content $path -Encoding UTF8
```

- `-replace` 支持正则；若只替换字面串，注意转义正则特殊字符（如 `[`、`$`）。
- 大文件时 exec 输出可能被截断，以是否报错和后续 build 为准。

### 使用顺序建议

1. 用 **Grep** 定位到文件与行号。
2. 用 **exec + 切块读取** 只读该行范围，确认上下文。
3. 用 **exec + 按行号替换** 或 **-replace** 做局部修改。
4. 必要时 **read** 或再 exec 切块读取核对，然后 **exec** 执行 build 做闭环检查。

## Tips

- Paths are relative to workspace.
- For large outputs, exec truncates. Ask user to run locally if needed.
- Prefer incremental edits over full rewrites for large files.
- 在进行重构 / Debug / 全局分析前，**先用 Grep / Glob 搜索，再进入具体文件修改**，避免盲目通读大文件。

