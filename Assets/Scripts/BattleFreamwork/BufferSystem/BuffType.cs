using System;

namespace BattleFramework.Core
{
    /// <summary>
    /// Buff类型位枚举 - 使用位标记实现高效的Buff检测
    /// ★ 2026-09-24 起语义放宽：Type 由「一种Buff占一个位」改为「标签位图」——
    ///   ① CSV 的 BuffType 列允许写多个枚举名的组合（用 ';' 分隔）；
    ///   ② 同一个位可以被多个 BuffId 共享（例如 id=2001/2002 都是 Stunned 位，只是数值不同）；
    ///   ⇒ Buff 的数量不再受 64 位上限约束，新增 Buff 只需新增 Id，不必抢枚举位。
    /// ⚠ 由此：「单位身上有没有某个Buff」必须按 Id 判定（BuffSystemPooled.AddBuff(int)），
    ///   位图只用于 O(1) 的类别级查询（是否被控制 / 是否中毒 / 能否移动）。
    /// </summary>
    [Flags]
    public enum BuffType : ulong
    {
        None = 0,

        // === 增益类 (Positive Buffs) ===
        /// <summary>攻击力提升</summary>
        AttackBoost = 1UL << 0,   //1
        /// <summary>防御力提升</summary>
        DefenseBoost = 1UL << 1,//2
        /// <summary>速度提升</summary>
        SpeedBoost = 1UL << 2,//4
        /// <summary>暴击率提升</summary>
        CriticalBoost = 1UL << 3,//8
        /// <summary>生命回复</summary>
        HealthRegen = 1UL << 4,
        /// <summary>魔法回复</summary>
        ManaRegen = 1UL << 5,
        /// <summary>无敌</summary>
        Invincible = 1UL << 6,
        /// <summary>护盾</summary>
        Shield = 1UL << 7,
        /// <summary>隐身</summary>
        Invisible = 1UL << 8,
        /// <summary>狂暴</summary>
        Berserk = 1UL << 9,
        /// <summary>吸血</summary>
        Lifesteal = 1UL << 10,
        /// <summary>反伤</summary>
        Thorns = 1UL << 11,
        /// <summary>闪避提升</summary>
        DodgeBoost = 1UL << 12,
        /// <summary>经验加成</summary>
        ExpBoost = 1UL << 13,
        /// <summary>金币加成</summary>
        GoldBoost = 1UL << 14,
        /// <summary>免疫控制</summary>
        ControlImmune = 1UL << 15,

        // === 减益类 (Negative Buffs/Debuffs) ===
        /// <summary>中毒</summary>
        Poisoned = 1UL << 16,
        /// <summary>燃烧</summary>
        Burning = 1UL << 17,
        /// <summary>冰冻</summary>
        Frozen = 1UL << 18,
        /// <summary>眩晕</summary>
        Stunned = 1UL << 19,
        /// <summary>沉默</summary>
        Silenced = 1UL << 20,
        /// <summary>缴械</summary>
        Disarmed = 1UL << 21,
        /// <summary>致盲</summary>
        Blinded = 1UL << 22,
        /// <summary>减速</summary>
        Slowed = 1UL << 23,
        /// <summary>虚弱</summary>
        Weakened = 1UL << 24,
        /// <summary>流血</summary>
        Bleeding = 1UL << 25,
        /// <summary>诅咒</summary>
        Cursed = 1UL << 26,
        /// <summary>恐惧</summary>
        Feared = 1UL << 27,
        /// <summary>嘲讽</summary>
        Taunted = 1UL << 28,
        /// <summary>禁疗</summary>
        HealingBlock = 1UL << 29,
        /// <summary>破甲</summary>
        ArmorBreak = 1UL << 30,
        /// <summary>腐蚀</summary>
        Corroded = 1UL << 31,

