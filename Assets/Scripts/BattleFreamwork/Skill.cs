using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#region 技能属性相关的枚举

public enum SkillTypes
{
    NormalSkill,          //普通攻击
    AttackSkill ,          //技能攻击
    SuperSkill             //怒气大招
}

public enum SkillHitTypes
{
    //HitType: 击中类型(0:对敌方目标 1:敌方范围 2:友方目标 3:友方范围 4:自己);
    SelectedEnemy = 0,
    ScaleEnemy = 1,
    SelectedFriend = 2,
    ScaleFriend = 3,
    Self = 4
}

public enum SkillHitSharpTypes
{ //HitSharpType: 攻击范围类型(0：对目标 1：圆形 2：扇形);
    None = 0,
    Circle = 1,
    Fan = 2 ,
    Forward =3

}

public enum SkillEffectTypes
{
    ImmedDamage = 0,     //立即伤害
    ImmedHeal = 1,       //立即治疗
    AddBuff = 2,         //添加Buff
    Sneer = 3,           //嘲讽 浮空
    Rigor = 4,         //僵直
    HitOut = 5,          //浮空
    HitBack=6,             //后退持续受击 （受击后退）
    HitOutDown=7,         //受到攻击飞出去（抛物线）
    HitDown=8,    //(受击倒地)
    Dizz=9,          //眩晕
    ForwardMove=10,   //拉近
    /// <summary>
    /// 一个游戏单元持续后退受击
    /// </summary>
    OneUnitHitBack=11,
    OnSetUnitEnable=12,
    /// <summary>
    /// 女以太
    /// 暴力抓取
    /// </summary>
    ViolenceToGrab=13,
    /// <summary>
    /// 浮空受击
    /// </summary>
    AirHit=14,
    /// <summary>
    /// 倒地持续受击
    /// </summary>
    DownHit=15,
    /// <summary>
    /// UNIT 移动
    /// </summary>
    OnUnitMove=16,
    /// <summary>
    /// 移动到目标并做一次弹跳
    /// </summary>
    OnUnitMoveTargetAndJump=17,
    /// <summary>
    /// 移动到目标
    /// </summary>
    OnUnitMoveTarget=18,
    /// <summary>
    /// 单一游戏单元的僵直
    /// </summary>
    Frozen=19,
    /// <summary>
    /// 测试的旋转
    /// </summary>
    SetRotate=20,
    AirHitForPlayer=21,
    /// <summary>
    /// 受击
    /// </summary>
    Hure=22,
}

public enum CanUseSkill
{
    NO,
    YES
}

/// <summary>
/// ★ 命中投递方式（投掷物方案 P1，2026-10-10）—— 与效果列表（SkillEffectTypes）**正交**的维度。
/// 效果号回答「命中之后发生什么」；本枚举回答「命中发生在**哪、什么时候**」。
/// </summary>
public enum SkillDeliveryType
{
    /// <summary>即时（缺省）：段时机点一到、以施法者为圆心立即结算 —— 全部既有技能的行为，逐行不变</summary>
    Instant = 0,

    /// <summary>弹道：段时机点一到生成投掷物（ProjectileManager.Spawn），结算延迟到弹着时刻、原点改为弹着点</summary>
    Projectile = 1,
}

#endregion

/// <summary>
/// 技能脚本   
/// </summary>
public class Skill
{

    public int SkillId;
    private SkillData m_SkillBaseData;
    
    public SkillData BaseData
    {
        get { return m_SkillBaseData; }
    }

 public bool NormalAttackCD { get; set; }

    /// <summary>
    /// 技能属性 控制
    /// 该字段表示技能是否可以使用
    /// </summary>
    private CanUseSkill m_CanUseSkill;
    public CanUseSkill CanUseSkill { get { return m_CanUseSkill; } set { m_CanUseSkill = value; } }

   
    /// <summary>
    /// 获得一个技能的ID
    /// </summary>
    /// <param name="dataId"></param>
    public Skill(int dataId)
    {
        SkillId = dataId;
        m_SkillBaseData = ConfigManager.GetSkillData(SkillId);
      
    }

    /// <summary>
    /// 攻击者
    /// </summary>
    private CombatUnitController m_SkillCaseter = null;
    public CombatUnitController SkillCaseter { get { return m_SkillCaseter; } }
    /// <summary>
    /// 被攻击者
    /// </summary>
    private CombatUnitController m_SkillTarget = null;

