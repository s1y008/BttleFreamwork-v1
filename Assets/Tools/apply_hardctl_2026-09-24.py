# -*- coding: utf-8 -*-
"""
2026-09-24 第二轮
1) 清理 GameStart.cs 的调试遗留 Debug.LogError(_list.Contains(3)) 及其专用死字段 _list
2) HardControl 与 AllControlEffects 的关系加固：
   a. 校验 HardControl 必须是 AllControlEffects 的子集（防两套口径漂移 → 免疫漏掉硬控）
   b. 控制类Buff若既非硬控也非沉默 → LogWarning 提示（新增控制Buff时不会静默失效）
   c. UnitBuffComponent.IsControlBuff 的 Silenced 判定由「等值」改「命中位」（组合标签位下等值会漏）
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
    assert n == 1, "ANCHOR MISS[%s]: count=%d\n%s" % (tag, n, old[:120])
    return text.replace(old, new, 1)

# ---------- 1. GameStart.cs 清理调试遗留 ----------
p = os.path.join(ROOT, "GameStart.cs")
t, crlf = load(p)

t = rep(t,
"""    List<int>_list=new List<int>(){1,2,3,4,5};
    
    void Start()""",
"""    void Start()""",
"gs-field")

t = rep(t,
"""        CreadNPC();
       Debug.LogError(_list.Contains(3));
        
      // m_Agent""",
"""        CreadNPC();

      // m_Agent""",
"gs-logerror")

save(p, t, crlf)
print("OK GameStart.cs")

# ---------- 2. BuffData.cs 契约加固 ----------
p = os.path.join(ROOT, r"BattleFreamwork\BattleData\BuffData.cs")
t, crlf = load(p)

t = rep(t,
"""        bool inControl  = (buff.Type & BuffType.AllControlEffects) != 0;
        bool inPositive = (buff.Type & BuffType.AllPositive) != 0;
        bool inNegative = (buff.Type & BuffType.AllNegative) != 0;""",
"""        bool inControl  = (buff.Type & BuffType.AllControlEffects) != 0;
        bool inPositive = (buff.Type & BuffType.AllPositive) != 0;
        bool inNegative = (buff.Type & BuffType.AllNegative) != 0;
        bool inHard     = (buff.Type & BuffType.HardControl) != 0;""",
"bd-vars")

t = rep(t,
"""        //反向：只查 Positive。AllPositive 是纯增益位（bit0~15），与任何控制/减益位无交集，不会误报；
        //      Control / Negative 的反向不查 —— AllNegative 历史上就含 Stunned/Silenced 等控制位，查了必然误报
        if (buff.Category != BuffCategory.Positive && inPositive)
        {
            Debug.LogError("[BuffData] ID=" + buff.Id + " 类别=" + buff.Category + "，但 " + buff.Type
                + " 命中了 BuffType.AllPositive（增益位）→ 会被当成增益统计/驱散，请核对 CSV 的 BuffCategory 列");
        }""",
"""        //反向：只查 Positive。AllPositive 是纯增益位（bit0~15），与任何控制/减益位无交集，不会误报；
        //      Control / Negative 的反向不查 —— AllNegative 历史上就含 Stunned/Silenced 等控制位，查了必然误报
        if (buff.Category != BuffCategory.Positive && inPositive)
        {
            Debug.LogError("[BuffData] ID=" + buff.Id + " 类别=" + buff.Category + "，但 " + buff.Type
                + " 命中了 BuffType.AllPositive（增益位）→ 会被当成增益统计/驱散，请核对 CSV 的 BuffCategory 列");
        }

        //★ 两套控制口径的漂移防护：HardControl 必须是 AllControlEffects 的子集。
        //  AllControlEffects = 免疫口径（ControlImmune 免疫哪些）；HardControl = FSM投影口径（哪些会切 Stun 态）。
        //  若某个硬控位不在免疫口径内 ⇒ 它无法被「免疫控制」免疫，属于静默失效，必须报错。
        if ((BuffType.HardControl & ~BuffType.AllControlEffects) != 0)
        {
            Debug.LogError("[BuffData] BuffType.HardControl 含有不在 AllControlEffects 内的位 → "
                + "这些硬控无法被 ControlImmune 免疫，请把缺失的位补进 BuffType.AllControlEffects");
        }

        //★ 软控提示：是控制类、但既不是硬控也不是沉默 ⇒ 免疫/驱散生效，却不会驱动 FSM 切任何控制态。
        //  这一条专门防「新增了一个控制Buff，以为它会定身，结果只有数值没有状态」。
        if (buff.Category == BuffCategory.Control && !inHard && (buff.Type & BuffType.Silenced) == 0)
        {
            Debug.LogWarning("[BuffData] ID=" + buff.Id + " (" + buff.Type + ") 是控制类，但既不在 BuffType.HardControl 内、"
                + "也不含 Silenced 位 ⇒ 免疫/驱散对它有效，但 UnitBuffComponent 不会为它切 Stun/Silence 状态。"
                + "如果它应当定身，请把该类型登记进 BuffType.HardControl");
        }""",
"bd-hardctl")

save(p, t, crlf)
print("OK BuffData.cs")

# ---------- 3. UnitBuffComponent.cs 判定改命中位 ----------
p = os.path.join(ROOT, r"BattleFreamwork\Controller\UnitBuffComponent.cs")
t, crlf = load(p)

t = rep(t,
"""    /// <summary>该Buff是否映射到FSM控制状态（硬控->Stun，沉默->Silence）</summary>
    private bool IsControlBuff(BuffType type)
    {
        return (BuffType.HardControl & type) != 0 || type == BuffType.Silenced;
    }""",
"""    /// <summary>该Buff是否映射到FSM控制状态（硬控->Stun，沉默->Silence）</summary>
    private bool IsControlBuff(BuffType type)
    {
        //★ 一律用「命中位」而不是「等值」：Type 现在是标签位图，组合位（如 Silenced|Slowed）下
        //  等值判断会漏判，导致沉默不切 Silence 状态
        return (BuffType.HardControl & type) != 0 || (BuffType.Silenced & type) != 0;
    }""",
"ubc-iscontrol")

save(p, t, crlf)
print("OK UnitBuffComponent.cs")

print("ALL DONE")
