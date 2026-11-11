# -*- coding: utf-8 -*-
"""
2026-09-24 1B 代码侧：Tags 列成为「行为归类」的真相源
原则：Tags 优先（数据驱动，新增Buff只改CSV）；该行未声明 Tags 时退回 BuffType 位判定（过渡期兜底），
      加载期对未声明的情况报 Error 指名，绝不静默降级。
"""
import io, os

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts"

def load(path):
    with io.open(path, "r", encoding="utf-8", newline="") as f:
        raw = f.read()
    return raw.replace("\r\n", "\n"), ("\r\n" in raw)

def save(path, text, crlf):
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text.replace("\n", "\r\n") if crlf else text)

def rep(text, old, new, tag):
    n = text.count(old)
    assert n == 1, "ANCHOR MISS[%s]: count=%d\n%s" % (tag, n, old[:150])
    return text.replace(old, new, 1)

# ============ 1. BuffType.cs：新增 BuffTags 常量类 ============
p = os.path.join(ROOT, r"BattleFreamwork\BufferSystem\BuffType.cs")
t, crlf = load(p)

t = rep(t,
"""    public enum BuffCategory
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
}""",
"""    public enum BuffCategory
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
}""",
"bt-tagsclass")

save(p, t, crlf)
print("OK BuffType.cs")

# ============ 2. BuffData.cs：Tags 字段 + 解析 + 契约 ============
p = os.path.join(ROOT, r"BattleFreamwork\BattleData\BuffData.cs")
t, crlf = load(p)

# 2.1 解析 Tags 列
t = rep(t,
"""            buff.Description = str[13];""",
"""            buff.Description = str[13];

            //★ 第15列 Tags = 行为标签（';' 分隔）。旧CSV没有这一列时退化为空标签集：
            //  此时由 BuffType 位判定兜底，加载期会针对控制类报 Error 指名，绝不静默降级
            buff.Tags = ParseTags(str.Length > 14 ? str[14] : null);""",
"bd-parse")

# 2.2 ParseTags 工具方法（放在 ValidateCategoryContract 之前）
t = rep(t,
"""    /// <summary>
    /// 类别↔标签位 双向契约校验（加载期一次性，零运行时开销）""",
"""    /// <summary>
    /// 解析 Tags 列：';' 分隔，去空白、去空项；缺失或全空返回空数组（不是 null，调用方免判空）
    /// </summary>
    private static string[] ParseTags(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return new string[0];
        }
        string[] parts = raw.Split(';');
        List<string> list = new List<string>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string tag = parts[i].Trim();
            if (tag.Length > 0)
            {
                list.Add(tag);
            }
        }
        return list.Count == 0 ? new string[0] : list.ToArray();
    }

    /// <summary>
    /// 类别↔标签位 双向契约校验（加载期一次性，零运行时开销）""",
"bd-parsetags")

# 2.3 契约：控制类必须声明行为标签
t = rep(t,
"""        //★ 软控提示：是控制类、但既不是硬控也不是沉默 ⇒ 免疫/驱散生效，却不会驱动 FSM 切任何控制态。""",
"""        //★ Tags（行为标签）契约：控制类必须声明行为标签（Control / HardControl / Silence 至少其一）。
        //  没声明 ⇒ 只能退回 BuffType 位判定（过渡期兜底），报 Error 指名 —— 等将来删掉位兜底，
        //  这些行会立刻失效，所以现在就必须点名
        if (buff.Category == BuffCategory.Control
            && !buff.HasTag(BuffTags.Control)
            && !buff.HasTag(BuffTags.HardControl)
            && !buff.HasTag(BuffTags.Silence))
        {
            Debug.LogError("[BuffData] ID=" + buff.Id + " 类别=Control，但 Tags 列没有声明任何行为标签"
                + "（Control / HardControl / Silence）→ 目前只能退回 BuffType 位判定，请在 Tags 列补上，"
                + "例如：Control;HardControl;MovementImpair");
        }

        //★ 软控提示：是控制类、但既不是硬控也不是沉默 ⇒ 免疫/驱散生效，却不会驱动 FSM 切任何控制态。""",
"bd-tagcontract")

# 2.4 BuffData 加 Tags 属性 + HasTag
t = rep(t,
"""    /// <summary>特效路径</summary>
    public string EffectName { get; set; }

}""",
"""    /// <summary>特效路径</summary>
    public string EffectName { get; set; }

    /// <summary>
    /// 行为标签（来自 CSV 的 Tags 列，';' 分隔）—— ★ 行为归类的真相源
    /// 恒非 null（未配置时是空数组）；非空时一切行为判定（硬控/沉默/移动限制/免疫/DoT）以它为准
    /// </summary>
    public string[] Tags { get; set; } = new string[0];

    /// <summary>是否带有指定行为标签（大小写不敏感）</summary>
    public bool HasTag(string tag)
    {
        if (string.IsNullOrEmpty(tag) || Tags == null)
        {
            return false;
        }
        for (int i = 0; i < Tags.Length; i++)
        {
            if (string.Equals(Tags[i], tag, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

}""",
"bd-field")