    /// <summary>
    /// ★ 2026-10-10 弹道投递新增只读口：出膛时 ProjectileManager 读它锁定追踪目标。
    /// 此前 m_SkillTarget 只进不出；既有代码零引用，纯新增。
    /// ⚠ 「飞行期间该字段不会被改写」的保证来自 TryCastSkill 的未结算弹道闸门
    ///   （ProjectileManager.HasUnresolvedProjectile）：SetSkillCaster 只发生在 AttackState.OnEnter，
    ///   闸门挡住了同一 Skill 实例的再次施放 ⇒ 弹在飞期间 m_SkillTarget 恒为出膛时锁定的那个目标。
    /// </summary>
    public CombatUnitController SkillTarget { get { return m_SkillTarget; } }

    /// <summary>
    /// 当前正在播放的段索引(1-based,0=未开始)
    /// ★ v4 改造:由字段改为 property(不再走 BeginNextSegment 自增);
    ///   ★ v5 P16 起赋值口收为 private,对外只经 SkillHit(segIdx) 传段号
    /// ★ v5 P16(2026-09-18) 内聚:赋值口收紧为 **private set** —— 外部一律走 `SkillHit(segIdx)` 传段号,
    ///   段号写入与结算收进同一个方法,从**类型系统**杜绝「段号已改、结算未发生」的中间态。
    ///   (此前唯一的外部写入点是 CombatUnitController.TriggerSegmentHit 里的 `skill.SegIdx = segIdx;`)
    /// 现在的两个消费者(均为**本类内部**,不再暴露给外部反查):
    ///   ① HitTarget —— 按段取 SkillEffectTypes 的分段效果列表;
    ///   ② ImmedDamage —— 按段算出受击特效 id 传给 OnDamaged。
    /// ⚠ v5 P10 起**不再**由「受击方反查 attacker.m_CurSkill.SegIdx」使用 —— 该跨对象反查路径已删除,
    ///   段号只在本类内部流转,外部拿不到也不需要拿。
    /// 归零时机:AttackState.OnEnter 按「轮」调用 ResetSegIdx()（原先挂在 IdleState.OnEnter 上）。
    /// </summary>
    private int m_SegIdx = 0;

    private float m_LastSegTime = -100f;
    private const float SEG_TIMEOUT_SECONDS = 3f; // 兜底:3 秒无新事件强制归零(异常保护)

    /// <summary>球形索敌缓冲区容量（超出容量的目标会被 Unity **静默丢弃**，见 OverlapTargets）</summary>
    private const int TARGET_BUFFER_SIZE = 64;
#if UNITY_EDITOR
    /// <summary>方案 F 验证用：SkillCastContext 的构造次数（应与 [SegEffect] 条数相等 ⇒ 每段一次）</summary>
    private static int s_CastBuildCount;
#endif
    private static readonly Collider[] s_TargetBuffer = new Collider[TARGET_BUFFER_SIZE];
    private static bool s_BufferOverflowWarned;

    /// <summary>
    /// 球形检测：结果写入共用缓冲区，返回**实际命中数**（&lt;= TARGET_BUFFER_SIZE）。
    /// ★ 2026-09-27（方案 F）：战斗路径零 GC —— 不用 Physics.OverlapSphere（它每次 new 一个 Collider[]）。
    /// ⚠ 静默丢弃违反工程铁律 5，故容量满时一次性 LogError 指名。此提示只报一次。
    /// </summary>
    private static int OverlapTargets(Vector3 center, float radius, int layer)
    {
        int count = Physics.OverlapSphereNonAlloc(center, radius, s_TargetBuffer, layer);
        if (count >= TARGET_BUFFER_SIZE && !s_BufferOverflowWarned)
        {
            s_BufferOverflowWarned = true;
            Debug.LogError("[Skill] 球形命中缓冲区已满（容量 " + TARGET_BUFFER_SIZE
                + "，半径 " + radius + "）：超出容量的目标会被 Unity **静默丢弃**，"
                + "表现为某些单位打不到 —— 请调大 TARGET_BUFFER_SIZE。此提示只报一次。");
        }
        return count;
    }

    // ══════════════ ★ 2026-10-10 弹道投递（投掷物方案 P2）══════════════
    // 【弹着结算原点】即时结算（既有路径）不设覆盖 ⇒ 三个形状查询仍读施法者 transform，行为逐行等价；
    // 弹着结算由 SkillHitAt 写入「弹着点 + 弹道方向」，结算完立刻清除 ——
    // 「写入与结算收进同一个方法」与 P16 的段号内聚是同一条纪律：需要注释守护顺序的两步操作都该收进一个方法。

    private Vector3 m_HitOrigin;        // 弹着结算原点（仅 m_HasHitOrigin=true 时有效）
    private Vector3 m_HitDir;            // 弹着结算朝向（弹着时刻的飞行方向）
    private bool m_HasHitOrigin;        // 是否处于弹着结算（false=即时结算，读施法者）

    public int SegIdx
    {
        get { return m_SegIdx; }
        private set { m_SegIdx = value; m_LastSegTime = Time.time; }
    }

