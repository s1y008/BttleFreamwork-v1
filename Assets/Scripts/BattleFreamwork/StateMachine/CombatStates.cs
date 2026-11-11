using UnityEngine;

#region 待机状态

/// <summary>待机状态：正常战斗决策（索敌 → 追击 → 攻击）</summary>
public class IdleState : CombatStateBase
{
    public IdleState(CombatUnitController owner) : base(owner) { }

    public override CombatState StateType { get { return CombatState.Idle; } }

    public override void OnEnter()
    {
        Owner.NavStop();
        // ★ v5 P13：动画改写已迁往 UnitView，状态类一律经 Owner.View 操作画面
        Owner.View.ResetSkillId();
        // ★ v5 P10：段索引归零已迁到 AttackState.OnEnter（在每轮攻击**开始时**归零），
        //   原先的 Owner.ResetSkillSegIdx() 随 Controller.m_CurSkill 字段一并移除，故此处不再调用。
    }

    public override void OnUpdate(float deltaTime)
    {
        Owner.DoAutoFight();
    }
}

#endregion

#region 移动、追击状态
/// <summary>移动 / 追击状态</summary>
public class MoveState : CombatStateBase
{
    public MoveState(CombatUnitController owner) : base(owner) { }

    public override CombatState StateType { get { return CombatState.Move; } }

    public override void OnEnter()
    {
        // 移动动画归属 UnitView；Owner.SetMoveAnim 已在 v5 P13 移除
        Owner.View.SetMoveAnim(true);
    }

    public override void OnUpdate(float deltaTime)
    {
        Owner.DoAutoFight();
    }
}


#endregion

#region 攻击施法状态

/// <summary>攻击 / 施法状态 — ★ v4 改造:FSM.OnUpdate 推进段号</summary>
public class AttackState : CombatStateBase
{
    /// ★ 施法时钟 —— 状态同步改造的第一个可替换边界（2026-09-28）。
    ///   默认注入 <c>AnimatorCastClock</c>（进度读本机 Animator），与改造前**逐行等价**；
    ///   联网 / 本地服务器模式改传 <c>LogicCastClock</c>，本文件的逻辑一行都不用动。
    ///   详见 <c>Net/CastClock.cs</c>。
    private readonly ICastClock _clock;

    public AttackState(CombatUnitController owner, ICastClock clock = null) : base(owner)
    {
        _clock = clock ?? new AnimatorCastClock(owner);
    }

    // ══════════════════ 字段区 · 阅读须知（讲给学生听的两条铁律）══════════════════
    // 本状态类的实例由 CombatUnitController.Start() → InitStateMachine() 创建，
    // 并在构造时注入 Owner（★ v5 P11 起 owner 走构造注入，不再由状态机每帧当参数传）。
    // 之后每次 RequestState(CombatState.Attack) 都是**复用同一个实例**，不会 new。
    //   → 铁律①：跨帧要记住的数据必须存成「字段」。写在方法里的局部变量一帧结束就没了，
    //             所以段号推进、阶段标记这些需要跨帧累积的东西只能是字段。
    //   → 铁律②：所有字段必须在 OnEnter 开头逐个重置。实例复用意味着上一个技能的残留
    //             会污染下一次施法 —— 例如上次打完 3 段后 _firedSegIdx=3，
    //             若 OnEnter 不归零，新技能会因为「已触发数 = 总段数」而**一段都不触发**。
    // ═══════════════════════════════════════════════════════════════════════════

  
    /// <summary>
    /// 攻击内部的两个子阶段（★ v5 P3 改造产物，唯一存放位置）
  /// 改造后：一维枚举明确表达「我当前在哪一步」，将来新增子阶段只需加一个枚举值。
    /// enum 默认底层类型是 int，字段实际只占 4 字节。
    /// </summary>
    private enum AttackPhase
    {
        WaitingForLeave,  // 刚进 Attack，还在等 Animator 真正离开 Idle（状态过渡的 1~2 帧延迟期）
        Playing           // 技能动画播放中，逐帧推进伤害段号
    }

    /// <summary>
    /// 当前处于哪个子阶段 —— 整个 AttackState 的流程主开关。
    /// 写：OnEnter 重置为 WaitingForLeave；OnUpdate 阶段一处推进成 Playing；OnExit 再次重置。
    /// 读：OnUpdate 阶段一处，唯一读取点（决定本帧是「继续等动画切走」还是「推进技能」）。
    /// 为什么显式写默认值：enum 的默认值确实是第一项，但写出来可以防止有人调整枚举顺序导致语义漂移。
    ///
    /// ⚠ 历史事故（2026-09-19 修复）：本字段连同下面的 _elapsed 一度是**纯死代码** ——
    ///   只有赋值、OnUpdate 从不读。后果是「等动画真正离开待机再开始」这道守卫彻底失效，
    ///   攻击能否放出全靠「Animator 过渡恰好是 0 帧」这个巧合（详见 OnUpdate 阶段一的注释）。
    /// </summary>
    private AttackPhase _phase = AttackPhase.WaitingForLeave;


