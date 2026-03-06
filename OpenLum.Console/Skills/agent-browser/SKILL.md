---
name: agent-browser
description: "通过本地 npm 安装的 agent-browser CLI 执行浏览器自动化：打开页面、快照、点击、输入、上传等。Use when: 浏览网页、自动化填表、截图、获取页面结构、需要浏览器操作时。"
---

# agent-browser Skill

通过 shell 调用本机 `agent-browser`（fast browser automation CLI for AI agents）。参考：<https://agent-browser.dev/commands>。

## 调用方式

```bash
agent-browser <command> [args] [options]
```


**打开网站**：默认使用 `--headed` 参数打开，**要显示浏览器窗口**（例如：`agent-browser open --headed "https://example.com"`）。无特殊需求时不要省略 `--headed`。

---

## Core

| 命令 | 说明 |
|------|------|
| `open <url>` | 打开 URL（别名：goto, navigate）。**默认加 `--headed`，显示窗口** |
| `click <sel>` | 点击元素；`--new-tab` 在新标签打开 |
| `dblclick <sel>` | 双击 |
| `fill <sel> <text>` | 清空并填入 |
| `type <sel> <text>` | 在元素内输入 |
| `press <key>` | 按键（Enter, Tab, Control+a）（别名：key） |
| `keyboard type <text>` | 在当前焦点输入（无需选择器） |
| `keyboard inserttext <text>` | 插入文本（不触发按键事件） |
| `keydown <key>` / `keyup <key>` | 按下/释放键 |
| `hover <sel>` / `focus <sel>` | 悬停 / 聚焦 |
| `select <sel> <val>` | 选择下拉项 |
| `check <sel>` / `uncheck <sel>` | 勾选 / 取消勾选 |
| `scroll <dir> [px]` | 滚动 up/down/left/right；`--selector <sel>` 限定元素 |
| `scrollintoview <sel>` | 将元素滚入视口 |
| `drag <src> <dst>` | 拖拽 |
| `upload <sel> <files...>` | 上传文件 |
| `screenshot [path]` | 截图；`--full` 整页；`--annotate` 带编号标注 |
| `pdf <path>` | 另存为 PDF |
| `snapshot` | 无障碍树 + refs（供后续 click/fill 用 @e1, @e2…） |
| `eval <js>` | 执行 JavaScript |
| `close` | 关闭浏览器（别名：quit, exit） |

## Get info

`agent-browser get <what> [selector]`  
what: `text`, `html`, `value`, `attr <name>`, `title`, `url`, `count`, `box`, `styles`

## Check state

`agent-browser is <what> <selector>`  
what: `visible`, `enabled`, `checked`

## Find elements

语义定位 + 动作（click, fill, type, hover, focus, check, uncheck, text）：

```
agent-browser find role <role> <action> [value]
agent-browser find text <text> <action>
agent-browser find label <label> <action> [value]
agent-browser find placeholder <ph> <action> [value]
agent-browser find alt <text> <action>
agent-browser find title <text> <action>
agent-browser find testid <id> <action> [value]
agent-browser find first <sel> <action> [value]
agent-browser find last <sel> <action> [value]
agent-browser find nth <n> <sel> <action> [value]
```

选项：`--name <name>` 按可访问名过滤 role；`--exact` 要求文本完全匹配。

示例：

```
agent-browser find role button click --name "Submit"
agent-browser find label "Email" fill "test@test.com"
agent-browser find alt "Logo" click
agent-browser find first ".item" click
agent-browser find last ".item" text
agent-browser find nth 2 ".card" hover
```

## Wait

| 用法 | 说明 |
|------|------|
| `wait <selector>` | 等待元素出现 |
| `wait <ms>` | 等待毫秒数 |
| `wait --text "Welcome"` | 等待文本出现 |
| `wait --url "**/dash"` | 等待 URL 匹配 |
| `wait --load networkidle` | 等待网络空闲 |
| `wait --fn "condition"` | 等待 JS 条件为真 |
| `wait --download [path]` | 等待下载完成 |

## Downloads

- `download <sel> <path>` — 点击元素触发下载并保存到 path
- `wait --download [path]` — 等待任意下载完成

默认下载目录：`--download-path <path>` 或环境变量 `AGENT_BROWSER_DOWNLOAD_PATH`。未设置时使用临时目录，浏览器关闭后删除。

## Mouse

```
agent-browser mouse move <x> <y>
agent-browser mouse down [button]
agent-browser mouse up [button]
agent-browser mouse wheel <dy> [dx]
```

## Settings（Browser）

```
agent-browser set viewport <w> <h>
agent-browser set device <name>          # 如 "iPhone 14"
agent-browser set geo <lat> <lng>
agent-browser set offline [on|off]
agent-browser set headers <json>
agent-browser set credentials <user> <pass>
agent-browser set media [dark|light]     # 会话内持久
```

全局深色/浅色：`agent-browser --color-scheme dark open https://example.com`

## Cookies & storage

