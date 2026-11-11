using UnityEngine;

/// <summary>
/// 效果 3：嘲讽 —— 迁自 <c>Skill.Sneer(target)</c>。
///
/// ★ 与 AddBuff 同源，同样已拍板走「顺手修」（见方案 4.6.2）：作用于 <c>Execute</c> 的 <c>target</c> 参数（本次命中的目标），
///   而非 m_SkillTarget。修复已在 4.6.1 / 4.6.2 提前落地 ⇒ 这里是**等价迁移**，不产生新的行为变更。
/// </summary>
public class SneerEffect : ISkillEffect
{
    public int EffectId { get { return (int)SkillEffectTypes.Sneer; } }

    public void Execute(in SkillCastContext cast, IHitReceiver target)
    {
        if (target != null)
        {
            target.OnSneer(cast.Caster);
        }
    }
}
