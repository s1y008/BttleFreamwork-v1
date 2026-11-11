// ★ 技能编辑器 · 字段契约层 —— 全团队共享的**唯一真相源**
//
// 【它的职责】把 SkillData 的 28 个成员（含 Property 与 Field 两种形态）描述成一张元数据表。
//   数据读写层、时间轴控件、Gizmo、主窗口，全都**只认这张表**，任何模块都不许自己再写一份字段清单。
//
// 【为什么要有这一层】直接 enum typeof(SkillData).GetMembers() 也能拿到成员，但拿不到中文名、
//   分组、取值范围、引用哪张表 —— 而编辑器 90% 的价值恰恰在这些"人才能知道"的信息上。
//   把它们集中在一处，加字段时只改这一个文件的末行即可。
//
// 【⚠ 为什么 MemberKind 必须有】SkillData 里 ID / Name / Attack 等是 **Property（有 getter/setter）**，
//   而 SkillType / SegTimings / CastEffectTimings / HitEffect / EffectTypesBySegment 是 **Field（裸字段）**。
//   PropertyInfo 走 GetValue/SetValue，FieldInfo 走 GetValue/SetValue —— 虽然同名，但**是两个不同的类型**，
//   必须先知道是哪种才能正确缓存 Insurance ==> 反射时用错类型会拿到 null，且不报错（铁律 5 禁止的形态）。
//
// 【⚠ SkillType 错位（已知、暂不修，但编辑器必须暴露）】
//   枚举 SkillTypes 是 { NormalSkill=0, AttackSkill=1, SuperSkill=2 }，
//   而 SkillData.json 里现有技能填的是 1 / 2 / 3 —— 值与枚举整体差 1。
//   用户 2026-10-11 已拍板：**暂不修这一列，改用普攻白名单方案**。
//   ⇒ 编辑器用 EnumPopup 挂上去时会**当场显示为错位的值**，这是预期行为，别当成 bug 去"修"。
using System;
using System.Collections.Generic;

namespace SkillEditorKit
{
    /// <summary>字段分组 —— 决定它在右面板的哪一张卡片里出现。</summary>
    public enum FieldGroup
    {
        Basic,      // 基础
        Combat,     // 战斗判定
        Impact,     // 击退/浮空
        Timeline,   // 时间轴（段与时机）
        Effects,    // 特效与 Buff
        Delivery    // 投递方式（弹道）
    }

    /// <summary>成员形态 —— Property 和 Field 的反射写入路径不同，必须先分清。</summary>
    public enum MemberKind
    {
        Property,
        Field
    }

    /// <summary>该字段的值指向哪张外部配置表 —— 决定要不要给下拉选择器。</summary>
    public enum RefTable
    {
        None,
        Buff,         // BufferData.json
        CastEffect,   // EffectData.json（出手特效 AttackEffects）
        HitEffect,    // EffectData.json（受击特效 HitEffect）
        Projectile,   // ProjectileData.json
        EffectType    // Skill.cs 里的 SkillEffectTypes 枚举（效果号）
    }

    /// <summary>
    /// 一个字段的全部元信息。
    /// ⚠ 故意用普通 class 而不是 record —— Unity 2023.2 虽然支持 C# 9，
    ///   但这些对象会长期存活并被各窗口持有，record 的值相等语义在这里只会添乱。
    /// </summary>
    public sealed class SkillFieldMeta
    {
        /// <summary>C# 成员名，必须与 SkillData 里的名字**逐字符一致**（大小写敏感）。</summary>
        public string MemberName;

        /// <summary>界面显示的中文名。</summary>
        public string Label;

        public FieldGroup Group;

        public MemberKind Kind;

        public RefTable Reference;

        /// <summary>true = 只读（如 ID），界面灰显。</summary>
        public bool ReadOnly;

        /// <summary>是否有取值范围约束。</summary>
        public bool HasRange;

        public float Min;

        public float Max;

        /// <summary>鼠标悬停提示 —— 教学场景里这条比字段名更有用。</summary>
        public string Tooltip;

        public SkillFieldMeta(string memberName, string label, FieldGroup group, MemberKind kind,
                              RefTable reference = RefTable.None, bool readOnly = false,
                              float min = 0f, float max = 0f, string tooltip = "")
        {
            MemberName = memberName;
            Label = label;
            Group = group;
            Kind = kind;
            Reference = reference;
            ReadOnly = readOnly;
            Tooltip = tooltip;

            // HasRange 由区间自身判定：min < max 才算有效区间，
            // 避免每个调用点再传一个 bool（两个 bool 容易传反，且无法自证）
            Min = min;
            Max = max;
            HasRange = min < max;
        }

        public override string ToString()
        {
            return $"{Group}/{Label}({MemberName})";
        }
    }

