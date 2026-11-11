using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★ 投掷物管理器（投掷物方案 P2/P3，2026-10-10）。
///
/// 【职责】投掷物「生成 → 逐帧驱动 → 回收」的全生命周期；命中结算回流 <c>Skill.SkillHitAt</c>，
///   效果列表 / 受击特效 / 伤害公式 / IHitReceiver / UnitHitReaction 全部零改动。
///
/// 【零 GC 纪律】与 SkillHitInternal 同款（SkillCastContext 注释里「绝对的 0 GC」的拍板）：
///   · 弹体 GameObject 全部池化（SetActive 复用），禁止逐发 Instantiate/Destroy 弹体本身；
///   · 迭代全部 for + 下标、倒序 + swap-remove，无 foreach 装箱、无 LINQ、无逐帧字符串（日志 Editor-only）；
///   · 本类无需自备 Overlap 缓冲：单体弹命中 = 纯距离判定（XZ 平面），
///     AoE 结算经 SkillHitAt → SkillHitInternal 复用 Skill 自己的 s_TargetBuffer —— 两类路径都不在本类做物理查询。
///
/// 【驱动】自建 "Projectiles" GO + 薄 MonoBehaviour 喂 dt，对齐 EffectManager 构造期自建宿主（DontDestroyOnLoad）的做法。
/// 【联网前瞻】运动只依赖 dt 积分、不读 Time.time 单调性、不读 Animator ——
///   将来接 ICastClock 固定步长模式时换 Tick 来源即可，弹道逻辑零改动（见 Net/CastClock.cs）。
/// </summary>
public static class ProjectileManager
{
    private const int INITIAL_CAPACITY = 16;

    private static readonly List<Projectile> s_Active = new List<Projectile>(INITIAL_CAPACITY);
    private static readonly Stack<Projectile> s_Free = new Stack<Projectile>();

    private static Transform s_Root;
    private static ProjectileManagerDriver s_Driver;

    /// <summary>Editor 探针口：当前在飞弹体数（诊断 / 回归脚本用）</summary>
    public static int ActiveCount { get { return s_Active.Count; } }

    /// <summary>
    /// 出膛（SkillHit 弹道分流的唯一调用方）。
    /// 出膛点 = 施法者脚底 + LaunchHeight；方向优先指向锁定目标（无目标沿施法者正前方）。
    /// ⚠ 结算身份（caster/target）由 Skill 实例自带 —— 起手闸门保证弹未落地时不会被再次 SetSkillCaster 改写。
    /// </summary>
    public static void Spawn(Skill skill, int segIdx)
    {
        ProjectileData cfg = ConfigManager.GetProjectileData(skill.BaseData.ProjectileId);
        if (cfg == null)
        {
            // 加载期 ValidateProjectileBindings 已拦过配置缺失 —— 运行时仍兜一道，指名不静默（铁律 5）
            Debug.LogError("[ProjectileManager] 技能 " + skill.BaseData.ID + " 配了弹道投递但 ProjectileData 查无 ProjectileId="
                + skill.BaseData.ProjectileId + " —— 本段结算被丢弃");
            return;
        }

        CombatUnitController caster = skill.SkillCaseter;
        if (caster == null)
        {
            // ★ 2026-10-13 评审 D：cfg 缺失已指名，施法者缺失同样不许静默（铁律 5）。
            //   正常链路 AttackState.OnEnter 恒写 Owner —— 走到这里只可能是调试直调漏写 caster、
            //   或未来新入口绕过了施法状态机；本段结算被丢弃必须留名。
            Debug.LogError("[ProjectileManager] 技能 " + skill.BaseData.ID + " 段" + segIdx
                + " 出膛时施法者为空（SkillCaseter == null）—— 本段结算被丢弃。"
                + "正常链路不会发生，请检查是否绕过 AttackState 直调且未 SetSkillCaster。");
            return;
        }

        EnsureDriver();

        Projectile p = (s_Free.Count > 0) ? s_Free.Pop() : CreateProjectile();
        s_Active.Add(p);

        Vector3 launchPos = caster.transform.position + Vector3.up * cfg.LaunchHeight;

        CombatUnitController target = skill.SkillTarget;
        Vector3 dir;
        if (target != null)
        {
            // 水平出膛：命中判定也在 XZ 平面（见 Projectile.FlyTick），高度只影响表现不参与判定
            dir = target.transform.position - caster.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
            {
                dir = caster.transform.forward;
            }
            dir.Normalize();
        }
        else
        {
            dir = caster.transform.forward;
        }

        p.Launch(skill, segIdx, cfg, launchPos, dir);

#if UNITY_EDITOR
        Debug.Log("[Projectile] 出膛 " + caster.name + " 技能" + skill.BaseData.ID + " 段" + segIdx
            + " → " + cfg.Name + " 轨迹=" + cfg.TrajectoryType + " 速度=" + cfg.Speed);
#endif
    }

    /// <summary>
    /// 起手闸门（投掷物方案 P3）：该技能（本单位持有的这个 Skill 实例）是否存在未结算弹道。
    /// 由 <c>CombatUnitController.TryCastSkill</c> 调用 —— 关闭 m_SkillTarget / m_SkillCaseter
    /// 被再次施放改写的跨帧竞态：Skill 实例按单位持有，引用相等即 (单位, 技能) 二元组。
    /// （SetSkillCaster 只发生在 AttackState.OnEnter；闸门挡住了同一实例的再次施放 ⇒ 飞行期间字段恒定。）
    /// </summary>
    public static bool HasUnresolvedProjectile(Skill skill)
    {
        if (skill == null || s_Active.Count == 0)
        {
            return false;
        }
        for (int i = 0; i < s_Active.Count; i++)
        {
            if (s_Active[i].Skill == skill)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>逐帧推进；true = 该弹生命周期结束（命中 / 自爆 / 打空），调用方负责回收</summary>
    public static void ManualUpdate(float deltaTime)
    {
        // 倒序遍历 + swap-remove：回收不影响后续下标（与 BuffSystemPooled 同款纪律）
        for (int i = s_Active.Count - 1; i >= 0; i--)
        {
            Projectile p = s_Active[i];
            if (p.FlyTick(deltaTime))
            {
                int last = s_Active.Count - 1;
                s_Active[i] = s_Active[last];
                s_Active.RemoveAt(last);
                p.Despawn();
                s_Free.Push(p);
            }
        }
    }

    private static Projectile CreateProjectile()
    {
        GameObject go = new GameObject("Projectile");
        go.transform.SetParent(s_Root, false);
        // 弹体创建即入池（禁用态），AddComponent 在 active GO 上会立刻走 Awake —— 本组件无 Awake 逻辑
        Projectile p = go.AddComponent<Projectile>();
        go.SetActive(false);
        return p;
    }

    private static void EnsureDriver()
    {
        if (s_Driver != null)
        {
            return;
        }
        GameObject go = new GameObject("Projectiles");
        Object.DontDestroyOnLoad(go);   // 对齐 EffectManager 自建宿主的做法
        s_Root = go.transform;
        s_Driver = go.AddComponent<ProjectileManagerDriver>();
    }
}

/// <summary>薄驱动器：只把主循环时间喂给 ProjectileManager（逻辑本体在静态类里，与 EffectManager 的分工同构）</summary>
internal sealed class ProjectileManagerDriver : MonoBehaviour
{
    private void Update()
    {
        ProjectileManager.ManualUpdate(Time.deltaTime);
    }
}