save(p, t, crlf)
print("OK BuffData.cs")

# ============ 3. BuffSystemPooled.cs：tagCounts + 判定改标签优先 ============
p = os.path.join(ROOT, r"BattleFreamwork\BufferSystem\BuffSystemPooled.cs")
t, crlf = load(p)

# 3.1 字段
t = rep(t,
"""        /// <summary>延后移除队列：迭代/清算期间收到的移除请求先入队，由最外层持有者统一清算""",
"""        /// <summary>行为标签计数：标签 → 当前带该标签的存活Buff数量（O(1) 查询，随存活Buff重算）</summary>
        private readonly Dictionary<string, int> _tagCounts = new Dictionary<string, int>();

        /// <summary>延后移除队列：迭代/清算期间收到的移除请求先入队，由最外层持有者统一清算""",
"bsp-field")

# 3.2 查询 API
t = rep(t,
"""        /// <summary>
        /// 检查是否有指定Buff（位运算，O(1)复杂度）
        /// </summary>""",
"""        /// <summary>
        /// ★ 是否存在带指定**行为标签**的Buff（数据驱动判定的首选入口）
        /// </summary>
        /// <param name="tag">见 BuffTags 常量（Control / HardControl / Silence / MovementImpair / DoT）</param>
        public bool HasBuffWithTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }
            int count;
            return _tagCounts.TryGetValue(tag, out count) && count > 0;
        }

        /// <summary>
        /// 检查是否有指定Buff（位运算，O(1)复杂度）
        /// </summary>""",
"bsp-hastag")

# 3.3 CanMove 接入标签
t = rep(t,
"""        public bool CanMove()
        {
            return !HasAnyBuff(BuffType.AllMovementImpair);
        }""",
"""        public bool CanMove()
        {
            //★ 标签优先（数据驱动），未声明 Tags 的行退回位判定
            return !HasBuffWithTag(BuffTags.MovementImpair) && !HasAnyBuff(BuffType.AllMovementImpair);
        }""",
"bsp-canmove")

# 3.4 ApplyBuff 里 tagCounts 更新 + 免疫调用改签名
t = rep(t,
"""            // 检查免疫（标签位图：命中任一免疫类别即免疫）
            if (IsImmuneTo(buffData.Type))
                return null;""",
"""            // 检查免疫（★ 标签优先：目标带 Control 标签即算控制类）
            if (IsImmuneTo(buffData))
                return null;""",
"bsp-immunecall")

t = rep(t,
"""            // 置位：标签位图（组合位整体置上，IsImmuneTo / HasAnyBuff 用「命中任一位」语义，天然兼容）
            _buffFlags |= buffData.Type;

            OnBuffAdded?.Invoke(buff);""",
"""            // 置位：标签位图（组合位整体置上，IsImmuneTo / HasAnyBuff 用「命中任一位」语义，天然兼容）
            _buffFlags |= buffData.Type;

            //★ 事件回调里（SyncControlStateFromBuffs）要查 HasBuffWithTag，故必须在 OnBuffAdded 之前刷新
            RecomputeTagCounts();

            OnBuffAdded?.Invoke(buff);""",
"bsp-applytag")

t = rep(t,
"""            //★ 标签位可被多个Buff共享 ⇒ 不能用 &= ~Type 清位（会把仍生效的同位Buff一起清掉）
            //  改为按当前存活Buff全量重算：Buff数量是十量级，移除是低频操作，开销可忽略
            RecomputeFlags();""",
"""            //★ 标签位可被多个Buff共享 ⇒ 不能用 &= ~Type 清位（会把仍生效的同位Buff一起清掉）
            //  改为按当前存活Buff全量重算：Buff数量是十量级，移除是低频操作，开销可忽略
            RecomputeFlags();
            //★ 同上：行为标签计数也按存活Buff重算（在 OnBuffRemoved 之前，回调里要查 HasBuffWithTag）
            RecomputeTagCounts();""",
"bsp-removetag")

# 3.5 RecomputeTagCounts 方法
t = rep(t,
"""        /// <summary>
        /// 按当前存活Buff重算标签位图（移除Buff后调用）
        /// </summary>
        private void RecomputeFlags()""",
"""        /// <summary>
        /// 按当前存活Buff重算「行为标签计数」
        /// 与 RecomputeFlags 同理：标签可被多个Buff共享，不能做增量减一，全量重算才不会错
        /// </summary>
        private void RecomputeTagCounts()
        {
            _tagCounts.Clear();
            for (int i = 0; i < _activeBuffs.Count; i++)
            {
                string[] tags = _activeBuffs[i]._BuffData.Tags;
                if (tags == null)
                {
                    continue;
                }
                for (int j = 0; j < tags.Length; j++)
                {
                    int count;
                    _tagCounts.TryGetValue(tags[j], out count);
                    _tagCounts[tags[j]] = count + 1;
                }
            }
        }

        /// <summary>
        /// 按当前存活Buff重算标签位图（移除Buff后调用）
        /// </summary>
        private void RecomputeFlags()""",
"bsp-recomputetags")

