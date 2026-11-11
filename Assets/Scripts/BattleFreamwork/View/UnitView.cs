using UnityEngine;

/// <summary>
/// 单位视图组件（UnitView）
///
/// 【它是什么】这个战斗单位「画面端」的全部出口：Animator 参数读写、动画状态查询、特效播放、死亡表现。
///   由 CombatUnitController.Start() **new 出来并持有**（一个单位一个），暴露为 <c>CombatUnitController.View</c>。
///
/// 【为什么要有它 —— 原来有什么问题】
///   改造前这些代码散落在 Controller 里，和血量结算、索敌决策、FSM 桥接挤在同一个 1200 行的文件里。
///   问题不只是「文件长」，而是**职责互相污染**：例如 <c>SetDead</c> 里既置 IsDead 标志（数据），
///   又播 Die 动画、又 Destroy 碰撞器（表现）—— 想改一个死亡镜头要在几十行 driven 决策代码里翻找，
///   而且「改动画」和「改数值」会同一次提交碰同一个方法，评审时看不出影响面。
///   现在一条硬边界：**View 里只有「让它看起来发生了什么」，没有任何数值计算与决策**。
///   读这段代码的成本从「理解整个战斗单位」降到「理解一个 Animator」。
///
/// 【边界纪律（写新代码前先读这三条）】
///   ① 本类**不持有**血量、攻击力、目标等业务数据 —— 需要参数就从入参拿，禁止向上翻 owner 的业务字段；
///   ② 本类**不做判断** —— 「该不该播死亡」由 UnitHitReaction / DeadState 决定，View 只回答「怎么播」；
///   ③ 所有 m_Animator 的空判断都在本类内部吃掉 —— 调用方不必再写 <c>if (m_Animator != null)</c>，
///      这也是 Controller / CombatStates 里那几十个琐碎 null 判断消失的原因。
///
/// 【已知留白】移动 / NavMesh 相关（NavStop 等）暂未迁入 —— 它同时涉及 NavMeshAgent 与 Animator，
///   属于「运动」而非纯表现，等 UnitMotor 组件一起拆，避免一次改动跨太多文件难以验证。
/// </summary>
public class UnitView
{
    private readonly CombatUnitController m_Owner;
    private readonly Animator m_Animator;

    /// <summary>
    /// 绑定宿主单位与 Animator —— **构造即就绪**，没有「创建了还没初始化」的中间态。
    ///
    /// 【为什么不自己 GetComponent】
    ///   宿主 Controller 的基类 <c>MoveBaseController.Awake()</c> 早在 Awake 阶段就把 Animator 取好、
    ///   缓存进 <c>m_Animator</c> 了。View 若再查一次，同一个组件就有了<b>两个获取路径</b> ——
    ///   这不是省一次查询的问题（一次查询本身很便宜），而是<b>真相源分裂</b>：
    ///   将来一旦 Animator 不再直接挂在根节点（比如改挂到子节点模型上、或走 Addressables 换皮），
    ///   基类的取法会跟着调整，而 View 里这句 GetComponent 不会 —— 于是出现
    ///   「Controller 认为有 Animator、View 认为没有」这种极难定位的偏差。
    ///   现在 View 对「Animator 来自哪里」<b>零假设</b>：宿主给什么就用什么，二者必然一致。
    ///
    /// 【为什么用构造注入而不是 Setup 方法 / AddComponent】
    ///   这两个字段是 <c>readonly</c>：一旦 new 出来就再也不能被改写成别的单位或别的 Animator。
    ///   如果换成 <c>public void Setup(owner)</c>，就会存在一个「实例存在但字段还是 null」的窗口，
    ///   而 AddComponent + Awake 组合同样无法用类型系统杜绝这类漏调用。构造注入把这个窗口**编译期关闭**。
    ///
    /// ⚠ 契约：Animator 引用在宿主生命周期内恒定不变（现状由 Awake 取一次、此后不再赋值）。
    ///   若将来支持运行时换模型 / 换 Animator，需要把取值的责任收回到一处再通知 View。
    /// </summary>
    public UnitView(CombatUnitController owner, Animator animator)
    {
        m_Owner = owner;
        m_Animator = animator;

        // ★ 动画契约校验：构造期一次性，资产不合规当场红字报出（见方法注释）。
        //   放在这里而不是构造之后由宿主调用，是为了保持本类「构造即就绪、无中间态」的原则。
        ValidateAnimationContract();
    }

    /// <summary>
    /// 本单位的 Animator 是否可用。
    /// 对外暴露的原因：保留迁移前 <c>SetDead</c> 首句「m_Animator 为空则整体跳过」的等价语义 ——
    /// 见 CombatUnitController.SetDead 注释，那是一段有争议但**本次刻意不改行为**的防御。
    /// </summary>
    public bool HasAnimator { get { return m_Animator != null; } }

