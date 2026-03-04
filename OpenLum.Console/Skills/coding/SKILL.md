---
name: coding
description: "Local coding workflow: read files, write files, run commands. Use when: editing code, building, refactoring, or exploring a codebase. Success = compile passes; user runs the program."
---

# Coding Skill

Use `read`, `write`, `list_dir`, and `exec` for coding in the workspace.

**铁律**：编译通过 = 完成。**禁止** exec 运行程序（如 `dotnet run`、`python xxx.py`）。用户自行运行。

## When to Use

- Editing or creating files
- Building, linters
- Exploring project structure
- Refactoring or fixing bugs

Success = compile passes. Do not run the program; let the user run it.

## Workflow

1. **list_dir(path)** — Explore structure. Use "." for workspace root.
2. **read(path)** — Read files. Use limit for large files.
3. **write(path, content)** — Create or overwrite files.
4. **exec(command)** — Run shell commands (build, git). Working dir is workspace.
   - **直接使用 PowerShell 语法**：链式用 `;`，禁止 `&&`、`cmd /c`、bash 风格。含空格路径用 `& "path"`。

## Tips

- Paths are relative to workspace.
- For large outputs, exec truncates. Ask user to run locally if needed.
- Prefer incremental edits over full rewrites for large files.
