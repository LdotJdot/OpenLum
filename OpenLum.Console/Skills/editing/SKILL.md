---
name: editing
description: "文本/代码的增量编辑：通过 exec 执行 PowerShell 实现 ReadRange、ReplaceRangeWithText、ReplaceAll、InsertLines、DeleteRange 等操作。Use when: 按行号或按内容修改文件、编程时局部替换、避免整文件重写。可与 coding / csharp skill 配合。"
---

# Editing Skill（editing）

在**没有**单独「按行号读/按片段替换」工具时，用 **exec** 执行 PowerShell 完成文本编辑。所有操作均以**命令行（Shell）**实现，适合编程场景下的增量修改。

## When to Use

- 按行号替换一段内容（ReplaceRangeWithText）
- 按行号只读某一段（ReadRange）
- 按字面或正则整文件替换（ReplaceAll / ReplaceAllRegex）
- 在指定行后插入、删除行范围、追加行
- 与 **coding**、**csharp** skill 配合：先 Grep/SemanticSearch 定位，再调用本 skill 中的方法做局部编辑

## 约定

- **路径**：相对工作区（如 `.\src\Foo.cs`）或绝对路径；含空格时用单引号 `'...'`。
- **行号**：一律 **1-based**；PowerShell 数组 0-based，脚本内已做 `$s-1..$e-1` 转换。
- **编码**：写文件统一 `-Encoding UTF8`。
- **执行方式**：将下面任一方法的 PowerShell 片段通过 **exec** 运行，按需替换变量值。

---

## 1. ReadRange — 只读第 S 行～第 E 行

Grep 得到行号后，只读该区间，避免 read 只读前 N 行读不到后面。

**用法**：`ReadRange(path, startLine, endLine)` → 用 exec 执行下面，输出即该范围内容。

```powershell
$path = '.\src\Foo.cs'; $s = 101; $e = 150; (Get-Content $path)[$s-1..$e-1]
```

---

## 2. ReplaceRangeWithText — 按行号替换一段为多行文本

只改第 S 行～第 E 行，其余行不动。新内容行数可与原段不同。

**用法**：`ReplaceRangeWithText(path, startLine, endLine, newLines)`。`newLines` 为字符串数组，在 PowerShell 中写成 `@('行1','行2')`；若多行字符串则用 `"L1`nL2" -split "`n"`。

```powershell
$path = '.\src\Foo.cs'; $s = 101; $e = 110
$lines = Get-Content $path
$new = @('        // 新代码行1', '        // 新代码行2')
$lines[$s-1..$e-1] = $new
$lines | Set-Content $path -Encoding UTF8
```

**多行字符串示例**（一段新代码）：

```powershell
$path = '.\src\Foo.cs'; $s = 101; $e = 110
$lines = Get-Content $path
$new = @"
        public void NewMethod() {
            return;
        }
"@ -split "`r?`n"
$lines[$s-1..$e-1] = $new
$lines | Set-Content $path -Encoding UTF8
```

---

## 3. ReplaceAll — 按字面串整文件替换（第一次或全部）

整文件查找并替换**字面**字符串。若只替换第一次出现，用 `ReplaceFirst` 逻辑（见下）。

**用法**：`ReplaceAll(path, oldText, newText)`。注意：`-replace` 默认是正则，字面串中 `[ ] $ . *` 等需转义；下面用 `[Regex]::Escape()` 做字面替换。

```powershell
$path = '.\src\Foo.cs'
$old = '旧片段'; $new = '新片段'
(Get-Content $path -Raw) -replace [Regex]::Escape($old), $new | Set-Content $path -Encoding UTF8 -NoNewline
```

若希望保留原文件末尾换行，可先读再替换再写：

```powershell
$path = '.\src\Foo.cs'
$old = '旧片段'; $new = '新片段'
$content = Get-Content $path -Raw
$content = $content -replace [Regex]::Escape($old), $new
[IO.File]::WriteAllText((Resolve-Path $path).Path, $content, [Text.UTF8Encoding]::new($false))
```

