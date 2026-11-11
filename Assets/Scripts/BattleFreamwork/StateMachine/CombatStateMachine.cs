using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 战斗状态机（第一阶段）
/// 职责：状态注册、转换合法性校验、逐帧 Tick 驱动。
/// 状态行为由 ICombatState 实现；各状态自己持有 owner（★ v5 P11 起走构造注入，见 CombatStateBase）。
///
/// ★ 2026-09-24 收尾清理：构造函数参数 owner 与 _owner 字段已删除。
///   v5 P11 去掉三个回调的 owner 形参后，这个字段在全类中**零读取**，
///   成了一个赋值一次、再没人看的改造残留（它当时保留的两条理由均已不成立：
///   需要 owner 的从来是状态，不是状态机）。
///   现在 owner 只有一条路径：InitStateMachine 里 new XxxState(this) → CombatStateBase.Owner，
///   那才是 7 个子类实际读取的那一份（CombatStates.cs 内 40 处）。
/// </summary>
public class CombatStateMachine
{
    private readonly Dictionary<CombatState, ICombatState> _states = new Dictionary<CombatState, ICombatState>();
    private readonly Dictionary<CombatState, HashSet<CombatState>> _transitions = new Dictionary<CombatState, HashSet<CombatState>>();

    private CombatState _currentState = CombatState.Idle;
    private CombatState _previousState = CombatState.Idle;

    public CombatState CurrentState { get { return _currentState; } }
    public CombatState PreviousState { get { return _previousState; } }

    /// <summary>状态切换事件：参数为 (旧状态, 新状态)</summary>
    public event Action<CombatState, CombatState> OnStateChanged;

    /// <summary>
    /// 构造：装载转换表。
    ///
    /// ⚠ 此处原本还有一个 <c>CombatUnitController owner</c> 参数（对应被删掉的 _owner 字段），
    ///   2026-09-24 删除：三个回调自 v5 P11 起不再接收 owner 实参，状态机便再也用不上它，
    ///   全类零读取。留着只会让人误以为状态机需要知道自己在服务谁。
    ///   谁要用 owner 谁自己注入 —— 是状态，不是状态机。
    ///
    /// <param name="applyAssetTable">
    /// 是否用转换表资产（Resources/Config/CombatTransitionTable）覆盖代码内置表，默认 true。
    /// ★ 传 false 的用途只有一个：编辑器工具要读取「纯代码内置表」时
    ///   （矩阵窗口的「从代码导入」），否则它会读到资产内容，形成「导入自己」的循环。
    /// </param>
    /// </summary>
    public CombatStateMachine(bool applyAssetTable = true)
    {
        // ★ 仅 Editor 下会真正装载内置表（见 InitDefaultTransitions 的 #if UNITY_EDITOR 包裹）；
        //   运行时（打包后）这里是空方法，转换关系完全来自下面的资产覆盖。
        InitDefaultTransitions();
        if (applyAssetTable)
            ApplyTransitionTableAsset();
    }

    /// <summary>
    /// 用转换表资产整体覆盖代码内置表（★ 2026-10-10）。
    ///
    /// 【为什么是「整体覆盖」而不是「增量合并」】
    ///   合并会让最终生效的规则取决于「代码配了什么 + 资产配了什么」，
    ///   出问题时要同时看两处才知道一条边为什么存在 —— 这正是硬编码时代最难排查的地方。
    ///   整体覆盖保证：配了资产，资产就是唯一真相。
    ///
    /// 【为什么失败时不抛异常】
    ///   状态机在 Controller.Start() 里构造，抛异常会让整个单位初始化失败、连锁 NRE。
    ///   ⇒ 改为「保留现状 + 逐条 LogError 指名」：行为可见地坏掉、日志直接指向根因，
    ///     而不是静默兜底。
    ///   ★ 注意运行时没有内置表可退（见 InitDefaultTransitions 的 #if UNITY_EDITOR），
    ///     所以这里失败的后果是「该状态机无任何合法转换」，必须靠日志立刻发现。
    /// </summary>
    private void ApplyTransitionTableAsset()
    {
        CombatTransitionTable table = CombatTransitionTable.Load();
        if (table == null)
        {
            CombatTransitionTable.LogMissingOnce();
            return;
        }

        if (!table.Enabled)
        {
            LogDisabledOnce(table.name);
            return;
        }

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        if (!table.Validate(errors, warnings))
        {
            // fail-fast 指名：逐条报错，不静默降级
            StringBuilder sb = new StringBuilder();
            sb.Append("[TransitionTable] 资产「").Append(table.name).Append("」校验失败，")
              .Append("本状态机将无任何合法转换（单位会卡死在当前状态）。问题如下：");
            for (int i = 0; i < errors.Count; i++)
                sb.Append("\n  · ").Append(errors[i]);
            Debug.LogError(sb.ToString());
            return;
        }

        for (int i = 0; i < warnings.Count; i++)
            Debug.LogWarning("[TransitionTable] " + table.name + "：" + warnings[i]);

        Dictionary<CombatState, HashSet<CombatState>> built =
            new Dictionary<CombatState, HashSet<CombatState>>();
        table.BuildTransitions(built);

        _transitions.Clear();
        foreach (KeyValuePair<CombatState, HashSet<CombatState>> kv in built)
            _transitions[kv.Key] = kv.Value;
    }

