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
openlum-browser.exe [--headless] <command> [args]
```

## 命令

### navigate — 打开 URL
```
navigate --url <URL> --headless
```

### snapshot — 获取页面结构（经典模式：仅带 ref 的交互元素）
```
snapshot [--maxChars N]
```
> 仅列出可交互元素（button/link/textbox 等）并分配 ref，便于 click/type 使用。  
> 推荐：**所有交互相关的决策（点哪里、在哪个控件输入）应优先通过 snapshot 完成。**

### find — 按文本或角色搜索元素并返回坐标
```
find --text "<部分或完整文本>" [--role <角色名>] [--limit N]
```
> 用于**无 ref 时**定位元素：按可见文本或 ARIA 角色搜索，返回匹配项的中心坐标与边界框。  
> 返回字段：`matches[].index`、`matches[].centerX`、`matches[].centerY`、`matches[].width`、`matches[].height`、`matches[].text`。  
> 典型用法：先 `find --text "提交"` 得到 `centerX/centerY`，再 `click --x <centerX> --y <centerY>` 点击。  
> `--role` 可选，如 `button`、`link`；`--limit` 默认 10，最大 50。

### type — 输入文本
```
type --ref <REF> --text <TEXT> [--submit]
```

### click — 点击元素
```
click --ref <REF> [--force]
click --x <X> --y <Y>
```
> 两种方式二选一：**ref**（来自 snapshot）或 **坐标 x,y**（来自 find 的 centerX/centerY）。  
> 坐标点击在页面内用 `elementFromPoint(x,y)` 取该点元素并触发 click，兼容 iframe 与覆盖层。

### page_text — 提取页面纯文本（长文/兜底）
```
page_text [--maxChars N]
```
> 仅用于**阅读大量文本内容**或在 snapshot 中找不到某段文字时做兜底排查。
> **不要直接依赖 page_text 去“猜”要点哪个元素**，page_text 不包含 ref，无法直接交互。

### upload — 上传文件（路径为绝对路径或相对当前工作目录）
```
upload --ref <REF> --paths <path1> [path2...]
```

### tabs — 标签页列表/切换
```
tabs [--switch N]
```

### eval — 在页面中执行 JS
```
eval --expr "<JS 表达式>" [--maxChars N]
```
> 注意：`expr` 是一个 JS 表达式，例如 `() => window.location.href` 或
> `() => ({ title: document.title, href: location.href })`。返回值会被序列化为 JSON 字符串并截断到 `maxChars`。

### quit — 退出
```
quit
```
直接执行 `quit` 即退出，关闭浏览器，失去浏览器当前上下文。

## 示例

```powershell
# 打开 Bing，显示窗口
& "skills/webbrowser/browser/openlum-browser.exe" --headless navigate --url "https://cn.bing.com"

# 获取快照（找搜索框 ref）
& "skills/webbrowser/browser/openlum-browser.exe" snapshot

# 输入并搜索
& "skills/webbrowser/browser/openlum-browser.exe" type --ref 2 --text "ldotjdot" --submit

# 无头模式打开
& "skills/webbrowser/browser/openlum-browser.exe" --headless navigate --url "https://example.com"

# 按文本查找再按坐标点击（无 ref 时）
& "skills/webbrowser/browser/openlum-browser.exe" find --text "提交" --limit 5
& "skills/webbrowser/browser/openlum-browser.exe" click --x 125 --y 212

# 退出（关闭浏览器，失去当前上下文）
& "skills/webbrowser/browser/openlum-browser.exe" quit
```

## 前置条件

- 已安装 Edge 或 Chrome（系统浏览器）。未安装时直接报错。

## 错误处理

- 若返回「未检测到 Edge/Chrome」：直接告知用户安装，严禁执行 playwright.ps1 install。

## 典型流程

### 交互优先走 snapshot；无 ref 时用 find + 坐标点击

1. `navigate --url https://cn.bing.com` → 返回 snapshot 和 refs
2. 从 snapshot 中找目标控件对应的 `ref`，用 `click --ref <REF>` / `type --ref <REF> ...` 操作
3. 若目标在 snapshot 中无 ref（如静态文案、非标准控件），用 **find** 定位：
   - `find --text "提交"` 或 `find --text "确定" --role button` → 得到 `matches[].centerX/centerY`
   - `click --x <centerX> --y <centerY>` 点击该位置
4. 如出现新标签页，用 `tabs --switch N` 切换后继续以上步骤

### 需要读取大量文本或 snapshot 中找不到某段文字时

1. 先 `snapshot`，尝试在 snapshot 中搜索关键字；若能找到，继续按 ref 交互
2. 若 snapshot 中完全找不到该文字，再调用 `page_text` 获取更多纯文本内容（只用于阅读和排查）
3. 如确认是页面可访问性不足，可配合 `eval` 在 DOM 中按文本/选择器查找并操作元素
