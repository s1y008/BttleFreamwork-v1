using System.Collections;
using UnityEngine;
using BattleFramework.Core;

/// <summary>
/// 单位受击反应组件（UnitHitReaction）—— ★ v5 P13 从 CombatUnitController 抽离（2026-09-17）
///
/// 【它是什么】「一次受击从进门到出门」的**编排者**。血量怎么变、火花什么时候出、要不要死、
///   仇恨要不要改、要不要进控制态，这一串顺序以前写在 Controller 的 <c>OnDamaged</c> 里，
///   现在整体搬到这里。Controller 保留同名方法作为对外契约（Facade），实现体指向此处。
///
/// 【为什么不直接写成纯静态工具类】
///   受击反应需要「这个单位当前的样子」—— 死了没、能不能被攻击、目标是谁、Animatior 有没有被冻结。
///   把 owner 挂在字段里（构造后不再变）比每帧传参更省。
///
/// 【为什么不用 AddComponent 挂在单位身上】
///   本类**不需要**任何 MonoBehaviour 能力：不订阅 Unity 消息、不需要 Inspector 配置、不参与序列化。
///   作为普通类由 Controller 持有即可 —— 单位 GameObject 上少两个对美术/策划可见却不可配的空组件，
///   也不必担心「prefab 上残留一个 Missing Script」「组件顺序影响谁先 Awake」这类挂载副作用。
///
/// 【职责三分 —— 本类处在中间那一层】
///     UnitView        画面端：只回答「怎么让它看起来发生了」
///     UnitHitReaction 编排层：决定「受这一下要播什么、切什么状态」（本类）
///     Controller      数据与决策：血量真相源、索敌、FSM 桥接
///   向上可以调 View 出画面、可以调 owner 改数据 / 切状态；但**不允许**反过来让 View 或 FSM 依赖本类。
///
/// 【返回值 Why】ApplyHit 返回 HitOutcome 而不是 void：调用方（含将来的 20 个技能效果桩）
///   需要知道「这一下有没有真的打进去」「是不是打死了」来决定后续 —— 返回枚举比让人再去查 IsDead 省一次误会。
/// </summary>
public class UnitHitReaction
{
    /// <summary>一次受击的结局，供调用方决定后续行为</summary>
    public enum HitOutcome
    {
        /// <summary>没真打进去：攻击者为空 / 已死亡 / 不可被攻击 —— 数值、特效、状态全部未改动</summary>
        Ignored,
        /// <summary>正常结算：扣了血、播了特效、可能改了仇恨</summary>
        Normal,
        /// <summary>这一下致死：已请求 Dead 状态（注意此时本单位的 IsDead 由 DeadState 链路最终决定）</summary>
        Dead,
    }

    private readonly CombatUnitController m_Owner;
    private readonly UnitView m_View;

    /// <summary>
    /// 绑定宿主单位与视图（由 <c>CombatUnitController.Start</c> 构造时一次性传入）。
    ///
    /// ⚠ 这里是**构造注入**，取代原先的 <c>Setup(owner, view)</c> + AddComponent 两步式：
    ///   旧写法下 <c>m_Owner</c> / <c>m_View</c> 在 Setup 被调用前恒为 null，而「有没有调用」没有任何编译期保证，
    ///   一旦漏掉，报错会出现在 ApplyHit 这种离现场很远的调用点。现在两个字段都是 <c>readonly</c>，
    ///   只要有实例就一定绑好了宿主 —— 与方法体内那一串 <c>m_View != null</c> 判空一起被彻底删掉。
    /// </summary>
    public UnitHitReaction(CombatUnitController owner, UnitView view)
    {
        m_Owner = owner;
        m_View = view;
    }

    //═════════════════════════ 受击管线 ═════════════════════════

