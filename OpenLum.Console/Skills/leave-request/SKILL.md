---
name: leave-request
description: "在 BEWG 门户完成请假申请：登录 portal.bewg.net.cn、跳转 iTalent 人力资源系统(italent.cn)、进入请假、填写表单、上传证明材料。Use when: 用户要求请假、提交请假申请、人力资源请假。"
---

# 请假申请 Skill

在 BEWG 门户登录后，跳转 iTalent 人力资源系统（https://www.italent.cn/）完成请假申请。需用户先登录门户或确认已登录，Agent 完成导航、表单填写、附件上传。

## 使用场景

- 用户说「请假」「提交请假」「人力资源请假」
- 用户指定一个目录，要求从中找证明材料并完成请假申请

## 工作流程

### 1. 用户提供信息

用户会说明请假类型、日期、事由等。若指定目录，可从中找证明材料（如病假条、证明等）。若未说明，先询问。

### 2. 识别文件（若用户指定目录）

用 `list_dir` 列出指定目录及子目录，识别证明材料：

| 类型 | 扩展名 / 特征 | 用途 |
|------|---------------|------|
| **附件** | .pdf, .doc, .docx, .jpg, .jpeg, .png 等 | 上传至「附件」或「证明材料」区域 |

### 3. 导航到请假页面

1. `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" navigate --url "https://portal.bewg.net.cn/"')`
2. 若未登录：用下方登录凭据或等待用户手动登录
3. **登录进入门户后，停止。** 提示用户：「请手动点击门户中的「人力资源系统」进入 iTalent，完成后回复确认。」
4. 用户确认后，`exec(command='& "skills/webbrowser/browser/openlum-browser.exe" snapshot')` 检查当前是否已进入 iTalent；若未登录 iTalent，用下方登录凭据填入
5. `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" snapshot')` → 进入「我的请假」区域，查找并点击「请假申请」
6. 进入请假表单页

### 4. 填写表单

1. `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" snapshot')` 获取表单内 textbox、combobox、date 等 ref
2. 尽量填写可填项：
   - **请假类型**：病假、事假、年假、调休等（`exec(command='& "skills/webbrowser/browser/openlum-browser.exe" click --ref N')` 选择下拉）
   - **开始日期 / 结束日期**：按用户说明
   - **请假事由**：按用户说明
   - **备注**：其他需要说明的内容
3. 使用 `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" type --ref N --text "..."')` 填入文本框，`exec(command='& "skills/webbrowser/browser/openlum-browser.exe" click --ref N')` 选择下拉或日期。若点击无效：加 `--force`

### 5. 上传附件（若有证明材料）

1. `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" snapshot')` 找到「上传附件」「附件」或相关 file input 的 ref
2. `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" upload --ref N --paths "path1" "path2"')`：paths 为证明材料路径
3. 若有多个附件上传入口，按页面结构分别上传

### 6. 填写完成，不提交

- 再次 `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" snapshot')` 检查必填项是否已填
- **Agent 只负责填写，不点击提交**。提交确认必须由用户本人完成，提示用户检查后自行点击提交

## 登录凭据

- 用户名：`luojin`
- 密码：`********`

Agent 可用 `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" type --ref N --text "凭据"')` 在登录页填入。注意：勿将含密码的 skill 提交到公开仓库。

## 工具调用顺序

```
exec(command='& "skills/webbrowser/browser/openlum-browser.exe" navigate --url "https://portal.bewg.net.cn/"')
[用登录凭据或用户登录门户]
[停止] 提示用户手动点击「人力资源系统」进入 iTalent，回复确认后再继续
[用户确认后]
exec(command='& "skills/webbrowser/browser/openlum-browser.exe" snapshot') → 确认已进入 iTalent，进入「我的请假」，找「请假申请」ref
exec(command='& "skills/webbrowser/browser/openlum-browser.exe" click --ref N')；若无响应可试 --force
exec(command='& "skills/webbrowser/browser/openlum-browser.exe" snapshot') → 找各表单字段、附件上传 ref
exec(command='& "skills/webbrowser/browser/openlum-browser.exe" type --ref N --text "..."') 或 click --ref N
[若用户指定目录] list_dir(path=用户指定目录) → 识别证明材料
[若有附件] exec(command='& "skills/webbrowser/browser/openlum-browser.exe" upload --ref N --paths "path1"')
[检查必填项后停止，不点击提交。提示用户确认后自行提交]
```

## 路径与权限

- upload 直接传文件路径（绝对路径或相对当前工作目录）。

## 注意事项

1. **登录**：可用 skill 中的登录凭据通过 exec+type 填入；若有验证码需用户手动完成。
2. **门户→人力资源系统**：登录门户后，Agent 会暂停并提示用户手动点击进入人力资源系统，用户确认后再继续后续步骤。
3. **验证码**：如有验证码，由用户手动完成。
4. **点击无效**：可试 `force=true`。
5. **新标签页**：若点击打开了新标签页，需先 `exec(command='& "skills/webbrowser/browser/openlum-browser.exe" tabs')` 查看、`tabs --switch N` 切换，再 snapshot 继续操作。
6. **页面结构变化**：门户或 iTalent 若改版，snapshot 中的 ref 会变，需根据当前页面重新查找。
7. **必填项**：确保请假类型、开始/结束日期、事由等必填项已填写。
8. **提交**：Agent 只填写表单，不点击提交按钮；提交必须由用户确认后完成。
9. **证明材料**：病假等通常需上传医院证明，事假可不上传，按公司规定处理。