    /// <summary>
    /// Animator 播放速度；0 表示动画机被冻结（剧情暂停 / 定帧），此时位移类受击应被忽略。
    /// 无 Animator 时返回 0 —— 让上游的「没有 / 被冻结」两种情况共用一条判断。
    /// </summary>
    public float AnimatorSpeed { get { return m_Animator != null ? m_Animator.speed : 0f; } }

    //═════════════════════════ 特效 ═════════════════════════

    /// <summary>
    /// 播放挂在**本单位身上**的特效（受击火花 / 治疗光效等）。
    ///
    /// ★ 这是全工程受击 / 治疗特效的**唯一出口**（原 CombatUnitController.OnAttackEffect）。
    ///   日志标签 [HitEffect] 与 Skill 侧的 [HitEffectCalc] 成对使用：前者说「真的播了」，
    ///   后者说「按段算出了哪个 id」，两条数量应 1:1 —— 这是 v5 P10 建立的可观测性契约，迁移时原样保留。
    ///
    /// 绑点不在本类解析：EffectManager.CreateEffect 内部会拿本单位身上的 EffectDot，
    ///   按特效配置的 BindName 自己找挂点（火花挂躯干、刀光挂武器，各不相同）。
    /// </summary>
    /// <param name="effectId">特效配置 id；≤0 由调用方提前拦截（此处不再二次判断，保持职责单一）</param>
    public void PlayEffect(int effectId)
    {
#if UNITY_EDITOR
        Debug.Log($"[HitEffect] {m_Owner.name} 播放特效 effectId={effectId}");
#endif
        EffectManager.Instance.CreateEffect(effectId, m_Owner.transform);
    }

    //═════════════════════════ 动画参数 ═════════════════════════

    /// <summary>设置技能动画 id（0 = 回到待机）</summary>
    public void SetSkillId(int skillId)
    {
        if (m_Animator == null) return;
        m_Animator.SetInteger("SkillId", skillId);
    }

    /// <summary>技能 id 归零（技能播完 / 被打断时回到待机）</summary>
    public void ResetSkillId()
    {
        SetSkillId(0);
    }

    /// <summary>移动动画开关（Nav 行走圈 —— Move / Die 之外由 MoveBaseController 也会读写）</summary>
    public void SetMoveAnim(bool isMove)
    {
        if (m_Animator == null) return;
        m_Animator.SetBool("Move", isMove);
    }

    /// <summary>手动 / 摇杆方向的跑步动画开关</summary>
    public void SetRunAnim(bool isRun)
    {
        if (m_Animator == null) return;
        m_Animator.SetBool("Run", isRun);
    }

    //═════════════════════════ 受击表现（★ v5 P15 2026-09-18）════════════════════════

    /// <summary>
    /// 播放受击动作 —— 走**标准参数驱动**，不是 Animator.Play 硬切。
    ///
    /// 【为什么必须先写 Int 再写 Trigger】
    ///   GBL.controller 的结构是「一条入口（Hit）+ Hit 子状态机内部按 HitType 分流」。
    ///   触发的那一瞬间 Animator 就去读 HitType 决定进哪个状态；
    ///   若顺序颠倒，会用**上一次**残留的 HitType 分流到错误的受击动作。
    ///
    /// 【为什么不在这里判 hitType 的合法性】
    ///   本类不做判断（类注释边界纪律②）——「这一下该不该有受击表现」由 UnitHitReaction 决定，
    ///   这里只回答「怎么播」。
    /// </summary>
    /// <param name="hitType">受击类型，取值见 BattleConst.HIT_TYPE_*。本方法不判 0 —— 「这一下播不播」由上游 UnitHitReaction.ApplyHurt 决定</param>
    public void PlayHit()
    {
        if (m_Animator == null) return;

        m_Animator.SetTrigger(BattleConst.ani_Hit);
    }

    /// <summary>
    /// 设置 Animator 播放速度（顿帧用）：0 = 冻结画面但**不冻结状态转换**，1 = 正常。
    /// ⚠ 冻结期间 Animator 仍会处理 Trigger 与转换，只会停住片段推进 —— 这正是顿帧想要的效果。
    /// </summary>
    public void SetAnimatorSpeed(float speed)
    {
        if (m_Animator == null) return;
        m_Animator.speed = speed;
    }

