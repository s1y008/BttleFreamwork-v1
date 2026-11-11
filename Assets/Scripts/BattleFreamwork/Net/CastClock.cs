using UnityEngine;

/// <summary>
/// ★★ 施法时钟 —— 状态同步改造的**第一个可替换边界**（2026-09-28）。
///
/// 【它解决什么问题】
///   改造前，<c>AttackState</c> 有三处直接读 Animator 来决定战斗逻辑：
///     ① 段号推进      <c>CombatStates.cs:262</c> → <c>info.normalizedTime</c>
///     ② 起手守卫      <c>TryEnterPlaying</c>     → <c>!Owner.View.IsInIdleState</c>
///     ③ 结束判定      <c>TryFinishSkill</c>      → <c>!Owner.View.IsInIdleState</c>
///   这三处把「什么时候结算伤害」钉死在了**本机动画播放进度**上。
///   而状态同步的本质要求是：服务器必须能**脱离 Unity** 独立跑完整战斗逻辑 —— 服务器上没有 Animator，
///   于是一次施法永远不会推进段号，技能永不结算。
///
/// 【为什么不直接在调用点写 if (isNetwork)】
///   那会把「伤害结算 / 段号推进」写成两套，bug 要修两遍，且单机那条路迟早腐烂。
///   正确做法是：**逻辑只有一份，变化的只是它问谁要时间**。本接口就是那个「问谁要时间」的接缝。
///
/// 【三个方法收敛成一个概念】
///   ①②③ 看似三件事，其实是同一个量 —— **施法进度**：
///     ② 起手 = 进度 > 0（已离开待机）      ③ 结束 = 进度已到终点
///     ① 推进 = 进度跨过 CSV 里配置的时机点
///   所以只暴露一个 <c>GetProgress()</c> + 一个 <c>IsCasting()</c> 就够，不需要三个抽象。
/// </summary>
public interface ICastClock
{
    /// <summary>
    /// 一轮施法开始时调用，把进度归零。
    /// 对应 <c>AttackState.OnEnter</c> —— 语义上的「一轮攻击开始」点。
    /// </summary>
    void Reset();

    /// <summary>
    /// 每帧推进一次内部计时（dt = 上一帧到本帧秒数）。
    /// ⚠ 对 <see cref="AnimatorCastClock"/> 是空实现 —— 它的进度由 Animator 自己累积，
    ///   额外计时反而会引入第二个时间源，那是确定性问题的经典成因。
    /// </summary>
    void Tick(float deltaTime);

    /// <summary>
    /// 当前施法的**归一化进度**（0 = 起手，1 = 播完），用于与 CSV 的
    /// <c>SegTimings</c> / <c>CastEffectTimings</c> 逐点比对。
    /// ⚠ 这两个配置列本来就是归一化 0~1 的纯数据（见 <c>SkillData.SegTimings</c> 注释），
    ///   所以换成逻辑时钟后**不需要改动任何一行 CSV**。
    /// </summary>
    float GetProgress();

    /// <summary>
    /// 是否仍处在「施法中」（既没播完、也还没回到待机）。
    /// 语义等价于改造前的 <c>!Owner.View.IsInIdleState</c>。
    /// </summary>
    bool IsCasting();
}

/// <summary>
/// 单机（默认）实现 —— 进度来自本机 Animator 的播放进度。
///
/// ⚠⚠ 本类必须与改造前的代码**逐行等价**，否则单机行为会漂移。
///   等价性核对（改造前 → 改造后）：
///     段号推进：<c>Owner.View.StateInfo.normalizedTime</c> → <c>GetProgress()</c>      完全等价
///     起手守卫：<c>!Owner.View.IsInIdleState</c>            → <c>IsCasting()</c>        完全等价
///     结束判定：<c>!Owner.View.IsInIdleState</c>            → <c>IsCasting()</c>        完全等价
///   native 调用次数也不变：改造前每帧 3 次（StateInfo 1 + IsInIdleState 2），改造后仍是 3 次。
/// </summary>
public sealed class AnimatorCastClock : ICastClock
{
    private readonly CombatUnitController _owner;

    public AnimatorCastClock(CombatUnitController owner)
    {
        _owner = owner;
    }

    /// <summary>Animator 自己会累积进度，此处刻意留空（见接口注释）。</summary>
    public void Reset() { }

    /// <summary>Animator 自己会累积进度，此处刻意留空（见接口注释）。</summary>
    public void Tick(float deltaTime) { }

    public float GetProgress()
    {
        return _owner.View.StateInfo.normalizedTime;
    }

    public bool IsCasting()
    {
        return !_owner.View.IsInIdleState;
    }
}

/// <summary>
/// 逻辑时钟实现 —— 进度由纯数值累加得出，**不依赖 Unity 任何对象**。
///
/// 这是状态同步 / 本地服务器模式下要用的那个：服务器拿不到 Animator，
/// 就由它按固定步长累加时间，用「已过时长 / 技能总时长」得出同样的 0~1 进度，
/// 于是 <c>SegTimings</c> 那套配置在服务器上照样能逐点命中。
///
/// 【当前状态】本类**尚未接线** —— 本轮只完成接缝（S1 的边界），
///   <c>AttackState</c> 默认仍注入 <see cref="AnimatorCastClock"/>，单机行为零变化。
///   接入方式（将来的事）：<c>new AttackState(this, new LogicCastClock(duration))</c>。
///
/// ⚠ 尚未解决的前提：<c>duration</c>（技能总时长）目前 CSV 里没有这一列，
///   需要从动画长度取一次常量，或新增配置列（**属于用户手工改配置范畴，AI 不代劳**）。
/// </summary>
public sealed class LogicCastClock : ICastClock
{
    private readonly float _duration;
    private float _elapsed;

    /// <param name="duration">本轮施法的总时长（秒）。必须 &gt; 0，否则进度恒为 1（视为立即播完）。</param>
    public LogicCastClock(float duration)
    {
        _duration = duration;
        _elapsed = 0f;
    }

    public void Reset() { _elapsed = 0f; }

    public void Tick(float deltaTime) { _elapsed += deltaTime; }

    public float GetProgress()
    {
        // ⚠ 除法守卫：duration 为 0 时不能返回 NaN（NaN 参与任何比较都返回 false，
        //   会让段号永远推进不了，且**不报任何错** —— 属铁律 5 禁止的静默降级）。
        //   直接判为「已播完」，让上层逻辑照常走完并可由 OnExit 的 fail-fast 暴露配置问题。
        if (_duration <= 0f)
        {
            return 1f;
        }
        return _elapsed / _duration;
    }

    public bool IsCasting()
    {
        return _elapsed < _duration;
    }
}
