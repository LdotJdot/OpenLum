---
name: webbrowser
description: "浏览网页。与 read skill 风格一致：直接 exec 调用 exe，传参执行，stdout 为结果。"
---

# 网页浏览器 Skill

与 read skill 统一：直接调用 exe，传命令行参数，stdout 为 JSON 结果。

## exe 路径

| exe | 说明 |
|-----|------|
| skills/webbrowser/browser/openlum-browser.exe | 浏览器操作 |

## 命令格式

```
openlum-browser.exe [--visible|--headless] <command> [args]
```

- 浏览器可见性由 exe 目录下的 `openlum-browser.json` 指定（`forceVisible`：true 时强制显示，false 时由 `init` 的 headless/visible 决定）

## 命令

### navigate — 打开 URL
```
navigate --url <URL>
```

### snapshot — 获取页面结构
```
snapshot [--maxChars N]
```

### type — 输入文本
```
type --ref <REF> --text <TEXT> [--submit]
```

### click — 点击元素
```
click --ref <REF> [--force]
```

### page_text — 提取页面纯文本
```
page_text [--maxChars N]
```

### upload — 上传文件（路径为绝对路径或相对当前工作目录）
```
upload --ref <REF> --paths <path1> [path2...]
```

### tabs — 标签页列表/切换
```
tabs [--switch N]
```

### init — 切换配置（如可见性）
```
init [--visible|--headless]
```

### quit — 退出
```
quit
```
直接执行 `quit` 即退出，关闭浏览器，失去浏览器当前上下文。

## 示例

```powershell
# 打开 Bing，显示窗口
& "skills/webbrowser/browser/openlum-browser.exe" --visible navigate --url "https://cn.bing.com"

# 获取快照（找搜索框 ref）
& "skills/webbrowser/browser/openlum-browser.exe" snapshot

# 输入并搜索
& "skills/webbrowser/browser/openlum-browser.exe" type --ref 2 --text "ldotjdot" --submit

# 无头模式打开
& "skills/webbrowser/browser/openlum-browser.exe" navigate --url "https://example.com"

# 退出（关闭浏览器，失去当前上下文）
& "skills/webbrowser/browser/openlum-browser.exe" quit
```

## 前置条件

- 已安装 Edge 或 Chrome（系统浏览器）。未安装时直接报错。

## 错误处理

- 若返回「未检测到 Edge/Chrome」：直接告知用户安装，严禁执行 playwright.ps1 install。

## 典型流程

1. `navigate --url https://cn.bing.com`（加 `--visible` 显示窗口）→ 返回 snapshot 和 refs
2. 从 snapshot 找到搜索框 ref
3. `type --ref N --text "关键词" --submit` → 返回新页面 snapshot
4. 新标签页时 `tabs --switch 1` 切换后再操作
