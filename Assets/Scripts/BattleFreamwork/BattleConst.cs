/// <summary>
/// 战斗框架字符串常量（Tag/Layer/公共动画名）
/// 全部为编译期常量；登录/界面等业务字符串不属于战斗框架，不放在此处
/// </summary>
public static class BattleConst
{
    //=====================Tag========================

    /// <summary>
    /// 玩家
    /// </summary>
    public const string tag_Player = "Player";

    /// <summary>
    /// 敌人TAG
    /// </summary>
    public const string tag_NPC = "Enemy";

    //=====================Layer========================

    /// <summary>
    /// 玩家层
    /// </summary>
    public const string layer_warrior = "Warrior";

    /// <summary>
    /// 攻击NPC层
    /// </summary>
    public const string layer_AttackNpc = "AttackNpc";

    //=====================公共动画名称========================

    /// <summary>
    /// 待机
    /// </summary>
    public const string ani_IdleC = "IdleC";

    /// <summary>
    /// 待机 AnimatorState 名（NYT.controller 中的状态名）
    /// 注意：与动画片段名 ani_IdleC("IdleC") 不是一回事，
    /// AnimatorStateInfo.IsName 比的是状态名，用片段名永远匹配不上
    /// </summary>
    public const string ani_IdleState = "Idle";

    //=====================受击表现（★ v5 P15 · 2026-09-18）========================
    //
    // 这一组常量把「代码」与「GBL.controller 的动画参数」钉在一起：
    // 参数名改了只需改这里，不允许在别处出现 "Hit" / "HitType" 的字面量。

    /// <summary>受击触发参数（Trigger）—— 敌人 Animator 的受击入口</summary>
    public const string ani_Hit = "Hit";

    //=====================死亡表现（★ v5 P15-fix · 2026-09-18）========================

    /// <summary>
    /// 死亡参数名。
    /// ⚠ 这个参数**全工程类型不统一**，是本次修复的核心：
    ///   · 敌人 GBL.controller → **Bool**（默认 false，需写 true）
    ///   · 玩家 NYT.controller → **压根没有这个参数**
    ///   所以调用方（UnitView.PlayDeath）必须**先查类型再写值**，
    ///   不能像以前那样无脑 SetTrigger —— 对 Bool 参数 SetTrigger 不会有任何效果。
    /// </summary>
    public const string ani_Die = "Die";

    /// <summary>受击类型参数（Int）—— 与 ani_Hit 同时写入，决定播哪一段受击动作</summary>
    public const string ani_HitType = "HitType";

    /// <summary>0 = 不播受击动作（调用方传 0 即视为「这一段没有受击表现」）</summary>
    public const int HIT_TYPE_NONE = 0;

    /// <summary>轻型受击 → LightHit（★ 当前唯一接通的一档）</summary>
    public const int HIT_TYPE_LIGHT = 1;

    /// <summary>
    /// 以下 2~6 是 HitType 协议的**预留档位**，目前动画层只建了 HitType=1 的分支，
    /// 写了也不会有对应动作。留在这里是为了让「将来加受击类型」时
    /// 有个唯一的地方可以照着加常量 + 在 Hit 子状态机里加一条 Entry 分支。
    /// ⚠ 切勿以为传这些值就能出效果 —— CSV 列 13 现在只有「受击」一个语义，没有强度之分。
    /// </summary>
    public const int HIT_TYPE_HEAVY = 2;   // HeavyHit
    public const int HIT_TYPE_FLOAT = 3;   // FloatHit（浮空）
    public const int HIT_TYPE_DOWN = 4;    // Down（击倒）
    public const int HIT_TYPE_DIZZY = 5;   // Dizzy（眩晕）
    public const int HIT_TYPE_FROZEN = 6;  // Frozen（冰冻）

    /// <summary>
    /// 受击顿帧时长（秒）—— 命中瞬间把**受击方**的 Animator.speed 归零，营造打击感。
    /// ⚠ 只作用于受击方：攻击方的段号推进依赖 normalizedTime，冻结它会导致多段技能卡段。
    /// </summary>
    public const float HIT_STOP_SECONDS = 0.08f;
}