    /// <summary>
    /// 进入下一段(动画事件回调入口):超时容错后自增并返回新段号
    /// 首次调用返回 1,第二次返回 2,以此类推
    /// ⚠ v4 起已被 AttackState→TriggerSegmentHit 的「直接赋段号」取代,全工程已无调用点(仅剩本方法自身)。
    ///   正常重置职责现由 AttackState.OnEnter → ResetSegIdx() 承担,本方法仅作历史保留/异常兜底。
    public int BeginNextSegment()
    {
        if (Time.time - m_LastSegTime > SEG_TIMEOUT_SECONDS)
        {
            // 兜底:跨技能或长时间无新事件,强制归零
            m_SegIdx = 0;
        }
        m_SegIdx++;
        m_LastSegTime = Time.time;
        
        return m_SegIdx;
    }

    /// <summary>
    /// 显式重置段索引(★ v5 P10 起由 AttackState.OnEnter 在每轮攻击开始时直接调用)
    /// </summary>
    public void ResetSegIdx()
    {
        m_SegIdx = 0;
        m_LastSegTime = -100f;
    }

    public void SetSkillCaster(CombatUnitController m, CombatUnitController tar)
    {
        // ★ 2026-10-13 评审 B1：把「飞行期间 caster/target 恒定」的注释约定升级为结构性防护。
        //   该恒定是弹着结算正确性的根（在飞弹体一直追着出膛时锁定的 m_SkillTarget 打）；
        //   此前仅由「SetSkillCaster 只发生在 AttackState.OnEnter + TryCastSkill 闸门」两处注释守护，
        //   将来任何新效果 / 新入口（强制攻击、换目标类 Buff）再调一次 SetSkillCaster，
        //   在飞弹体就会静默改打新写入的目标 —— 现在当场指名，不静默（铁律 5）。
        if (ProjectileManager.HasUnresolvedProjectile(this))
        {
            Debug.LogError("[Skill] 技能 " + SkillId + " 存在未结算弹道时被 SetSkillCaster 改写 —— "
                + "在飞弹体将按新写入的 caster/target 结算。正常链路闸门（TryCastSkill）已挡住再次起手，"
                + "请检查这个新调用点是否绕过了闸门。");
        }

        m_SkillCaseter = m;
        m_SkillTarget = tar;

    }

    /// <summary>
    /// 因为特效已经从伤害段解耦 —— AttackEffects 有 5 条就能播 5 个，哪怕这个技能只有 3 段伤害。
    /// </summary>
    /// <param name="effectIndex">1-based 特效序号，取值范围 1 ~ GetCastEffectCount()</param>
    public void PlayCastEffect(int effectIndex)
    {
        if (m_SkillCaseter == null) return;

        int castEffectId = BaseData.GetCastEffectId(effectIndex);
        if (castEffectId <= 0) return;   // 该序号没有可播的特效 → 跳过，不退化到播第 1 个

#if UNITY_EDITOR
        // ★ v5 P17：原本这里写的是 Debug.LogError —— 但「正常播出一个特效」并不是错误。
        //   每次施法刷 3 条红字，会让 Console 里真正的 Error（比如资产契约缺失）被淹没，
        //   与 UnitView 契约校验定下的「告警不能被噪音埋掉」是同一条原则。降级为普通日志，
        //   前缀 [Cast] 保持不变，已有的日志回归比对脚本不受影响。
        Debug.Log($"[Cast]   特效#{effectIndex} effectId={castEffectId} t={BaseData.GetCastEffectTiming(effectIndex - 1):F3}");
#endif

        // ★ 绑点不再由 Skill 计算：EffectManager.CreateEffect 内部会拿 transform 上的 EffectDot，
        //   按特效配置的 BindName 自己找挂点（刀光挂在武器上、受击特效挂在躯干上，各不相同）。
        EffectManager.Instance.CreateEffect(castEffectId, m_SkillCaseter.transform);
    }

    /// <summary>
    /// 设置技能时间
    /// </summary>
    public void SetCD(float skillCD /*Animator anim*/)
    {
        NormalAttackCD = true;

        //TimerManager.Instance.AddTimer("Skill"/* + m_SkillCaseter.gameObject.name */+ SkillId.ToString(), skillCD, RestCD, null);
    }

