---
name: grep
description: "文件内容搜索匹配优先考虑，ripgrep (rg) 递归搜索纯文本，支持正则。通过 exec 调用 Skills/grep/rg.exe，用于在工作区或指定目录搜索内容。"
---

# 文本搜索 Skill（grep / ripgrep）

ripgrep (rg) 递归搜索当前目录及子目录中的纯文本，支持正则表达式。默认尊重 .gitignore，跳过隐藏和二进制文件。Agent 通过 exec 调用本 skill 目录下的 rg.exe。

## 可执行文件

| 路径 | 说明 |
|------|------|
| grep/rg.exe | ripgrep 14.x，递归正则搜索 |

## 基本用法

```
rg [OPTIONS] PATTERN [PATH ...]
```

- `PATTERN`：正则表达式（若以 `-` 开头，用 `-e PATTERN` 或 `-- PATTERN`）
- `PATH`：要搜索的文件或目录，可省略（默认当前目录递归）

## 常用参数

### 搜索行为
| 参数 | 说明 |
|------|------|
| `-i` | 忽略大小写 |
| `-F` | 按字面字符串（禁用正则） |
| `-w` | 整词匹配 |
| `-v` | 反向匹配（输出不包含的行） |
| `-e PATTERN` | 指定模式，可多次使用 |
| `-f FILE` | 从文件读取模式（每行一个） |

### 输出控制
| 参数 | 说明 |
|------|------|
| `-n` | 显示行号 |
| `-c` | 每文件匹配数 |
| `-l` | 仅显示含匹配的文件名 |
| `-L` | 仅显示不含匹配的文件名 |
| `-C N` | 显示匹配行前后 N 行 |
| `-A N` | 显示匹配行后 N 行 |
| `-B N` | 显示匹配行前 N 行 |
| `-o` | 仅输出匹配部分 |
| `--color=never` | 关闭颜色（适合管道） |

### 文件筛选
| 参数 | 说明 |
|------|------|
| `-g GLOB` | 包含/排除匹配 GLOB 的文件（`!*.log` 排除） |
| `-t TYPE` | 只搜指定类型（如 cs, py, md） |
| `-T TYPE` | 排除类型 |
| `-d N` | 限制递归深度 |
| `--max-depth=N` | 同上 |
| `.` 或 `--hidden` | 搜索隐藏文件 |

### 其他
| 参数 | 说明 |
|------|------|
| `-j N` | 线程数 |
| `-m N` | 每文件最多匹配 N 行 |
| `--stats` | 输出统计 |
| `--type-list` | 列出支持的文件类型 |

## 使用示例（包含多模式 OR 搜索）

```bash
# 在 workspace 搜索 "TODO"
exec: Skills/grep/rg.exe "TODO" .

# 在指定目录搜索，显示行号
exec: Skills/grep/rg.exe -n "function" src/

# 忽略大小写、显示前后 2 行
exec: Skills/grep/rg.exe -i -C 2 "error" logs/

# 仅搜索 .cs 文件
exec: Skills/grep/rg.exe -t cs "class"

# 仅输出含匹配的文件名
exec: Skills/grep/rg.exe -l "deprecated"

# 字面字符串（非正则）
exec: Skills/grep/rg.exe -F "exact.string"

# 限制深度、关闭颜色
exec: Skills/grep/rg.exe -d 2 --color=never "pattern" folder/

# 多关键字 OR 搜索（推荐：正则分组）
# 匹配包含 "io" 或 "path" 或 "xxx" 的行
exec: Skills/grep/rg.exe -n "(io|path|xxx)" .

# 多关键字 OR 搜索（等价写法：多次 -e）
exec: Skills/grep/rg.exe -n -e "io" -e "path" -e "xxx" .
```

## Agent 调用方式（优先绝对路径 + 多模式）

- **优先使用绝对路径调用本 skill 的 exe**，例如：

```
exec: d:/Data/个人/FAV/MyCoreProj/Projects/OpenLumSharp/OpenLum.Console/Skills/grep/rg.exe -n "(io|path|xxx)" d:/Data/个人/FAV/MyCoreProj/Projects/OpenLumSharp
```

- 也可以从 workspace 根目录使用以 `Skills/` 开头的路径（推荐于相对路径）：

```
exec: Skills/grep/rg.exe -n "(io|path|xxx)" .
```

- 仅在路径较短且上下文已明确 workspace 根目录时，才使用工作区相对路径：

```
exec: grep/rg.exe -n -e "io" -e "path" -e "xxx" src/
```

输出到 stdout，Agent 可解析结果。