    /// <summary>
    /// 本次正在释放的技能实例（注意是 Skill 对象，不是技能 Id）。
    /// ★ v5 P9 来源：OnEnter 直接读 Owner.CurSelectedSkill（原为 context 传 int skillId，再 GetSkill 线性查表）。
    /// 用途：OnUpdate 里①读它的 BaseData.SegTimings 取时机表；②作为 TriggerSegmentHit / PlayCastEffect 的调用主体。
    /// ⚠ 可能为 null：切状态那一刻 Owner.CurSelectedSkill 为空（决策流程提前 return / 已被置 null）时，
    ///   整个段推进 / 刀光逻辑会全部静默跳过 —— 动画照播但一段伤害都不触发。
    ///   v5 P9 起 OnEnter 会为此打一条 LogWarning，不再是无声失败。
    /// 释放：OnExit 置 null，切断引用让 Skill 对象可被 GC 回收，避免长期持有造成驻留。
    /// </summary>
    private Skill _currentSkill;

    /// <summary>
    /// 已触发的段数 —— 全类最容易看错的一个字段，因为它有「双重身份」：
    ///   · 作为「数量」：== 2 表示已经打完 2 段；
    ///   · 作为「下标」：恰好等于**下一段**在 _segTimings 里的位置（打完 2 段 → 下一段是 [2]）；
    ///   · 自增之后又变成 1-based 的「段号」，直接传给 TriggerSegmentHit(skill, _firedSegIdx)。
    /// 因此 0 表示一段未触发，合法取值范围是 0 ~ _segCount（含）。
    /// 写：OnEnter 归零；OnUpdate ⑮ 处自增。
    /// 读：OnUpdate ⑬ 的条件判断、⑭ 的数组下标、⑯ 的传参，共三处。
    /// </summary>
    private int _firedSegIdx;

    /// <summary>
    /// 本次技能的总段数 —— ★ v5 P6 修正：取值 = BaseData.GetSegmentCount()，而**不是** SegTimings.Length。
    /// 【为什么不能再用 SegTimings.Length】那是把「时机点个数」当「伤害段数」用，两者本就不等价：
    ///   · 时机表是 float 列表，多敲一个分号就多一个元素，段数会被一次手滑悄悄改掉且不报错；
    ///   · 段数是玩法数值（策划定：这一刀 1 段还是 3 段），时机表是表现参数（美术定：卡在动画第几帧），
    ///     混在一列意味着改表现会误伤玩法；
    ///   · 现在特效已有独立时间轴，各条时间轴的条目数本就允许互不相等，更不该互相充当长度来源。
    ///   于是 CSV 新增了 int 列 SegCount 专门表达段数，时机表回归它本职的「在什么时候结算」。
    ///
    /// 【例】10003 三连斩：SegCount=3 + SegTimings="0.3;0.6;0.9" → 3 段，分别落在 30% / 60% / 90%。
    /// 【取 0 的含义】该技能不分段（或段数/时机任一没配），段推进整体不工作 —— 与旧版行为一致。
    /// 【实际生效值】GetSegmentCount() 内部取 min(SegCount, SegTimings.Length)，
    ///   保证段数永远不会越过时机点个数去访问 _segTimings[_firedSegIdx]（否则越界崩溃）。
    /// 为什么单独存一份而不是每帧去问：省一次方法调用，且能在条件里安全短路。
    /// </summary>
    private int _segCount;

    /// <summary>
    /// 段时机表：每个元素是该段伤害对应的动画归一化进度，取值 0~1，由 CSV 的 SegTimings 列解析而来。
    /// 例：10003 = [0.3, 0.6, 0.9]，表示动画播到 30% / 60% / 90% 时各结算一段伤害。
    /// ⚠ 这是**引用**，指向配置数据（Skill.BaseData）里的数组，多个单位、多次施法共用同一份。
    ///   本类只允许读，**绝对不能写** —— 一旦改动就是改了配置，会影响战场上所有用这个技能的单位。
    /// </summary>
    private float[] _segTimings;