    /// <summary>
    /// 击中目标
    /// </summary>
    /// <param name="target">受击者</param>
    /// <param name="skillEffectIndex"></param>
    /// <summary>
    /// ★ 2026-09-27 决策 2：由 public 收成 private —— 全工程外部零调用点（scan_refs 已确认），
    ///   命中结算的唯一对外入口是 <c>SkillHit(segIdx)</c>，与 v5 P16 的设计意图一致。
    /// </summary>
    private void HitTarget(CombatUnitController target, in SkillCastContext cast)
    {
        if (m_SkillCaseter == null || target == null)
            return;

        // ★ v5 P10：这里原先写 `m_SkillCaseter.m_CurSkill = this;`，目的是让**受击方**稍后反查
        //   `attacker.m_CurSkill.SegIdx` 来取受击特效。该反查路径已删除（受击特效 id 改由本类
        //   ImmedDamage 按段算好直接传参），因此这一行连同 m_CurSkill 字段一并移除 —— 不再需要
        //   把「当前是谁在打」写到 Controller 上给外人看。

        // ★ 按**当前段号**取该段的效果列表 —— 全技能唯一的真相源是 CSV 列 13 "SkillEffectTypes"。
        //   写法：| 分段、; 分效；没写 | 则整串只在第 1 段生效，后续段取到 null（什么都不做）。
        //   段号来源（★ v5 P16 起）：AttackState → Controller.TriggerSegmentHit → SkillHit(segIdx)，
        //   段号以参数进来，由本类自己写入 m_SegIdx 后再结算，所以此处读到的必然是本次命中所属的段。
        //   ⚠ m_SegIdx 为 0 的情形（非动画事件入口，例如 Buff Tick 调过来）按第 1 段处理，
        //      与旧版「取不到逐段配置就用共用列表」的行为一致。
        List<int> effectTypes = BaseData.GetEffectTypesForSegment(m_SegIdx);
        if (effectTypes == null || effectTypes.Count == 0)
            return;

        for (int i = 0; i < effectTypes.Count; i++)
        {
            // 未知 / 负值（如 -1 表示"该段什么都不做"）会落到 ExecuteEffect 的 default 分支，是安全的空操作
            SkillEffectTypes effectType = (SkillEffectTypes)effectTypes[i];
            ExecuteEffect(effectType, target, in cast);
        }
    }

    private void ExecuteEffect(SkillEffectTypes effectType, CombatUnitController target, in SkillCastContext cast)
    {
        // ★ 23 分支 switch 已删：效果实现全部迁往 ISkillEffect 策略类（见 SkillEffect/ 目录）。
        //   ★ 未注册的效果号 → **三态**（用户 2026-09-24 拍板，见方案 4.7）：
        //     ① 已知空桩（KnownStubIds 白名单内）→ 静默跳过，与旧 switch default 逐字等价
        //       （CSV 列 13 的 -1 / 空段依赖这一契约，不可改成报错）；
        //     ② 未知效果号 → LogError 指名，绝不静默 —— 这个数字只可能来自「CSV 配错」
        //       或「忘了 Register」，两种都是必须暴露的错误，不能伪装成"这个效果没配"。
        if (!SkillEffectRegistry.TryGet((int)effectType, out ISkillEffect effect))
        {
            if (SkillEffectRegistry.IsKnownStub((int)effectType))
            {
                return;                       // ① 已知空桩：安全空操作
            }

            Debug.LogError($"[SkillEffect] 效果号 {(int)effectType}（技能 {BaseData.ID} 第{m_SegIdx}段）未注册："
                         + "要么它是空桩请加进 KnownStubIds，要么忘了 Register —— 不能静默跳过");
            return;                           // ② 已指名报错
        }

        // ★ 2026-09-27 方案 F：不再 new 上下文 —— cast 由 SkillHitInternal 每段构造一次传进来。
        //   （目标走第二参数；struct 走接口方法不装箱 ⇒ 战斗路径零 GC）
        effect.Execute(in cast, target);
    }

    public void OnAirHitForPlayer()
    {
        if (m_SkillTarget.IsDead)
            return;
       
    }

    public void OnSetRotate()
    {
       
    }

    public void OnUnitMoveTarget()
    {
       
    }

    /// <summary>
    /// 游戏单元向目标移动并做弹跳的动作
    /// </summary>
    private void OnUnitMoveTargetAndJump()
    {
       
    }

    /// <summary>
    /// 敌人的移动
    /// </summary>
    private void OnUnitMove()
    {
       
    }

    /// <summary>
    /// 倒地持续受击
    /// </summary>
    private void OnDownHit()
    {
        
    }

    /// <summary>
    /// 暴力抓取
    /// </summary>
    private void OnViolenceToGrab()
    {
       
    }

    /// <summary>
    /// 设置敌人的消失
    /// </summary>
    private void OnSetUnitEnable()
    {
       
    }

    private void OnAirHit()
    {

    }

    /// <summary>
    /// 单个游戏单元后退持续受击
    /// </summary>
    public void OnOneUnitHitBack()
    {
       
    }

    public void OnForwardMove()
    {
        
    }

