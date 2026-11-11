# -*- coding: utf-8 -*-
"""
2026-09-24：把「顺手修」路线的最终决策同步进《战斗框架抽象方案.md》
  ① Sneer 与 AddBuff 同样处理（都走 ctx.Target），取消 PrimaryTarget 字段
  ② ImmedDamage 判空被用户接受（改善项）
  ③ 4.6.1 的验证情况从「无法实测」更新为「已用探针实测」，并新增 4.6.3 实测节

铁律：多处块替换一律走脚本 + 命中断言 + 写回复验。文档为纯 LF。
"""
import io, sys, os, shutil

P = r"E:\Project\BttleFreamwork-v1\战斗框架抽象方案.md"
BAK = P + ".bak.2026-09-24-sneer2"

src = io.open(P, encoding="utf-8", newline="").read()
orig = src

pairs = []

# ── ① 4.6 标题：范围由 AddBuff 扩到 AddBuff / Sneer
pairs.append((
"## 4.6 ★ 用户拍板的行为变更：`AddBuff` 目标来源（2026-09-24）—— **已独立落地，见 4.6.1**",
"## 4.6 ★ 用户拍板的行为变更：`AddBuff` / `Sneer` 目标来源（2026-09-24）—— **已独立落地，见 4.6.1 / 4.6.2**",
))

# ── ② 4.6.1 的验证情况：从「尚未实测」改为「已用探针实测」+ 两个配置层事实
pairs.append((
"""**验证情况**：

- 编译 0 Error；Play Error 恒 17（既有项），零新增；
- ⚠ **当前场景无法实测** —— 全表有 12 个技能配置了效果 2（`10010 / 10014 / 10015 / 10016 / 10018 / 10026 /
  10074 / 10080 / 10107 / 10108 / 10124 / 20048`），但**当前战斗场景只用 10003**（`WarrorController.AutoFightUpdate`
  硬编码 `GetSkill(10003)`），`10003` 的列 13 = `0|0;22|0;22` **不含效果 2** ⇒ 该路径未被触发，Play 无 Buff 日志。
- ⇒ 多目标场景下的行为差异**尚未实测**，需要用上述任一技能构造场景验证（或改 `WarrorController` 临时换技能）。""",
"""**验证情况**：

- 编译 0 Error；Play Error 恒 17（既有项），零新增；
- ✅ **已用探针实测**（不再只是"编译过了"，见 4.6.3）—— 原「当前场景无法实测」的缺口已补上：
  取场景里的真实单位 + 临时 `new Skill(10026)`，把 `m_SkillTarget` 设成 null / 别的单位做对照，
  证明 Buff 确实挂在**传入的 target** 上、而不是字段。
- ⚠ 顺带查出两条配置层事实（详见 4.6.3）：① 全表配了效果 2 的 12 个技能，其 **BuffID 列（0-based 17）
  全部为空** ⇒ `AddBuff` 的 for 循环跑 0 次，**即使被调用也是空转**（这才是 Play 里从无 Buff 日志的真因）；
  ② 当前场景只用 `10003`（`WarrorController` 硬编码），其列 13 = `0|0;22|0;22` 不含效果 2。""",
))

