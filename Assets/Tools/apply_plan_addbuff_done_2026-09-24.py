# -*- coding: utf-8 -*-
"""
2026-09-24  方案文档回写：AddBuff 目标来源变更已在实际代码中提前独立落地
⇒ 阶段一实施时 AddBuffEffect 用 ctx.Target 即为等价迁移，方案整体恢复为 100% 纯重构
"""
import io

P = r"E:\Project\BttleFreamwork-v1\战斗框架抽象方案.md"
text = io.open(P, encoding="utf-8", newline="").read()
assert "\r\n" not in text, "文档不是纯 LF"

pairs = []

# ── 1. 结论先行：恢复「纯重构」表述，但说明变更已提前落地
pairs.append((
"""- ⚠ **本方案不再是 100% 纯重构**：含**一处经用户拍板的行为变更** —— `AddBuff` 的作用目标从字段 `m_SkillTarget`
  改为「本次命中的目标」（详见 **4.6**）。除这一处外，其余全部严格等价。""",
"""- ★ **本方案恢复为 100% 纯重构**：`AddBuff` 的作用目标从字段 `m_SkillTarget` 改为「本次命中的目标」
  这一处**行为变更，已于 2026-09-24 作为独立修复提前落地**（详见 **4.6 / 4.6.1**）。
  ⇒ 阶段一实施时 `AddBuffEffect` 用 `ctx.Target` 即为**等价迁移**，不再产生新的行为变更。"""))

# ── 2. 4.6 标题加状态标注
pairs.append((
"### 4.6 ★ 用户拍板的行为变更：`AddBuff` 目标来源（2026-09-24）",
"### 4.6 ★ 用户拍板的行为变更：`AddBuff` 目标来源（2026-09-24）—— **已独立落地，见 4.6.1**"))

# ── 3. 在 4.6 的「待确认：Sneer」之前插入 4.6.1
pairs.append((
"""#### 待确认：`Sneer` 是否同样处理""",
"""#### 4.6.1 ★ 该变更已提前独立落地（2026-09-24）

**决策后不等阶段一，直接把修复做进了代码** —— 因为它是独立成立的 bug 修复，且做完之后**方案反而变简单了**：

| | 若留到阶段一做 | 已提前独立落地（现状） |
|---|---|---|
| 阶段一性质 | 含 1 处行为变更，需与重构**分步回归** | **纯重构**，可一次性回归 |
| 回归负担 | 要构造多目标场景单独验证 | 修复时单独验一次即可 |
| `AddBuffEffect` 写法 | `ctx.Target`（变更点） | `ctx.Target`（**等价迁移**） |

**实际改动**（`Skill.cs`，备份 `.bak.2026-09-24-addbuff`）：

```csharp
// 前：无参，用字段
case SkillEffectTypes.AddBuff: AddBuff();
public void AddBuff() { ... m_SkillTarget.OnAddBuff(...) ... }

// 后：接受本次命中目标
case SkillEffectTypes.AddBuff: AddBuff(target);
public void AddBuff(CombatUnitController target) { ... target.OnAddBuff(...) ... }
```

**验证情况**：

- 编译 0 Error；Play Error 恒 17（既有项），零新增；
- ⚠ **当前场景无法实测** —— 全表有 12 个技能配置了效果 2（`10010 / 10014 / 10015 / 10016 / 10018 / 10026 /
  10074 / 10080 / 10107 / 10108 / 10124 / 20048`），但**当前战斗场景只用 10003**（`WarrorController.AutoFightUpdate`
  硬编码 `GetSkill(10003)`），`10003` 的列 13 = `0|0;22|0;22` **不含效果 2** ⇒ 该路径未被触发，Play 无 Buff 日志。
- ⇒ 多目标场景下的行为差异**尚未实测**，需要用上述任一技能构造场景验证（或改 `WarrorController` 临时换技能）。

#### 待确认：`Sneer` 是否同样处理"""))

# ── 4. 6.1.3 ②·补 标注已完成
pairs.append((
"""**②·补 `AddBuff` 签名改造（★ 用户拍板的行为变更，非等价迁移）**：""",
"""**②·补 `AddBuff` 签名改造（★ 用户拍板的行为变更，非等价迁移）—— ✅ 已于 2026-09-24 提前完成，见 4.6.1**："""))

# ── 5. 6.1.3 ②·补 的取舍说明后加一句
pairs.append((
"""> ⚠ 这一步**必须在 switch 删除之前或同时完成**，且**不能**与「switch → 注册表」混在同一次回归里验证：
> 前者改行为，后者不改。分两步走才能区分「重构引入的漂移」与「拍板带来的变更」。""",
"""> ⚠ 这一步**必须在 switch 删除之前或同时完成**，且**不能**与「switch → 注册表」混在同一次回归里验证：
> 前者改行为，后者不改。分两步走才能区分「重构引入的漂移」与「拍板带来的变更」。
>
> ✅ **现状（2026-09-24）**：这一步**已经做完了**（`Skill.cs` 已改为 `AddBuff(CombatUnitController target)`），
> 所以阶段一实施时本步**跳过**，`AddBuffEffect` 直接用 `ctx.Target` 即为等价迁移 —— 方案恢复纯重构。"""))

# ── 6. 风险表第 10 行标注已落地
pairs.append((
"""| 10 | ★ **AddBuff 目标来源变更**（用户拍板，非等价迁移） | 必须与「switch→注册表」重构**分步实施、分步回归**；按 6.1.5 第 5~6 项构造多目标场景验证；单目标场景结果必须与改造前逐条一致 |""",
"""| 10 | ★ **AddBuff 目标来源变更**（用户拍板，非等价迁移） | ✅ **已提前独立落地**（4.6.1），与阶段一解耦，阶段一因此恢复纯重构。遗留：多目标场景**尚未实测**，需用配了效果 2 的技能（如 10026 / 20048）构造场景补验 |"""))

# ── 7. 总览图标注更新
pairs.append((
"""  ImmedDamageEffect       HureEffect                AddBuffEffect★ / SneerEffect / …将来19桩
                                                     ★=唯一行为变更：作用目标 m_SkillTarget → 本次命中目标（用户拍板，见4.6）""",
"""  ImmedDamageEffect       HureEffect                AddBuffEffect  / SneerEffect / …将来19桩
                                                     AddBuff 的目标来源修复已提前落地（4.6.1），故本阶段全程等价"""))

for old, new in pairs:
    n = text.count(old)
    assert n == 1, "命中 %d 次（应为 1）: %r" % (n, old[:70])
    text = text.replace(old, new)

io.open(P, "w", encoding="utf-8", newline="").write(text)

back = io.open(P, encoding="utf-8", newline="").read()
for old, new in pairs:
    assert new in back, "写回复验失败: %r" % new[:60]
assert "\r\n" not in back, "行尾被污染"
print("OK 方案文档 — 回写 %d 处，字数 %d" % (len(pairs), len(back)))