    /// <summary>
    /// ★ 取第 segIdx 段（1-based）命中时，受击方身上要播的特效 id。
    ///
    /// 【★ 2026-09-28 规则变更】受击特效来源由 CSV 列 24（1-based 第 25 列 HitEffectIds，该列已于 2026-09-28 整列删除）
    ///   改为 **列 14（1-based 第 15 列 HitEffect）** —— 全工程唯一受击特效真相源。
    ///   取值规则同步调整为三段式：
    ///     ① 没配受击特效 / 列表无效 / 段号 &lt;= 0   → 0（该段不播）
    ///     ② segIdx &lt;= 特效数                        → ids[segIdx - 1]（按下标一一对应）
    ///     ③ segIdx &gt;  特效数                       → **复用最后一个**
    ///
    /// 【规则 ③ 是本次核心】策划只配 1 个特效而技能打 3 段是常态，
    ///   三段都播同一个；配 2 个打 3 段则第 3 段沿用第 2 个。
    ///   旧规则「越界返回 0」会让「配 1 个打 3 段」变成只有第 1 段有火花、
    ///   后两段光掉血不冒光 —— 看起来像特效丢失，排查成本极高。
    ///   复用末尾是显式的、可预期的行为：配 1 个就等于「所有段都用这个」。
    ///
    /// 【约束】特效数不得大于生效段数（多配的永远没有段可以播 = 配置错误），
    ///   由 <c>SkillData.ValidateHitEffectCount</c> 在加载期一次性 LogError 指名。
    /// </summary>
    public int GetHitEffectIdForSegment(int segIdx)
    {
        // 取值走封装：空列会被 Str2IntList("") 解析成含一个 0 的 List，直接用会把「没配」当「配了 1 个」
        List<int> ids = BaseData.GetHitEffectIdList();
        if (ids == null || ids.Count == 0 || segIdx <= 0)
            return 0;

        // ② 段号落在特效表范围内 → 按下标一一对应
        if (segIdx <= ids.Count)
        {
            int id = ids[segIdx - 1];
            return id > 0 ? id : 0;
        }

        // ③ 段数多于特效数 → 后续段一律复用最后一个（只配 1 个时即「所有段都播它」）
        int last = ids[ids.Count - 1];
        return last > 0 ? last : 0;
    }

    #region 获取技能的单次伤害
    /// <summary>
    /// 获取技能单次伤害
    /// </summary>
    /// <param name="attactker"></param>
    /// <param name="_DamageCoe"></param>
    /// <returns></returns>
    public int GetSkillOneDamage()
    {
       
        
            int hp = GetSkillDamage();

           
            //随机暴击成功率。
            //if (CommonHelper.OnProbability(BaseData.Crit))
            if (CommonHelper.RollCrit(BaseData.Crit))
            {
                hp = (int)Math.Round(hp * 1.5f);
            }
            //return (int)Math.Round(hp / _DamageCoe);
            return hp;

    }

    /// <summary>
    /// 获取该技能总伤害
    /// </summary>
    /// <returns></returns>
    private int GetSkillDamage()
    {
        if (m_SkillCaseter == null || m_SkillTarget == null)
            return 0;
        //穿透加成
        float extraDamagePercent = (100 + m_SkillCaseter.CurCombatUnit.Penetrate / 400.0f - m_SkillTarget.CurCombatUnit.Parry / 400.0f) / 100f;
        // 防御系数
        float defPercent = (12000 + m_SkillTarget.CurCombatUnit.Defend) / (12000 + m_SkillTarget.CurCombatUnit.Defend * 10.0f);
        //最终伤害
        int atk = (int)Math.Max(Math.Round((m_SkillCaseter.CurCombatUnit.Damage * defPercent * extraDamagePercent) * BaseData.Attack), 1);
       
        return atk;
    }

   

    #endregion

    /// <summary>
    /// 治疗
    /// </summary>
    public void ImmedHeal()
    {
        if (m_SkillTarget == null)
        {
            return;
        }
        //治疗量=受疗者攻击力×配置系数（Para4），治疗特效ID=Para5
        //OnHeal内部：Hp取MaxHp上限 + 播放治疗特效
        //int hp = (int)(unit.CurCombatUnit.Damage * BaseData.Para4);
        //unit.OnHeal(attacker, hp, (int)BaseData.Para5);`
    }

    /// <summary>
    /// 僵直后倒地
    /// </summary>
    public void Rigor()
    {
        
    }

    /// <summary>
    /// 单一的僵直动作
    /// </summary>
    public void OnUnitRigor()
    {
       
    }

    /// <summary>
    /// 后退受击
    /// </summary>
    public void HitBack()
    {
       
    }

    public void HitOutDown()
    {
      
       
       
    }

    /// <summary>
    /// 击飞(浮空)
    /// </summary>
    public void HitOut()
    {
       
    }

    /// <summary>
    /// 击倒
    /// </summary>
    public void OnHitDown()
    {
       
    }
    /// <summary>
    /// 眩晕
    /// </summary>
   public void OnDizz()
    {
        
    }

