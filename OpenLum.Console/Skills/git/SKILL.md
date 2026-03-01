---
name: git
description: "Git operations via shell. Use when: commit, push, pull, branch, status, diff, log, or other git commands. Use exec to run git."
---

# Git Skill

Use `exec` to run `git` commands. Working directory is workspace. **exec 用 PowerShell 语法**：链式用 `;` 不用 `&&`。

## When to Use

- Check status, diff, log
- Create branches, switch branches
- Commit, push, pull
- Stash, merge, rebase (when user requests)

## When NOT to Use

- Cloning from GitHub → `exec("git clone ...")` is fine
- GitHub-specific (PRs, issues, CI) → use github skill

## Common Commands (run via exec)

**Status:** `git status`, `git diff`, `git log --oneline -10`
**Branch:** `git branch`, `git checkout -b feature-x`
**Commit:** `git add .`, `git commit -m "message"`, `git push`
**Remote:** `git pull`, `git fetch`, `git remote -v`