    /// <summary>
    /// ★ 28 个字段的完整清单 —— **加字段时只改这里**。
    ///
    /// 【数据来源】逐字段核对 `Assets/Scripts/BattleFreamwork/ConfigData/SkillData.cs`（2026-10-12 核对）。
    /// 【命名约定】JSON 键名 = C# 属性名（JsonConfigLoader 用 Newtonsoft 直构 List&lt;SkillData&gt;），
    ///   所以这张表的 MemberName 同时也是 JSON 的键名 —— **改这里等于改配置格式**。
    /// </summary>
    public static class SkillFieldMetaTable
    {
        private static readonly List<SkillFieldMeta> _rows = new List<SkillFieldMeta>
        {
            // ── 基础 ────────────────────────────────────────────
            new SkillFieldMeta("ID", "技能 ID", FieldGroup.Basic, MemberKind.Property,
                readOnly: true, tooltip: "主键。SkillData 的成员名是全大写 ID，不是 Id —— 写错会 CS1061"),
            new SkillFieldMeta("Name", "技能名称", FieldGroup.Basic, MemberKind.Property),
            new SkillFieldMeta("Icon", "图标资源", FieldGroup.Basic, MemberKind.Property,
                tooltip: "目前仅 WarriorController 使用，UI 侧暂未读取"),
            new SkillFieldMeta("SkillType", "技能类型", FieldGroup.Basic, MemberKind.Field,
                tooltip: "⚠ 已知错位：枚举 0/1/2 而配置填 1/2/3，2026-10-11 拍板暂不修（走普攻白名单）"),
            new SkillFieldMeta("SkillThrowType", "投技优先级", FieldGroup.Basic, MemberKind.Property,
                min: 0, max: 5, tooltip: "0敌普攻 1玩家普攻 2敌技能 3玩家技能 4敌霸体 5玩家投技/怒气"),
            new SkillFieldMeta("CDTime", "冷却时间(秒)", FieldGroup.Basic, MemberKind.Property, min: 0, max: 30),

            // ── 战斗判定 ────────────────────────────────────────
            new SkillFieldMeta("AttackDist", "攻击距离", FieldGroup.Combat, MemberKind.Property,
                min: 0, max: 20, tooltip: "进入该距离才会发起攻击。Scene Gizmo 会画这个值的外环"),
            new SkillFieldMeta("HitType", "击中类型", FieldGroup.Combat, MemberKind.Property,
                tooltip: "0选敌 1敌范围 2选友 3友范围 4自己"),
            new SkillFieldMeta("SkillHitSharpType", "攻击范围类型", FieldGroup.Combat, MemberKind.Property,
                tooltip: "0对目标 1圆形 2扇形 3前方"),
            new SkillFieldMeta("HitNum", "命中数量", FieldGroup.Combat, MemberKind.Property,
                min: -1, max: 20,
                tooltip: "-1 = 不限命中数量（Skill.cs:734 只在 > -1 时才限制）；0 = 一个目标都打不到，别乱填"),
            new SkillFieldMeta("AttackRadius", "攻击半径", FieldGroup.Combat, MemberKind.Property,
                min: 0, max: 20, tooltip: "圆形/扇形范围的半径。Scene Gizmo 会画这个值"),
            new SkillFieldMeta("AttackAngle", "扇形角度", FieldGroup.Combat, MemberKind.Property,
                min: 0, max: 360, tooltip: "仅当攻击范围类型 = 扇形时生效。Scene Gizmo 会画这个值"),
            new SkillFieldMeta("Warning", "警示距离", FieldGroup.Combat, MemberKind.Property,
                min: 0, max: 20, tooltip: "警示圈半径，Scene Gizmo 会用虚线环画出"),

            // ── 数值 ────────────────────────────────────────────
            new SkillFieldMeta("Attack", "攻击力", FieldGroup.Combat, MemberKind.Property,
                min: 0, max: 500,
                tooltip: "滑杆范围按全表实际分布设定（现表最大 500，2026-10-12 统计）"),
            new SkillFieldMeta("Crit", "暴击率", FieldGroup.Combat, MemberKind.Property,
                min: 0, max: 1, tooltip: "配置约定是 0~1 的小数，不是 0~100"),

            // ── 击退 / 浮空 ─────────────────────────────────────
            new SkillFieldMeta("BackDis", "后退距离", FieldGroup.Impact, MemberKind.Property, min: 0, max: 20),
            new SkillFieldMeta("BackTime", "后退时间", FieldGroup.Impact, MemberKind.Property, min: 0, max: 5),
            new SkillFieldMeta("AriHeight", "浮空高度", FieldGroup.Impact, MemberKind.Property, min: 0, max: 20),
            new SkillFieldMeta("AriTime", "浮空时间", FieldGroup.Impact, MemberKind.Property, min: 0, max: 5),

            // ── 时间轴（这三个正是编辑器要重点呈现的） ──────────
            new SkillFieldMeta("SegCount", "伤害段数", FieldGroup.Timeline, MemberKind.Property,
                min: 0, max: 12, tooltip: "0 = 不分段。真实生效段数 = min(SegCount, SegTimings 长度)"),
            new SkillFieldMeta("SegTimings", "段时机表", FieldGroup.Timeline, MemberKind.Field,
                tooltip: "★ 归一化 0~1 的动画进度，不是秒。动画改长时出手点会自动跟随"),
            new SkillFieldMeta("CastEffectTimings", "出手特效时机", FieldGroup.Timeline, MemberKind.Field,
                tooltip: "★ 归一化 0~1，与 AttackEffects 按下标一一配对"),

            // ── 特效与 Buff ─────────────────────────────────────
            new SkillFieldMeta("AttackEffects", "出手特效 id", FieldGroup.Effects, MemberKind.Property,
                reference: RefTable.CastEffect, tooltip: "挂在施法者武器挂点上；id<=0 视为未配"),
            new SkillFieldMeta("HitEffect", "受击特效 id", FieldGroup.Effects, MemberKind.Field,
                reference: RefTable.HitEffect, tooltip: "★ 全工程唯一的受击特效真相源，按段号顺序对应"),
            new SkillFieldMeta("BuffID", "附带 Buff", FieldGroup.Effects, MemberKind.Property,
                reference: RefTable.Buff, tooltip: "⚠ 现有配置里 11 个技能的这一列仍为空"),

            // ── 逐段效果（既是时间轴数据，也是效果引用） ─────────
            new SkillFieldMeta("EffectTypesBySegment", "逐段效果号", FieldGroup.Timeline, MemberKind.Field,
                reference: RefTable.EffectType,
                tooltip: "用 | 分段、; 分效。★ 没有 | 时整串只在第 1 段生效"),

            // ── 投递方式（2026-10-10 弹道方案新增） ─────────────
            new SkillFieldMeta("DeliveryType", "投递方式", FieldGroup.Delivery, MemberKind.Property,
                tooltip: "0 即时（缺省，全部既有技能）／ 1 弹道"),
            new SkillFieldMeta("ProjectileId", "投掷物 id", FieldGroup.Delivery, MemberKind.Property,
                reference: RefTable.Projectile, tooltip: "仅 DeliveryType=1 时生效；0 = 未配"),
        };

