# -*- coding: utf-8 -*-
"""
2026-09-24  把用户拍板固化进《战斗框架抽象方案.md》
决策：AddBuff() 改为接受受击者 CombatUnitController target（作用于本次命中目标，而非字段 m_SkillTarget）
性质：★ 非等价迁移，是本方案唯一一处经用户拍板的行为变更，需单独立项回归
"""
import io

P = r"E:\Project\BttleFreamwork-v1\战斗框架抽象方案.md"
text = io.open(P, encoding="utf-8", newline="").read()
assert "\r\n" not in text, "文档不是纯 LF，需重新确认行尾"

pairs = []

# ── 1. 结论先行：补一句「含一处行为变更」
pairs.append((
"""- 项目已有成功先例（`ICombatState` 接口化 FSM、UnitView / UnitHitReaction 组件化、Buff Tags 数据驱动），团队风格完全接受这种演进。
""",
"""- 项目已有成功先例（`ICombatState` 接口化 FSM、UnitView / UnitHitReaction 组件化、Buff Tags 数据驱动），团队风格完全接受这种演进。
- ⚠ **本方案不再是 100% 纯重构**：含**一处经用户拍板的行为变更** —— `AddBuff` 的作用目标从字段 `m_SkillTarget`
  改为「本次命中的目标」（详见 **4.6**）。除这一处外，其余全部严格等价。
"""))

# ── 2. 4.4 表格：AddBuff 行标注
pairs.append((
"| 2 | AddBuff | ✅ 已实现 | OnAddBuff → BuffSystem | 已迁移 |",
"| 2 | AddBuff | ✅ 已实现 | OnAddBuff → BuffSystem | 已迁移（★ 目标来源变更，见 4.6） |"))

# ── 3. 新增 4.6 节（插在 4.5 之后、「## 5. 可行性评估」之前）
pairs.append((
"""**攻击端没有「沉默」效果号**。`SkillEffectTypes` 0~22 中无 Silence，但受击端 `OnSilence → ApplySilence → SilenceState` 全链路已就绪。这正是开闭原则价值的直接例证：策略化后新增「沉默」= 追加枚举值 23 + 一个效果类 + 一行注册，**不再需要动 `Skill.cs` 的 switch**。

---

## 5. 可行性评估""",
"""**攻击端没有「沉默」效果号**。`SkillEffectTypes` 0~22 中无 Silence，但受击端 `OnSilence → ApplySilence → SilenceState` 全链路已就绪。这正是开闭原则价值的直接例证：策略化后新增「沉默」= 追加枚举值 23 + 一个效果类 + 一行注册，**不再需要动 `Skill.cs` 的 switch**。

### 4.6 ★ 用户拍板的行为变更：`AddBuff` 目标来源（2026-09-24）

> **决策：`AddBuff()` 改为接受受击者 `CombatUnitController target`，作用于「本次命中的目标」，而不是字段 `m_SkillTarget`。**
> 用户原话：「AddBuff()这个方法中的参数应该要传递，受击者 CombatUnitController」。

#### 成因（评审实测发现）

`ExecuteEffect` 里两类 case 的目标来源**不一致**：

```csharp
// A 类：无参方法，内部用字段 m_SkillTarget
case SkillEffectTypes.AddBuff: AddBuff();      // → Skill.cs:588  m_SkillTarget.OnAddBuff(...)
case SkillEffectTypes.Sneer:  Sneer();         // → Skill.cs:599  m_SkillTarget.OnSneer(...)

// B 类：吃参数，用 ExecuteEffect 传入的 target
case SkillEffectTypes.ImmedDamage: ImmedDamage(target);   // → target.OnDamaged(...)
case SkillEffectTypes.Hure:        OnHure(target);        // → target.HitReaction.ApplyHurt(...)
```

而 `SkillHitSharpTypes`（`0=单体 / 1=圆形 / 2=扇形`）中 —— **回归基准技能 10003 = 2（扇形）**，
全表绝大多数技能是 1/2 ⇒ 走 `HitTargetsInCircle / HitTargetsInFan` 遍历命中列表、逐个 `HitTarget(unit)`。
于是同一技能同时命中 N 个目标时：

| 效果 | 改造前 | 改造后（用户拍板） |
|---|---|---|
| 伤害(0) / 受击表现(22) | 每个命中目标各自生效 | **不变**（等价） |
| **AddBuff(2)** | **只作用于 `m_SkillTarget` 一人** | **作用于每个命中目标** |
| Sneer(3) | 只作用于 `m_SkillTarget` 一人 | **保持现状**（同源问题，尚未拍板，见下） |

#### 顺带修掉的隐含 bug

改造前若主目标不在本次命中列表内（例如扇形边缘没扫到他），**Buff 仍会加到他身上**。
改造后 Buff 只作用于真正被命中的目标，语义自洽。

#### ⚠ 验收影响

这**不是等价迁移**，「行为零变化」原则在此处**不适用**。因此：

1. 本项从阶段一的「纯重构」中**剥离**，作为**独立的行为变更**单独回归（回归方法见 6.1.5 补充项）；
2. 其余 3 个效果（0 / 3 / 22）的迁移仍然严格等价，回归基准不变；
3. 阶段一因此不再是「行为零变化」的纯重构 —— 6.0 全局约定中的该条需按本节加限定语。

#### 待确认：`Sneer` 是否同样处理

`Sneer()` 是**完全同源**的问题（同样无参、同样用 `m_SkillTarget`）。本次用户只对 `AddBuff` 拍板，
故 `Sneer` **暂按保持现状**（等价迁移，`SneerEffect` 内改用 `ctx.PrimaryTarget`，见 6.1.2 ⑤）。
若一并修改，需同样登记为行为变更并单独回归 —— **等用户明确后再动**。

---

## 5. 可行性评估"""))

