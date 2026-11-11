# -*- coding: utf-8 -*-
"""
2026-09-24 1B 代码侧（第二批）：BuffSystemPooled.cs + UnitBuffComponent.cs
（BuffType.cs / BuffData.cs 已由 apply_bufftags_code_2026-09-24.py 改完，本脚本不再碰）
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

# ============ BuffSystemPooled.cs ============
p = os.path.join(ROOT, r"BattleFreamwork\BufferSystem\BuffSystemPooled.cs")
t, crlf = load(p)

# 1) 字段
t = rep(t,
"""        /// <summary>
        /// 延后移除队列：迭代/清算期间收到的移除请求先入队，由最外层持有者统一清算""",
"""        /// <summary>行为标签计数：标签 → 当前带该标签的存活Buff数量（O(1) 查询，随存活Buff重算）</summary>
        private readonly Dictionary<string, int> _tagCounts = new Dictionary<string, int>();

        /// <summary>
        /// 延后移除队列：迭代/清算期间收到的移除请求先入队，由最外层持有者统一清算""",
"bsp-field")

# 2) 查询 API
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

# 3) CanMove
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

# 4) 免疫调用点
t = rep(t,
"""            // 检查免疫（标签位图：命中任一免疫类别即免疫）
            if (IsImmuneTo(buffData.Type))
                return null;""",
"""            // 检查免疫（★ 标签优先：目标带 Control 标签即算控制类）
            if (IsImmuneTo(buffData))
                return null;""",
"bsp-immunecall")

# 5) ApplyBuff 里刷新 tagCounts
t = rep(t,
"""            // 置位：标签位图（组合位整体置上，IsImmuneTo / HasAnyBuff 用「命中任一位」语义，天然兼容）
            _buffFlags |= buffData.Type;

            OnBuffAdded?.Invoke(buff);""",
"""            // 置位：标签位图（组合位整体置上，IsImmuneTo / HasAnyBuff 用「命中任一位」语义，天然兼容）
            _buffFlags |= buffData.Type;

            //★ 事件回调里（SyncControlStateFromBuffs）会查 HasBuffWithTag，故必须在 OnBuffAdded 之前刷新
            RecomputeTagCounts();

            OnBuffAdded?.Invoke(buff);""",
"bsp-applytag")

# 6) 移除时刷新 tagCounts
t = rep(t,
"""            RecomputeFlags();""",
"""            RecomputeFlags();
            //★ 同上：行为标签计数也按存活Buff重算（放在 OnBuffRemoved 之前，回调里要查 HasBuffWithTag）
            RecomputeTagCounts();""",
"bsp-removetag")

# 7) RecomputeTagCounts 方法
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

# 8) IsImmuneTo 改签名 + IsControlTagged
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
            // 免疫控制：自身带免疫（标签或位 —— 将来 CSV 补 ControlImmune 行后即可走标签）
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
        /// ★ Tags 优先（数据驱动：CSV 写 Control 即可，不必抢枚举位）；
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

# ============ UnitBuffComponent.cs ============
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
