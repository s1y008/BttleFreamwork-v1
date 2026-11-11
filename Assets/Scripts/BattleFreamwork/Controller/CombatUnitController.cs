using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using BattleFramework.Core;


/// <summary>
/// 敌人状态枚举
/// </summary>
public enum EnemyState
{
    NOR,
    DOWN,
    AIR,
    Gas,   //中毒状态
           /// <summary>
           /// BeBounce   NPC受到玩家自身BUFF时的状态
           /// </summary>
    BeBounce,
    /// <summary>
    /// 持续受击状态
    /// </summary>
    ContiHit,
    SubSpeed,


}

/// <summary>
/// 玩家状态枚举
/// </summary>
public enum WarriorState
{
    NOR = 0,
    ATTACK = 1,
    HIT = 2,
    GOD = 3,
    Die = 4,
    RESTING = 5,
    ShortHit = 6,
}


/// <summary>
/// 中间层
/// 在 此脚本下会细分NPCController或者PlayerController
/// </summary>
public class CombatUnitController : MoveBaseController, IHitReceiver
{
    protected CombatUnit m_CurCombatUnit;
    public CombatUnit CurCombatUnit
    {
        get
        {
            return m_CurCombatUnit;
        }
        set
        {
            m_CurCombatUnit = value;
            if (m_CurCombatUnit != null)
            {
                //战斗链路Buff接线：数据变更时重绑Buff组件（订阅新单位的BuffSystem）
                if (m_BuffComponent != null)
                {
                    m_BuffComponent.Rebind(m_CurCombatUnit);
                }
                //人物出生的方法
                m_CurCombatUnit.OnSpawn();
            }
        }
    }
    protected CombatUnit NpcCombatUnit;

    /// <summary>
    /// ★ 重置段索引的"唯一主关"位于 IdleState.OnEnter(详见 CombatStates.cs)
    ///   段计数不应在 OnSkillCase 内重置,否则段间隔 >0.5s 时会把第 2/3 段误判为新一轮而退化成段 1
    /// </summary>

    /// <summary>
    /// Buff组件：Buff事件订阅/DoT结算/控制状态仲裁（Start时自动挂载，见UnitBuffComponent）
    /// </summary>
    private UnitBuffComponent m_BuffComponent;

    /// <summary>
    /// ★ v5 P13 新增：视图组件 —— 本单位一切「画面端」动作的唯一出口（动画参数 / 特效 / 死亡表现）。
    /// 抽离动机详见 UnitView 类注释；此处只保留引用，Controller 自身**不再直接碰 Animator**。
    /// 它是 public 只读的：状态机要播动画、受击组件要出火花，都得经由它，不允许绕回去摸 m_Animator。
    ///
    /// ⚠ 注意它是**普通 C# 类，不是 MonoBehaviour**：由本类 Start 里 <c>new UnitView(this, m_Animator)</c> 持有，
    ///   不会出现在单位的 Inspector 上，也就谈不上在 prefab 里拖引用 —— 访问它的唯一途径是 <c>Owner.View</c>。
    /// </summary>
    private UnitView m_View;
    public UnitView View { get { return m_View; } }

    /// <summary>
    /// ★ v5 P13 新增：受击反应组件 —— 一次受击从进门到出门的编排者（扣血 / 特效 / 死亡 / 仇恨 / 控制态）。
    /// 抽离动机详见 UnitHitReaction 类注释。Controller 上的 OnDamaged / OnHitOut 等方法保留原名作为
    /// **对外契约（Facade）**，实现体全部转发到此组件 —— 好处是 Skill / UnitBuffComponent 等既有调用点
    /// 一行都不用改，将来二十个技能效果桩要对接时也仍然只认识 Controller 这一层。
    ///
    /// ⚠ 同 View：普通 C# 类，由 Start 里 <c>new UnitHitReaction(this, m_View)</c> 持有。
    /// </summary>
    private UnitHitReaction m_HitReaction;
    public UnitHitReaction HitReaction { get { return m_HitReaction; } }

    private void OnDestroy()
    {
        //组件清算：退订事件并清空Buff；施加者反查表注销由组件自身的OnDestroy兜底
        if (m_BuffComponent != null)
        {
            m_BuffComponent.Shutdown();
        }
    }

    

    
    /// <summary>
    /// 对象是否死亡
    /// </summary>
    protected bool m_IsDead = false;
    public bool IsDead { get { return m_IsDead; } set { m_IsDead = value; } }

    /// <summary>
    /// 战斗状态机（第一阶段 FSM）
    /// </summary>
    protected CombatStateMachine m_StateMachine = null;

    /// <summary>
    /// 当前战斗状态
    /// </summary>
    public CombatState CurrentState
    {
        get { return m_StateMachine != null ? m_StateMachine.CurrentState : CombatState.Idle; }
    }

    /// <summary>
    /// 状态机是否已初始化（Buff组件仲裁控制状态前检查）
    /// </summary>
    public bool HasStateMachine { get { return m_StateMachine != null; } }

    /// <summary>
    /// 是否处于沉默状态（由 SilenceState 控制）
    /// </summary>
    public bool IsSilenced { get; set; }

    /// <summary>
    /// 能否进行攻击   0-能够攻击，其他表示不能攻击
    /// </summary>
    public int CanAttack { get { return CurCombatUnit.CantAttack; } set { CurCombatUnit.CantAttack = value; } }

    /// <summary>
    /// 能够进行被攻击  0:能够被攻击，其他表示不能被攻击
    /// 比如：有可能在隐身状态什么的 
    /// 
    /// </summary>
    public int CanbeAttack { get { return CurCombatUnit.CanbeAttack; } set { CurCombatUnit.CanbeAttack = value; } }

    /// <summary>
    /// 当前的血量
    /// </summary>
    public int Hp { get { return CurCombatUnit.HP; } set { CurCombatUnit.HP = value; } }

    /// <summary>
    /// 最大血量
    /// </summary>
    public int MaxHp { get { return CurCombatUnit.MaxHP; } }

    /// <summary>
    /// 血量比
    /// </summary>
    public float HpRatio { get { return ((float)CurCombatUnit.HP / CurCombatUnit.MaxHP); } }

    /// <summary>
    /// 战斗目标
    /// </summary>
    public CombatUnitController m_Target;

