using UnityEngine;

/// <summary>
/// ★ 投掷物实体（投掷物方案 P2/P3，2026-10-10）—— 池化复用，由 ProjectileManager 独占驱动（FlyTick / Despawn）。
///
/// 【职责边界】只回答「飞到哪了」：直线 / 追踪两种轨迹 + 命中 / 自爆判定。
///   「命中后发生什么」一律回流 <c>Skill.SkillHitAt</c>（写段号 + 写弹着原点 + 原样结算）——
///   效果列表、受击特效、伤害公式、IHitReceiver / UnitHitReaction 全部零改动。
///
/// 【判定规则】命中半径在 <b>XZ 平面</b>度量 —— 与出膛高度解耦：球贴手高飞、目标站地面，都不影响判定。
///   ★ 2026-10-13 评审 A2：命中判定由「逐帧点距离」升级为「本帧扫掠线段」——
///     点判定窗口只有 2×HitRadius 宽，帧步长 Speed×dt 超窗即隧穿（现配置一次 200ms 卡顿恰等于整窗）。
///   ★ 2026-10-13 评审 A1：无目标出膛（盲射）不做命中判定，朝出膛方向直飞至超程/超时自爆收场 ——
///     旧版无目标时伪瞄准点 = 出膛点前方 1 米（恰在飞行路径上），按点判定必贴脸「假命中」。
///【结算规则（★ 已拍板）】伤害按<b>命中时刻</b>取施法者属性（与即时技能语义一致，不做发射时刻快照）。
///   · AoE 弹（HitType=Scale*）：自爆点即结算原点，超程/超时自爆也结算 —— 打不中也不白放；
///   · 单体弹（HitType=Selected* / Self）：只有真正命中目标才结算；超程/超时 = 打空丢弃，不追伤。
///   · 打尸体：受击侧 ApplyHit 对 IsDead / CanbeAttack&gt;0 返回 Ignored（既有死亡保护），天然安全。
///
///【召回规则】目标死亡 / 施法者死亡 / 施法者被打断 —— 弹<b>不召回</b>，照常飞行结算（MMO 惯例）。
///   目标死亡后追最后记录位置到点，按上面的结算规则收场。
/// </summary>
public sealed class Projectile : MonoBehaviour
{
    private Skill m_Skill;
    private int m_SegIdx;
    private ProjectileData m_Cfg;
    private CombatUnitController m_Target;

    private Vector3 m_Pos;        // 本帧位置（每帧写回 transform）
    private Vector3 m_Dir;        // 当前飞行方向（水平单位向量）
    private Vector3 m_LastAimPos; // 目标最后记录的瞄准点（目标死亡/丢失后仍朝这里飞）
    private float m_Travelled;    // 已飞距离（米）—— MaxRange 判据
    private float m_Elapsed;      // 已飞时长（秒）—— LifeTime 判据
    private GameObject m_FxInstance;

    /// <summary>★ 2026-10-13 评审 A1：出膛时是否锁定瞄准目标（false = 盲射，飞行期间不做命中判定）</summary>
    private bool m_HasAim;

    /// <summary>所属技能实例 —— 起手闸门（ProjectileManager.HasUnresolvedProjectile）按引用比对</summary>
    public Skill Skill { get { return m_Skill; } }

    /// <summary>出膛（ProjectileManager.Spawn 独占调用）</summary>
    internal void Launch(Skill skill, int segIdx, ProjectileData cfg, Vector3 pos, Vector3 dir)
    {
        m_Skill = skill;
        m_SegIdx = segIdx;
        m_Cfg = cfg;
        m_Target = skill.SkillTarget;   // 出膛时锁定；飞行期间不会被改写（闸门保证，见 ProjectileManager 类注释）
        // ★ 2026-10-13 评审 A1：是否参与命中判定在出膛时一次性定型。旧版无目标路径把「伪瞄准点」设成
        //   pos+dir（出膛点前方 1 米）—— 恰好在飞行路径正中央，按点距离判定必在 (1-HitRadius) 米处
        //   「假命中」= 贴脸 0.2 米爆炸。无目标 ⇒ 盲射：不判命中，直飞到超程/超时自爆收场。
        m_HasAim = m_Target != null;
        m_Pos = pos;
        m_Dir = dir;
        m_Travelled = 0f;
        m_Elapsed = 0f;
        m_LastAimPos = m_HasAim
            ? m_Target.transform.position + Vector3.up * cfg.LaunchHeight
            : pos;

        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(dir);
        gameObject.SetActive(true);

        AttachFlightFx(cfg.FxId);
    }