    /// <summary>
    /// ★ v5 P4 改造 + ★ v5 P5 语义升级：出手特效轨道的下标游标（已播出几个特效）。
    ///
    /// 【P4 为什么从 bool 改成游标】旧版是 bool _castFired「整个技能只播一次」，
    /// 于是三段技能只有第一段有拖尾，后两段都是光秃秃的挥砍。改成游标后可以逐个播出多个特效。
    ///
    /// 【P5 为什么不再叫 _castFiredSegIdx】因为它已经**与「段」脱钩**：
    ///   旧语义 = 已播过刀光的「段」数，上限被卡在 _segCount，所以特效数量被迫等于伤害段数；
    ///   新语义 = 已播出的「特效」数，上限是 GetCastEffectCount()，
    ///          与伤害段数无关 —— 3 段伤害配 5 个特效、或 1 个特效配任意时间点，都能表达。
    /// 于是它与伤害用的 _firedSegIdx 形成**两条完全独立的时间轴**，各自按自己的节奏前进。
    ///
    /// 【取值】0 ~ _castCount，语义与 _firedSegIdx 一致：既是「已播数量」又是「下一个待播下标」。
    /// 【写】OnEnter 归零；OnUpdate 特效推进处自增。
    /// </summary>
    private int _castCursor;

    /// <summary>
    /// ★ v5 P5 新增：本次技能总共要播几个出手特效，= BaseData.GetCastEffectCount()（v5 P8 从 Skill 下沉到 SkillData）。
    /// 单独缓存一份而不是每帧去问（省一次 List 访问 + null 判断）。
    /// ⚠ 它可能小于、等于或大于伤害段数 _segCount —— 这正是「特效与段解耦」的体现。
    /// ⚠ 它的值是 min(特效 id 数, 时机数)：
    ///    · CSV 没 CastEffectTimings → 必为 0 → 整个特效轨道不播（这是刻意设计，见 GetCastEffectCount 注释）
    ///    · id 与时机数量不等 → 多出来的那些条目被丢弃，加载时会有 [SkillData] 警告提示
    /// </summary>
    private int _castCount;

    // ══════════════ ★ v5 P17 出手特效「掉帧体检」专用字段（2026-09-19）══════════════
    // 起因：上报「第二次攻击时前两段特效同时播出」。探针实测证实是**单帧长卡顿**所致 ——
    //   进入攻击后某帧 normalizedTime 从 0.13 直接跳到 0.35，一次性跨过 0.14 / 0.33 两个时机点，
    //   而旧实现用 while 在同一帧把它们全部补播出来 ⇒ 两个刀光喷在同一瞬间。
    // 修复：推进改为 if（每帧最多播一个，其余顺延下一帧，不漏播），
    //   同时用下面三个字段量化「本帧到底跳了多少」，让卡顿可被观测而不是默默出错。

    /// <summary>
    /// 判定「出手特效晚点」的阈值（归一化进度，0.02 ≈ 60fps 下的 3~4 帧）。
    /// 用途：准时播出时的超出量只有千分之几，一旦被单帧跨越就会成倍突破这个下限。
    /// ⚠ 必须是**绝对阈值** —— 详见 OnUpdate ⑰ 处记录的踩坑：拿本帧步长做倍数比较会恒假。
    /// </summary>
    private const float CAST_LAG_MIN_RATIO = 0.02f;

    /// <summary>
    /// 上一帧读到的动画归一化进度。负值是哨兵，表示「本轮还没有有效值」。
    /// 唯一用途：与当前帧相减得到本帧真实推进量。
    /// </summary>
    private float _prevNormTime = -1f;

    /// <summary>本帧动画的推进量（归一化）。状态刚切换的那一帧记为 0（见赋值的注释）。</summary>
    private float _animFrameStep;

    /// <summary>本次施法是否已报过延迟 —— 每次施法最多一条告警，避免掉一次帧刷一屏。</summary>
    private bool _castLagWarned;

    // ══════════════ ★ 2026-09-29 收尾标志（修复「一次按键 = 无限连击」）══════════════
    //   起因：技能选中后 m_CurSelectedSkill 常驻非空，AutoFightUpdate 每帧拿它再 TryCastSkill，
    //        表现为「按一次 A 就一直打」。收尾时清空它即可，但收尾本身有两条独立路径，
    //        需要两个标志分别表达「谁到了」和「是否已经收过」

    /// <summary>
    /// 伤害段是否已全部打完（_firedSegIdx 触及 _segCount）。
    ///   段打完即可收招，不必等 Animator 把收招动画播完 —— 玩家诉求：打完最后一段就能再出手。
    ///   ⚠ _segCount == 0 的技能（全表 233 个）永远置不了 true，靠「动画播完」那条路兜底。
    /// </summary>
    private bool _segmentsDone;

    /// <summary>
    /// 本次施法是否已完成收尾（OnSkillCastFinished + 切 Idle）。
    ///   两条结束路径谁先到谁触发，本标志保证收尾**只做一次**（否则回调会被调用两遍）。
    /// </summary>
    private bool _castFinished;

    public override CombatState StateType { get { return CombatState.Attack; } }