    /// <summary>
    /// 设置敌方层
    /// </summary>
    public virtual int EnemyLayer { get { return 0; } }

    /// <summary>
    /// 设置友方层
    /// </summary>
    public virtual int FriendLayer { get { return (1 << gameObject.layer); } }

    /// <summary>
    /// 获取敌人的Tag
    /// </summary>
    public virtual string EnemyTag { get { return null; } }

    /// <summary>
    /// 设置友方Tag
    /// </summary>
    public virtual string FriendTag { get { return gameObject.tag; } }



    /// <summary>
    /// 当前选中的技能
    /// </summary>
    protected Skill m_CurSelectedSkill = null;

    /// <summary>
    /// ★ v5 P9 新增：当前选中技能的**只读**对外视图，供 AttackState.OnEnter 取本次要释放的技能。
    /// 为什么只读：写入权留在 Controller（DoAutoFight 选技能、距离不足时置 null 都是决策行为），
    ///   状态层只允许「看」。状态擅自改会让下一帧的决策逻辑读到不一致的数据。
    /// 为什么不再传参：技能本来就是单位自己的业务字段、有唯一真相源，
    ///   AttackState 取它属于「查询」，而不是「接收一次性参数」（对比旧的 int skillId 走 object 通道）。
    /// </summary>
    public Skill CurSelectedSkill { get { return m_CurSelectedSkill; } }

    /// <summary>
    /// ★ v5 P9 新增：本次击退 / 浮空的硬直时长（秒），供 KnockbackState.OnEnter 读取。
    /// 写入方只有三处受击入口（OnHitOut / OnPlayerAirHit / OnAirHit），
    /// 且都写在 RequestState(CombatState.Knockback) 的**前一行**。
    ///
    /// ⚠ 为什么保留「先写字段、再切状态」而不封装成 EnterKnockback(duration) 方法：
    ///   刻意的取舍 —— 全工程状态切换入口统一为 RequestState 一个，不额外增加同义方法
    ///   （两套入口并存最容易出现「有人走了 A 路径没走 B 路径」的漏改）。
    ///   代价：写入可能被遗漏或被 FSM 拒绝时残留，故 KnockbackState 内对 ≤ 0 的值做了兜底
    ///   （DEFAULT_DURATION_SECONDS），且该字段的语义是「单位当前被击退多久」，
    ///   而进入 Knockback 的业务路径只有那三个入口，每次必然重新写入覆盖。
    /// </summary>
    public float KnockbackDuration { get; private set; }

    /// <summary>
    /// ★ v5 P13：KnockbackDuration 的**受控写入口**，唯一调用方是 UnitHitReaction.ApplyKnockback。
    /// 为什么要绕一层方法而不是把 setter 改成 public：
    ///   字段的语义是「这个单位本次被击退多久」，写入必须紧贴 RequestState(Knockback) 的前一行才成立；
    ///   private set + 单一方法，等于把「谁能写」锁死在那个唯一正确的位置。
    ///   若改成 public set，任何人 anywhere 都能改，KnockbackState.OnEnter 读到的就可能不是本次的值。
    /// ⚠ 顺序契约同前：调用方必须**先**写它、**再** RequestState(Knockback)。
    /// </summary>
    /// <param name="seconds">本次击退 / 浮空的硬直时长（秒）</param>
    public void SetKnockbackDuration(float seconds)
    {
        KnockbackDuration = seconds;
    }

    /// <summary>
    /// 是否需要移动到目标
    /// </summary>
    protected bool m_NeedMoveTarget = false;


   
    // ★ v5 P13 删除四个零引用成员（本次用 scan_refs.py 全量扫描确认，非 grep 截断判断）：
    //     m_EffectGo / EffectGo、_isHitPoint / _IsHitPoint、count、_SubHpCount
    //   它们既无生产读取点，也无写入点，属于早期「先留着说不定有用」的占位字段。
    //   留着它们会让后来者误以为「当前技能特效可以从 Controller 取」，实际早已改由 EffectManager 挂载。
   
    protected override void Start()
    {
        base.Start();
        if (CurCombatUnit != null)
        {
            m_Speed = CurCombatUnit.Speed;
        }

        // ★ v5 P13：先建 View —— 状态机注册完成后的首个 OnEnter 就会调 NavStop → View.SetMoveAnim，
        //   创建顺序若放在状态机之后，那一次调用会撞上空引用。
     
        if (m_View == null)
        {
            m_View = new UnitView(this, m_Animator);
        }
        if (m_HitReaction == null)
        {
            m_HitReaction = new UnitHitReaction(this, m_View);
        }

        InitStateMachine();

        //自动挂载Buff组件并绑定（Buff事件订阅/DoT结算/控制仲裁都在组件内）
        if (m_BuffComponent == null)
        {
            m_BuffComponent = gameObject.AddComponent<UnitBuffComponent>();
        }
        m_BuffComponent.Setup(this, CurCombatUnit);
    }
    //═══════════════ 球形索敌的共用缓冲区（2026-09-24）═══════════════
    // ★ 为什么不用 Physics.OverlapSphere：它每次调用都在引擎内部 new 一个 Collider[]，
    //   这些数组活不过一帧就成垃圾 ⇒ 每帧索敌 = 每帧一次堆分配，堆到 GC 来收。
    //   OverlapSphereNonAlloc 把结果填进调用方给的数组，容量固定、反复复用 ⇒ 零分配。
    //
    // ★★ 为什么敢让所有单位共用这一份（安全性论证）：
    //   四个 FindTargetInRadius_XXX 都是「同步执行、当场消费完」—— 循环里只挑出**一个**
    //   CombatUnitController 返回，不返回数组、也不跨帧持有其中的 Collider。
    //   Unity 游戏逻辑只有主线程，不存在两次调用交错的可能 ⇒ 共用安全。
    //   ⚠ 若将来某个方法要把结果存起来跨帧用，就必须改回独享数组 —— 届时会读到别人写进去的东西。
    //
    // ⚠⚠ 容量超限属于「静默降级」（违反项目铁律 5）：目标数超过容量时 Unity 会把多出来的
    //   部分**静默丢弃**，既不报错也不给提示。索敌丢目标的表现是「有些敌人永远不被选中」，
    //   而且无从排查 ⇒ 下面做一次性 LogError 指名（不是静默吞掉）。
    private const int TARGET_BUFFER_SIZE = 64;
    private static readonly Collider[] s_TargetBuffer = new Collider[TARGET_BUFFER_SIZE];
    private static bool s_BufferOverflowWarned;