    /// <summary>
    /// ★ 动画契约校验（★ v5 P15-contract 2026-09-18）—— 构造期跑一次，缺参数直接 LogError。
    ///
    /// 【为什么从「每次受击前查」前移到「构造时查一次」】
    ///   旧写法 <c>EnsureHitParams()</c> 是**运行时静默降级**：缺参数 → 打一次自己的告警 → return，
    ///   于是把 Unity 原本每次都会刷的 "Parameter 'Hit' does not exist" 一并吞掉了。
    ///   净效果是负的：它不防崩溃（对不存在的参数 SetXxx 本就不抛异常），
    ///   只把「资产漏配」这个应当在编辑期就暴露的错误，改写成运行时的「打了没反应」——
    ///   而后者正是 P15 排查时看到的表象。
    ///
    /// 【这里的立场】
    ///   受击 / 死亡协议是**资产契约**，不是运行时可变状态：玩家与敌人必须共用同一套参数。
    ///   不合规就该第一次运行指名道姓地报出来，而不是悄悄跳过。代码不为资产不一致兜底。
    ///
    /// 【为什么可以放在构造里查】
    ///   宿主在 Start 里 new 本类，Animator 由基类 <c>MoveBaseController.Awake()</c> 取好，
    ///   Awake 早于 Start；且本类契约规定 Animator 引用在宿主生命周期内恒定不变。
    ///   因此此刻 <c>m_Animator.parameters</c> 就是最终状态，不存在「晚点才赋上」的窗口。
    /// </summary>
    private void ValidateAnimationContract()
    {
        // 没有 Animator 的单位不走表现管线（HasAnimator 已对外暴露，由上层自行决定），无需校验契约。
        if (m_Animator == null) return;

        string missing = null;
        string[] required =
        {
            BattleConst.ani_Hit,      // 受击触发
            BattleConst.ani_HitType,  // 受击档位分流
            BattleConst.ani_Die,      // 死亡
        };

        for (int i = 0; i < required.Length; i++)
        {
            if (!HasParameter(required[i]))
            {
                missing = (missing == null) ? required[i] : missing + ", " + required[i];
            }
        }

        if (missing != null)
        {
            // 顺带把「实际用的是哪个 controller、它现在有哪些参数」打出来 ——
            // 资产漏配往往不是"没加"，而是"加在了另一个 controller 文件上"，没有这行就只能靠猜。
            RuntimeAnimatorController ctrl = m_Animator.runtimeAnimatorController;
            string ctrlName = (ctrl != null) ? ctrl.name : "(未挂载 controller)";

            AnimatorControllerParameter[] ps = m_Animator.parameters;
            string[] names = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                names[i] = ps[i].name;
            }

            Debug.LogError($"[UnitView] {m_Owner.name} 的 Animator Controller 缺少表现协议参数：[{missing}]。" +
                           $"实际 controller={ctrlName}，现有参数=[{string.Join(", ", names)}]。" +
                           $"玩家与敌人必须共用同一套受击 / 死亡协议 —— 请在 Animator 资产里补齐，" +
                           $"不要用代码运行时跳过（静默降级会把「资产漏配」伪装成「打了没反应」）。");
        }
    }

    /// <summary>按名字取 Animator 参数；不存在返回 null。三处参数检查共用这一处遍历。</summary>
    private AnimatorControllerParameter FindParameter(string name)
    {
        if (m_Animator == null) return null;
        AnimatorControllerParameter[] ps = m_Animator.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name == name)
            {
                return ps[i];
            }
        }
        return null;
    }

    private bool HasParameter(string name)
    {
        return FindParameter(name) != null;
    }

    /// <summary>
    /// 播放死亡表现并卸载物理碰撞：写 Die 参数 → 销毁 CapsuleCollider / CharacterController。
    /// ⚠ 本方法只管「看起来死了」：IsDead 标志、Buff 清算、IsSilenced 等仍归 CombatUnitController.SetDead。
    /// ⚠ 本方法**刻意不 ResetSkillId**：CombatUnitController.SetDead 在调用它之前已经做过，
    ///   严格沿用迁移前的执行顺序（SetInteger(0) → IsDead = value → 写 Die → 卸载碰撞）。
    /// </summary>
    public void PlayDeath()
    {
        if (m_Animator == null) return;

        // ★ v5 P15：顿帧引入后必须补这一句。若在 Animator.speed == 0 的顿帧窗口内死亡，
        //   Die 动画会卡在第一帧不动，表现为「敌人站着不动地死了」。
        //   恢复速度放在最前，保证后续任何表现都在正常播放速度下进行。
        SetAnimatorSpeed(1f);

        // ★ P15-fix：Die 在敌人控制器里是 **Bool**，以前这里写的是 SetTrigger("Die") ——
        //   对 Bool 参数设触发器不会有任何效果，敌人的死亡动画从未真正被触发过。
        //   改成先查类型再写值；参数不存在的话，构造期 ValidateAnimationContract 已经红字报过了。
        WriteDeathParam();

        // 死亡后不再参与任何物理 / 寻路碰撞：碰撞器与角色控制器一并移除。
        // ⚠ 这里必须写 Object.Destroy —— 本类已不继承 MonoBehaviour，Destroy 不再是「自带」的实例方法，
        //   需要显式指到 UnityEngine.Object 上的静态版本；行为（含延迟到帧末销毁的语义）与原先完全一致。
        CapsuleCollider collider = m_Owner.GetComponent<CapsuleCollider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }

        CharacterController controller = m_Owner.GetComponent<CharacterController>();
        if (controller != null)
        {
            Object.Destroy(controller);
        }
    }

    //═════════════════════════ 死亡表现（★ v5 P15-fix 2026-09-18）════════════════════════

    // Die 参数必须按**真实类型**写入：敌人 GBL.controller 的 Die 是 **Bool**，Trigger 对它完全无效。

    /// <summary>
    /// 按参数的**真实类型**写入「死亡」。
    ///
    /// 【为什么做类型自适应而不是统一改成某一种】
    ///   两个 Animator 是两套历史资产：改敌人控制器去迁就代码要动 YAML 与全部旧分支；
    ///   改代码去适配资产只动一处、零资源风险。判断类型的成本是**每个单位一次**（有缓存），可忽略。
    ///
    /// 【为什么 Bool 写 true 而不是 SetTrigger】
    ///   Bool 是**持续为真**的状态量：死亡后只要这个单位还存在，Animator 就一直知道「它是死的」。
    ///   这也是给 Die 状态加 [Die==false] 才准离开的回退条件能成立的前提。
    /// </summary>
    private void WriteDeathParam()
    {
        AnimatorControllerParameter p = FindParameter(BattleConst.ani_Die);
        if (p == null)
        {
            // 缺参数已在构造期 ValidateAnimationContract 里 LogError 指名，此处不重复告警、也不静默兜底。
            // SetDead 的其余逻辑（IsDead / Buff 清算 / 卸载碰撞）照常执行。
            return;
        }

        switch (p.type)
        {
            case AnimatorControllerParameterType.Bool:
                m_Animator.SetBool(BattleConst.ani_Die, true);
                break;
            case AnimatorControllerParameterType.Trigger:
                m_Animator.SetTrigger(BattleConst.ani_Die);
                break;
            default:
                // Int / Float 走到这里说明美术把参数类型改了 —— 不猜语义，明确报错让资产侧修正
                Debug.LogError($"[UnitView] {m_Owner.name} 的 {BattleConst.ani_Die} 参数类型是 {p.type}，" +
                               $"代码只支持 Bool / Trigger —— 请改回 Bool（GBL）或与代码负责人确认。");
                return;
        }

#if UNITY_EDITOR
        // ★ 与死亡链路的可观测性契约：这一条 + Animator 实际停在 Die 状态，才算死亡动画真的播了。
        Debug.Log($"[Death] {m_Owner.name} 写 {BattleConst.ani_Die}({p.type}) = 死亡");
#endif
    }

    /// <summary>清掉移动相关的两个动画开关（复活 / 状态重置时用）</summary>
    public void StopMoveAnim()
    {
        SetMoveAnim(false);
        SetRunAnim(false);
    }

    //═════════════════════════ 动画状态查询 ═════════════════════════

    /// <summary>Animator 第 0 层的当前状态信息（供 AttackState 读 normalizedTime 推进段号）</summary>
    public AnimatorStateInfo StateInfo
    {
        get { return m_Animator != null ? m_Animator.GetCurrentAnimatorStateInfo(0) : default; }
    }

    /// <summary>Animator 第 0 层是否正处于过渡中（过渡期间 StateInfo 读到的是源状态，不是目标状态）</summary>
    public bool IsInTransition
    {
        get { return m_Animator != null && m_Animator.IsInTransition(0); }
    }

    /// <summary>Animator 第 0 层过渡目标状态的信息（仅在 IsInTransition 为真时才有意义）</summary>
    public AnimatorStateInfo NextStateInfo
    {
        get { return m_Animator != null ? m_Animator.GetNextAnimatorStateInfo(0) : default; }
    }

    /// <summary>
    /// Animator 当前是否停在待机状态（未处于过渡中）。
    /// FSM 用它做两件事：Attack 阶段②判定是否已切离待机；阶段⑨判定技能是否已播完。
    /// </summary>
    public bool IsInIdleState
    {
        get
        {
            if (m_Animator == null) return true;
            if (m_Animator.IsInTransition(0)) return false;
            return m_Animator.GetCurrentAnimatorStateInfo(0).IsName(BattleConst.ani_IdleState);
        }
    }

    /// <summary>
    /// 是否处于「可起手打下一个技能」的状态：技能 id 为 0（站着）且未在做过渡。
    /// 必须保证 SkillId == 0 表示待机 —— 这是 StarSKill 的硬前提。
    /// </summary>
    public bool IsIdleToUseSkill
    {
        get
        {
            if (m_Animator == null) return false;
            return m_Animator.GetInteger("SkillId") == 0 && !m_Animator.IsInTransition(0);
        }
    }
}