    public override void OnEnter()
    {
        // SetInteger("SkillId", ...) 到 Animator 真正过渡之间有一两帧仍停在待机,
        // 必须等它先切走、再切回来,否则进状态的当帧就会被误判成"已播完"
        _clock.Reset();

        _phase = AttackPhase.WaitingForLeave;
        _firedSegIdx = 0;
        _castCursor = 0;
        _castCount = 0;
        _currentSkill = null;
        _segCount = 0;
        _segTimings = null;
        _segmentsDone = false;
        _castFinished = false;

      
        // ★ v5 P9：技能改从 Owner 直接取（原为 context 传 int skillId，再 GetSkill 线性查表）。
        //   为什么查表多余：Owner.m_CurSelectedSkill 本身就出自 CurCombatUnit.m_SkillList，
        //   所以 GetSkill(m_CurSelectedSkill.SkillId) 返回的是**同一个实例** —— 把引用降级成 int
        //   再遍历捞回来纯属白跑（还多引入一次装箱）。现在一步到位，引用相等，零查找零分配。
        //   ⚠ 时序前提：本方法由 ChangeState **同步**调用，此刻 m_CurSelectedSkill 仍是
        //     本帧刚选中、即将释放的那个技能（Controller.StarSKill 内 SetInteger 后立即切状态）。
        _currentSkill = Owner.CurSelectedSkill;

        if (_currentSkill != null)
        {
            _currentSkill.SetSkillCaster(Owner, Owner.m_Target);

            // ★ v5 P10：段索引按「轮」归零迁到这里 —— 原先挂在 IdleState.OnEnter 上
            //   （经由 Controller.ResetSkillSegIdx → m_CurSkill.ResetSegIdx）。迁过来有两个好处：
            //   ① 贴合语义：归零点在「一轮攻击开始」而不是「回到待机」；
            //   ② 不再需要在 Controller 上保留一个技能引用专门用来中转。
            //   ⚠ 这是**双保险**：每次命中时 `SkillHit(segIdx)` 都会用传进来的段号覆盖 m_SegIdx，
            //     此处归零是为了防止将来出现「不经 SkillHit 的结算路径」取到上一轮的残留段号。
            _currentSkill.ResetSegIdx();

            // ★ v5 P6：段数改从 CSV 的 SegCount 列读取（策划显式配置），
            //   GetSegmentCount() 内部已取 min(SegCount, SegTimings.Length) 兜底，越界风险为零。
            _segCount = _currentSkill.BaseData.GetSegmentCount();

            _segTimings = _currentSkill.BaseData.SegTimings;

            // ★ v5 P5:出手特效的条目数独立于段数统计(可能 3 段配 5 个特效,也可能一个都不配)
            _castCount = _currentSkill.BaseData.GetCastEffectCount();
        }
       
    }

    // ★ 本方法是 AttackState 的心脏：整个技能释放过程中，它被主循环 Update() 每帧调用一次
    //    Owner      = 基类字段(构造注入); 状态机所属的战斗单位(施法者自己)
    //    deltaTime  = 上一帧到本帧的秒数（用于保险丝计时）
    public override void OnUpdate(float deltaTime)
    {
        // 逻辑时钟按帧累加进度（AnimatorCastClock 是空实现 ⇒ 单机零影响）
        _clock.Tick(deltaTime);

        // ① 起手守卫：等 Animator 真正离开待机，才开始推进本轮技能（实现见 TryEnterPlaying）
        //    返回 false = 本帧到此为止，调用方必须立刻 return。
        if (!TryEnterPlaying(deltaTime))
            return;

        // ② 结束判定：动画已播完、Animator 自动回到待机 ⇒ 本次攻击结束（实现见 TryFinishSkill）
        //    返回 true = 已请求切回 Idle，调用方必须立刻 return。
        if (TryFinishSkill())
            return;

        // ⑫ ★ v5 P1 性能优化点：整个 Playing 分支每帧只取一次进度值并复用（改造后仍是每帧一次）
        //    GetCurrentAnimatorStateInfo() 是一次 C# → Native(C++) 跨语言调用，
        //    单次开销虽小（约 0.01ms）但每帧都跑；改造前「段号推进」和「刀光」各处调一次，
        //    等于每帧 2 次 native call，现在合并为 1 次，36 帧的技能全程省下约 0.7ms

        // ★ 2026-09-28（状态同步 S1）：进度改由 ICastClock 提供，不再直接读 Animator。
        //   单机下 AnimatorCastClock.GetProgress() 与原先的 info.normalizedTime 逐值等价。
        float progress = _clock.GetProgress();


        // ③ 出手特效轨道（实现见 AdvanceCastEffect）
        //    ⚠ 必须排在伤害之前：同一帧若同时跨过特效点和伤害点（严重掉帧），
        //      先出特效再结算伤害，才不会出现「先见血量掉、后见特效」的倒挂观感。
        AdvanceCastEffect(progress);

        // ④ 伤害段推进（实现见 AdvanceSegment）
        AdvanceSegment(progress);

        // ⑤ 段数打完即收招（实现见 TryFinishBySegments）
        //    ★ 2026-09-29：不等 Animator 播完收招动画，打完最后一段就放玩家出手。
        //    返回 true = 已收招，调用方必须立刻 return。
        if (TryFinishBySegments())
            return;
    }

