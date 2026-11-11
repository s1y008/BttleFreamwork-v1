using UnityEngine;

/// <summary>
/// 效果 22：受击表现 —— 迁自 <c>Skill.OnHure(target)</c>（v5 P15 版，统一轻型受击）。
/// 逐句等价：目标或其 HitReaction 为空 → 整次受击被忽略。
/// </summary>
public class HureEffect : ISkillEffect
{
    public int EffectId { get { return (int)SkillEffectTypes.Hure; } }

    public void Execute(in SkillCastContext cast, IHitReceiver target)
    {
        // ★ 阶段二（2026-09-24）：不再直访 Target.HitReaction —— 那是实现方的内部编排组件，
        //   没法写进 IHitReceiver 契约。改走新增的 Facade OnHurt，语义等价（判空已下沉到实现内部）。
        if (target == null)
        {
            return;
        }

        target.OnHurt(cast.Caster);
    }
}