    /// <summary>
    /// ★ v5 P16（2026-09-18）：段命中的**唯一对外入口** —— 段号作为参数传入，写入与结算在同一方法内完成。
    ///
    /// 【为什么把 `skill.SegIdx = segIdx` 收进来】
    ///   改造前是「外部先赋段号、再调无参 SkillHit()」两步，一个原子动作被拆在两个类里，
    ///   只能靠注释反复声明「顺序不能颠倒」（Controller / AttackState / Skill 四处都写了这条）。
    ///   需要注释守护的执行顺序，本质是内聚不足；现在段号进参数，顺序由方法体一次性保证。
    ///   ⚠ 顺带把 `SegIdx` 的 setter 收成 private —— 外部再也无法把技能置于「段号已改、结算没发生」的中间态。
    ///
    /// 【与出手特效那条线的对称性】
    ///   P8 已把「外部写 SegIdx + 无参 PlayCastEffect()」改成显式传参 `PlayCastEffect(index)`；
    ///   P10 把受击特效 id 改成按段算好传参。此处补齐最后一块：入口段号也走传参。
    /// </summary>
    /// <param name="segIdx">本次命中的段号（1-based）；≤0 视为无效，直接不结算</param>
    public void SkillHit(int segIdx)
    {
        // 段号守卫内移：以前这层判断散在调用方，现在「无效段号不结算」是 Skill 自己的不变量
        if (segIdx <= 0) return;

        // ★ 2026-10-10 弹道分流（投掷物方案 P2）：DeliveryType 是与效果列表正交的投递维度 ——
        //   只改「结算发生在哪、什么时候」，效果列表 / 受击特效 / 伤害公式全部不动；
        //   弹着时经 SkillHitAt 原样走回下方即时路径（同一套 SkillHitInternal）。
        //   ⚠ 弹道路径**刻意不写 SegIdx**：结算被推迟到弹着时刻，此刻写段号会复活 P16 刚消灭的
        //     「段号已改、结算未发生」中间态 —— 段号只在弹着时刻写（见 SkillHitAt）。
        if (BaseData.DeliveryType == SkillDeliveryType.Projectile)
        {
            ProjectileManager.Spawn(this, segIdx);
            return;
        }

        SegIdx = segIdx;      // 写段号（private setter，顺带刷新 m_LastSegTime）
        SkillHitInternal();   // 结算
    }

    /// <summary>
    /// ★ 2026-10-10 弹道命中的结算入口（投掷物方案 P2）—— 由 Projectile 命中回调独占。
    /// 与 SkillHit(P16) 同款原子化：「写段号 + 写弹着原点 + 结算」收进同一个方法，
    /// 从类型系统杜绝「弹着结算读到施法者坐标」或「段号已改、结算未发生」的中间态。
    /// 结算完立刻清除覆盖：三个形状查询在无覆盖时仍读施法者 transform —— 即时路径行为与改造前逐行等价。
    /// </summary>
    /// <param name="segIdx">本次命中的段号（1-based，出膛时登记的那个）</param>
    /// <param name="origin">弹着点（世界坐标）</param>
    /// <param name="dir">弹着时刻的飞行方向（水平单位向量）</param>
    public void SkillHitAt(int segIdx, Vector3 origin, Vector3 dir)
    {
        if (segIdx <= 0) return;

        SegIdx = segIdx;          // 弹着时刻才写段号 —— 延续 P16「写入与结算内聚」
        m_HitOrigin = origin;      // 写弹着原点
        m_HitDir = dir;
        m_HasHitOrigin = true;
        try
        {
            SkillHitInternal();   // 弹着原点生效，原样走既有结算
        }
        finally
        {
            m_HasHitOrigin = false;   // 清除覆盖：下一次即时结算回到「读施法者」的原路径
        }
    }