    public override void OnExit()
    {
        // ★ 2026-09-29 P1①：被打断时也必须走收尾出口。
        //   转换表允许 Attack → Stun / Silence / Knockback / Dead，这几条路径**只调用 OnExit**，
        //   不会经过 FinishCast() ⇒ m_CurSelectedSkill 残留 ⇒ 玩家从硬控恢复回 Idle 后，
        //   AutoFightUpdate 每帧「技能非空 + 有目标」⇒ **没按 A 也自动再放一次技能**。
        //
        //   ⚠ 这里只回调、不切状态：本方法由 ChangeState 在切换过程中同步调用，
        //      此刻再 RequestState 属于嵌套切换（状态机不支持），Idle 由打断方自己负责进入。
        //
        //   ⚠ 下方那条 P17 fail-fast 在本分支（_castFinished == false = 被打断）会照常触发，
        //      此时「特效没播完」是打断的正常结果，不是配置错 —— 看到它请先确认是不是被打断。
        if (!_castFinished)
        {
            _castFinished = true;
            Owner.OnSkillCastFinished();
        }

        // ★ v5 P17 fail-fast：正常退出的前提是「技能动画已播完」，此时所有出手特效都应已播出。
        //   若仍有剩余，只有两种成因 —— 动画提前被打断，或 CastEffectTimings 配到了 1.0 之后
        //   （normalizedTime 封顶 1，配 1.0 边界、配 >1 则永远够不到）。两者都是要修的问题，不许静默丢弃。
        //   ⚠ 不会误报：CombatStateMachine.CanTransitTo ① 禁止自转换，
        //      Attack→Attack 不会走到 OnExit，所以「玩家连点」不会触发这条。
        if (_currentSkill != null && _castCursor < _castCount)
        {
            Debug.LogError($"[AttackState] {Owner.name} 技能 {_currentSkill.SkillId} 退出时仍有 " +
                           $"{_castCount - _castCursor} 个出手特效未播出（已播 {_castCursor}/{_castCount}）—— " +
                           $"请检查动画是否提前被打断，或 CastEffectTimings 是否配到了 1.0 及之后。");
        }

        _phase = AttackPhase.WaitingForLeave;
        _prevNormTime = -1f;
        _animFrameStep = 0f;
        _castLagWarned = false;
        _currentSkill = null;
        _segTimings = null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OnUpdate 的四个步骤本体 —— ★ 2026-09-22 从 OnUpdate 里逐一抽出成方法。
    // 抽出的唯一目的：让 OnUpdate 读起来就是一条流水线（守卫 → 结束 → 特效 → 伤害），
    //   每一步的细节藏在各自正文里，要看再往下钻。**逻辑与原实现逐行等价，行为零变化**。
    //
    // ⚠ 改这里之前务必先读三条约定：
    //   ① bool 返回值即控制流：TryEnterPlaying / TryFinishSkill 返回「要停」的信号，
    //      OnUpdate 拿到就必须立刻 return —— 别在中间插代码，否则会跳过守卫或结束判定。
    //   ② 每帧最多推进一格：两处推进都用 if 而不是 while，掉帧时宁可顺延到下一帧，
    //      也不在同一帧连播两个刀光 / 连打两段（★ v5 P17 实测结论，别改回 while）。
    //   ③ info 由 OnUpdate 统一读一次再传进来（★ v5 P1：少一次 GetCurrentAnimatorStateInfo
    //      的 C# → Native 跨语言调用，一个技能全程省约 0.7ms）。
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ① 起手守卫：等 Animator 真正离开待机动画，才允许开始推进本轮技能。
    ///
    /// 【为什么需要它】SetInteger("SkillId", ...) 到 Animator 真正过渡之间有 1~2 帧仍停在待机，
    ///   若不等它切走就开始读进度，读到的是**待机动画的进度**而不是技能动画的，
    ///   会导致技能还没起手就被判定「已播完」（见本文件字段区注释里记录的历史事故）。
    ///
    /// 【返回 true】本帧可以继续往下推进 —— 要么早已进入 Playing，要么本帧刚转入 Playing。
    /// 【返回 false】还在等 / 已判定超时 —— 调用方必须立刻 return，本帧不得做任何推进。
    /// </summary>
    private bool TryEnterPlaying(float deltaTime)
    {
        // 早已进入 Playing ⇒ 本帧直接继续推进（绝大多数帧走这条，开销一次数值比较）
        if (_phase != AttackPhase.WaitingForLeave)
            return true;

        // 已经切进技能动画 ⇒ 转入 Playing。
        // ⚠ 刻意**不 return**：本帧继续往下走推进逻辑，
        //   保证「要不要多等一帧」这件事不改变任何既有时序（尤其敌人）。
        if (_clock.IsCasting())
        {
            _phase = AttackPhase.Playing;
            return true;
        }

        return false;
    }

    /// <summary>
    /// ② 结束判定：技能动画是否已被 Animator 自己播完（自动回到待机）。
    ///
    /// 【返回 true】已请求切回 Idle —— 调用方必须立刻 return，
    ///   防止后续代码在新状态下执行旧状态的逻辑。
    /// 【返回 false】动画还在播，本帧继续推进特效与伤害段。
    /// </summary>
    private bool TryFinishSkill()
    {
        // 仍停在技能动画（或仍在过渡中）⇒ 没播完，本帧不结束
        if (_clock.IsCasting())
            return false;

        // 收招走统一出口（段数打完那条路也会走它，保证只做一次）
        FinishCast();
        return true;
    }

    /// <summary>
    /// ⑤ 段数打完即收招 —— ★ 2026-09-29：玩家「一次按键 = 一次释放」的关键一环。
    ///
    /// 【为什么不等动画播完】原先唯一的结束条件是「Animator 自己播完技能动画」，
    ///   但技能动画往往比最后一段伤害长（收招摆造型），这段时间玩家被锁在 Attack 状态里。
    ///
    /// 【为什么还要等出手特效播完】CastEffectTimings 是**独立于伤害段**的时间轴，
    ///   允许特效配在最后一段之后（例如收招残影 0.95）；此时若直接退，
    ///   OnExit 那条 fail-fast 会误报 —— 那不是配置错，是我们抢跑。
    ///
    /// 【返回 true】已收招，调用方必须立刻 return。
    /// </summary>
    private bool TryFinishBySegments()
    {
        if (!_segmentsDone)            return false;   // 段还没打完
        if (_castCursor < _castCount)  return false;   // 还有刀光没播，等下一帧
        FinishCast();
        return true;
    }

    /// <summary>
    /// 一次技能释放的**唯一收尾出口** —— 两条结束路径（段数打完 / 动画播完）都会走到这里。
    ///
    /// 做两件事：
    ///   ① 回调 Owner.OnSkillCastFinished()：告诉 Controller「这次释放结束了」。
    ///      ★ 基类默认什么都不做（敌人靠持续持有技能实现连续攻击，清空会打断 AI）；
    ///        玩家在 WarrorController 里 override 清空 m_CurSelectedSkill ——
    ///        否则 AutoFightUpdate 每帧拿残留技能再 TryCastSkill ⇒ 一次按键 = 无限连击。
    ///   ② 切回 Idle：IdleState.OnEnter 会 NavStop + ResetSkillId，
    ///      「置空 Animator 的 SkillId」这一步由它负责，本处不再直接碰 Animator。
    ///
    /// ⚠ _castFinished 保证只收一次：两条路径可能在同一轮里先后到达。
    /// </summary>
    private void FinishCast()
    {
        if (_castFinished)
            return;
        _castFinished = true;

        Owner.OnSkillCastFinished();
        Owner.RequestState(CombatState.Idle);
    }

    /// <summary>
    /// ③ 出手特效（刀光）轨道推进 —— ★ v5 P5 改造：走**独立时间轴**，不再依附于伤害段。
    ///
    /// 【旧实现的限制】把时机写死成 SegTimings[i] - 0.05，等于规定「每个伤害段配一个、且必须早 5%」；
    ///   现在时机改由 CSV 的 CastEffectTimings 逐个提供（读不到就返回 -1，该条不播），
    ///   于是「3 段伤害配 5 个特效」「起手蓄力光环」「收招残影」都能纯配表实现，
    ///   特效数量也可以多于或少于伤害段数（两者上限各自独立）。
    ///
    /// 【每帧最多播一个】用 if 而非 while（★ v5 P17）：严重掉帧一帧跨过两个时机点时，
    ///   第二个顺延到下一帧 —— 延迟但不重复，这是「两个刀光同时冒出来」的直接成因。
    /// </summary>
    private void AdvanceCastEffect(float progress)
    {
        // 前置条件：有技能实例 + 还有没播的出手特效（都播完后此判断为假，后续帧直接跳过，零开销）
        if (_currentSkill == null || _castCursor >= _castCount)
            return;

        // 向**配置**询问第 _castCursor 个特效的播出时机（v5 P8 起走 BaseData/SkillData）：
        // 只认 CSV 的 CastEffectTimings[idx]，读不到就返回 -1（该条不播）。
        float castTiming = _currentSkill.BaseData.GetCastEffectTiming(_castCursor);
        if (castTiming < 0f || progress < castTiming)
            return;

        // 游标先自增再调用：保证每个特效只播一次，杜绝每帧重复创建
        _castCursor++;

        // 播放第 n 个特效（1-based）。注意传的是**特效序号**而不是段号 ——
        //   显式传参让「播哪个」由调用时刻决定，彻底摆脱对 skill.SegIdx 塞值时序的依赖，
        //   也让特效总数可以多于或少于伤害段数。
        _currentSkill.PlayCastEffect(_castCursor);
    }

    /// <summary>
    /// ④ 伤害段推进 —— 整条链路唯一的伤害出口。
    ///
    /// 【三道前置条件，缺一不可】
    ///   _currentSkill != null       → OnEnter 时成功从 Owner.CurSelectedSkill 取到技能实例
    ///   _segTimings   != null       → CSV 里配了 SegTimings 列（例：0.3;0.6;0.9）
    ///   _firedSegIdx  <  _segCount  → 还有未触发的段
    /// </summary>
    private void AdvanceSegment(float progress)
    {
        if (_currentSkill == null || _segTimings == null || _firedSegIdx >= _segCount)
            return;

        // ★ 核心判定：动画归一化进度是否跨过了「下一段」的时机点
        //    normalizedTime：0 = 动画起点，1 = 动画终点（Loop 动画会持续累加超过 1）
        //    _segTimings[_firedSegIdx]：_firedSegIdx 存的是「已触发段数」，
        //    它正好等于「下一段在数组里的下标」（已触发 0 段 → 检查 [0]；已触发 1 段 → 检查 [1]）
        if (progress < _segTimings[_firedSegIdx])
            return;

        // 先自增：让 _firedSegIdx 从「已触发数量」变成「本次要打的段号（1-based，配合 CSV 的段配置）」
        _firedSegIdx++;

        // 触发第 n 段伤害
        //    ★ v5 P16：段号以**参数**形式送进去（Controller 转发给 skill.SkillHit(segIdx)），
        //      Skill 内部一次性完成「写段号 → 结算」，不再有外部「先写后调」的两步时序。
        //      此前靠注释在四处声明的「顺序绝不能颠倒」随之失效 —— 顺序由方法体保证，不由人保证。
        //    ★ v5 P10 的既有结论不变：受击特效 id 由 Skill.ImmedDamage 直接读自己的
        //      m_SegIdx 算好后传参，段号只在 Skill 内部流转，不存在跨对象时序窗口。
        //    ⚠ 已知隐患：此处用 if 而非 while，若一帧跨过两个时间点（严重掉帧），
        //    第二段会顺延到下一帧才触发 —— 延迟但不漏段，可接受
        Owner.TriggerSegmentHit(_currentSkill, _firedSegIdx);

        // ★ 2026-09-29：最后一段已触发 ⇒ 伤害部分结束，可以收招了。
        //   ⚠ 这里只打标记，真正的收招在 OnUpdate ⑤ 处统一做 —— 出手特效可能配在
        //     最后一段之后（独立时间轴），要等它们播完再退，否则 OnExit 的
        //     「仍有 N 个出手特效未播出」fail-fast 会把「我们抢跑」误报成「配置错」。
        if (_firedSegIdx >= _segCount)
        {
            _segmentsDone = true;
        }
    }

}

#endregion

#region 眩晕状态：硬控，不可移动 / 攻击 / 施法

/// <summary>眩晕状态：硬控，不可移动 / 攻击 / 施法</summary>
public class StunState : CombatStateBase
{
    public StunState(CombatUnitController owner) : base(owner) { }

