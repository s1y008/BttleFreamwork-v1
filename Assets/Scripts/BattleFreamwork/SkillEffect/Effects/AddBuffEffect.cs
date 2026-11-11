using UnityEngine;

/// <summary>
/// 效果 2：添加 Buff —— 迁自 <c>Skill.AddBuff(target)</c>。
///
/// ★★ 用户 2026-09-24 拍板：作用于 <c>Execute</c> 的 <c>target</c> 参数（本次命中的目标），**不是** m_SkillTarget。
///   这是本方案唯一的行为变更（原状：无论命中几个，Buff 只加给主目标一人），
///   已于 2026-09-24 提前独立落地（见方案 4.6.1），故此处是**等价迁移**，不再产生新的行为变更。
///
/// ⚠ 若 <c>BaseData.BuffID</c> 为空，本效果不产生任何作用 —— 属配置问题，不是代码问题。
///   2026-09-29 起：10002「天诛」已配 BuffID=[1003]（流血）；其余配了效果 2 的技能仍为空，
///   由 SkillDataManager.ValidateBuffBindings 在加载期 LogError 指名（不再静默失效）。
/// </summary>
public class AddBuffEffect : ISkillEffect
{
    public int EffectId { get { return (int)SkillEffectTypes.AddBuff; } }

    public void Execute(in SkillCastContext cast, IHitReceiver target)
    {
        if (target == null)
        {
            return;
        }

        // BuffID 为配置表 Id 列表：逐个经 OnAddBuff 查表并挂到目标的 Buff 系统
        // （OnAddBuff 内部：BuffSystem.AddBuff(buffId, attacker.GetInstanceID())）
        for (int i = 0; i < cast.BaseData.BuffID.Count; i++)
        {
            target.OnAddBuff(cast.Caster, cast.Skill, cast.BaseData.BuffID[i]);

#if UNITY_EDITOR
            // ★ 诊断：AddBuff 成功路径原本静默无痕（只有失败才 LogError），
            //   导致「配了效果 2 但没挂上」与「挂上了但看不见」无法区分。
            //   与 [HitEffectCalc] 同风格：仅 Editor 可见，真机零开销。
            Debug.Log(string.Format(
                "[AddBuff] 技能{0} 第{1}段 → 目标={2} buffId={3}",
                cast.BaseData == null ? 0 : cast.BaseData.ID,
                cast.SegIdx,
                target.name,
                cast.BaseData.BuffID[i]));
#endif
        }
    }
}
