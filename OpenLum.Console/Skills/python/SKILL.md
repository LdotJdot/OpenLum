---
name: python
description: "Writes and runs local Python scripts for data processing, file transformations, batch generation, web scraping, and automation tasks. Use when: the user says 'write a script', 'automate', 'Python', 'batch process', 'convert CSV', 'parse JSON', 'scrape', or when built-in tools and existing skills cannot handle a complex data/file task. Scripts are organized in workspace `script/` subdirectories."
---

# Python Skill

Writes local Python scripts for tasks that built-in tools or other skills cannot handle — data processing, format conversion, batch file operations, web scraping, test data generation, etc.

## Directory Convention

- **Root:** `script/`
- **Per task:** create a named subfolder, e.g. `script/20260305-data-clean/` or `script/json-to-csv-converter/`
- All Python files for one task stay in its subfolder.

## Commands (PowerShell)

```powershell
# Run a script
python .\script\<task-folder>\main.py

# With arguments
python .\script\<task-folder>\main.py --input data.json --output result.csv

# Virtual environment (when dependencies needed)
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install requests pandas
```

> Paths with spaces require quotes: `python ".\script\my-task\main.py"`

## Workflow

1. **Analyze** — clarify input/output formats, data scale, edge cases.
2. **Plan** — decide subfolder name, required dependencies, whether a venv is needed.
3. **Write** — read existing scripts or data samples first, then create `main.py`. Keep code clear and modular.
4. **Validate** — run `python -m py_compile main.py` for syntax check. For large or sensitive output, prompt the user to run locally rather than executing directly.