    // ★ 本类刻意**不声明任何私有字段**（比如 _stunTimer）。原因是 Buff<->FSM 整合后的约定：
    //   硬控的持续时间由 Buff 系统独占持有（Owner.HasHardControlBuff() 查询），
    //   状态机这边只负责「每帧问一下还在不在」，不再自己记一份倒计时。
    //   好处是避免「Buff 认为结束了、FSM 却以为还没结束」的双份真相对不上；
    //   代价是每帧多一次 Buff 查询 —— 这笔交易划算，因为正确性优先于微小开销。
    //   （对比 KnockbackState：暂时没有对应的持续型 Buff，所以它仍保留本地 _timer）

    public override CombatState StateType { get { return CombatState.Stun; } }

    public override void OnEnter()
    {
        Owner.NavStop();
        // ★ v5 P13：动画改写已迁往 UnitView，状态类一律经 Owner.View 操作画面
        Owner.View.ResetSkillId();
    }

    public override void OnUpdate(float deltaTime)
    {
        // Buff<->FSM整合：眩晕时长由Buff系统统一管理，这里仅查询硬控Buff是否仍存在
        if (!Owner.HasHardControlBuff())
        {
            Owner.RequestState(CombatState.Idle);
        }
    }
}

#endregion

#region 沉默状态：可移动 / 索敌，但不可施法

/// <summary>沉默状态：可移动 / 索敌，但不可施法（由 TryCastSkill 拦截）</summary>
public class SilenceState : CombatStateBase
{
    public SilenceState(CombatUnitController owner) : base(owner) { }