    /// <summary>
    /// 技能攻击（结算本体）
    /// ⚠ 本方法读 m_SegIdx 取本段效果与受击特效 id —— **不直接调用**，一律经 <c>SkillHit(int segIdx)</c>。
    /// </summary>
    private void SkillHitInternal()
    {
        //判断攻击者如果为空则跳过
        if (m_SkillCaseter == null)
        {
            return;    
        }

#if UNITY_EDITOR
        // ★ 诊断：打印本段实际采用的效果列表，便于核对分段配置是否生效
        //   （放在索敌之前，即使本段一个目标都没打到也能看到解析结果）
        List<int> dbgTypes = BaseData.GetEffectTypesForSegment(m_SegIdx);
        string dbgStr = (dbgTypes == null || dbgTypes.Count == 0) ? "(空)" : string.Join(",", dbgTypes);
        Debug.Log($"[SegEffect] 技能{BaseData.ID} 第{m_SegIdx}段 效果=[{dbgStr}]");
#endif

       //判断当前这个技能的攻击类型是对敌人还是对友军
        // ★ 2026-09-27（方案 F）：段级施法上下文 —— 本段内恒定不变，全段**只构造这一次**；
        //   命中目标变化频率最高，走 Execute 的第二参数，不进本结构。
        // ★ 2026-09-28：Caster / BaseData / SegIdx 不再由此处逐个传入 —— 它们是本类自己的东西，
        //   SkillCastContext 内部从 Skill 派生（SegIdx 在构造瞬间取快照），真相源只有一个。
        var cast = new SkillCastContext(this);
#if UNITY_EDITOR
        // ★ 验证用（Editor only）：cast 是 struct，构造不进堆；这里只数**次数**，
        //   用来实锤「每段只构造一次」—— 条数应等于 [SegEffect]。
        s_CastBuildCount++;
        Debug.Log($"[CastCtx] 构造#{s_CastBuildCount} 技能{BaseData.ID} 第{m_SegIdx}段");
#endif

        switch (BaseData. HitType)
        {
            //如果当前的技能类型是对敌人和玩家或者自身（单一伤害）
            case SkillHitTypes.SelectedEnemy:
            case SkillHitTypes.SelectedFriend:
            case SkillHitTypes.Self:
                {
                    HitTarget(m_SkillTarget, in cast);
                }
                break;
            case SkillHitTypes.ScaleEnemy:
            case SkillHitTypes.ScaleFriend:
                {   
                    //当前的技能是是圆形、扇形、还是正前方
                    switch (BaseData.SkillHitSharpType)
                    {
                        
                        case SkillHitSharpTypes.Circle:
                            HitTargetsInCircle(in cast); 
                            break;
                        case SkillHitSharpTypes.Fan:
                            HitTargetsInFan(in cast);
                            break;
                        case SkillHitSharpTypes.Forward:
                            HitForwardInEnemy(in cast);
                            break;
                        default:
                            break;
                    }
                }
                break;
           
        }
    }
    /// <summary>
    /// 查询圆形范围
    /// </summary>
    /// <param name="attacker"></param>
    private void HitTargetsInCircle(in SkillCastContext cast)
    {
        int count = 0;
        // ★ 2026-09-27（方案 F）：NonAlloc + 共用静态缓冲区 —— 战斗路径零 GC。
        //   ⚠ 必须按返回的命中数遍历，按缓冲区 Length 走会把上一次的陈旧对象当成命中。
        // ★ 2026-10-10 弹道投递：检测中心改读「结算原点来源」—— 即时结算（无覆盖）= 施法者位置，
        //   行为与改造前逐行等价；弹着结算 = 弹着点。
        Vector3 center = m_HasHitOrigin ? m_HitOrigin : m_SkillCaseter.transform.position;
        int hitCount = OverlapTargets(center, BaseData.AttackRadius + 0.1f, TargetLayerMask);
        for (int i = 0; i < hitCount; i++)
        {
            Collider c = s_TargetBuffer[i];
            CombatUnitController unit = c.GetComponent<CombatUnitController>();
            if (unit != null)
            {
                if (BaseData. HitNum > -1)
                {
                    if (count >= BaseData.HitNum)
                    {
                        break;
                    }
                }
                HitTarget(unit, in cast);
                count++;
            }
        }
    }

    /// <summary>
    /// 查询扇形范围目标
    /// </summary>
    /// <param name="attacker"></param>
    private void HitTargetsInFan(in SkillCastContext cast)
    {
        int count = 0;
       
        // ★ 2026-10-10 弹道投递：检测中心/朝向改读「结算原点来源」—— 即时结算（无覆盖）= 施法者位置/朝向，
        //   行为与改造前逐行等价；弹着结算 = 弹着点/飞行方向。
        Vector3 origin = m_HasHitOrigin ? m_HitOrigin : m_SkillCaseter.transform.position;
        Vector3 dir = m_HasHitOrigin ? m_HitDir : m_SkillCaseter.transform.forward;
        //物理判断，以攻击者为中心，当前技能配置的范围进行检测
        // ★ 2026-09-27（方案 F）：NonAlloc + 共用静态缓冲区 —— 战斗路径零 GC。
        //   ⚠ 必须按返回的命中数遍历，按缓冲区 Length 走会把上一次的陈旧对象当成命中。
        int hitCount = OverlapTargets(origin, BaseData.AttackRadius + 0.1f, TargetLayerMask);
        if (hitCount <= 0)
        {
            return;
        }

        for (int i = 0; i < hitCount; i++)
        {
            Collider c = s_TargetBuffer[i];
            CombatUnitController unit = c.GetComponent<CombatUnitController>();
            
            if (unit != null)
            {
                Vector3 testDir = c.transform.position - origin;
                testDir.y = 0;
                if (testDir == Vector3.zero)
                {        
                    continue;
                }
                testDir.Normalize();
                //点乘，判断目标在什么位置，返回值如果大于0，则表示目标在攻击者前方
                float dotProduct = Vector3.Dot(testDir, dir);
                if (dotProduct > 0)
                {
                    //获得角度
                   // float angleCos = Mathf.Cos(AttackAngel / 2.0f);
                    float angleCos = Mathf.Cos(BaseData.AttackAngle);
                    if (dotProduct >= angleCos)
                    {
                        if (BaseData.HitNum > -1)
                        {
                            if (count >= BaseData.HitNum)//判断当前需要攻击目标的数量大于等于配置文件中配置的数量则跳过
                            {
                                break;
                            }
                        }
                        HitTarget(unit, in cast);
                        count++;
                    }
                }
            }
        }
    }

