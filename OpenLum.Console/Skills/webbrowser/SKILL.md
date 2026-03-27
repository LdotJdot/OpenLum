---
name: webbrowser
description: "Controls a local browser to navigate URLs, interact with page elements, extract text, and automate web workflows via openlum-browser.exe. Use when: the user says 'visit URL', 'open website', 'browse to', 'fetch page', 'web scraping', 'fill out form', 'click button', or needs to interact with any web page. Supports headless mode, element clicking/typing by ref, page snapshots, file uploads, and tab management."
---

# Web Browser Skill

Automates browser interactions via `openlum-browser.exe`. All commands return JSON on stdout.

**Exe path:** `skills/webbrowser/browser/openlum-browser.exe`

## Commands

| Command | Usage | Description |
|---------|-------|-------------|
| `navigate` | `navigate --url <URL> [--headless]` | Open a URL |
| `snapshot` | `snapshot [--maxChars N]` | Get page structure with element refs |
| `type` | `type --ref <REF> --text <TEXT> [--submit]` | Type text into an element |
| `click` | `click --ref <REF> [--force]` | Click an element |
| `page_text` | `page_text [--maxChars N]` | Extract page plain text |
| `upload` | `upload --ref <REF> --paths <path1> [path2...]` | Upload files |
| `tabs` | `tabs [--switch N]` | List or switch tabs |
| `quit` | `quit` | Close browser (loses context) |

## Examples

```powershell
# Open Bing
& "skills/webbrowser/browser/openlum-browser.exe" navigate --url "https://cn.bing.com"

# Get snapshot to find element refs
& "skills/webbrowser/browser/openlum-browser.exe" snapshot

# Type and submit search
& "skills/webbrowser/browser/openlum-browser.exe" type --ref 2 --text "ldotjdot" --submit

# Headless mode
& "skills/webbrowser/browser/openlum-browser.exe" --headless navigate --url "https://example.com"
```

## Workflow

1. `navigate --url <URL>` → returns snapshot with element refs
2. Inspect snapshot to find target element ref
3. `type`/`click` on the ref → verify by running `snapshot` again to confirm the action succeeded
4. For new tabs: `tabs --switch N` before further interaction
5. When done: `quit` to close the browser

## Prerequisites

- Edge or Chrome must be installed. If missing, inform the user — **never** run `playwright.ps1 install`.

## Error Handling

- **"Edge/Chrome not detected"**: tell the user to install a supported browser.
- **Element ref invalid**: re-run `snapshot` to get updated refs before retrying.