# 3.6 IsImmuneTo 改签名
t = rep(t,
"""        /// <summary>
        /// 检查是否免疫指定Buff
        /// </summary>
        private bool IsImmuneTo(BuffType buffType)   //中毒
        {
            // 免疫控制
            if (HasBuff(BuffType.ControlImmune))
            {
                if ((buffType & BuffType.AllControlEffects) != 0)
                    return true;
            }

            // 无敌免疫所有减益
            if (HasBuff(BuffType.Invincible))
            {
                if ((buffType & BuffType.AllNegative) != 0)
                    return true;
            }

            return false;
        }""",
"""        /// <summary>
        /// 检查是否免疫指定Buff
        /// </summary>
        private bool IsImmuneTo(BuffData data)   //中毒
        {
            // 免疫控制：自身带免疫（标签或位，将来 CSV 补 ControlImmune 行后走标签）
            if (HasBuffWithTag(BuffTags.ImmuneControl) || HasBuff(BuffType.ControlImmune))
            {
                if (IsControlTagged(data))
                    return true;
            }

            // 无敌免疫所有减益
            if (HasBuff(BuffType.Invincible))
            {
                if (data.Category == BuffCategory.Negative || (data.Type & BuffType.AllNegative) != 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 该Buff是否属于「控制类」（免疫控制的判定口径）
        /// ★ Tags 优先（数据驱动：CSV 写 Control 即可，不用抢枚举位）；
        ///   该行没声明 Tags 时退回 BuffType 位判定 —— 加载期已对这类行报 Error 指名
        /// </summary>
        private static bool IsControlTagged(BuffData data)
        {
            if (data.Tags != null && data.Tags.Length > 0)
            {
                return data.HasTag(BuffTags.Control)
                    || data.HasTag(BuffTags.HardControl)
                    || data.HasTag(BuffTags.Silence);
            }
            return (data.Type & BuffType.AllControlEffects) != 0;
        }""",
"bsp-immune")

save(p, t, crlf)
print("OK BuffSystemPooled.cs")

# ============ 4. UnitBuffComponent.cs：硬控/沉默判定改标签优先 ============
p = os.path.join(ROOT, r"BattleFreamwork\Controller\UnitBuffComponent.cs")
t, crlf = load(p)

t = rep(t,
"""    /// <summary>是否存在硬控Buff</summary>
    public bool HasHardControlBuff()
    {
        return m_Unit != null && m_Unit.BuffSystem.HasAnyBuff(BuffType.HardControl);
    }

    /// <summary>是否存在沉默Buff</summary>
    public bool HasSilenceBuff()
    {
        return m_Unit != null && m_Unit.BuffSystem.HasBuff(BuffType.Silenced);
    }

    /// <summary>该Buff是否映射到FSM控制状态（硬控->Stun，沉默->Silence）</summary>
    private bool IsControlBuff(BuffType type)
    {
        //★ 一律用「命中位」而不是「等值」：Type 现在是标签位图，组合位（如 Silenced|Slowed）下
        //  等值判断会漏判，导致沉默不切 Silence 状态
        return (BuffType.HardControl & type) != 0 || (BuffType.Silenced & type) != 0;
    }""",
"""    /// <summary>是否存在硬控Buff</summary>
    public bool HasHardControlBuff()
    {
        if (m_Unit == null)
        {
            return false;
        }
        //★ Tags 优先（数据驱动：CSV 写 HardControl 即可）；该行没声明 Tags 时退回位判定
        return m_Unit.BuffSystem.HasBuffWithTag(BuffTags.HardControl)
            || m_Unit.BuffSystem.HasAnyBuff(BuffType.HardControl);
    }

    /// <summary>是否存在沉默Buff</summary>
    public bool HasSilenceBuff()
    {
        if (m_Unit == null)
        {
            return false;
        }
        return m_Unit.BuffSystem.HasBuffWithTag(BuffTags.Silence)
            || m_Unit.BuffSystem.HasBuff(BuffType.Silenced);
    }

    /// <summary>该Buff是否映射到FSM控制状态（硬控->Stun，沉默->Silence）</summary>
    private bool IsControlBuff(Buff buff)
    {
        if (buff == null || buff._BuffData == null)
        {
            return false;
        }
        BuffData d = buff._BuffData;

        //★ Tags 优先（数据驱动）；该行没声明 Tags 时退回位判定
        //  （退回时一律用「命中位」不用「等值」：Type 是标签位图，组合位下等值会漏判）
        if (d.Tags != null && d.Tags.Length > 0)
        {
            return d.HasTag(BuffTags.HardControl) || d.HasTag(BuffTags.Silence);
        }
        return (BuffType.HardControl & d.Type) != 0 || (BuffType.Silenced & d.Type) != 0;
    }""",
"ubc-judge")

n = t.count("if (IsControlBuff(buff._BuffData.Type))")
assert n == 2, "IsControlBuff 调用点应恰好 2 处，实为 %d" % n
t = t.replace("if (IsControlBuff(buff._BuffData.Type))", "if (IsControlBuff(buff))")

save(p, t, crlf)
print("OK UnitBuffComponent.cs")

print("ALL DONE")