# ── 4. 6.0 全局约定：给「行为零变化」加限定语
pairs.append((
"""- **行为零变化**：阶段一/二均为纯重构；既有日志前缀（`[SegEffect]` / `[HitEffectCalc]` / `[Combat]` / `[HitReact]`）一个都不许改 —— 它们是回归比对基准。""",
"""- **行为零变化（★ 有唯一例外）**：除 **`AddBuff` 目标来源变更**（用户 2026-09-24 拍板，见 4.6）外，
  阶段一/二均为纯重构；既有日志前缀（`[SegEffect]` / `[HitEffectCalc]` / `[Combat]` / `[HitReact]`）一个都不许改 —— 它们是回归比对基准。"""))

# ── 5. 6.1.2 ⑤ AddBuffEffect 草案：加 ★ 说明；SneerEffect 改用 PrimaryTarget
pairs.append((
"""/// <summary>效果 2：添加Buff —— 迁自 Skill.AddBuff，逐句等价</summary>
public class AddBuffEffect : ISkillEffect
{
    public int EffectId { get { return (int)SkillEffectTypes.AddBuff; } }

    public void Execute(SkillEffectContext ctx)
    {
        if (ctx.Target == null) return;
        for (int i = 0; i < ctx.BaseData.BuffID.Count; i++)
        {
            ctx.Target.OnAddBuff(ctx.Caster, ctx.Skill, ctx.BaseData.BuffID[i]);
        }
    }
}

/// <summary>效果 3：嘲讽 —— 迁自 Skill.Sneer，逐句等价</summary>
public class SneerEffect : ISkillEffect
{
    public int EffectId { get { return (int)SkillEffectTypes.Sneer; } }

    public void Execute(SkillEffectContext ctx)
    {
        if (ctx.Target != null)
        {
            ctx.Target.OnSneer(ctx.Caster);
        }
    }
}""",
"""/// <summary>效果 2：添加Buff —— 迁自 Skill.AddBuff
/// ★★ 用户 2026-09-24 拍板：作用于 ctx.Target（本次命中的目标），**不是** m_SkillTarget。
///    这是本方案唯一的行为变更（原状：无论命中几个，Buff 只加给主目标一人）。见 4.6。</summary>
public class AddBuffEffect : ISkillEffect
{
    public int EffectId { get { return (int)SkillEffectTypes.AddBuff; } }

    public void Execute(SkillEffectContext ctx)
    {
        if (ctx.Target == null) return;
        for (int i = 0; i < ctx.BaseData.BuffID.Count; i++)
        {
            ctx.Target.OnAddBuff(ctx.Caster, ctx.Skill, ctx.BaseData.BuffID[i]);
        }
    }
}

/// <summary>效果 3：嘲讽 —— 迁自 Skill.Sneer
/// ⚠ 与 AddBuff 同源（原状同样只作用于 m_SkillTarget），但未获拍板 ⇒ **保持等价**，
///   故这里用 ctx.PrimaryTarget（= m_SkillTarget）而不是 ctx.Target。
///   若将来用户决定一并修改，改回 ctx.Target 并把本项登记为行为变更即可。</summary>
public class SneerEffect : ISkillEffect
{
    public int EffectId { get { return (int)SkillEffectTypes.Sneer; } }

    public void Execute(SkillEffectContext ctx)
    {
        if (ctx.PrimaryTarget != null)
        {
            ctx.PrimaryTarget.OnSneer(ctx.Caster);
        }
    }
}"""))

# ── 6. SkillEffectContext 草案：补 PrimaryTarget 字段
pairs.append((
"""    /// <summary>当前段号（1-based；0 = 非分段入口，按第 1 段处理，与 GetEffectTypesForSegment 语义一致）</summary>
    public int SegIdx;
}""",
"""    /// <summary>当前段号（1-based；0 = 非分段入口，按第 1 段处理，与 GetEffectTypesForSegment 语义一致）</summary>
    public int SegIdx;
    /// <summary>
    /// 主目标（= 迁移前 Skill 的 m_SkillTarget，即 SelectTartgetBySkill 选中的那一个）。
    /// ★ 存在的意义：AddBuff / Sneer 改造前用的是它，而 ImmedDamage / OnHure 用的是本次命中目标。
    ///   已获拍板改用 Target 的效果（AddBuff）不用它；未获拍板、需保持等价的效果（Sneer）用它。
    /// </summary>
    public CombatUnitController PrimaryTarget;
}"""))

