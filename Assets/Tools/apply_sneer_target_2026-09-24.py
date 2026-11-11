# -*- coding: utf-8 -*-
"""
2026-09-24 用户拍板「顺手修」路线的收尾：
  ① Sneer() 无参 + m_SkillTarget → Sneer(CombatUnitController target)（与 AddBuff 同源）
  ② ImmedDamage 补 target null 判空（改造前会 NRE）
  ③ 清理上一轮 AddBuff 上方遗留的重复 <summary> 块

铁律：多处块替换一律走脚本 + 命中断言 + 写回复验；CRLF 保持。
"""
import io, sys, shutil, os

P = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Skill.cs"
BAK = P + ".bak.2026-09-24-sneer"

raw = io.open(P, encoding="utf-8", newline="").read()
src = raw.replace("\r\n", "\n")
orig = src

pairs = []

# ① switch 调用点：Sneer(); -> Sneer(target);
pairs.append((
    "                    Sneer(); ",
    "                    Sneer(target);",
))

# ② Sneer 方法体：无参 + m_SkillTarget -> 接受 target
pairs.append((
"""    /// <summary>
    /// 嘲讽
    /// </summary>
    public void Sneer()
    {
        if (m_SkillTarget != null)
        {
            m_SkillTarget.OnSneer(m_SkillCaseter);
        }
    }
""",
"""    /// <summary>
    /// 嘲讽（效果 3 / Sneer）
    ///
    /// ★ 2026-09-24 用户拍板：与 AddBuff 同源改造，改为接受受击者 <paramref name="target"/>。
    ///   【改造前的问题】本方法是无参的，内部用字段 m_SkillTarget（SelectTartgetBySkill 选中的那一个）；
    ///   而同一 switch 里的 ImmedDamage(target) / OnHure(target) 用的是 ExecuteEffect 传入的 target。
    ///   圆形(1)/扇形(2) 技能走 HitTargetsInCircle/Fan 逐个 HitTarget(unit) ⇒ 一次命中 N 个目标时，
    ///   伤害打 N 个人、嘲讽却只作用于 m_SkillTarget 一人（主目标没被扫到时甚至会嘲讽错人）。
    ///   【改造后】嘲讽只作用于真正被命中的目标，作用范围与伤害一致。
    /// ⚠ 这是行为变更（非等价重构）：单目标场景结果不变，仅多目标场景出现差异。
    /// </summary>
    /// <param name="target">本次命中的目标（来自 ExecuteEffect 的 target 参数）</param>
    public void Sneer(CombatUnitController target)
    {
        if (target != null)
        {
            target.OnSneer(m_SkillCaseter);
        }
    }
""",
))

# ③ ImmedDamage 补 null 判空（放在诊断日志之后、OnDamaged 之前）
pairs.append((
"""        target.OnDamaged(m_SkillCaseter, GetSkillOneDamage(), hitEffectId);""",
"""        // ★ 2026-09-24 用户拍板：补 null 判空。改造前这里不判，target 为 null 时直接 NRE。
        //   刻意放在 [HitEffectCalc] 诊断日志之后 —— 保留「命中了 null 目标」的可观测性，
        //   判空若提到方法最前，这类异常就再也看不见了。
        if (target == null)
        {
            return;
        }

        target.OnDamaged(m_SkillCaseter, GetSkillOneDamage(), hitEffectId);""",
))

# ④ 清理 AddBuff 上方遗留的重复 summary 块
pairs.append((
"""    /// <summary>
    /// 添加BUFF
    /// </summary>
    /// <summary>
    /// 添加Buff（效果 2 / AddBuff）""",
"""    /// <summary>
    /// 添加Buff（效果 2 / AddBuff）""",
))

for i, (old, new) in enumerate(pairs, 1):
    cnt = src.count(old)
    if cnt != 1:
        print("[FAIL] 第 %d 块命中 %d 次（期望 1）" % (i, cnt))
        print("--- old ---")
        print(repr(old[:200]))
        sys.exit(1)
    src = src.replace(old, new, 1)
    print("[OK] 第 %d 块已替换" % i)

out = src.replace("\n", "\r\n")
io.open(P, "w", encoding="utf-8", newline="").write(out)

# 写回复验
chk = io.open(P, encoding="utf-8", newline="").read().replace("\r\n", "\n")
for i, (old, new) in enumerate(pairs, 1):
    if new not in chk:
        print("[FAIL] 复验：第 %d 块新内容不在磁盘文件中" % i)
        if os.path.exists(BAK):
            shutil.copyfile(BAK, P)
            print("[ROLLBACK] 已从备份还原")
        sys.exit(1)
print("[VERIFY] 全部 %d 块已落盘" % len(pairs))
print("CRLF 行 =", chk.count("\n"), "| 文件含 \\r 行 =", io.open(P, encoding='utf-8', newline='').read().count("\r\n"))
print("残留 m_SkillTarget.OnSneer =", chk.count("m_SkillTarget.OnSneer"))
print("残留 Sneer(); =", chk.count("Sneer();"))