        /// <summary>全部字段（只读视图）。</summary>
        public static IReadOnlyList<SkillFieldMeta> All
        {
            get { return _rows; }
        }

        /// <summary>取某个分组的全部字段，按清单顺序。</summary>
        public static List<SkillFieldMeta> ByGroup(FieldGroup group)
        {
            List<SkillFieldMeta> list = new List<SkillFieldMeta>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Group == group) list.Add(_rows[i]);
            }
            return list;
        }

        /// <summary>按成员名查找；找不到返回 null（调用方必须判空，不许静默跳过）。</summary>
        public static SkillFieldMeta Find(string memberName)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].MemberName == memberName) return _rows[i];
            }
            return null;
        }

        /// <summary>六个分组的固定展示顺序 —— 界面按此排列，不随枚举值漂移。</summary>
        public static readonly FieldGroup[] GroupOrder = new FieldGroup[]
        {
            FieldGroup.Basic,
            FieldGroup.Combat,
            FieldGroup.Impact,
            FieldGroup.Timeline,
            FieldGroup.Effects,
            FieldGroup.Delivery
        };

        /// <summary>分组的中文标题。</summary>
        public static string GroupName(FieldGroup group)
        {
            switch (group)
            {
                case FieldGroup.Basic: return "基础信息";
                case FieldGroup.Combat: return "战斗判定";
                case FieldGroup.Impact: return "击退与浮空";
                case FieldGroup.Timeline: return "施法时间轴";
                case FieldGroup.Effects: return "特效与 Buff";
                case FieldGroup.Delivery: return "投递方式";
                default: return group.ToString();
            }
        }
    }

    /// <summary>
    /// 时间轴轨道的定义 —— 三个轨道共享同一条 0~1 归一化横轴，
    /// 每条轨道的数据来源不同，这是时间轴控件与 SkillData 之间唯一的耦合面。
    /// </summary>
    public enum TimelineTrackId
    {
        Segment,      // 伤害段 —— 数据来自 SegTimings
        CastEffect,   // 出手特效 —— 数据来自 CastEffectTimings
        EffectType    // 逐段效果号 —— 数据来自 EffectTypesBySegment
    }

    /// <summary>时间轴控件与外界约定的配色（避开 Odin 主题色，保证深浅两种皮肤下都可读）。</summary>
    /// <summary>
    /// ★ 效果号（SkillEffectTypes）的中文目录 —— 界面下拉与「未实现」标注的**唯一真相源**。
    ///
    /// 【为什么中文名必须手工维护一份】
    ///   Skill.cs 里的中文只写在**注释**里（如 `HitOut = 5, //浮空`），反射拿不到；
    ///   而「5 是浮空、10 是拉近」这类语义只有人知道 —— 这恰恰是契约层存在的理由。
    ///
    /// 【为什么「是否未实现」不在本表写死】
    ///   KnownStubIds 会随着填桩不断减少，在这里抄一份就会**立刻腐化**成假信息。
    ///   所以一律实时问 SkillEffectRegistry.IsKnownStub（运行时的唯一真相源）。
    ///   ⇒ 填完某个桩却忘了从 KnownStubIds 里删，界面会一直标红 —— 那正是**它该报的错**。
    ///
    /// 【为什么标红是硬要求】
    ///   19 个效果号目前是空桩（打出去没反应）。若不标红，策划选中「5 浮空」后
    ///   会得到「配了却什么都没发生」—— 属于铁律 5 明令禁止的静默降级。
    /// </summary>
    public static class SkillEffectTypeCatalog
    {
        /// <summary>效果号 → 中文名。下标 = SkillEffectTypes 的数值（0~22）。</summary>
        private static readonly string[] _labels = new string[]
        {
            "立即伤害",       // 0  ImmedDamage
            "立即治疗",       // 1  ImmedHeal
            "添加Buff",       // 2  AddBuff
            "嘲讽",           // 3  Sneer
            "僵直",           // 4  Rigor
            "浮空",           // 5  HitOut
            "受击后退",       // 6  HitBack
            "击飞(抛物线)",   // 7  HitOutDown
            "受击倒地",       // 8  HitDown
            "眩晕",           // 9  Dizz
            "拉近",           // 10 ForwardMove
            "持续后退受击",   // 11 OneUnitHitBack
            "设置单位可用",   // 12 OnSetUnitEnable
            "暴力抓取",       // 13 ViolenceToGrab
            "浮空受击",       // 14 AirHit
            "倒地持续受击",   // 15 DownHit
            "单位移动",       // 16 OnUnitMove
            "移动到目标并弹跳", // 17 OnUnitMoveTargetAndJump
            "移动到目标",     // 18 OnUnitMoveTarget
            "僵直(单体)",     // 19 Frozen
            "测试旋转",       // 20 SetRotate
            "浮空受击(玩家)", // 21 AirHitForPlayer
            "受击表现"        // 22 Hure
        };

        /// <summary>效果号总数（SkillEffectTypes 共 23 个值：0~22）。</summary>
        public static int Count { get { return _labels.Length; } }

        /// <summary>
        /// 取中文名。越界返回空串 —— 越界的效果号是配置错误，
        /// 由 <see cref="SkillEffectRegistry"/> 在运行时报错指名，本类不越俎代庖。
        /// </summary>
        public static string LabelOf(int value)
        {
            if (value < 0 || value >= _labels.Length) return "";
            return _labels[value];
        }

        /// <summary>下拉里显示的完整文本：「5 浮空」；未实现则追加「（未实现）」。</summary>
        public static string OptionLabel(int value)
        {
            string name = LabelOf(value);
            if (string.IsNullOrEmpty(name)) name = "未知";
            return value + " " + name + (IsStub(value) ? "（未实现）" : "");
        }

        /// <summary>
        /// 该效果号当前是不是**空桩**（配了也不会有任何反应）。
        /// 实时问 SkillEffectRegistry —— 不在此处维护副本，避免 KnownStubIds 变动后信息腐化。
        /// </summary>
        public static bool IsStub(int value)
        {
            return SkillEffectRegistry.IsKnownStub(value);
        }
    }

    public static class TimelinePalette
    {
        /// <summary> Background of track negative space</summary>
        public static readonly UnityEngine.Color TrackBg = new UnityEngine.Color(0.22f, 0.23f, 0.27f);
        /// <summary>尚未触发的时机点</summary>
        public static readonly UnityEngine.Color Pending = new UnityEngine.Color(0.94f, 0.62f, 0.15f);
        /// <summary>已经触发过的时机点</summary>
        public static readonly UnityEngine.Color Fired = new UnityEngine.Color(0.29f, 0.78f, 0.65f);
        /// <summary>播放头（Play 实时刷新）</summary>
        public static readonly UnityEngine.Color Playhead = new UnityEngine.Color(0.89f, 0.29f, 0.29f);
        /// <summary>轨道标签文字</summary>
        public static readonly UnityEngine.Color Label = new UnityEngine.Color(0.62f, 0.64f, 0.68f);
    }
}
