using UnityEngine;

/// <summary>
/// 技能「施法上下文」—— 一次份额结算期间**恒定不变**的那一半数据。
///
/// ★ 2026-09-27 方案 F（用户拍板）：把原 <c>SkillEffectContext</c> 按「变化频率」拆成两层 ——
///   ① 本结构（段级）：攻击者 / 技能 / 配置 / 段号 ⇒ **每段只构造一次**；
///   ② 本次命中的目标：变化频率最高（AoE 里每个目标一次），改由 <c>ISkillEffect.Execute</c>
///      的**第二个参数**传入，不再混进本结构。
///
/// ★ 2026-09-28 简化（用户提出）：构造只传 **<c>Skill</c> 实例本身**。
///   Caster / BaseData / SegIdx 原本就是把 Skill 内部已有的东西**抄一份**进来，
///   既啰嗦又埋雷 —— 四个参数由调用方自由组合，理论上能造出「Skill 是 A、SegIdx 是 B 的段号」
///   这种自相矛盾的上下文。现在真相源收敛到一个：Skill 自身。
///   <para>
///   · <c>Caster</c>   → 转发 <c>Skill.SkillCaseter</c>（public get，构造回合 SetSkillCaster 写入后不变）<br/>
///   · <c>BaseData</c> → 转发 <c>Skill.BaseData</c>（<c>m_SkillBaseData</c> 构造时一次性赋值，此后恒定）<br/>
///   · <c>SegIdx</c>   → **快照**，见下方 ⚠
///   </para>
/// </summary>
/// <remarks>
/// ⚠⚠ 为什么 <c>SegIdx</c> 保留字段做**快照**、而不写成 <c>Skill.SegIdx</c> 转发：
///   <c>SegIdx</c> 是 Skill 上的**可变**字段（<c>SkillHit(segIdx)</c> 每次进来都会覆盖它）。
///   本结构的生命周期 = <c>SkillHitInternal()</c> 的同步调用栈；只要结算期间发生**重入**
///   （例如某效果内部又触发一次命中、或未来 Buff Tick 反击），转发读到的就是**内层**改过的段号，
///   而外层剩下的效果会拿这个错段号去取效果列表 —— 只在特定技能组合下偶现，且**不报任何错**，
///   正是工程铁律 5 明令禁止的静默降级。
///   代价仅一个 int 字段（4 字节栈上，且 <c>in</c> 传参本来就不拷贝），换来一整类隐蔽 bug 的免疫。
///   <para>
///   ⚠ <c>Caster</c>/<c>BaseData</c> 转发是安全的：二者在结算期间不存在改写入口
///   （<c>SetSkillCaster</c> 只在起手时调，<c>m_SkillBaseData</c> 只在构造时赋值）。
///   </para>
///
/// ★ 仍是 <c>readonly struct</c>（2026-09-27 决策 1，未变）：
///   struct 在栈上构造，不进堆 ⇒ 战斗中**零 GC**（用户明确要求「绝对的 0 GC」）；
///   接口方法传 struct **不会装箱**（装箱只发生在 struct → object / 接口的转换），
///   配合 <c>in</c> 参数连拷贝都省掉。
///
/// ⚠⚠ 两条纪律（写在这里是因为违反后**不会报错，只会悄悄退回堆分配**）：
///   ① 效果类**不得**把 cast 存进字段、或被 lambda / 协程捕获 —— 一旦转 object 就装箱回堆；
///   ② 本结构只在「一次 <c>SkillHit(segIdx)</c> 结算期间」有效，不得跨帧持有。
///   构造点共 2 处：<c>Skill.SkillHitInternal()</c>（生产）+ <c>SkillEffectTargetProbe</c>（Editor 探针）。
/// </remarks>
public readonly struct SkillCastContext
{
    /// <summary>
    /// 技能实例 —— 本结构**唯一真正持有**的数据，其余成员均由它派生。
    /// <para>效果类经它访问 <c>GetHitEffectIdForSegment</c> / <c>GetSkillOneDamage</c> 等段相关方法。</para>
    /// </summary>
    public readonly Skill Skill;

    /// <summary>
    /// 当前段号（1-based）的**快照**，构造瞬间取自 <c>Skill.SegIdx</c>。
    /// （为何不转发：见类型 Remarks —— 防重入期间段号被内层改写。）
    /// </summary>
    public readonly int SegIdx;

    /// <summary>
    /// 构造一个段级上下文。**只需要技能实例本身**，其余数据从它派生。
    /// </summary>
    /// <param name="skill">正在结算的技能（不可为 null；传 null 会在本处直接 NRE —— 不做静默兜底，见铁律 5）</param>
    public SkillCastContext(Skill skill)
    {
        Skill = skill;
        SegIdx = skill.SegIdx;
    }

    /// <summary>攻击者 —— 转发 <c>Skill.SkillCaseter</c>。</summary>
    public CombatUnitController Caster { get { return Skill.SkillCaseter; } }

    /// <summary>技能配置行 —— 转发 <c>Skill.BaseData</c>。</summary>
    public SkillData BaseData { get { return Skill.BaseData; } }
}