# ── ③ 「待确认 Sneer」整段 → 4.6.2（已拍板并落地）+ 4.6.3（实测节）
pairs.append((
"""#### 待确认：`Sneer` 是否同样处理

`Sneer()` 是**完全同源**的问题（同样无参、同样用 `m_SkillTarget`）。本次用户只对 `AddBuff` 拍板，
故 `Sneer` **暂按保持现状**（等价迁移，`SneerEffect` 内改用 `ctx.PrimaryTarget`，见 6.1.2 ⑤）。
若一并修改，需同样登记为行为变更并单独回归 —— **等用户明确后再动**。""",
"""### 4.6.2 ★ `Sneer` 同源改造已拍板并落地（2026-09-24）

**用户拍板：`Sneer` 与 `AddBuff` 同样处理 —— 走「顺手修」，均作用于 `ctx.Target`。**

| 项 | 决策 |
|---|---|
| `AddBuffEffect` / `SneerEffect` | 都用 **`ctx.Target`**，不再需要 `ctx.PrimaryTarget` 字段 |
| 性质 | 两处均登记为 **bug fix**（非等价迁移），回归清单单列（第 5~6 项） |
| `ImmedDamage` 判空 | 用户确认接受 —— 属改善项，保留 |

> 由此 **`SkillEffectContext` 取消 `PrimaryTarget` 字段**（该字段本就是为「保等价」而设）：
> 所有技能效果的目标来源**只有一个** = 本次命中的目标，语义不再分叉。
> 阶段一实施时两个效果类统一写 `ctx.Target`，且都是**等价迁移**（因为修复已提前落地）。

**实际改动**（`Skill.cs`，备份 `.bak.2026-09-24-sneer`）：

```csharp
// 前：无参，用字段
case SkillEffectTypes.Sneer: Sneer();
public void Sneer() { if (m_SkillTarget != null) m_SkillTarget.OnSneer(m_SkillCaseter); }

// 后：接受本次命中的目标
case SkillEffectTypes.Sneer: Sneer(target);
public void Sneer(CombatUnitController target) { if (target != null) target.OnSneer(m_SkillCaseter); }
```

同时给 `ImmedDamage` 补上 `target == null` 判空（改造前会 NRE）。判空**刻意放在 `[HitEffectCalc]` 诊断日志
之后** —— 若提到方法最前，「命中了 null 目标」这类异常就再也看不见了。

### 4.6.3 ★ 多目标行为差异已用探针实测（2026-09-24）

原 4.6.1 里「当前场景无法实测」的缺口，用一个 **Play 模式探针**补上了：
`Assets/Editor/BuffDiagnostics/SkillEffectTargetProbe.cs`（菜单 `Tools/Diag/SkillEffectTargetProbe`，幂等、不写资产）。

**构造思路**：不去搭真实的多目标场景，而是直接把 `m_SkillTarget` 设成「null」或「另一个单位」做对照 ——
旧实现（无参 + 取字段）在这两种情形下都不会作用于 victim，新实现必须让 victim 拿到结果，二者结论截然相反。

| 实验 | 构造 | 实测结果 |
|---|---|---|
| A | `m_SkillTarget = null`，`AddBuff(victim)` | victim Buff **0 → 1** ⇒ ★ 用的是传入的 target（旧实现此处必为 0） |
| B | `m_SkillTarget = caster(≠victim)`，`AddBuff(victim)` | victim 0→1 / caster **0→0** ⇒ ★ 只作用于 victim |
| C | `m_SkillTarget = null`，`Sneer(victim)` | `victim.m_Target = caster` ⇒ ★ 嘲讽作用于 victim |
| D | `ImmedDamage(null)` | 未抛异常 ⇒ ★ 判空生效（改造前此处 NRE） |

> 探针临时 `new Skill(10026)` 并**注入 BuffID**（CSV 该列为空，不注入则循环 0 次），结束前
> `ClearAllBuffs()` 复原，不影响场景。四项全绿 ⇒ 目标来源改造**代码层面已验证正确**。

⚠ **由此查出的配置层事实（需你决定是否补配，属 CSV 改动）**：

1. 全表 12 个配了效果 2 的技能，其 **BuffID 列全部为空** ⇒ `AddBuff` 目前是空转，
   代码改对了在游戏里也看不出效果；
2. **效果 3（Sneer）在整张技能表里零配置** ⇒ 该分支从未被任何技能触发过。

> 也就是说：本次两处修复**已证明代码正确**，但要让它们真正生效，还得给 CSV 补 BuffID / 配效果 3。
> 回归清单第 5~6 项的核心断言已被本探针提前覆盖；阶段一实施时仍应按清单在真实多目标场景走一遍。""",
))

# ── ④ SkillEffectContext：取消 PrimaryTarget 字段
pairs.append((
"""    /// <summary>
    /// 主目标（= 迁移前 Skill 的 m_SkillTarget，即 SelectTartgetBySkill 选中的那一个）。
    /// ★ 存在的意义：AddBuff / Sneer 改造前用的是它，而 ImmedDamage / OnHure 用的是本次命中目标。
    ///   已获拍板改用 Target 的效果（AddBuff）不用它；未获拍板、需保持等价的效果（Sneer）用它。
    /// </summary>
    public CombatUnitController PrimaryTarget;""",
"""    // ★ 2026-09-24：原计划的 PrimaryTarget（= m_SkillTarget）字段**已取消**。
    //   用户拍板 AddBuff / Sneer 均走「顺手修」，统一作用于 Target（见 4.6.2），
    //   不再需要「为保等价而存在的第二目标」—— 所有效果的目标来源只有一个，语义不再分叉。""",
))