        // === 特殊状态 ===
        /// <summary>魅惑</summary>
        Charmed = 1UL << 32,
        /// <summary>变形</summary>
        Polymorphed = 1UL << 33,
        /// <summary>石化</summary>
        Petrified = 1UL << 34,
        /// <summary>睡眠</summary>
        Sleeping = 1UL << 35,
        /// <summary>混乱</summary>
        Confused = 1UL << 36,
        /// <summary>狂暴（负面）</summary>
        Enraged = 1UL << 37,
        /// <summary>嗜血</summary>
        Bloodlust = 1UL << 38,
        /// <summary>神圣</summary>
        Divine = 1UL << 39,
        /// <summary>暗影</summary>
        Shadow = 1UL << 40,
        /// <summary>飞行</summary>
        Flying = 1UL << 41,
        /// <summary>潜行</summary>
        Stealth = 1UL << 42,
        /// <summary>狂怒</summary>
        Rage = 1UL << 43,
        /// <summary>集中</summary>
        Focused = 1UL << 44,
        /// <summary>嗜血狂暴</summary>
        Frenzy = 1UL << 45,

        // === 组合标记 ===
        /// <summary>所有增益</summary>
        AllPositive = AttackBoost | DefenseBoost | SpeedBoost | CriticalBoost |
                      HealthRegen | ManaRegen | Invincible | Shield | Invisible |
                      Berserk | Lifesteal | Thorns | DodgeBoost | ExpBoost |
                      GoldBoost | ControlImmune,

        /// <summary>所有减益</summary>
        AllNegative = Poisoned | Burning | Frozen | Stunned | Silenced |
                      Disarmed | Blinded | Slowed | Weakened | Bleeding |
                      Cursed | Feared | Taunted | HealingBlock | ArmorBreak | Corroded,

        /// <summary>所有控制效果</summary>
        AllControlEffects = Stunned | Frozen | Feared | Silenced | Disarmed |
                           Blinded | Taunted | Charmed | Polymorphed | Petrified |
                           Sleeping | Confused,

        /// <summary>持续伤害效果</summary>
        AllDamageOverTime = Poisoned | Burning | Bleeding | Corroded,

        /// <summary>移动限制</summary>
        AllMovementImpair = Frozen | Stunned | Slowed | Petrified | Sleeping,

        /// <summary>
        /// 硬控：不可移动 / 不可攻击 / 不可施法（眩晕/冰冻/石化/睡眠）—— FSM 依此切 Stun 态
        /// ★ 2026-09-24 从 UnitBuffComponent 的私有常量提升为公有组合标记：
        ///   新增硬控种类时只改这一处，FSM 仲裁 / 免疫 / 驱散全部自动生效
        /// </summary>
        HardControl = Stunned | Frozen | Petrified | Sleeping,
       
    }

    /// <summary>
    /// Buff类别
    /// </summary>
    public enum BuffCategory
    {
        /// <summary>增益</summary>
        Positive,
        /// <summary>减益</summary>
        Negative,
        /// <summary>控制</summary>
        Control,
        /// <summary>特殊</summary>
        Special
    }

    /// <summary>
    /// Buff 行为标签（对应 BufferData.csv 的 Tags 列，';' 分隔）—— ★ 1B 数据驱动归类的核心
    /// 与 BuffType 的分工：**BuffType = 身份位图（我是谁）；Tags = 行为归类（我该被怎么处理）**。
    ///   ⇒ 新增一个 Buff，只需在 CSV 的 Tags 列写标签，**不必再抢枚举位、不必改任何判定代码**。
    /// ⚠ 未声明 Tags 的行会退回 BuffType 位判定（过渡期兜底），加载期对此报 Error 指名，绝不静默降级。
    /// </summary>
    public static class BuffTags
    {
        /// <summary>控制类 —— 免疫控制（ControlImmune）的判定口径</summary>
        public const string Control = "Control";
        /// <summary>硬控：不可移动 / 攻击 / 施法 —— FSM 切 Stun</summary>
        public const string HardControl = "HardControl";
        /// <summary>沉默：不可施法（仍可移动与普攻）—— FSM 切 Silence</summary>
        public const string Silence = "Silence";
        /// <summary>移动限制：不可移动</summary>
        public const string MovementImpair = "MovementImpair";
        /// <summary>持续伤害：走 Tick 结算</summary>
        public const string DoT = "DoT";
        /// <summary>自带免疫控制：挂上后免疫一切带 Control 标签的Buff</summary>
        public const string ImmuneControl = "ImmuneControl";
    }
}