    // 同 StunState：不持有本地计时字段，沉默是否结束由 Owner.HasSilenceBuff() 说了算。
    // 注意 OnExit 里要把 Owner.IsSilenced 复位为 false —— 这是本状态对外唯一的副作用，
    // 一旦漏掉，单位即便离开 Silence 也会永远放不出技能。

    public override CombatState StateType { get { return CombatState.Silence; } }

    public override void OnEnter()
    {
        Owner.IsSilenced = true;
    }

    public override void OnUpdate(float deltaTime)
    {
        // 沉默期间仍可移动 / 索敌 / 普攻，仅禁止施法
        Owner.DoAutoFight();

        // Buff<->FSM整合：沉默时长由Buff系统统一管理，这里仅查询沉默Buff是否仍存在
        if (!Owner.HasSilenceBuff())
        {
            Owner.RequestState(CombatState.Idle);
        }
    }

    public override void OnExit()
    {
        Owner.IsSilenced = false;
    }
}
#endregion

#region 击退 / 浮空状态：硬控，短暂硬直后回到待机

/// <summary>击退 / 浮空状态：硬控，短暂硬直后回到待机</summary>
public class KnockbackState : CombatStateBase
{
    public KnockbackState(CombatUnitController owner) : base(owner) { }

    // 注意：本类同样遵循 AttackState 那条「实例复用」铁律，两个字段都得依靠 OnEnter 重置；
    // 而且这里必须靠 OnEnter 重置 _timer —— 若漏了，第二次被击退时 _timer 会带着上次的余值直接超时。

