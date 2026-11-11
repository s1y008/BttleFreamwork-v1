using UnityEngine;

/// <summary>
/// 战斗单位状态枚举（第一阶段 FSM 统一状态集）
/// 说明：本枚举取代此前散落且未接入状态机的 EnemyState / WarriorState，
///       作为战斗单位状态的唯一真相源。
/// </summary>
public enum CombatState
{
    Idle = 0,       // 待机：正常战斗决策（索敌 → 追击 → 攻击）
    Move = 1,       // 移动 / 追击
    Attack = 2,     // 攻击 / 施法
    Stun = 3,       // 眩晕：不可移动、不可攻击、不可施法（硬控）
    Silence = 4,    // 沉默：可移动、可索敌，但不可施法
    Knockback = 5,  // 击退 / 浮空：短暂硬直，不可操作
    Dead = 6,       // 死亡：终态
}

/// <summary>
/// 战斗状态接口：每个具体状态实现「进入 / 逐帧更新 / 离开」三阶段回调。
///
/// ★ v5 P11 改造：三个回调**全部去掉 owner 参数**，owner 改由构造注入（见 CombatStateBase）。
///
/// 【为什么删】
///   状态实例与战斗单位是 1:1 —— CombatUnitController.Start() → InitStateMachine() 里逐行
///   new IdleState() / new AttackState()…… 每个单位各持一套；且 AttackState 自带 8 个跨帧字段，
///   两个单位共享必串台。既然实例本来就专属一个单位，每帧再把 owner 当参数传，
///   就是把同一个恒等对象重复传一遍（改造前唯一调用点 CombatStateMachine.Tick 传的永远是 _owner；
///   而那个 _owner 已于 2026-09-24 连同构造函数参数一起删除 —— 状态机根本不需要知道自己在服务谁）。
///
/// 【顺带清掉的假依赖】
///   改造前 21 个方法签名里，7 个的 owner 参数从未被方法体读过：DeadState.OnUpdate / OnExit、
///   AttackState.OnExit、Idle | Move | Stun | Knockback 的 OnExit。
///   —— 与 v5 P9 删 object context 是同一病灶：签名承诺了一个并不存在的依赖。
///
/// 【收益口径】性能 ≈ 0（引用传参不装箱、不分配，JIT 后与读字段差一条指令）。
///   真实收益是结构：签名诚实 + 6 个空实现方法可删 + 与 CombatStateMachine 的注入风格统一。
/// </summary>
public interface ICombatState
{
    CombatState StateType { get; }

    /// <summary>进入状态时调用（所需数据一律从基类的 Owner 取）</summary>
    void OnEnter();

    /// <summary>由状态机每帧 Tick 调用</summary>
    void OnUpdate(float deltaTime);

    /// <summary>离开状态时调用</summary>
    void OnExit();
}

/// <summary>
/// 状态基类：把「owner 引用 + 三个回调」这块 7 个状态共用的样板收拢到一处。
///
/// 【为什么是构造注入，而不是在 OnEnter 里存一份】
///   readonly 字段在对象诞生瞬间就有效，不存在「没进过 OnEnter 就被 Tick」的窗口 ——
///   那个窗口在幽灵状态路径上是真实存在的：转换表里有边、但该状态未 RegisterState 时，
///   ChangeState 会静默跳过 OnEnter，却已经把 _currentState 改掉了。
///
/// 【⚠ 代价】三个回调是 virtual 空实现，于是「新建状态时忘了写 OnEnter」不再被编译器拦截，
///   会退化成静默无行为。StateType 仍是 abstract，能部分兜住 —— 新增状态时务必自查一遍。
///
/// 【⚠ 命名】字段用 PascalCase 的 Owner 而非 _owner：这是给所有子类使用的受保护成员，
///   与「本类的私有实现细节」在命名上要能一眼区分开（原对照方是状态机里的 _owner 字段，
///   已于 2026-09-24 删除，辨别 Owner 归属的理由依然成立 —— 它属于状态，不属于状态机）。
/// </summary>
public abstract class CombatStateBase : ICombatState
{
    /// <summary>本状态所属的战斗单位（构造注入，永不为 null）</summary>
    protected readonly CombatUnitController Owner;

    protected CombatStateBase(CombatUnitController owner)
    {
        Owner = owner;
    }

    public abstract CombatState StateType { get; }

    public virtual void OnEnter() { }
    public virtual void OnUpdate(float deltaTime) { }
    public virtual void OnExit() { }
}
