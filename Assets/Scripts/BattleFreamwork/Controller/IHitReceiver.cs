/// <summary>
/// 受击行为契约 —— <c>CombatUnitController</c> 九个受击 Facade 的正式化（阶段二，2026-09-24）。
///
/// 【为什么要这一层】
///   调用方（<c>Skill</c> / <c>ISkillEffect</c> 效果类）此前依赖具体的 <c>CombatUnitController</c>，
///   而技能效果二十多个桩将来要逐个填充 —— 每填一个就多一处对具体类的硬依赖。
///   先收口成行为契约，填桩时面向接口写，将来出现第二种受击单位（霸体怪 / 建筑 / 宠物 / 木桩）
///   不必回头改效果层。
///
/// 【为什么是单接口而不是七八个细粒度接口（6.2.2 已拍板）】
///   今天只有一种实现类，拆分收益为零、维护成本翻倍；等真的出现第二种单位类型再拆。
///
/// 【签名纪律】
///   九个既有方法与实现**逐字一致**（含 <c>params object[]</c> 与默认参数），
///   靠 C# 隐式实现成立 ⇒ 实现类零改动。⚠ 改本接口任一条签名都必须同步改实现，
///   且**两侧默认参数值必须相同**（经接口调用取接口默认值、经类调用取类默认值，
///   不一致会产生静默的行为分叉，属 6.2.2 点名的风险项）。
///
/// 【契约约定（迁自 v5 P13，实现方须各自遵守）】
///   · attacker 为空 / 受击者已死 / 不可被攻击 → 整次受击被静默忽略（不是错误，不报错）；
///   · <c>OnDamaged</c> 先扣血，致死则请求 Dead 且**不再播受击特效**；未死才播；
///   · hitEffectId == 0 表示本段没有受击特效（正常情况，不是错误）。
///
/// ★★ 【纪律：效果类不得绕过本契约】
///   本接口**刻意不暴露** <c>HitReaction</c> / <c>View</c> 等内部组件 —— 组件结构是实现细节，
///   一旦进契约，将来换了单位类型就无处安放。需要新的受击语义时，**新增 Facade 方法**，
///   而不是在效果类里 <c>target.XXX 组件.ApplyYyy()</c>。（本轮正是因此新增了 <c>OnHurt</c>：
///   <c>HureEffect</c> 原本直调 <c>Target.HitReaction.ApplyHurt</c>，现已收口。）
/// </summary>
public interface IHitReceiver
{
    /// <summary>
    /// 单位名，仅供诊断日志打印。
    /// ⚠ 刻意取小写 <c>name</c>：与 <c>UnityEngine.Object.name</c> 同名，
    ///   <c>CombatUnitController</c> 继承自 <c>MonoBehaviour</c>，其 <c>name</c> 属性**隐式满足**本成员，
    ///   无需任何实现代码 —— 效果类要打印目标名时，就不必把 Target 转回具体类。
    ///   （若将来出现非 MonoBehaviour 的实现类，该类须自行提供 name。）
    /// </summary>
    string name { get; }

    /// <summary>受击结算（伤害）→ 返回本次结局</summary>
    UnitHitReaction.HitOutcome OnDamaged(CombatUnitController attacker, float hp, int hitEffectId);

    /// <summary>
    /// 受击表现（顿帧 / 受击动画）—— ★ 新增 Facade，转发 <c>HitReaction.ApplyHurt</c>。
    /// 原来 <c>HureEffect</c> 直访 <c>Target.HitReaction</c>，等于把受击编排组件泄漏进了契约缝隙；
    /// 收口后效果层只认「我要一次受击表现」，不认「你内部由谁来编排」。
    /// </summary>
    void OnHurt(CombatUnitController attacker);

    /// <summary>受治疗（超过 MaxHp 按上限截断）</summary>
    void OnHeal(CombatUnitController attacker, int hp, int effectId);

    /// <summary>挂 Buff（按 buffId 查 CSV，Id 是唯一主键）</summary>
    void OnAddBuff(CombatUnitController attacker, Skill skillInfo, int buffid, bool _isBuff = false);

    /// <summary>嘲讽：把本单位的目标锁定为 attacker</summary>
    void OnSneer(CombatUnitController attacker);

    /// <summary>击飞（浮空）</summary>
    void OnHitOut(CombatUnitController attacker, float dist, float time, bool airHit, bool isMove = false);

    /// <summary>浮空受击（玩家版，仅时长）</summary>
    void OnPlayerAirHit(CombatUnitController attacker, float time, params object[] args);

    /// <summary>浮空受击（完整参数版）</summary>
    void OnAirHit(CombatUnitController attacker, float dist, float time, bool airHit, params object[] args);

    /// <summary>眩晕（内部按 CSV 的 Tags=HardControl 反查配置行，不再硬编码 BuffType）</summary>
    void OnDizz(CombatUnitController unit, CombatUnitController attacker, float time, bool isDizz);

    /// <summary>沉默（内部按 CSV 的 Tags=Silence 反查配置行）</summary>
    void OnSilence(CombatUnitController attacker, float time);
}