    /// <summary>
    /// 兜底击退时长（秒）：Owner 未写入有效值时使用。
    /// 提成命名常量而不是把 0.4f 散落在判断里 —— 将来调手感只改这一处。
    /// </summary>
    public const float DEFAULT_DURATION_SECONDS = 0.4f;

    /// <summary>
    /// 本次击退的持续时长（单位：秒）。
    /// ★ v5 P9 起来源改为 Owner.KnockbackDuration（原先走 object context 传 float，每次装箱）。
    ///   写入方是三个受击入口：OnHitOut / OnPlayerAirHit / OnAirHit —— 它们在切状态之前先赋值。
    /// 兜底：值 ≤ 0（漏写或传入非法时长）时取 DEFAULT_DURATION_SECONDS。
    /// </summary>
    private float _duration;

    /// <summary>
    /// 击退已经进行的时长（单位：秒）。每帧 += deltaTime，累积到 ≥ _duration 就 RequestState(Idle)。
    /// 写：OnEnter 归零；OnUpdate 累加。
    /// 这是本框架里唯一一处「状态自己管倒计时」的地方 —— Stun / Silence 都改成查 Buff 了，
    /// 只有 Knockback 还保留本地计时（因为击退没有对应的持续型 Buff）。
    /// </summary>
    private float _timer;

    public override CombatState StateType { get { return CombatState.Knockback; } }

    public override void OnEnter()
    {
        // ★ v5 P9：改为从 Owner 读（原为 (context is float) ? (float)context : 0.4f，含拆箱）。
        //   为什么这里不做「取完即清」：KnockbackDuration 的语义是「这个单位本次被击退多久」，
        //   而进入 Knockback 的业务路径**只有** OnHitOut / OnPlayerAirHit / OnAirHit 三个入口，
        //   它们必然先写入再切状态 —— 所以即便某次请求被 FSM 拒绝导致字段残留，
        //   也会在下一次真正进入 Knockback 之前被重新覆盖，读不到脏值。
        _duration = Owner.KnockbackDuration;
        if (_duration <= 0f) _duration = DEFAULT_DURATION_SECONDS;
        _timer = 0f;

        Owner.NavStop();
        // ★ v5 P13：同上，动画改写经 Owner.View
        Owner.View.ResetSkillId();
    }

    public override void OnUpdate(float deltaTime)
    {
        _timer += deltaTime;
        if (_timer >= _duration)
        {
            Owner.RequestState(CombatState.Idle);
        }
    }
}

#endregion

#region 死亡状态


/// <summary>死亡状态：终态</summary>
public class DeadState : CombatStateBase
{
    public DeadState(CombatUnitController owner) : base(owner) { }

    public override CombatState StateType { get { return CombatState.Dead; } }

    public override void OnEnter()
    {
        Owner.DoDead();
    }
}

#endregion