    /// <summary>
    /// ★ 受击的唯一落地点：扣血 → 播受击特效 → 死亡则切 Dead → 顺手记仇恨。
    ///
    /// 【执行顺序为什么不能乱】
    ///   ① 先扣血 —— 数值是后面所有判断的依据（先判死会让最后一下的伤害不再体现在日志里）；
    ///   ② 再播特效 —— 死了则直接 return，避免尸体上加一朵火花；
    ///   ③ 最后改仇恨 —— 死者不需要仇恨，所以写在之后自然被跳过。
    ///
    /// 【hitEffectId 的来源】由攻击方 Skill.ImmedDamage 按**当前段号**算好后传下来（v5 P10 契约），
    ///   本类与受击方都不再反查攻击者的状态 —— 这是「段号只在 Skill 内部流转」的最后一环。
    /// </summary>
    /// <param name="attacker">攻击者；为空则整次受击被忽略</param>
    /// <param name="damage">本次伤害值（正数，已由调用方算好暴击与穿透）</param>
    /// <param name="hitEffectId">受击特效 id；<b>0 = 本段没有受击特效</b>，静默跳过</param>
    public HitOutcome ApplyHit(CombatUnitController attacker, int damage, int hitEffectId)
    {
        if (attacker == null || m_Owner.IsDead || m_Owner.CanbeAttack > 0)
        {
            return HitOutcome.Ignored;
        }

        // ① 数值落地：Hp 的 setter 直接写 CombatUnit.HP，取 Max(0, ...) 保证不会掉成负数
        m_Owner.Hp = Mathf.Max(0, m_Owner.Hp - damage);

        // ★ P0 优化:仅 Editor 打印,Release 构建零开销 + 消除字符串插值 GC
#if UNITY_EDITOR
        Debug.Log($"[Combat] {m_Owner.name} 受击 dmg={damage} HP={m_Owner.Hp}");
#endif

        // ② 致死：死亡优先于表现，直接请求终态并收尾
        if (m_Owner.Hp <= 0)
        {
            m_Owner.RequestState(CombatState.Dead);
            return HitOutcome.Dead;
        }

        // ③ 表现：id > 0 才播，0 表示该段没有配置受击特效（不是错误）
        if (hitEffectId > 0)
        {
            m_View.PlayEffect(hitEffectId);
        }

        // ④ 仇恨：还活着且当前没有目标时，把攻击者记为反击对象
        if (m_Owner.m_Target == null)
        {
            m_Owner.m_Target = attacker;
        }

        return HitOutcome.Normal;
    }

    //═════════════════════════ 受击表现（★ v5 P15 2026-09-18）════════════════════════

    /// <summary>
    /// 顿帧令牌 —— 连击时作废旧协程的「恢复速度」动作。
    /// 每次受击 +1，协程醒来时只有令牌仍是最新值才允许把速度恢复成 1；
    /// 否则第 1 段的协程会在第 2 段还处于顿帧中时提前解冻，打击感被吃掉一半。
    /// </summary>
    private int m_HitStopToken;

    /// <summary>
    /// ★ 受击**表现**的落点：顿帧 → 播受击动作。
    ///
    /// 【它与 ApplyHit 的分工】
    ///   ApplyHit 管「数值与结局」（扣血 / 致死 / 特效 / 仇恨）；
    ///   本方法管「看起来发生了什么」。两者由 Skill 侧按 CSV 列 13 分别触发：
    ///   效果 0(ImmedDamage) 走 ApplyHit，效果 22(Hure) 走本方法。
    ///   ⇒ 没配 Hure 的段依然会掉血、出火花，只是**没有受击动作** —— 这正是 10003 第 1 段的现状。
    ///
    /// 【为什么不在这里打断攻击（2026-09-18 用户拍板撤销）】
    ///   实现过一次：受击时若 CurrentState == Attack 就 RequestState(Idle)。改完弃用的理由是
    ///   ——**它天然对称**。本方法同时服务玩家与敌人，一旦接上，敌人技能配了效果 22 时
    ///   玩家的攻击也会被同样打断（CSV 里有 37 个技能配了 22）。
    ///   「挨一刀就被打断」是强玩法约束，属于**数值 / 规则层**的决策，
    ///   不能在受击表现这条通用管线上偷偷生效；将来要做应在受击方侧加
    ///   「是否允许被受击打断」的显式配置（含霸体 / 韧性），由 FSM 或 Buff 判定后再切 Idle。
    ///
    /// 【顿帧为什么只冻受击方】
    ///   攻击方的段号推进读的是自己 Animator 的 normalizedTime，
    ///   把它一起冻住，10003 的三段推进会直接卡死在半路。
    /// </summary>
    /// <param name="attacker">攻击者；为空则忽略</param>
    /// <param name="hitType">受击类型（BattleConst.HIT_TYPE_*）；≤ 0 视为「这一段没有受击动作」</param>
    public void ApplyHurt(CombatUnitController attacker)
    {
        if (attacker == null || m_Owner.IsDead )
        {
            return;
        }

#if UNITY_EDITOR
        // ★ 与 Skill 侧的 [SegEffect] 成对使用：一条「第 N 段效果含 22」对应一条「受击反应」。
        Debug.Log($"[HitReact] {m_Owner.name} 受击反应 来自={attacker.name} ");
#endif

        // ① 顿帧：只冻本单位（受击方）
        //m_Owner.StartCoroutine(HitStopRoutine());

        // ② 受击动作：经 View 走标准参数驱动
        m_View.PlayHit();
    }