# ── ⑤ SneerEffect 草案：PrimaryTarget → Target
pairs.append((
"""/// <summary>效果 3：嘲讽 —— 迁自 Skill.Sneer
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
}""",
"""/// <summary>效果 3：嘲讽 —— 迁自 Skill.Sneer
/// ★ 与 AddBuff 同源，同样已拍板走「顺手修」（见 4.6.2）：作用于 ctx.Target（本次命中的目标），
///   而非 m_SkillTarget。修复已在 4.6.1 / 4.6.2 提前落地 ⇒ 这里是**等价迁移**，不产生新的行为变更。</summary>
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
))

# ── ⑥ 步骤①的 Sneer 注释：尚未拍板 → 已一并落地
pairs.append((
"""> `Sneer()` 是同源问题，但**尚未拍板**，本步**不动它**（`SneerEffect` 走 `ctx.PrimaryTarget` 保持等价）。""",
"""> ✅ `Sneer()` 同源问题**已一并拍板并落地**（见 4.6.2）：改为 `Sneer(CombatUnitController target)`，
> 故本步 `SneerEffect` 也直接用 `ctx.Target`，与 `AddBuffEffect` 一致 —— 同样跳过，方案保持纯重构。""",
))

# ── ⑦ 遗留表第 10 行：补「已实测」
pairs.append((
"""| 10 | ★ **AddBuff 目标来源变更**（用户拍板，非等价迁移） | ✅ **已提前独立落地**（4.6.1），与阶段一解耦，阶段一因此恢复纯重构。遗留：多目标场景**尚未实测**，需用配了效果 2 的技能（如 10026 / 20048）构造场景补验 |""",
"""| 10 | ★ **AddBuff 目标来源变更**（用户拍板，非等价迁移） | ✅ **已提前独立落地**（4.6.1），与阶段一解耦，阶段一因此恢复纯重构；多目标行为差异**已用探针实测**（4.6.3，四项全绿） |""",
))

# ── ⑧ 遗留表第 11 行：待确认 → 已拍板落地
pairs.append((
"""| 11 | 同类问题漏改：`Sneer` 与 `AddBuff` 同源 | 已识别并在 4.6 登记为「待确认」；未获拍板前 `SneerEffect` 走 `ctx.PrimaryTarget` 保持等价，禁止顺手一起改 |""",
"""| 11 | 同类问题漏改：`Sneer` 与 `AddBuff` 同源 | ✅ **已拍板并一并落地**（4.6.2）：`Sneer(CombatUnitController target)` + 探针实测通过；`SkillEffectContext` 因此**取消 `PrimaryTarget` 字段** |""",
))

for i, (old, new) in enumerate(pairs, 1):
    cnt = src.count(old)
    if cnt != 1:
        print("[FAIL] 第 %d 块命中 %d 次（期望 1）" % (i, cnt))
        print("--- old 前 120 字 ---")
        print(repr(old[:120]))
        sys.exit(1)
    src = src.replace(old, new, 1)
    print("[OK] 第 %d 块已替换" % i)

io.open(P, "w", encoding="utf-8", newline="").write(src)

# 写回复验
chk = io.open(P, encoding="utf-8", newline="").read()
for i, (old, new) in enumerate(pairs, 1):
    if new not in chk:
        print("[FAIL] 复验失败：第 %d 块新内容不在磁盘中" % i)
        if os.path.exists(BAK):
            shutil.copyfile(BAK, P)
            print("[ROLLBACK] 已从备份还原")
        sys.exit(1)

print("[VERIFY] 全部 %d 块已落盘" % len(pairs))
print("残留 PrimaryTarget =", chk.count("PrimaryTarget"), "（应为 0）")
print("残留 尚未实测 =", chk.count("尚未实测"))
print("LF =", chk.count("\n"), "CRLF =", chk.count("\r\n"))
