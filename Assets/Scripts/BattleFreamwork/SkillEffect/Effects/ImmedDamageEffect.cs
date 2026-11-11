using UnityEngine;

/// <summary>
/// 效果 0：立即伤害 —— 迁自 <c>Skill.ImmedDamage(target)</c>，逐句等价。
///
/// ★ 受击特效 id 由 Skill 按当前段号算出后**直接传下去**（v5 P10）：
///   改造前这里传的是整个列表、受击方再反查攻击者的 m_CurSkill.SegIdx，既错位又跨对象耦合。
/// </summary>
public class ImmedDamageEffect : ISkillEffect
{
    public int EffectId { get { return (int)SkillEffectTypes.ImmedDamage; } }

    public void Execute(in SkillCastContext cast, IHitReceiver target)
    {
        int hitEffectId = cast.Skill.GetHitEffectIdForSegment(cast.SegIdx);

#if UNITY_EDITOR
        // ★ v5 P10 诊断：段号 → 受击特效 id。与下游 [HitEffect] 成对出现即可确认
        //   「按段取到了正确的 id」且「下游真的播了」—— 改造前这条链路两个环节都不可观测。
        Debug.Log($"[HitEffectCalc] 技能{cast.BaseData.ID} 第{cast.SegIdx}段 → 受击特效 id={hitEffectId} 目标={(target != null ? target.name : "null")}");
#endif

        // ★ 2026-09-24 用户拍板：补 null 判空（改造前这里不判，target 为 null 会 NRE）。
        //   判空刻意放在 [HitEffectCalc] 日志之后 —— 提到最前会丢失「命中了 null 目标」的可观测性。
        if (target == null)
        {
            return;
        }

        target.OnDamaged(cast.Caster, cast.Skill.GetSkillOneDamage(), hitEffectId);
    }
}
