---
name: search
description: "Searches workspace text and code files using Grep (ripgrep) for exact/regex matching and SemanticSearch for concept-level discovery. Use when: finding class/method/interface definitions, locating error codes or log messages, searching configuration keys, exploring where a feature is implemented, or narrowing scope before reading files. Handles .cs, .ts, .js, .json, .md, and other text files. Does NOT handle PDFs, Office docs, or binary files (use the read skill for those)."
---

# Search Skill

Searches workspace text and code files. Uses **Grep** (ripgrep) for exact/regex matching and **SemanticSearch** for concept-level discovery.

## Workflow

1. **Grep first** — for known strings, class names, error codes, log text:

   ```
   Grep: pattern: "class\\s+OrderService", type: "cs"
   Grep: pattern: "ERR_1234", output_mode: "files_with_matches"
   Grep: pattern: "Payment failed", glob: "**/*.cs"
   ```

   Start with `output_mode: "files_with_matches"` to list affected files, then switch to `content` with `-C`/`-A`/`-B` for context.

2. **SemanticSearch second** — for concept-level questions ("where is authentication handled?", "how is HTTP wrapped?"):

   ```
   SemanticSearch: query: "HTTP API call wrapper", target_directories: ["OpenLum.Console/"]
   ```

   Then refine with Grep inside the candidate files.

3. **Combine with other skills** — pair with `csharp` + `coding` for refactoring; use `search` to find inputs before handing off to `python` for processing.

## Tips

- **Narrow scope**: set `path` to a subdirectory and `glob` to target file types (`"**/*.cs"`, `"**/*.{ts,tsx}"`).
- **Case-insensitive**: use `-i: true` when unsure about casing.
- **Regex patterns**: `"HttpClient\\s*\\("`, `"ILogger<[^>]+>"`.

## Out of Scope

- Binary/PDF/Office/CAD → `read` skill
- GitHub/web search → `github` or `webbrowser` skill

