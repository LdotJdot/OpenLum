---
name: github
description: "GitHub operations via gh CLI. Use when: checking PR/issue status, creating issues/PRs, viewing CI runs, or querying GitHub API. Requires gh CLI. Use exec to run gh commands."
---

# GitHub Skill

Use `exec` to run the `gh` CLI for GitHub repositories, issues, PRs, and CI.

## When to Use

- Checking PR status, reviews, or merge readiness
- Creating, closing, or commenting on issues
- Creating or merging pull requests
- Viewing CI/workflow run status and logs
- Querying GitHub API

## When NOT to Use

- Local git (commit, push, pull) → use the git skill
- Cloning → `exec("git clone ...")`
- Non-GitHub repos → different CLIs

## Setup

```bash
gh auth login
gh auth status
```

## Common Commands (run via exec)

**PRs:** `gh pr list`, `gh pr view 55`, `gh pr create`, `gh pr merge 55 --squash`
**Issues:** `gh issue list`, `gh issue create`, `gh issue close 42`
**CI:** `gh run list`, `gh run view <id>`, `gh run view <id> --log-failed`
**API:** `gh api repos/owner/repo/pulls/55 --jq '.title'`

Always use `--repo owner/repo` when not in a git repo. Use `--json` and `--jq` for structured output.