    /// <summary>
    /// 顿帧协程：冻结 → 等待 → （若仍是最新令牌）恢复。
    /// ⚠ 协程挂在 Owner 这个 MonoBehaviour 上：单位销毁时协程自动终止，不会访问已销毁对象。
    /// </summary>
    private IEnumerator HitStopRoutine()
    {
        int token = ++m_HitStopToken;

        m_View.SetAnimatorSpeed(0f);
        yield return new WaitForSeconds(BattleConst.HIT_STOP_SECONDS);

        // 令牌已变说明又挨了一下 —— 让最新那个协程负责恢复，本协程直接退出
        if (token == m_HitStopToken)
        {
            m_View.SetAnimatorSpeed(1f);
        }
    }

    /// <summary>
    /// 治疗结算（与受击对称的另一条管线）。
    /// 原型为 Controller.OnHeal（当前全工程零调用点，Skill.ImmedHeal 仍是空桩），
    /// 迁移时保持原逻辑：不超过上限加血 + 播治疗特效，但不做「死亡 reverse」之类的额外行为。
    /// </summary>
    public void ApplyHeal(CombatUnitController attacker, int healAmount, int effectId)
    {
        if (attacker == null || m_Owner.IsDead)
        {
            return;
        }

        m_Owner.Hp = Mathf.Min(m_Owner.MaxHp, healAmount + m_Owner.Hp);
        m_View.PlayEffect(effectId);
    }

    //═════════════════════════ 受击引发的控制态 ═════════════════════════

    /// <summary>
    /// ★ 统一入口：受击被击退 / 浮空 / 击飞 → Knockback。
    ///
    /// 【为什么三个旧入口（OnHitOut / OnAirHit / OnPlayerAirHit）收敛成一个】
    ///   迁移前这三个方法做的事**完全相同**（写时长 → RequestState(Knockback)），
    ///   差别只在防御判断：OnHitOut 多一层 animator 判空、OnPlayerAirHit 多一层 time<=0、
    ///   OnAirHit 一个都没有 —— 同一个结果三条不一样的前置条件，是典型的复制粘贴漂移。
    ///   全量扫描确认三者此刻**外部零调用**（Skill.ExecuteEffect 里对应的效果分支仍是空桩），
    ///   所以这是唯一低成本的统一窗口：现在收敛不留隐患，将来二十个效果桩只需对接这一个入口。
    ///
    /// 【保留的语义】
    ///   · animator 为空或被冻结（speed == 0，如剧情暂停）时不接受击退 —— 沿用 OnHitOut 的防御；
    ///   · 时长 ≤ 0 **直接忽略**不进 Knockback —— 取 OnPlayerAirHit 的语义。
    ///     ⚠ 这是三选一：OnAirHit 原本会把 0 写进去，靠 KnockbackState 的 DEFAULT_DURATION_SECONDS 兜底。
    ///       本类选择在上游拦掉，KnockbackState 的兜底降级为「字段被绕过本入口直接写入」时的最后防线。
    /// </summary>
    /// <param name="attacker">攻击者；为空则忽略</param>
    /// <param name="duration">硬直时长（秒）；≤ 0 忽略本次击退</param>
    public void ApplyKnockback(CombatUnitController attacker, float duration)
    {
        if (attacker == null || m_Owner.IsDead)
        {
            return;
        }
        if (duration <= 0f)
        {
            return;
        }
        // 动画缺失、或动画机被冻结时不接受位移类受击
        if (!m_View.HasAnimator || m_View.AnimatorSpeed == 0f)
        {
            return;
        }

        // ★ 顺序不可颠倒：先写时长再切状态 —— ChangeState 是**同步**调用，
        //   KnockbackState.OnEnter 会在这一次调用返回之前就读取 KnockbackDuration。
        m_Owner.SetKnockbackDuration(duration);
        m_Owner.RequestState(CombatState.Knockback);
    }