    private void HitForwardInEnemy(in SkillCastContext cast)
    {
        // ★ 2026-10-10 弹道投递：即时结算（无覆盖）保留原实现**逐行不动** —— InverseTransformPoint
        //   依赖施法者的完整旋转，等价改写风险大于收益；弹着结算没有施法者旋转可用，
        //   走「原点 + 前向投影」的等价计算（局部 z 分量 = 到原点向量在前向上的点积）。
        if (!m_HasHitOrigin)
        {
            // ★ 2026-09-27（方案 F）：NonAlloc + 共用静态缓冲区 —— 战斗路径零 GC。
            //   ⚠ 必须按返回的命中数遍历，按缓冲区 Length 走会把上一次的陈旧对象当成命中。
            int hitCount = OverlapTargets(m_SkillCaseter.transform.position, 20 + 0.1f, TargetLayerMask);
            for (int i = 0; i < hitCount; i++)
            {
                CombatUnitController enemy = s_TargetBuffer[i].GetComponent<CombatUnitController>();
                Vector3 pos = m_SkillCaseter.transform.InverseTransformPoint(enemy.transform.position);
                if (pos.z > -0.3f)
                {
                    //获取玩家与主角之间的向量
                    float distance = Vector3.Distance(Vector3.zero, pos);
                    if (distance < BaseData.AttackDist)
                    {
                        HitTarget(enemy, in cast);
                    }
                }
            }
            return;
        }

        // ── 弹着结算路径：检测中心 = 弹着点，前向 = 弹道方向（与上面的即时分支语义等价）──
        int impactCount = OverlapTargets(m_HitOrigin, 20 + 0.1f, TargetLayerMask);
        for (int i = 0; i < impactCount; i++)
        {
            CombatUnitController enemy = s_TargetBuffer[i].GetComponent<CombatUnitController>();
            Vector3 pos = s_TargetBuffer[i].transform.position - m_HitOrigin;
            float forwardDist = Vector3.Dot(pos, m_HitDir);   // 等价于局部空间的 z 分量
            if (forwardDist > -0.3f)
            {
                //获取弹着点与目标之间的向量
                float distance = pos.magnitude;
                if (distance < BaseData.AttackDist)
                {
                    HitTarget(enemy, in cast);
                }
            }
        }
    }

    /// <summary>
    /// 根据技能选择目标层
    /// </summary>
    public int TargetLayerMask
    {
        get
        {
            if (m_SkillCaseter == null)
            {
                return 0;
            }
            CombatUnitController controller = m_SkillCaseter.GetComponent<CombatUnitController>();
            if (controller == null)
            {
                return 0;
            }
            if (BaseData.HitType == SkillHitTypes.ScaleEnemy || BaseData.HitType == SkillHitTypes.SelectedEnemy)
            {
                return controller.EnemyLayer;
            }
            else if (BaseData.HitType == SkillHitTypes.ScaleFriend || BaseData.HitType == SkillHitTypes.SelectedFriend)
            {
                return controller.FriendLayer;    
            }
            return 0;
        }
    }

    /// <summary>
    /// 根据技能选择目标Tag
    /// </summary>
    public string TargetTag
    {
        
        get
        {
            if (m_SkillCaseter == null)
            {
                return null;
            }
            CombatUnitController controller = m_SkillCaseter.GetComponent<CombatUnitController>();
            if (controller == null)
            {
                return null;
            }
            if (BaseData.HitType == SkillHitTypes.ScaleEnemy || BaseData.HitType == SkillHitTypes.SelectedEnemy)
            {
                return controller.EnemyTag;
            }
            else if (BaseData.HitType == SkillHitTypes.ScaleFriend || BaseData.HitType == SkillHitTypes.SelectedFriend)
            {
                return controller.FriendTag;
            }
            return null;
        }
    }
}