    private static bool s_TableDisabledLogged;

    private static void LogDisabledOnce(string assetName)
    {
        if (s_TableDisabledLogged) return;
        s_TableDisabledLogged = true;
        Debug.LogWarning("[TransitionTable] 资产「" + assetName
            + "」的 Enabled 为 false，运行时将无任何合法转换（单位会卡死在当前状态）。");
    }

#if UNITY_EDITOR
    /// <summary>
    /// 默认转换表：定义各状态之间的合法转换。
    /// Dead 为终态（无出边），受控状态结束后回到 Idle。
    ///
    /// ★ 2026-10-10 B 档：本方法**只在 Editor 下编译**。
    ///   【为什么还在】Editor 需要一份「代码基线」，用于两件事：
    ///     ① 新建转换表资产时用它填充，保证「资产起点 == 当前线上行为」（迁移零差异）；
    ///     ② 矩阵窗口的「重置为代码基线」。
    ///   【为什么运行时不再保留】运行时留着它 = 同时存在两份真相源：
    ///     改了它不生效（被资产覆盖）且**不报错** —— 这是静默失效，
    ///     正是 v4 根因 A「转换表只定义允许怎么走，不保证运行时真这么走」的翻版。
    ///   现在运行时转换关系的唯一来源是 Resources/Config/CombatTransitionTable 资产，
    ///   缺失时由 CombatTransitionTable.LogMissingOnce 报 **Error** 指名，不再有兜底表。
    /// </summary>
    private void InitDefaultTransitions()
    {
        // Idle 可转换到：移动 / 攻击 / 三种受控 / 死亡
        AddTransition(CombatState.Idle, CombatState.Move);
        AddTransition(CombatState.Idle, CombatState.Attack);
        AddTransition(CombatState.Idle, CombatState.Stun);
        AddTransition(CombatState.Idle, CombatState.Silence);
        AddTransition(CombatState.Idle, CombatState.Knockback);
        AddTransition(CombatState.Idle, CombatState.Dead);

        // Move 可转换到：待机 / 攻击 / 三种受控 / 死亡
        AddTransition(CombatState.Move, CombatState.Idle);
        AddTransition(CombatState.Move, CombatState.Attack);
        AddTransition(CombatState.Move, CombatState.Stun);
        AddTransition(CombatState.Move, CombatState.Silence);
        AddTransition(CombatState.Move, CombatState.Knockback);
        AddTransition(CombatState.Move, CombatState.Dead);

        // Attack 可转换到：待机 / 三种受控 / 死亡（受击可打断攻击）
        AddTransition(CombatState.Attack, CombatState.Idle);
        AddTransition(CombatState.Attack, CombatState.Stun);
        AddTransition(CombatState.Attack, CombatState.Silence);
        AddTransition(CombatState.Attack, CombatState.Knockback);
        AddTransition(CombatState.Attack, CombatState.Dead);

        // Stun 可转换到：待机 / 沉默（眩晕到期回退到仍生效的沉默）/ 死亡
        AddTransition(CombatState.Stun, CombatState.Idle);
        AddTransition(CombatState.Stun, CombatState.Silence);
        AddTransition(CombatState.Stun, CombatState.Dead);

        // Silence 可转换到：待机 / 眩晕 / 击退 / 死亡
        AddTransition(CombatState.Silence, CombatState.Idle);
        AddTransition(CombatState.Silence, CombatState.Stun);
        AddTransition(CombatState.Silence, CombatState.Knockback);
        AddTransition(CombatState.Silence, CombatState.Dead);

        // Knockback 可转换到：待机 / 眩晕 / 沉默（击退中可被控制Buff打断）/ 死亡
        AddTransition(CombatState.Knockback, CombatState.Idle);
        AddTransition(CombatState.Knockback, CombatState.Stun);
        AddTransition(CombatState.Knockback, CombatState.Silence);
        AddTransition(CombatState.Knockback, CombatState.Dead);

        // Dead 为终态，无出边
    }
#else
    /// <summary>
    /// 运行时（打包后）的空实现：转换关系的唯一来源是转换表资产，代码里不再内置任何边。
    /// 保留同名方法只是为了让构造函数的调用点在两种编译条件下都能编译通过。
    /// </summary>
    private void InitDefaultTransitions() { }
#endif