---

## 4. ReplaceAllRegex — 按正则整文件替换

**用法**：`ReplaceAllRegex(path, pattern, replacement)`。`pattern` 为正则，`replacement` 中可用 `$1` 等捕获组。

```powershell
$path = '.\src\Foo.cs'
(Get-Content $path -Raw) -replace '(\w+)\s*=\s*old', '$1 = new' | Set-Content $path -Encoding UTF8 -NoNewline
```

---

## 5. ReplaceFirst — 只替换第一次出现（字面）

**用法**：`ReplaceFirst(path, oldText, newText)`。

```powershell
$path = '.\src\Foo.cs'
$old = '第一个要改的'; $new = '改成的'
$content = Get-Content $path -Raw
$idx = $content.IndexOf($old)
if ($idx -ge 0) {
  $content = $content.Substring(0,$idx) + $new + $content.Substring($idx + $old.Length)
  [IO.File]::WriteAllText((Resolve-Path $path).Path, $content, [Text.UTF8Encoding]::new($false))
}
```

---

## 6. InsertLines — 在指定行后插入多行

在第 `afterLine` 行**之后**插入若干行（不删除原有行）。

**用法**：`InsertLines(path, afterLine, newLines)`。`newLines` 为行数组，如 `@('line1','line2')`。

```powershell
$path = '.\src\Foo.cs'; $after = 50
$lines = Get-Content $path
$insert = @('        // 插入行1', '        // 插入行2')
$before = $lines[0..($after-1)]; $rest = $lines[$after..($lines.Length-1)]
$before + $insert + $rest | Set-Content $path -Encoding UTF8
```

若要在**第 N 行之前**插入，则 `$after = N-1`，再用上面逻辑（即在第 N-1 行后插入）。

---

## 7. DeleteRange — 删除第 S 行～第 E 行

**用法**：`DeleteRange(path, startLine, endLine)`。

```powershell
$path = '.\src\Foo.cs'; $s = 101; $e = 105
$lines = Get-Content $path
$before = $lines[0..($s-2)]   # 1-based: 行 1..(s-1)
$after  = $lines[$e..($lines.Length-1)]
$before + $after | Set-Content $path -Encoding UTF8
```

当 `$s -eq 1` 时 `$lines[0..($s-2)]` 为空，可写为：

```powershell
$path = '.\src\Foo.cs'; $s = 101; $e = 105
$lines = Get-Content $path
$before = if ($s -le 1) { @() } else { $lines[0..($s-2)] }
$after  = if ($e -ge $lines.Length) { @() } else { $lines[$e..($lines.Length-1)] }
$before + $after | Set-Content $path -Encoding UTF8
```

---

## 8. AppendLines — 在文件末尾追加行

**用法**：`AppendLines(path, newLines)`。

```powershell
$path = '.\src\Foo.cs'
$append = @('', '// 追加行1', '// 追加行2')
Add-Content -Path $path -Value $append -Encoding UTF8
```

---

## 使用顺序建议（与 coding / csharp 配合）

1. 用 **Grep** 或 **SemanticSearch** 定位到文件与行号。
2. 需要确认上下文时：**exec + ReadRange** 只读该行范围。
3. 按需选择 **ReplaceRangeWithText**、**ReplaceAll**、**ReplaceFirst**、**InsertLines**、**DeleteRange** 等，通过 **exec** 执行对应 PowerShell。
4. 修改后可用 **read** 或再次 ReadRange 核对，再 **exec** 执行 build（如 `dotnet build -v normal`）做闭环检查。

## Tips

- 大文件时 exec 输出可能被截断，以是否报错和后续 build 为准。
- 含单引号的字符串在 PowerShell 中可写为 `'don''t'`（双单引号转义）。
- 优先增量编辑，避免整文件重写导致误删逻辑。