# ── 7. 6.1.3 新增改造点 ②·补
pairs.append((
"""**② `ExecuteEffect` 方法体替换**：""",
"""**②·补 `AddBuff` 签名改造（★ 用户拍板的行为变更，非等价迁移）**：

`public void AddBuff()` → `public void AddBuff(CombatUnitController target)`，方法体内的 `m_SkillTarget` 全部换成 `target`；
`ExecuteEffect` 中对应 case 改为 `AddBuff(target);`。

```csharp
public void AddBuff(CombatUnitController target)     // ★ 新增参数
{
    if (target == null)                              // ★ 判空对象由 m_SkillTarget 改为 target
    {
        return;
    }
    for (int i = 0; i < BaseData.BuffID.Count; i++)
    {
        target.OnAddBuff(m_SkillCaseter, this, BaseData.BuffID[i]);
    }
}
```

> ⚠ 这一步**必须在 switch 删除之前或同时完成**，且**不能**与「switch → 注册表」混在同一次回归里验证：
> 前者改行为，后者不改。分两步走才能区分「重构引入的漂移」与「拍板带来的变更」。
>
> `Sneer()` 是同源问题，但**尚未拍板**，本步**不动它**（`SneerEffect` 走 `ctx.PrimaryTarget` 保持等价）。

**② `ExecuteEffect` 方法体替换**："""))

# ── 8. 6.1.5 回归验证：补充行为变更的回归方法
pairs.append((
"""补充抽查：从 `SkillData.csv` 检索配置了效果 2（AddBuff）或 3（Sneer）的技能行，实测 1~2 个（如无则记录"无抽查对象"即可）。""",
"""> **★ 行为变更专项回归（AddBuff，见 4.6）** —— 上面 1~4 是「等价性」验证，这 5~6 项单独验「变更是否如预期」：
>
> 5. 构造**扇形/圆形技能同时命中 ≥2 个目标**的场景（10003 Shape=2；也可用 20003/20008 等 Shape=2 技能，
>    或临时把某技能 AttackRadius 调大以确保多命中）；
> 6. 改造前留底：确认 Buff **只挂在 `m_SkillTarget` 一人身上**（另几个目标无 Buff）；
>    改造后：确认**每个被命中目标都挂上 Buff**，且主目标若未被命中则**不再**挂 Buff。
>
> 判定：改造前后在「单目标命中」场景下结果必须**完全一致**；仅在「多目标命中」场景下出现上述差异 —— 差异只应出现在这一处。
> 若其他任何环节出现差异 ⇒ 重构引入漂移，立即回退排查。

补充抽查：从 `SkillData.csv` 检索配置了效果 2（AddBuff）或 3（Sneer）的技能行，实测 1~2 个（如无则记录"无抽查对象"即可）。"""))

# ── 9. 风险表新增两行
pairs.append((
"""| 9 | 效果类越界访问 | 6.4 填桩纪律：只经 Facade 回调；无状态 |""",
"""| 9 | 效果类越界访问 | 6.4 填桩纪律：只经 Facade 回调；无状态 |
| 10 | ★ **AddBuff 目标来源变更**（用户拍板，非等价迁移） | 必须与「switch→注册表」重构**分步实施、分步回归**；按 6.1.5 第 5~6 项构造多目标场景验证；单目标场景结果必须与改造前逐条一致 |
| 11 | 同类问题漏改：`Sneer` 与 `AddBuff` 同源 | 已识别并在 4.6 登记为「待确认」；未获拍板前 `SneerEffect` 走 `ctx.PrimaryTarget` 保持等价，禁止顺手一起改 |"""))

# ── 10. 总览图标注
pairs.append((
"""  ImmedDamageEffect       HureEffect                AddBuffEffect / SneerEffect / …将来19桩""",
"""  ImmedDamageEffect       HureEffect                AddBuffEffect★ / SneerEffect / …将来19桩
                                                     ★=唯一行为变更：作用目标 m_SkillTarget → 本次命中目标（用户拍板，见4.6）"""))

# ── 执行
for old, new in pairs:
    n = text.count(old)
    assert n == 1, "命中 %d 次（应为 1）: %r" % (n, old[:70])
    text = text.replace(old, new)

io.open(P, "w", encoding="utf-8", newline="").write(text)

# 写回复验
back = io.open(P, encoding="utf-8", newline="").read()
for old, new in pairs:
    assert new in back, "写回复验失败，新内容未落盘: %r" % new[:60]
assert "\r\n" not in back, "行尾被污染成 CRLF"
print("OK 《战斗框架抽象方案.md》 — 已固化 %d 处改动，字数 %d → %d" % (len(pairs), 20760, len(back)))