```
agent-browser cookies                    # 获取全部 cookie
agent-browser cookies set <name> <val>  # 设置（支持 --url, --domain, --path, --httpOnly, --secure, --sameSite, --expires）
agent-browser cookies clear

agent-browser storage local              # 全部 localStorage
agent-browser storage local <key>        # 某 key
agent-browser storage local set <k> <v>
agent-browser storage local clear

agent-browser storage session            # 同上，sessionStorage
```


## Tabs & frames

```
agent-browser tab                  # 列出标签
agent-browser tab new [url]        # 新标签
agent-browser tab <n>              # 切换到第 n 个
agent-browser tab close [n]        # 关闭标签
agent-browser window new           # 新浏览器窗口
agent-browser frame <sel>          # 进入 iframe
agent-browser frame main           # 回到主框架
```

## Dialogs

```
agent-browser dialog accept [text]  # 接受弹窗（可选填写 prompt 文本）
agent-browser dialog dismiss        # 关闭
```



## Auth vault

```
agent-browser auth save <name> [opts]   # 保存登录配置
agent-browser auth login <name>
agent-browser auth list
agent-browser auth show <name>
agent-browser auth delete <name>
```

save 选项：`--url`（必填）、`--username`（必填）、`--password` 或 `--password-stdin`、`--username-selector`、`--password-selector`、`--submit-selector`。

示例：`echo "pass" | agent-browser auth save github --url https://github.com/login --username user --password-stdin`

## Confirmation

使用 `--confirm-actions` 时，指定类型的操作会返回 `confirmation_required` 而非直接执行，需用 `confirm <id>` 或 `deny <id>` 批准/拒绝。超时 60 秒未确认则自动拒绝。

```
agent-browser --confirm-actions eval,download eval "document.title"
# 返回 confirmation_required 与 id
agent-browser confirm c_8f3a1234
```

## State management

```
agent-browser state save <path>
agent-browser state load <path>
agent-browser state list
agent-browser state show <file>
agent-browser state rename <old> <new>
agent-browser state clear [name]
agent-browser state clear --all
agent-browser state clean --older-than <days>
```

## Sessions

`agent-browser session` / `agent-browser session list`

## Navigation

`agent-browser back` / `agent-browser forward` / `agent-browser reload`

## Setup

- `agent-browser install` — 下载浏览器（首次）
- `agent-browser install --with-deps` — Linux 下同时安装系统依赖

## Diff

- `diff snapshot` — 当前与上次 snapshot 对比
- `diff screenshot --baseline` — 当前与基线图对比
- `diff url <u1> <u2>` — 两页面对比

---

## Snapshot 选项

- `-i, --interactive` — 仅交互元素
- `-c, --compact` — 去掉空结构节点
- `-d, --depth <n>` — 限制树深度
- `-s, --selector <sel>` — 限定在 CSS 选择器范围内

示例：`agent-browser snapshot -i`

---


## Local files

用 `file://` 打开本地文件（PDF、HTML 等）需加 `--allow-file-access`：

```
agent-browser --allow-file-access open file:///path/to/document.pdf
agent-browser --allow-file-access open file:///path/to/page.html
agent-browser screenshot output.png
```

仅 Chromium；该标志允许页面内 JS 访问其他本地文件。

---

## Command chaining

浏览器由后台 daemon 保持，可用 `&&` 在同一 shell 中串联命令：

```
agent-browser open example.com && agent-browser wait --load networkidle && agent-browser snapshot -i
agent-browser fill @e1 "user@example.com" && agent-browser fill @e2 "pass" && agent-browser click @e3
agent-browser open example.com && agent-browser wait --load networkidle && agent-browser screenshot page.png
```

不需要解析中间输出时用 `&&`；需要先解析 snapshot 再根据 ref 操作时，分开执行命令。

---

## Examples

```
agent-browser open example.com
agent-browser snapshot -i
agent-browser click @e2
agent-browser fill @e3 "test@example.com"
agent-browser find role button click --name Submit
agent-browser get text @e1
agent-browser screenshot --full
agent-browser screenshot --annotate
agent-browser wait --load networkidle
agent-browser --cdp 9222 snapshot
agent-browser --auto-connect snapshot
agent-browser --color-scheme dark open example.com
agent-browser --profile ~/.myapp open example.com
agent-browser --session-name myapp open example.com
```

---

## 典型流程

1. `agent-browser open https://cn.bing.com`（需窗口时加 `--headed`）
2. `agent-browser wait --load networkidle`（可选）
3. `agent-browser snapshot -i` → 得到 @e1, @e2 等 ref
4. `agent-browser fill @e2 "关键词"`，再 `press Enter` 或 `click @e3`
5. 新标签：`agent-browser tab` 查看，`agent-browser tab 1` 切换
6. `agent-browser close`

## 前置条件

- 已安装：`npm install -g agent-browser`，首次运行 `agent-browser install`。
- 或：`npx agent-browser <command> ...`（不安装时较慢）。