    /// <summary>
    /// 球形检测：结果写入共用缓冲区，返回**实际命中数**（&lt;= TARGET_BUFFER_SIZE）。
    /// ⚠ 调用方必须按返回值 count 遍历 —— 缓冲区 count 之后的位置仍保留着上一次的 Collider 引用，
    ///   按 s_TargetBuffer.Length 走会把陈旧对象当成本次命中。
    /// </summary>
    private static int OverlapTargets(Vector3 center, float radius, int layer)
    {
        int count = Physics.OverlapSphereNonAlloc(center, radius, s_TargetBuffer, layer);
        if (count >= TARGET_BUFFER_SIZE && !s_BufferOverflowWarned)
        {
            s_BufferOverflowWarned = true;
            Debug.LogError("[CombatUnitController] 球形索敌缓冲区已满（容量 " + TARGET_BUFFER_SIZE
                + "，检测半径 " + radius + "）：超出容量的目标会被 Unity **静默丢弃**，"
                + "表现为某些单位永远索不到敌 —— 请调大 TARGET_BUFFER_SIZE。此提示只报一次。");
        }
        return count;
    }

    #region 查找最近攻击目标
    /// <summary>
    /// 在目标中查找距离最近的攻击单位
    /// </summary>
    /// <param name="layer">层</param>
    /// <param name="includeCanbeAttack">是否包含不能被攻击的单位</param>
    /// <returns></returns>
    public virtual CombatUnitController FindTargetInRadius_MinDistance(int layer, bool includeCanbeAttack = false)
    {
        float minDistance = -1;  //最近距离
        CombatUnitController selectedUnit = null;//被选择的对象

        //LayerMask.NameToLayer(string layerName)的作用是将指定层的“名称”字符串转换成对应的Int 型的LayerMask码。
        //获取以执行此脚本的游戏物体为中心，在一定半径内搜索所有曾经为Warrior的GameObj； 返回一个碰撞体数组
        Collider[] colliders = Physics.OverlapSphere(transform.position, CurCombatUnit.Spring, layer);

        // ⚠ 2026-09-24 清理：此处原本有一条 region 注释说「foreach 遍历碰撞数组会造成内存垃圾，
        //   顾此方法放弃，改用下边的 for 循环遍历」，但方法体实际仍是 foreach ——
        //   说明那次改造当年只做了一半（真正改成 for 的是下面的 FindTargetForMove_MinDistance）。
        //   而且这条经验本身已过期：Unity 2019.3 之后 foreach 遍历数组不再产生额外 GC
        //   （本工程是 2023.2.20f1c1 ⇒ 现在这么写没有问题）。
        //   ★ 本方法真正的分配源是上面那行 Physics.OverlapSphere（每次调用 new 一个 Collider[]），
        //     按 2026-09-24 的决定暂不改动 —— 它只在 ScaleEnemy 分支丢失目标时才走到。
        foreach (Collider c in colliders)
        {
            CombatUnitController unit = c.gameObject.GetComponent<CombatUnitController>();
            if (unit==null||unit.IsDead)
            {
                continue;
            }
            //如果包含不能被攻击的对象并且
            if (!includeCanbeAttack && unit.CanbeAttack>0)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, unit.transform.position);
            if (minDistance<0)
            {
                minDistance = distance;
                selectedUnit = unit;
            }
            else
            {
                if (minDistance>distance)
                {
                    minDistance = distance;
                    selectedUnit = unit;
                }
            }
        }