    /// <summary>注册状态实现</summary>
    public void RegisterState(ICombatState state)
    {
        if (state == null) return;
        _states[state.StateType] = state;
    }

    /// <summary>添加一条合法转换</summary>
    public void AddTransition(CombatState from, CombatState to)
    {
        if (!_transitions.ContainsKey(from))
            _transitions[from] = new HashSet<CombatState>();
        _transitions[from].Add(to);
    }

    /// <summary>
    /// 判断从当前状态能否转换到目标状态（FSM 的唯一转换闸门，仅被 ChangeState 调用）。
    /// 三重短路校验，顺序不可调换：
    /// ① 禁自转换——避免重复触发 OnEnter（如 AttackState.OnEnter 的 ResetSegIdx 按轮归零）；
    /// ② 防 KeyNotFoundException——_transitions 为稀疏表，Dead 等无出边状态根本没有 Key；
    /// ③ 查白名单——HashSet O(1) 判定。
    ///   ★ 规则唯一来源 = Resources/Config/CombatTransitionTable 资产；
    ///     仅 Editor 下且资产缺失时，才回落到 InitDefaultTransitions 内置表。
    /// 特例：Dead 是终态，因表中无 Key 而在 ② 处短路，任何出向转换被拒。
    /// 注意：本方法只回答"规则表里有没有这条边"，不校验 to 是否已 RegisterState，
    /// 因此"进入表但未注册实现"会产生无行为的幽灵状态（ChangeState 静默跳过 OnEnter）。
    /// </summary>
    public bool CanTransitTo(CombatState to)
    {
        return _currentState != to                          // ① 禁自转换：状态机不做重入，避免 OnExit/OnEnter 重复执行
            && _transitions.ContainsKey(_currentState)      // ② 无出边的状态（Dead）在此短路，同时保证 ③ 行索引器安全
            && _transitions[_currentState].Contains(to);    // ③ 查转换白名单（EnumEqualityComparer，无装箱）
    }

    /// <summary>
    /// 切换状态：校验合法转换 → 旧状态 OnExit → 新状态 OnEnter。
    /// 返回是否切换成功。
    ///
    /// ★ v5 P9：context 参数已删除（原为 object，弱类型 + 装箱）。
    ///   需要额外数据的状态改为在 OnEnter 里向 owner 取（见 ICombatState.OnEnter 注释）。
    ///   ⚠ 注意转换被拒时直接 return false —— 此时 OnEnter 不会执行，
    ///     所以「先写 owner 字段、再调 ChangeState」的写法在失败路径上会留下未消费的值，
    ///     下次进入同一状态时务必确认该值已被重新写入（见 KnockbackDuration 注释）。
    ///
    /// ★ v5 P11：OnExit / OnEnter 的 owner 实参也已删除。各状态在构造时就已经拿到 owner
    ///   （CombatUnitController.InitStateMachine 里 new XxxState(this)），无需状态机再转交。
    ///   顺带一个好处：这正是「幽灵状态」更安全的直接原因 —— 即便某状态从未执行过 OnEnter，
    ///   它的 Owner 字段也早已有效，不会出现空引用。
    /// </summary>
    public bool ChangeState(CombatState to)
    {
        if (!CanTransitTo(to))
            return false;

        CombatState from = _currentState;

        ICombatState fromState;
        if (_states.TryGetValue(from, out fromState))
            fromState.OnExit();

        _previousState = from;
        _currentState = to;

        ICombatState toState;
        if (_states.TryGetValue(to, out toState))
            toState.OnEnter();

        OnStateChanged?.Invoke(from, to);

        return true;
    }

    /// <summary>进入初始状态（触发初始状态 OnEnter）</summary>
    public void Start()
    {
        ICombatState state;
        if (_states.TryGetValue(_currentState, out state))
            state.OnEnter();
    }

    /// <summary>每帧驱动当前状态</summary>
    public void Tick(float deltaTime)
    {
        ICombatState state;
        if (_states.TryGetValue(_currentState, out state))
            state.OnUpdate(deltaTime);
    }
}
