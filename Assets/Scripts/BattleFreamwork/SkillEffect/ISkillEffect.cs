/// <summary>
/// 技能命中效果策略 —— <c>SkillEffectTypes</c> 每个枚举值对应一个实现类。
///
/// ★ <c>EffectId</c> 必须与 <c>SkillEffectTypes</c> 枚举值、CSV 列 13 的数字一一对应
///   （配置契约：只能追加，不得重排 / 复用）。
///
/// ★ 未注册的效果号 → **三态处理**（用户 2026-09-24 拍板，见《战斗框架抽象方案》4.7）：
///   ① <c>KnownStubIds</c> 白名单内的已知空桩 → 静默跳过（与旧 switch default 逐字等价）；
///   ② 未知效果号 → LogError 指名（技能 / 段号 / 效果号），绝不静默降级（项目铁律 5）；
///   ③ 加载期按效果号聚合出「被 CSV 配置但未实现」的清单，只 Warning 一次 —— 不刷屏。
///
/// ★ 填桩纪律（见方案 6.4）：效果类必须**无状态**，且只经 Controller 的 Facade 回调
///   （<c>OnDamaged</c> / <c>OnAddBuff</c> / <c>OnSneer</c> / <c>HitReaction</c>）作用于目标，
///   不直访目标或攻击者的内部字段。
/// </summary>
public interface ISkillEffect
{
    /// <summary>效果编号（= SkillEffectTypes 枚举值，= CSV 列 13 里的数字）</summary>
    int EffectId { get; }

    /// <summary>执行一次该效果</summary>
    /// <param name="cast">本次段结算的施法上下文（每段构造一次：攻击者 / 技能 / 配置 / 段号）</param>
    /// <param name="target">本次命中的目标（每个目标传一次）</param>
    void Execute(in SkillCastContext cast, IHitReceiver target);
}