    /// <summary>每帧推进；返回 true = 生命周期结束（调用方回收）。零 GC：无分配、日志 Editor-only</summary>
    internal bool FlyTick(float dt)
    {
        float step = m_Cfg.Speed * dt;
        m_Elapsed += dt;
        m_Travelled += step;

        // ── 追踪：每帧把方向朝「目标当前瞄准点」转一点（角速度受限）；直线弹保持出膛方向 ──
        //   目标已死/已销毁（Unity fake-null）时不再更新记录点 —— 朝最后记录位置继续飞
        if (m_Cfg.TrajectoryType == ProjectileTrajectoryType.Homing && m_Target != null)
        {
            m_LastAimPos = m_Target.transform.position + Vector3.up * m_Cfg.LaunchHeight;
            Vector3 toAim = m_LastAimPos - m_Pos;
            toAim.y = 0f;
            if (toAim.sqrMagnitude > 1e-6f)
            {
                // RotateTowards 一步完成「限角转向」（maxRadiansDelta），无需手写弧插值
                float maxRadians = m_Cfg.HomingTurnRate * Mathf.Deg2Rad * dt;
                m_Dir = Vector3.RotateTowards(m_Dir, toAim.normalized, maxRadians, 0f);
                m_Dir.y = 0f;
                m_Dir.Normalize();
            }
        }

        // ★ 2026-10-13 评审 A2：记下本帧移动前的位置 —— 命中判定从「点距离」升级为「扫掠线段」，
        //   问的是「本帧移动路径是否扫过瞄准点」，帧步长再大（高速弹 / 掉帧）也不会整窗跨过。
        Vector3 prev = m_Pos;
        m_Pos += m_Dir * step;
        transform.position = m_Pos;
        transform.rotation = Quaternion.LookRotation(m_Dir);

        // ── 命中判定（XZ 平面扫掠；盲射（m_HasAim=false）不判命中，见类注释）──
        if (m_HasAim && SweepHitXZ(prev, m_Pos, CurrentAimPos(), m_Cfg.HitRadius))
        {
            Resolve(true);
            return true;
        }

        // ── 超程 / 超时自爆（双保险）──
        if (m_Travelled >= m_Cfg.MaxRange || m_Elapsed >= m_Cfg.LifeTime)
        {
            Resolve(false);
            return true;
        }

        return false;
    }

    /// <summary>当前瞄准点：目标活着读实时位置（直线弹也追得上移动目标）；目标销毁（fake-null）后读最后记录点</summary>
    private Vector3 CurrentAimPos()
    {
        return (m_Target != null)
            ? m_Target.transform.position + Vector3.up * m_Cfg.LaunchHeight
            : m_LastAimPos;
    }

    /// <summary>
    /// ★ 2026-10-13 评审 A2：XZ 平面「线段到点」最近距离判定（纯标量运算，零分配零 GC）。
    ///   from→to = 本帧扫掠线段；返回线段上是否有点与 aim 的 XZ 距离 ≤ radius。
    ///   线段长近 0（帧步长极小）时退化为点距离判定，与旧行为一致。
    /// </summary>
    private static bool SweepHitXZ(Vector3 from, Vector3 to, Vector3 aim, float radius)
    {
        float segX = to.x - from.x;
        float segZ = to.z - from.z;
        float toAimX = aim.x - from.x;
        float toAimZ = aim.z - from.z;
        float lenSqr = segX * segX + segZ * segZ;
        float t;
        if (lenSqr < 1e-8f)
        {
            t = 0f;
        }
        else
        {
            t = (toAimX * segX + toAimZ * segZ) / lenSqr;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
        }
        float dx = from.x + segX * t - aim.x;
        float dz = from.z + segZ * t - aim.z;
        return dx * dx + dz * dz <= radius * radius;
    }

    /// <summary>命中 / 自爆结算（本类唯一的「进战斗管线」出口）</summary>
    private void Resolve(bool hitTarget)
    {
#if UNITY_EDITOR
        Debug.Log("[Projectile] " + (hitTarget ? "命中" : "自爆") + " 技能" + m_Skill.BaseData.ID
            + " 段" + m_SegIdx + " pos=" + m_Pos);
#endif
        // 弹着点环境特效（不绑点；目标身上的受击火花由技能 HitEffect 列走既有管线，与此正交）
        if (m_Cfg.ImpactFxId > 0)
        {
            EffectManager.Instance.CreateEffectAt(m_Cfg.ImpactFxId, m_Pos, transform.rotation);
        }

        // AoE 弹：自爆点即结算原点（打空也在落点结算）；单体弹：只有真命中才结算（打空 = 白打）
        bool aoe = m_Skill.BaseData.HitType == SkillHitTypes.ScaleEnemy
                || m_Skill.BaseData.HitType == SkillHitTypes.ScaleFriend;
        if (aoe || hitTarget)
        {
            m_Skill.SkillHitAt(m_SegIdx, m_Pos, m_Dir);   // 写段号 + 弹着原点 + 原样走既有结算
        }
    }

    /// <summary>回收（ProjectileManager 独占调用）：清引用 + 落特效 + 禁用回池</summary>
    internal void Despawn()
    {
        DetachFlightFx();
        m_Skill = null;
        m_Cfg = null;
        m_Target = null;
        gameObject.SetActive(false);
    }

    // ══════════════ 飞行表现 ══════════════

    /// <summary>
    /// 飞行体特效：isSave:false 实例化后挂在弹体上跟随。
    /// ⚠ 绝不能走 isSave:true 单实例池 —— EffectManager 池按 ResName 只留一个实例，
    ///   飞行体占用后会被下一次施法特效抢走，表现为「弹还没落地特效突然瞬移回手上」（P20 事故形态）。
    /// </summary>
    private void AttachFlightFx(int fxId)
    {
        if (fxId <= 0)
        {
            return;
        }
        EffectData fx = ConfigManager.GetEffectData(fxId);
        if (fx == null)
        {
            return;   // 悬空 id 已由 ValidateProjectileBindings 在加载期 LogError 指名，运行时不重复报
        }

        GameObject instance = EffectManager.Instance.CreateEffect(fx.ResName, false);
        if (instance == null)
        {
            return;   // CreateEffect 内部已对 prefab 缺失 LogError
        }
        instance.transform.SetParent(transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        m_FxInstance = instance;
    }

    /// <summary>落特效：每发一实例一销毁 —— 与既有受击特效路径同款成本档位（后续可升级为特效配对池）</summary>
    private void DetachFlightFx()
    {
        if (m_FxInstance == null)
        {
            return;
        }
        Destroy(m_FxInstance);
        m_FxInstance = null;
    }
}