        return selectedUnit;
    }
    #endregion

    /// <summary>
    /// 角色向前移动中，确定什么时候停止移动
    /// </summary>
    /// <param name="layer">层</param>
    /// <param name="includeCanbeAttack">是否包含不能被攻击的单位</param>
    /// <returns></returns>
    public virtual CombatUnitController FindTargetForMove_MinDistance(int layer)
    {
        float minDistance = -1;  //最近距离
        CombatUnitController selectedUnit = null;//被选择的对象

        // ★ 2026-09-24：改用共用缓冲区 + NonAlloc（原理见上方 OverlapTargets 注释），
        //   注意必须按 count 遍历，不能按 s_TargetBuffer.Length。
        int count = OverlapTargets(transform.position, CurCombatUnit.MoveSpring, layer);
        for (int i = 0; i < count; i++)
        {
            CombatUnitController unit = s_TargetBuffer[i].gameObject.GetComponent<CombatUnitController>();
            if (unit == null || unit.IsDead)
                continue;
            //如果包含不能被攻击的对象并且
            if (unit.CanbeAttack > 0)
                continue;

            float distance = Vector3.Distance(transform.position, unit.transform.position);
            if (minDistance < 0)
            {
                minDistance = distance;
                selectedUnit = unit;
            }
            else
            {
                if (minDistance > distance)
                {
                    minDistance = distance;
                    selectedUnit = unit;
                }
            }
        }
        return selectedUnit;
    }

    #region 查找随机攻击目标
    /// <summary>
    /// 查找随机单位
    /// </summary>
    /// <param name="layer"></param>
    /// <param name="includeCanBeAttack"></param>
    /// <returns></returns>
    public virtual CombatUnitController FindTargetInRadius_Random(int layer, bool includeCanBeAttack = false)
    {
        // ★ 2026-09-24：改用共用缓冲区 + NonAlloc（原理见上方 OverlapTargets 注释）。
        // ⚠ 这里还剩一处分配：allUnit 每次 new List<object>()。可以比照 s_TargetBuffer 复用
        //   （调用前 Clear 即可，安全前提与上方缓冲区相同），本次按「一次一事」未动。
        int count = OverlapTargets(transform.position, CurCombatUnit.Spring, layer);
        List<object> allUnit = new List<object>();

        for (int i = 0; i < count; i++)
        {
            CombatUnitController unit = s_TargetBuffer[i].gameObject.GetComponent<CombatUnitController>();
            if (unit == null || unit.IsDead)
            {
                continue;
            }
            if (!includeCanBeAttack && unit.CanbeAttack > 0)
            {
                continue;
            }
            allUnit.Add(unit);
        }
        return CommonHelper.RandomObject(allUnit) as CombatUnitController;
    }
    #endregion

    #region 查找最少血量
    public virtual CombatUnitController FindTargetInRadius_MinHp(int layer, bool includeCanBeAttack = false)
    {
        float minHp = 0;
        CombatUnitController selectedUnit = null;

        // ★ 2026-09-24：改用共用缓冲区 + NonAlloc（原理见上方 OverlapTargets 注释）。
        int count = OverlapTargets(transform.position, CurCombatUnit.Spring, layer);

        for (int i = 0; i < count; i++)
        {
            CombatUnitController unit = s_TargetBuffer[i].gameObject.GetComponent<CombatUnitController>();
            if (unit == null || unit.IsDead)
            {
                continue;
            }
            if (!includeCanBeAttack && unit.CanbeAttack > 0)
            {
                continue;
            }

            float HpR = unit.HpRatio;//获得血量占比
            if (minHp == 0)
            {
                minHp = HpR;
                selectedUnit = unit;
            }
            else
            {
                if (HpR < minHp)
                {
                    minHp = HpR;
                    selectedUnit = unit;
                }
            }
        }
        return selectedUnit;
    }
    #endregion

    /// <summary>
    /// 根据技能查找目标
    /// </summary>
    protected void SelectTartgetBySkill()
    {
        if (m_CurSelectedSkill == null)//攻击单元当前是否有将要播放的技能
        {
            return;
        }
        //如果当前选择的技能类型是敌方范围或者是敌方目标
        if (m_CurSelectedSkill.BaseData.HitType == SkillHitTypes.ScaleEnemy )
        {
            if (m_Target != null)//如果攻击的目标不为空
            {
                if (m_Target.IsDead)//如果攻击目标已经死亡
                {
                    m_Target = null;//将攻击目标置空
                }
                else
                {
                    if (m_Target.CanbeAttack > 0)//判断攻击目标是否可以被攻击
                    {
                        m_Target = null;
                        return;
                    }
                    //判断攻击者与被攻击者之间的距离是否大于技能的最远攻击距离
                    bool dis = (transform.position - m_Target.transform.position).sqrMagnitude > m_CurSelectedSkill.BaseData.AttackDist * m_CurSelectedSkill.BaseData.AttackDist;

              
                    if (1 << ((m_Target.gameObject.layer)) != EnemyLayer || dis )
                    {
                        m_Target = null;
                    }

                }
            }
            //如果目标为空
            if (m_Target == null)
            {
                //查找最近的目标
                m_Target = FindTargetInRadius_MinDistance(EnemyLayer);
            }
        }
        //如果是单体攻击
        else if (m_CurSelectedSkill.BaseData.HitType == SkillHitTypes.SelectedEnemy)
        {
            //查找最近的目标
            m_Target = FindTargetInRadius_MinDistance(EnemyLayer);
        }
        //如果选择的目标是友方或者友方的方位
        else if (m_CurSelectedSkill.BaseData.HitType == SkillHitTypes.ScaleFriend || m_CurSelectedSkill.BaseData.HitType == SkillHitTypes.SelectedFriend)
        {
            m_Target = FindTargetInRadius_MinDistance(FriendLayer, true);
        }
        //如果当前选择的技能是针对自己的
        else if (m_CurSelectedSkill.BaseData.HitType == SkillHitTypes.Self)
        {
            m_Target = this;
        }
    }


    /// <summary>
    /// 移动至选中的目标
    /// </summary>
    protected void MoveTarget()
    {
        if (gameObject.tag == "Player")
        {
            SetDestination(new Vector3(gameObject.transform.position.x, gameObject.transform.position.y, gameObject.transform.position.z - 50));
            // Vector3.forward

        }
        else
        {
            if (!IsDead)
                SetDestination(m_Target.transform.position);
        }
    }

    /// <summary>
    /// 击中目标造成伤害 —— ★ v5 P13 起降为**对外契约（Facade）**，实现体已迁往 UnitHitReaction.ApplyHit。
    ///
    /// 【为什么保留签名而不让 Skill 直接调组件】
    ///   ① 调用方（Skill.ImmedDamage / UnitBuffComponent 的 DoT 结算）一行都不用改，改动不产生连带扩散；
    ///   ② 受击入口将来要对接二十个技能效果桩，让它们统一认识 Controller 这一层，而不是各自去找组件；
    ///   ③ 保留 virtual：子类仍可为本单位定制受击反应（在转发前后插入自己的逻辑即可）。
    ///
    /// 【三条契约约定（与迁移前逐字一致）】
    ///   · attacker 为空 / 已死亡 / 不可被攻击 → 整次受击被静默忽略；
    ///   · 先扣血，致死则请求 Dead 且**不再播受击特效**；未死才播；
    ///   · hitEffectId == 0 表示本段没有受击特效（正常情况，不是错误）。
    /// </summary>
    /// <param name="attactker">攻击者</param>
    /// <param name="hp">本次伤害值（内部按 int 截断）</param>
    /// <param name="hitEffectId">本次命中的受击特效 id（**0 = 该段无受击特效，跳过**）。
    ///   ★ v5 P10：由攻击方 Skill.ImmedDamage 按当前段号算好后传入，受击方不再反查攻击者状态。</param>
    /// <returns>本次受击结局；组件缺失时返回 Ignored（理论上不会发生，属防御性返回）</returns>
    public virtual UnitHitReaction.HitOutcome OnDamaged(CombatUnitController attactker, float hp, int hitEffectId)
    {
        if (m_HitReaction == null)
        {
            return UnitHitReaction.HitOutcome.Ignored;
        }
        return m_HitReaction.ApplyHit(attactker, (int)hp, hitEffectId);
    }

    // ★ v5 P13 删除 OnHurt(List<int>, ...) —— 方法体为空且全工程零引用（scan_refs.py 全量扫描确认）。
    //   它的注释本来说的是「显示伤害特效以及普通受击动画」，而真正的特效出口早已收敛到
    //   UnitView.PlayEffect；留着只会误导后来者以为这里是受击动画的开关。

    /// <summary>
    /// 受击表现（顿帧 / 普通受击动画）—— 实现体在 <c>UnitHitReaction.ApplyHurt</c>。
    ///
    /// ★ 阶段二新增（2026-09-24）：原本 <c>HureEffect</c> 直接写
    ///   <c>target.HitReaction.ApplyHurt(...)</c>，等于让效果层认识本单位内部的编排组件。
    ///   收口到本 Facade 之后，效果层只依赖 <c>IHitReceiver</c> 契约，
    ///   将来换单位类型不必动效果类（契约纪律见 IHitReceiver 类注释）。
    ///
    /// ⚠ 命名提示：上面那条注释说的是 v5 P13 删掉的空重载 <c>OnHurt(List&lt;int&gt;, ...)</c>，
    ///   与本方法**不是同一样东西**（签名完全不同），别看到同名就以为在走回头路。
    /// </summary>
    /// <param name="attacker">攻击者（内部用于朝向 / 仇恨）</param>
    public virtual void OnHurt(CombatUnitController attacker)
    {
        if (m_HitReaction == null)
        {
            return;
        }
        m_HitReaction.ApplyHurt(attacker);
    }

    /// <summary>
    /// 击中目标造成治疗 —— 同 OnDamaged，实现体已迁往 UnitHitReaction.ApplyHeal（超过上限按 MaxHp 截断）。
    /// </summary>
    /// <param name="attacker">治疗来源</param>
    /// <param name="hp">增加的血量</param>
    /// <param name="effectId">加血技能特效ID</param>
    public virtual void OnHeal(CombatUnitController attacker, int hp, int effectId /*List<int> hitEffects*/)
    {
        if (m_HitReaction == null)
        {
            return;
        }
        m_HitReaction.ApplyHeal(attacker, hp, effectId);
    }
    /// <summary>
    /// 添加BUFF，此方法做为接口留用
    /// </summary>
    /// <param name="attacker"></param>
    /// <param name="buffid"></param>
    public virtual void OnAddBuff(CombatUnitController attacker, Skill skillInfo, int buffid, bool _isBuff = false)
    {
        if (attacker == null || IsDead)
        {
            return;
        }

        //★ 走 Id 入口：BuffType 已降级为标签位图（同一个位可被多个BuffId共享），
        //  先转成 Type 再施加会张冠李戴（CSV里两个Id共用Stunned位时，永远只能加到第一个）。
        //  BuffId 不存在时 AddBuff 内部已打日志并返回 null。
        CurCombatUnit.BuffSystem.AddBuff(buffid, attacker.GetInstanceID());
    }

    /// <summary>
    /// 嘲讽
    /// </summary>
    /// <param name="attacker"></param>
    public virtual void OnSneer(CombatUnitController attacker)
    {
        if (attacker == null || IsDead)
        {
            return;
        }
        m_Target = attacker;
    }

    //═════════════════ 位移类受击（击退 / 浮空 / 击飞）══════════════════
    // ★ v5 P13：三个入口的实现体已统一迁往 UnitHitReaction.ApplyKnockback。
    //   它们此前做的事情完全一样（写时长 → RequestState(Knockback)），差别只在防御判断的多少，
    //   是典型的复制粘贴漂移。此处保留三个**语义化**入口作为对外契约 —— 击退 / 浮空 / 击飞
    //   将来很可能有不同的表现参数（方向、曲线、是否带位移），现在按语义分口，改起来不必再拆签名。
    //   ⚠ params object[] 空调用时不传参数会以 Array.Empty<object>() 传递，零装箱零分配，
    //     所以保留这个历史签名没有运行时成本；真要用数据请改 UnitHitReaction 那个干净入口。

    /// <summary>
    /// 击飞(浮空) —— 转发到 UnitHitReaction。
    /// 原实现里的两层防御（attacker/IsDead、Animator 缺失或被冻结）已由 Reaction 统一承担。
    /// </summary>
    public virtual void OnHitOut(CombatUnitController attacker, float dist, float time, bool airHit, bool isMove = false)
    {
        if (m_HitReaction == null) return;
        m_HitReaction.ApplyKnockback(attacker, time);
    }

    /// <summary>浮空受击（玩家版，仅时长）—— 转发到 UnitHitReaction</summary>
    public virtual void OnPlayerAirHit(CombatUnitController attacker, float time, params object[] args)
    {
        if (m_HitReaction == null) return;
        m_HitReaction.ApplyKnockback(attacker, time);
    }

    /// <summary>浮空受击（完整参数版）—— 转发到 UnitHitReaction</summary>
    public virtual void OnAirHit(CombatUnitController attacker, float dist, float time, bool airHit, params object[] args)
    {
        if (m_HitReaction == null) return;
        m_HitReaction.ApplyKnockback(attacker, time);
    }

    //===============================根据双方技能等级判断是否受击与受击===========================

    /// <summary>
    /// 眩晕 —— 实现体已迁往 UnitHitReaction.ApplyStun（★2026-09-24：内部不再转 BuffType.Stunned，改按 CSV 的 Tags=HardControl 反查配置行）。
    /// ★ 为什么仍然走 Buff 而不是「状态自己记倒计时」：免疫 / 叠加 / 驱散 / 时长这些语义
    ///   只有一份真相源才不会打架；FSM 的 StunState 只做「每帧问 Buff 还在不在」。
    /// </summary>
    /// <param name="unit">玩家</param>
    /// <param name="attacker">攻击者敌人</param>
    /// <param name="time">眩晕时间</param>
    /// <param name="isDizz">是否眩晕</param>
    public virtual void OnDizz(CombatUnitController unit, CombatUnitController attacker, float time, bool isDizz)
    {
        if (attacker == null || m_HitReaction == null)
        {
            return;
        }
        m_HitReaction.ApplyStun(attacker, time);
    }

    
    /// <summary>
    /// 沉默 —— 实现体已迁往 UnitHitReaction.ApplySilence（★2026-09-24：内部不再转 BuffType.Silenced，改按 CSV 的 Tags=Silence 反查配置行）。
    /// 与眩晕同走 Buff，由 UnitBuffComponent 的事件仲裁切入 Silence 状态。
    /// </summary>
    /// <param name="attacker">施加者</param>
    /// <param name="time">沉默时长（秒）</param>
    public virtual void OnSilence(CombatUnitController attacker, float time)
    {
        if (attacker == null || m_HitReaction == null)
        {
            return;
        }
        m_HitReaction.ApplySilence(attacker, time);
    }

    /// <summary>
    /// 死亡事件
    /// </summary>
    protected virtual void OnDead()
    {
        SetDead(true);

        // ★ v5 P15-sink：死亡即刻停用导航，必须排在 buff 清算之前。
        //   敌人死后由 AttackNpcController.Skin() 逐帧 Lerp 把尸体沉入地面，
        //   每帧只挪 0.167% 的剩余距离（约 8mm），本就极慢；
        //   NavMeshAgent 只要还 enabled 就会每帧把单位吸回导航网格表面 ——
        //   同一个 transform.position 两个写手，后者赢 ⇒ 肉眼完全看不到下沉，
        //   只有死亡动画照播。这正是「死亡后看不见缓慢下沉」的根因。
        //   修它在这一层而不是 MoveBaseController.OnAnimatorMove 里加 IsDead 判断：
        //   那里本来就有 if (m_NavMeshAgent.enabled) 闸门，禁用后自动短路，无需再散一处判断。
        StopNavigation();

        //死亡时清空所有Buff（OnBuffRemoved事件先于对象回收发出，回调内数据仍可读）
        if (m_BuffComponent != null)
        {
            m_BuffComponent.ClearBuffs();
        }
    }

    /// <summary>
    /// 设置死亡 —— ★ v5 P13：动画与卸载碰撞等**表现**部分已迁往 UnitView，此处只剩数据与编排。
    ///
    /// 迁移后的方法体仍然严格保持与原实现**逐句等价**的执行顺序：
    ///     m_Animator.SetInteger("SkillId", 0) → IsDead = value → SetTrigger("Die") → 销毁碰撞器
    ///
    /// ⚠ 刻意保留的争议点：「Animator 缺失则整体 return，连 IsDead 都不赋值」。
    ///   严格说这是个缺陷（单位死了却没被标记），但本次的目标是**重构而非修缺陷** ——
    ///   行为必须逐字等价，否则验证时无法确认差异来自搬移还是来自修改。
    ///   该缺陷已单独记入待办，改它属于另一个独立的、需要重新验证的变更。
    /// </summary>
    /// <param name="value">是否死亡</param>
    public void SetDead(bool value)
    {
        if (m_View == null || !m_View.HasAnimator)
        {
            return;
        }

        m_View.ResetSkillId();
        IsDead = value;

        if (value)
        {
            m_View.PlayDeath();
        }
    }

    //═════════════════ ★ v5 P13：以下三个动画查询属性已迁往 UnitView ══════════════════
    //   IsIdleToUseSkill      → View.IsIdleToUseSkill   （是否可起手下一个技能）
    //   IsAnimatorInIdleState → View.IsInIdleState      （是否停在待机未过渡，Attack 的起止判据）
    //   IsUseAttSkill         → 删除（全工程零引用，且逻辑与 IsInIdleState 重复）
    // 为什么连转发都不留：这三个问题的答案只存在于 Animator，Controller 里留转发等于继续承诺
    //   「我负责回答动画状态」，而它其实什么都不知道 —— 调用点直接写 Owner.View.Xxx 反而一眼可见
    //   「这是一次画面端查询」。改动面很小：CombatStates 2 处、本类 StarSKill 1 处、WarrorController 1 处。

    /// <summary>
    /// 是否可起手下一个技能 —— 转发到 UnitView（★ v5 P13）
    /// 保留这个薄转发的原因：它是 TryCastSkill / WarrorController 决策链路里反复出现的高频读点，
    /// 直接暴露 View 会把「决策依赖画面状态」这件事实藏进调用方字符串里；留一层短名字可读性更好。
    /// ★ 决策层用它做闸门本身是合理的：技能动画没播完就不该重新起手。
    /// </summary>
    public bool IsIdleToUseSkill
    {
        get { return m_View != null && m_View.IsIdleToUseSkill; }
    }

    /// <summary>
    /// 尝试进行普通攻击
    /// </summary>
    protected virtual void TryCastSkill()
    {
        if (IsSilenced)
        {
            return;
        }

        // ★ 2026-10-10 弹道闸门（投掷物方案 P3）：该技能还有未结算弹道 ⇒ 拒绝再次起手。
        //   关闭的竞态：Skill 实例按单位持有，弹在飞时若再次进 AttackState，OnEnter 的 SetSkillCaster
        //   会改写 m_SkillTarget —— 弹着结算会打到「后来锁的新目标」而不是出膛时锁的那个。
        //   闸门放 TryCastSkill（玩家/敌人 AI 共用入口），一处改动全覆盖；
        //   「弹没落地不放第二轮」也与 MMO 惯例一致。
        if (ProjectileManager.HasUnresolvedProjectile(m_CurSelectedSkill))
        {
            return;
        }

        float skillWarningDis = m_CurSelectedSkill.BaseData.Warning;
        float skillAttackDis = m_CurSelectedSkill.BaseData.AttackDist;
        if (this is WarrorController)
        {
            //如果与目标之间的距离大于等于当前选择的技能的警戒距离
            //或者 当前处于普通攻击并且当前技能处于CD中
            float dis = Dis(transform.position, m_Target.transform.position);
            if (dis >= Math.Pow(skillWarningDis,2)|| (m_CurSelectedSkill.NormalAttackCD && m_CurSelectedSkill.BaseData.SkillType == SkillTypes.NormalSkill))
            {
              //如果玩家点击了技能，敌人不在攻击范围之内的时候，点击无效
                m_CurSelectedSkill = null;
                //m_Animator.SetInteger("SkillId", 0);
                //m_NeedMoveTarget = true;
                return;
            }
            else
            {
                //m_NeedMoveTarget = false;
                //m_NavMeshAgent.stoppingDistance = skillAttackDis - 0.3f;
                //StopMove();
                StarSKill(skillAttackDis);
            }
        }
        else
        {
            //如果与目标之间的距离大于等于当前选择的技能的攻击距离 7> 6^2=36
            if ((Dis(transform.position, m_Target.transform.position)) > Math.Pow(skillWarningDis-0.3f, 2) /*skillWarningDis * skillWarningDis*/)
            {
             
                m_NeedMoveTarget = true;
                return;
            }
            else
            {
                m_NeedMoveTarget = false;
                PrepareAttack();
              
                StarSKill(skillAttackDis);
            }
        }
    }

    private void StarSKill(float skillAttackDis)
    {
       
            float dis = Dis(transform.position, m_Target.transform.position);
            if (dis <= Math.Pow(skillAttackDis, 2)&& IsIdleToUseSkill)
            {
                int index = CurCombatUnit.IsUseSkill.IndexOf(m_CurSelectedSkill.SkillId);
                if (index < 0)
                    CurCombatUnit.IsUseSkill.Add(m_CurSelectedSkill.SkillId);
                SetCd(m_CurSelectedSkill);
                // ★ v5 P13：动画参数写入改走 View（Controller 不再直接持有 Animator 语义）
                m_View.SetSkillId(m_CurSelectedSkill.SkillId);//需要修改成通过状态机进行播放动画

                // ★ 技能动画已触发 → 进入 Attack（Idle→Attack 合法转换）
                //   动画播完由 AttackState 切回 Idle，届时 IdleState.OnEnter 会 ResetSkillId()；
                //   段索引改由 AttackState.OnEnter 在每轮攻击**开始时** ResetSegIdx() 归零（v5 P10 迁移，
                //   比原先挂在 IdleState 上更贴合「一轮」语义，也不再依赖 Controller 上的技能引用）
                // ★ v5 P9:不再传 skillId —— AttackState.OnEnter 直接读 owner.CurSelectedSkill。
                //   省掉一次 GetSkill(id) 线性查表 + 一次 int 装箱，且拿到的是同一实例、语义更直白
                RequestState(CombatState.Attack);
            }
        
    }




    /// <summary>
    /// 方法提取
    /// 获取2个点之间的距离
    /// 相当于 Vector3.Distance(Vector3 pointA,Vector3 pointB) 
    /// </summary>
    /// <param name="pointA"></param>
    /// <param name="pointB"></param>
    /// <returns></returns>
    public float Dis(Vector3 pointA, Vector3 pointB)
    {
        return (pointA - pointB).sqrMagnitude;
    }




    /// <summary>
    /// 准备使用技能
    /// 处理站立和转向、以及停止移动
    /// </summary>
    private void PrepareAttack()
    {
        if (m_Target != null)
        {
            RotateToTarget(m_Target.transform.position);
        }
        StopMove();
    }

    #region 技能开始
    /// <summary>
    /// 技能开始 技能开始
    /// </summary>
    /// <param name="skillId"></param>
    private void OnSkillCase(string args)
    {
        // ★ v5 P2优化:反射路径已彻底禁用 
        //    - 伤害触发:由 AttackState.OnUpdate 通过 TriggerSegmentHit 独占
        //    - 段号自增:由 AttackState.OnUpdate 通过 normalizedTime 推进
        //    - 保留 SetSkillId(0) 仅作为防御:Animation Event 残留触发时清理 Animator 状态
        //    - 删除所有 string 解析 / GetSkill / SetSkillCaster / BeginNextSegment,消除反射开销
        // ★ v5 P13：判空已下沉到 UnitView 内部，此处不再重复判一次
        m_View.ResetSkillId();
        return;
    }
    /// <summary>
    /// 设置当前技能CD
    /// </summary>
    /// <param name="skill"></param>
    private void SetCd(Skill skill)
    {
        
    }


    /// <summary>
    /// 子弹击中目标
    /// </summary>
    /// <param name="hitTarget"></param>
    /// <param name="DamageCoeParm">动画中传递的伤害参数</param>
    /// <param name="dotBack"></param>
    /// <param name="parm"></param>
    public void OnBulletHit(GameObject hitTarget, int DamageCoeParm, bool dotBack, params object[] parm)
    {
        //Skill skill = parm[0] as Skill;
    }

   

    #endregion


    

    protected override void Update()
    {
        //驱动Buff系统（先于死亡判断：死亡当帧的Tick仍需正常结算）
        if (m_BuffComponent != null)
        {
            m_BuffComponent.Tick(Time.deltaTime);
        }

        if (IsDead)
        {
            return;
        }

        if (m_StateMachine != null)
        {
            m_StateMachine.Tick(Time.deltaTime);
        }
        else
        {
            AutoFightUpdate();
        }
    }

    /// <summary>
    /// 自动战斗Update;                                    
    /// </summary>
    protected virtual void AutoFightUpdate()
    {
        return;
    }

   
    #region 状态机桥接方法（第一阶段 FSM）

    /// <summary>
    /// 初始化状态机并注册七个状态
    /// </summary>
    private void InitStateMachine()
    {
        // ★ 2026-09-24：状态机不再接收 owner —— 它那边的 _owner 赋值后全类零读取，已连同构造参数删除。
        //   owner 只往下传给紧接着这 7 个状态；它们才是真正持有并读取 owner 的一方。
        m_StateMachine = new CombatStateMachine();
        // ★ v5 P11：每个状态在构造时注入 this —— 状态实例与本单位是 1:1 的（每单位各持一套），
        //   所以 owner 只需注入一次，不必再由状态机每帧当参数转交（详见 ICombatState 注释）。
        //   ⚠ 本方法在 Start() 里调用，此刻 MonoBehaviour 已完全初始化，传 this 安全。
        m_StateMachine.RegisterState(new IdleState(this));
        m_StateMachine.RegisterState(new MoveState(this));
        m_StateMachine.RegisterState(new AttackState(this));
        m_StateMachine.RegisterState(new StunState(this));
        m_StateMachine.RegisterState(new SilenceState(this));
        m_StateMachine.RegisterState(new KnockbackState(this));
        m_StateMachine.RegisterState(new DeadState(this));
        m_StateMachine.Start();
    }

    /// <summary>
    /// 请求切换状态（供状态实现类及外部受击事件调用）。
    ///
    /// ★ v5 P9：object context 参数已删除 —— 需要额外数据的状态改为在 OnEnter 里从 owner 读取。
    /// ⚠ 调用 Knockback 之前请先写 owner.KnockbackDuration（唯一带数据的状态切换）；
    ///   Attack 无需预写，技能由 AttackState 直接读 CurSelectedSkill。
    /// </summary>
    public bool RequestState(CombatState state)
    {
        if (m_StateMachine == null)
        {
            return false;
        }
        return m_StateMachine.ChangeState(state);
    }

    /// <summary>
    /// 一次技能释放结束的回调 —— 由 AttackState 收尾时调用
    ///   （两条结束路径：「段数打完」与「动画播完」，谁先到谁触发，且只触发一次）。
    ///
    /// ★ 基类默认是**空实现**：敌人靠「持续持有 m_CurSelectedSkill」实现连续攻击，
    ///   在这里清空会打断它的 AI 节奏，所以什么都不做。
    ///
    /// ⚠ 为什么是 public 而不是 protected：调用方 AttackState 是 CombatStateBase 的子类，
    ///   **不是**本类的子类 ⇒ protected 对它不可见（CS0122）。与状态类要用的
    ///   RequestState / NavStop / DoAutoFight 保持一致，都走 public。
    ///   玩家需要「一次按键 = 一次释放」，在 WarrorController 里 override 清空 ——
    ///   否则 AutoFightUpdate 每帧都会拿残留的技能再 TryCastSkill，表现为无限连击。
    ///
    /// 【为什么是回调而不是给 CurSelectedSkill 加 setter】
    ///   CurSelectedSkill 刻意只读 ——「写入权留在 Controller」。开 setter 等于放行
    ///   「谁都能改当前技能」，将来必定出现第二处误清空。留一个有名字的时机钩子，
    ///   调用方一眼看得出「为什么在这里改」。
    /// </summary>
    public virtual void OnSkillCastFinished()
    {
    }

    /// <summary>
    /// 停止移动（供状态实现类调用）
    /// </summary>
    public void NavStop()
    {
        if (m_NavMeshAgent != null)
        {
            m_NavMeshAgent.velocity = Vector3.zero;
            SetDestination(transform.position);
        }
        // ★ v5 P13：动画部分走 View；本方法仍留在 Controller 是因为它同时管 NavMesh，
        //   属于「运动」而非纯表现，等待后续 UnitMotor 拆分时一并迁出。
        if (m_View != null)
        {
            m_View.SetMoveAnim(false);
        }
    }

    // ★ v5 P13 删除 ResetSkillId() —— 迁往 UnitView.ResetSkillId（三个状态类的 OnEnter 改调 Owner.View）。
    //   连同 SetMoveAnim / GetCurrentAnimatorStateInfo 一起，Controller 至此**不再有一行代码触碰 Animator**，
    //   「画面改写必须经 View」这条边界才算真正落地（而不是嘴上说原则、身体还在摸 m_Animator）。

    //==================================================================================
    // ★ v4 段号驱动改造 (FSM.OnUpdate 推进方案)
    //==================================================================================

    // ★ v5 P10：原 `ResetSkillSegIdx()` 已删除。它此前只是 `m_CurSkill.ResetSegIdx()` 的中转，
    //   m_CurSkill 字段随之移除；「每轮归零段索引」的职责迁到 AttackState.OnEnter（那里本就持有
    //   _currentSkill 引用），比原先挂在 IdleState.OnEnter 上更贴合「一轮攻击开始」的语义。

    /// <summary>
    /// ★ v4 触发段号伤害 — 替代反射 OnSkillCase 的代码直调入口
    /// 由 AttackState.OnUpdate 每帧检测 normalizedTime >= SegTimings[i] 时调用
    ///   - 调 skill.SkillHit(segIdx) 走「写段号 → ImmedDamage → OnDamaged → 火花特效」链路
    ///     （★ v5 P16：段号改为**传参**，写入与结算内聚到 Skill 内部；
    ///      此前本方法先写 `skill.SegIdx = segIdx` 再调无参 `skill.SkillHit()`，把原子动作拆成了两半）
    ///   - 不重置 SegIdx,确保后续段触发时不会被错误归零
    /// </summary>
    public void TriggerSegmentHit(Skill skill, int segIdx)
    {
        if (skill == null) return;
       
        // ★ v5 P10：这里原先还有 `m_CurSkill = skill;` —— 唯一用途是让受击方事后反查段号。
        //   受击特效 id 现已由 Skill.ImmedDamage 按段算好直接传参，该字段与那行赋值一并移除。
        // ★ v5 P13：改读 View.StateInfo（输出字符串与迁移前完全一致，日志比对不会受影响）
#if UNITY_EDITOR
        // ★ 2026-09-27（方案 F · 零 GC）：Debug.Log 的字符串插值**在真机也会分配**，
        //   而本方法是段命中路径的必经入口 ⇒ 收进 UNITY_EDITOR，真机彻底不编译这一段。
        Debug.Log($"[AttackState] skill={skill.SkillId} seg={segIdx} t={(m_View != null ? m_View.StateInfo.normalizedTime : 0f):F3}");
#endif

        // ★ v5 P16：段号传参 —— 写段号与结算一并交给 Skill 内部完成，此处不再有 `skill.SegIdx = segIdx`。
        //   本方法保留下来的唯一理由就是上面那行日志：它要读本单位 View 的动画进度，而 Skill 不认识 View。
        //   职责因此变干净：编排与观测归 Controller，段号与结算归 Skill。
        skill.SkillHit(segIdx);
    }

    // ★ v5 P13 删除 GetCurrentAnimatorStateInfo() —— 迁往 UnitView.StateInfo。
    //   它原本存在的唯一理由是「m_Animator 在基类里是 protected，状态机拿不到」；
    //   现在 Controller 自己都不碰 Animator 了，这个转发 assembly 自然失去了存在意义，
    //   AttackState 改读 Owner.View.StateInfo —— 多打一个 "."，换来的是阅读时一眼看出「这是在问画面层」。

    // ★ v5 P13 删除 SetMoveAnim(bool) —— 迁往 UnitView.SetMoveAnim。
    //   调用点只有 MoveState.OnEnter 一处，改 Owner.View.SetMoveAnim(true) 即可。

    /// <summary>
    /// 执行自动战斗逻辑（供 Idle / Move / Attack / Silence 状态调用）
    /// </summary>
    public void DoAutoFight()
    {
        AutoFightUpdate();
    }

    /// <summary>
    /// 执行死亡逻辑（供 DeadState 调用）
    /// </summary>
    public void DoDead()
    {
        OnDead();
    }

    #endregion

    #region Buff状态查询（转发给Buff组件）

    /// <summary>是否存在硬控Buff（眩晕/冰冻/石化/睡眠）</summary>
    public bool HasHardControlBuff()
    {
        return m_BuffComponent != null && m_BuffComponent.HasHardControlBuff();
    }

    /// <summary>是否存在沉默Buff</summary>
    public bool HasSilenceBuff()
    {
        return m_BuffComponent != null && m_BuffComponent.HasSilenceBuff();
    }

    #endregion

}