    /// <summary>
    /// 眩晕 —— 统一走 Buff 系统（免疫 / 叠加 / 驱散 / 时长都由 Buff 管理），由 Buff 事件驱动 FSM 切 Stun。
    /// 本方法只做「把这一下翻译成一个带时长的 Buff」。
    ///
    /// ★ 2026-09-24：施加对象不再硬编码 BuffType.Stunned，改由 CSV 的 **Tags 列**反查
    ///   （取声明了 HardControl 标签、Id 最小的那一行）。原写法的两个坑：
    ///   ① AddBuff(BuffType) 只拿 GetData(type) 的首个登记者 ⇒ 同位的第二个 Buff 永远摸不到；
    ///   ② 新 Buff 哪怕在 CSV 写了 HardControl 标签，走这条路径也进不来 ⇒ Tags 数据驱动在此断裂。
    /// </summary>
    /// <param name="buffId">要挂的具体 Buff 配置行；0 = 按 HardControl 标签取代表行</param>
    public void ApplyStun(CombatUnitController attacker, float duration, int buffId = 0)
    {
        ApplyControlBuff(BuffTags.HardControl, buffId, attacker, duration);
    }

    /// <summary>沉默 —— 同 ApplyStun，按 Silence 标签取代表行，FSM 切 Silence 状态</summary>
    /// <param name="buffId">要挂的具体 Buff 配置行；0 = 按 Silence 标签取代表行</param>
    public void ApplySilence(CombatUnitController attacker, float duration, int buffId = 0)
    {
        ApplyControlBuff(BuffTags.Silence, buffId, attacker, duration);
    }

    /// <summary>
    /// 控制 Buff 落地：① 确定挂哪一行配置 → ② 挂到本单位 BuffSystem → ③ 用调用方时长覆盖配置默认时长。
    /// 时长由 Buff 独占持有，FSM 侧（StunState / SilenceState）只负责每帧查询，不各自倒计时。
    ///
    /// ★ 查询优先级：显式 buffId ＞ Tags 反查。**全程不碰 BuffType 枚举** ——
    ///   这是「新增一个控制 Buff 只需在 CSV 写一行」能真正闭环的最后一环。
    /// </summary>
    private void ApplyControlBuff(string tag, int buffId, CombatUnitController attacker, float duration)
    {
        if (attacker == null || m_Owner.IsDead || m_Owner.CurCombatUnit == null)
        {
            return;
        }

        BuffData buffData = buffId > 0
            ? ConfigManager.GetBuffData(buffId)
            : ConfigManager.GetBuffDataByTag(tag);

        if (buffData == null)
        {
            // fail-fast：资产漏配就该当场报错指名，不许变成「打了没反应」的隐性 bug
            Debug.LogError("[UnitHitReaction] 控制Buff施加失败 —— "
                + (buffId > 0
                    ? "找不到 BuffId=" + buffId + " 对应的配置行"
                    : "没有任何配置行声明了行为标签 '" + tag + "'")
                + "，请检查 BufferData.csv（owner=" + m_Owner.name + "）");
            return;
        }

        Buff buff = m_Owner.CurCombatUnit.BuffSystem.AddBuff(buffData.Id, attacker.GetInstanceID());
        if (buff != null && duration > 0f)
        {
            buff.RemainingTime = duration; // 用调用方时长覆盖配置默认时长
        }
    }
}
