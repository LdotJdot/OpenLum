# OpenLum 效率复盘：`fly报奖/support data` 多 Markdown 分析任务

## 任务

见 `entire-10-fly-support-desktop.example.txt`：分析 `D:\Desktop\fly报奖\support data` 下全部 `.md`，写出汇总到 `out-fly-support-summary.md`。

## 两次实跑对比（会话日志）

### Run A（迭代前：未强调「勿先递归列整树」）

- 首轮工具：`list_dir`（递归/大范围列举）→ 再 `glob` → 多轮 `read_many` → `submit_plan` → `read_many` → `write`。
- 问题：在已给**绝对路径**时仍先走**整树型列举**，徒增 token 与一轮延迟。

### Run B（迭代后：用户任务 + `list_dir` 描述补充）

- 首轮：部分模型会先 `submit_plan`（workflow 下常见），随后 **`glob` 直接筛 `.md`**，**未再出现 `list_dir` 递归**。
- 随后：`read_many` 分批读完 → `write`。
- 改进：去掉无效的「先列整树」一步，工具链更短。

## 量化（Run A 日志 `session-20260414-110416-*.log`）

- 含工具的 assistant 轮次：约 **7**（至 `write` 止），最后 `(none)` 为自然语言收束。
- `approx_prompt_chars` 随历史膨胀（多轮 read_many 结果进入上下文），峰值约 **47k 字符量级**（日志行 `approx_prompt_chars=47133`）。

## 改进方向（解耦原则）

| 层级 | 建议 |
|------|------|
| **固定系统提示** | 保持抽象（意图、安全、效率原则），不绑定具体工具名与文件类型。 |
| **工具 description** | 在 `list_dir` 等工具上说明「已知目录 + 按模式找文件时，避免先全树列举」——已加一句。 |
| **任务提示（用户/脚本）** | 对「已给绝对路径」的任务显式写清期望（勿先递归 list）——已写入 `entire-10`。 |
| **可选 `promptOverlay`（openlum.json）** | 团队/部署级短约束，经 **`## Config overlay`** 注入，与代码中的 `SystemPromptBuilder` 解耦。 |

## Cursor 类环境如何做（简要）

1. **系统层**：角色、合规、抽象工作方式（少占 token、少轮次）。
2. **工具层**：每个工具自带「怎么用、何时用」（OpenLum 即各 `ITool.Description`）。
3. **任务层**：用户输入、Skill、或 CI 脚本——把**领域步骤**放这里，而不是写进固定系统提示。
4. **可选覆盖层**：Cursor 常有 Rules / 团队设置；OpenLum 对应 **`promptOverlay`**，用于统一注入组织习惯，仍应避免重复工具说明书式内容。
