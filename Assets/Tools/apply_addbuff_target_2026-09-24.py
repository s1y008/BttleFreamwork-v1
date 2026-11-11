# -*- coding: utf-8 -*-
"""
2026-09-24  Skill.AddBuff() 改为接受受击者参数（用户拍板）
原状：无参方法，内部用字段 m_SkillTarget ⇒ 扇形/圆形技能命中多个目标时，Buff 只加给主目标一人
改后：接受 CombatUnitController target ⇒ 作用于每个真正被命中的目标
副作用（正向）：方案阶段一实施时 AddBuffEffect 用 ctx.Target 即成为等价迁移，
                方案整体恢复为 100% 纯重构，不再含行为变更。
"""
import io

P = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Skill.cs"
t = io.open(P, encoding="utf-8", newline="").read()
crlf = "\r\n" in t
text = t.replace("\r\n", "\n")

pairs = []

# ① case 调用点：AddBuff(); → AddBuff(target);
pairs.append((
"""                case SkillEffectTypes.AddBuff:
                    AddBuff();
                    break;""",
"""                case SkillEffectTypes.AddBuff:
                    AddBuff(target);
                    break;"""))

# ② 方法定义 + 方法体：无参 → 接受 target；m_SkillTarget → target
pairs.append((
"""    public void AddBuff()
    {
        if (m_SkillTarget == null)
        {
            return;
        }
        //BuffID为配置表Id列表：逐个经OnAddBuff查CSV并挂到目标的Buff系统
        //（OnAddBuff内部：GetBuffData判空 → BuffSystem.AddBuff(type, attacker.GetInstanceID())）
        for (int i = 0; i < BaseData.BuffID.Count; i++)
        {
                m_SkillTarget.OnAddBuff(m_SkillCaseter, this, BaseData.BuffID[i]);
            
        }
    }""",
"""    /// <summary>
    /// 添加Buff（效果 2 / AddBuff）
    ///
    /// ★ 2026-09-24 用户拍板：改为接受受击者 <paramref name="target"/>，作用于「本次命中的目标」。
    ///   【改造前的问题】本方法是无参的，内部用字段 m_SkillTarget（SelectTartgetBySkill 选中的那一个）；
    ///   而同一 switch 里的 ImmedDamage(target) / OnHure(target) 用的是 ExecuteEffect 传入的 target。
    ///   由于 SkillHitSharpTypes 中绝大多数技能是圆形(1)/扇形(2)，走 HitTargetsInCircle/Fan 逐个
    ///   HitTarget(unit) ⇒ 一次命中 N 个目标时，伤害扣 N 个人、Buff 却只加给 m_SkillTarget 一人。
    ///   更糟的是：主目标若不在本次命中列表内（扇形边缘没扫到），Buff 仍会加到他身上。
    ///   【改造后】Buff 只作用于真正被命中的目标，与伤害的作用范围一致。
    /// ⚠ 这是行为变更（非等价重构），单目标场景下结果不变，仅多目标场景出现差异。
    /// </summary>
    /// <param name="target">本次命中的目标（来自 ExecuteEffect 的 target 参数）</param>
    public void AddBuff(CombatUnitController target)
    {
        if (target == null)
        {
            return;
        }
        //BuffID为配置表Id列表：逐个经OnAddBuff查CSV并挂到目标的Buff系统
        //（OnAddBuff内部：GetBuffData判空 → BuffSystem.AddBuff(buffid, attacker.GetInstanceID())）
        for (int i = 0; i < BaseData.BuffID.Count; i++)
        {
                target.OnAddBuff(m_SkillCaseter, this, BaseData.BuffID[i]);
            
        }
    }"""))

for old, new in pairs:
    n = text.count(old)
    assert n == 1, "命中 %d 次（应为 1）: %r" % (n, old[:60])
    text = text.replace(old, new)

if crlf:
    text = text.replace("\n", "\r\n")
io.open(P, "w", encoding="utf-8", newline="").write(text)

back = io.open(P, encoding="utf-8", newline="").read().replace("\r\n", "\n")
for old, new in pairs:
    assert new in back, "写回复验失败: %r" % new[:60]
assert "public void AddBuff(CombatUnitController target)" in back, "新签名未落盘"
assert "AddBuff(target);" in back, "新调用点未落盘"
assert back.count("m_SkillTarget.OnAddBuff") == 0, "旧的 m_SkillTarget.OnAddBuff 仍残留"
print("OK Skill.cs — AddBuff 已改为接受 target 参数（crlf=%s）" % crlf)
