---
name: read
description: "如果要对纯文本文件或特定目标文件 如PDF/Office(docx,doc,pptx,ppt)/CAD 简单提取纯文本文本，则使用改技能执行。--start N --limit N；续读时 next_start = last_start + last_limit，禁止用固定步长猜测。"
---

# Read Skill

## 铁律（必须遵守）

1. **续读计算公式**：`next_start = last_start + last_limit`（下次起始 = 上次起始 + 上次 limit）。**禁止**用固定步长（如 +1000、+2000）推算 start，否则会重叠或跳读。
2. **直接调用 exe**：不预先探路。失败时 exe 会报错。

## exe 路径（相对应用目录）

| exe | 格式 |
|-----|------|
| skills/read/pdf/read-pdf.exe | .pdf |
| skills/read/docx/read-docx.exe | .docx |
| skills/read/pptx/read-pptx.exe | .pptx |
| skills/read/docppt/read-docppt.exe | .doc, .ppt |
| skills/read/dxf/read-dxf.exe | .dxf |
| skills/read/dwg/read-dwg.exe | .dwg |

纯文本：用内置读取能力，不用 exe。

## 命令

```
<exe> "path" --start N --limit N
```

- 首次：`--start 0 --limit N`
- 续读：`--start (上一轮 start + 上一轮 limit) --limit N`，**必须按公式计算**，不要猜测
- workspace 外文件：path 用绝对路径

## 分页示例（limit=2000）

| 轮次 | start | limit | 读取范围 |
|-----|-------|-------|---------|
| 1 | 0 | 2000 | 0～1999 |
| 2 | 2000 | 2000 | 2000～3999 |
| 3 | 4000 | 2000 | 4000～5999 |

若某次返回字符数 &lt; limit，说明已到末尾，无需再续读。

## 示例

```powershell
# 总结 PDF（一次调用）
& "skills/read/pdf/read-pdf.exe" "D:\Desktop\Document\报告.pdf" --start 0 --limit 5000

# 续读（上一轮 start=0, limit=5000，则本轮 start=5000）
& "skills/read/pdf/read-pdf.exe" "D:\Desktop\Document\报告.pdf" --start 5000 --limit 5000
```
