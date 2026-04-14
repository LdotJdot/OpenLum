---
name: python
description: "在缺少专用工具时，优先使用本地 Python 脚本完成复杂任务。临时脚本统一放在工作区 script 目录下的独立子文件夹中。"
---

# Python Skill

当没有更合适的专用工具（如特定 API、数据库工具等）时，可以用 **Python 脚本** 在本地完成复杂的数据处理、格式转换、批量生成等任务。

## 四步闭环（提高脚本质量与成功率）

与 coding 一致：**识别分析问题 → 规划路径 → 编写执行 → 检查验证**。

1. **识别分析问题** — 明确输入/输出格式、数据规模、边界情况与错误信息（若有）。
2. **规划路径** — 确定脚本放在 `script/任务名/`、需要哪些文件/参数、是否要虚拟环境或依赖。
3. **代码编写** — 先读已有类似脚本或数据样例，再写 `main.py`；保持结构清晰、可读。
4. **检查验证** — 逻辑与异常处理自查；输出量大或敏感时，提示用户本地运行并查看结果，不代为执行。

## 目录约定

- **工作区脚本根目录**：`script`
- **每个临时任务**：在 `script` 下新建一个 **独立子文件夹**，用有意义的名称（如时间戳或任务名），例如：
  - `script/20260305-data-clean/`
  - `script/json-to-csv-converter/`
- **该任务相关的所有 Python 文件** 都放在这个子文件夹中，避免不同任务的脚本混在一起。

## 常用 Python 命令（PowerShell）

- **运行脚本（推荐使用工作区 Python）**：
  - `python .\script\子文件夹\main.py`
- **指定脚本参数**：
  - `python .\script\子文件夹\main.py --input data.json --output result.csv`
- **创建虚拟环境（如有需要）**：
  - `python -m venv .venv`
  - `.\.venv\Scripts\Activate.ps1`
- **安装依赖**（在已激活的虚拟环境中）：
  - `pip install requests pandas`

> 注意：命令在 **PowerShell** 中执行，含空格路径时要用引号，例如：`python ".\script\my-task\main.py"`。

## 使用原则

1. **优先选择 Python**：当内置工具或现有 Skill 无法方便完成任务时（例如复杂数据处理、文件批量重构、生成测试数据等），优先考虑编写 Python 脚本。
2. **保持脚本可重用**：尽量把一次性的脚本写得清晰、模块化，方便后续复用或改造。
3. **不负责最终运行结果展示**：如果输出量很大或涉及真实业务数据，可以提示用户在本地手动运行脚本并查看结果。

## 典型流程

1. 在 `script` 下为当前任务新建目录，例如 `script/20260305-clean-logs/`。
2. 在该目录下创建 `main.py` 及必要的辅助模块。
3. 在代码中实现读取输入文件、处理逻辑、输出结果等功能。
4. 根据需要提示用户在 PowerShell 中执行类似命令：
   - `python .\script\20260305-clean-logs\main.py --input ".\logs\raw.log" --output ".\logs\clean.log"`

