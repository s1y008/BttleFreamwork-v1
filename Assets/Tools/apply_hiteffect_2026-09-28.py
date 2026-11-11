# -*- coding: utf-8 -*-
"""
2026-09-28 受击特效来源改造（用户拍板）
  ① 受击特效唯一真相源：CSV 列 24(0-based)=表格第25列 HitEffectIds  →  CSV 列 14(0-based)=表格第15列 HitEffect
  ② 取值规则：segIdx <= 特效数 → ids[segIdx-1]；segIdx > 特效数 → 复用最后一个
  ③ 铁定约束：特效数 <= 生效攻击段数（违规加载期 LogError 指名，仅生效段>0 时校验，避免 233 条噪音）

铁律：命中数==1 断言 / 幂等 / 写回重读复验 / CRLF 保持 / 复验忽略注释行
"""
import io
import os
import sys

ROOT = "E:/Project/BttleFreamwork-v1/Assets"
SKILL = os.path.join(ROOT, "Scripts", "BattleFreamwork", "Skill.cs")
SKILLDATA = os.path.join(ROOT, "Scripts", "BattleFreamwork", "ConfigData", "SkillData.cs")


def read(p, keep_eol=False):
    """默认把 CRLF 统一成 LF，便于用 LF 锚点匹配；keep_eol=True 时返回原始文本"""
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        s = f.read()
    return s if keep_eol else s.replace("\r\n", "\n")


def write(p, s):
    """按目标文件原有的换行风格写回（CRLF 文件保持 CRLF）"""
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        raw = f.read()
    if "\r\n" in raw:
        s = s.replace("\n", "\r\n")
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


def strip_comments(src):
    """复验用：去掉 // /// /* 起始行，避免命中自己写的注释"""
    out = []
    for ln in src.split("\n"):
        t = ln.strip()
        if t.startswith("//") or t.startswith("///") or t.startswith("*") or t.startswith("/*"):
            continue
        out.append(ln)
    return "\n".join(out)


# ══════════════════ Skill.cs ══════════════════
SKILL_OLD = '''    /// <summary>
    /// 取指定段对应的受击特效 id（1-based 段号 → <c>HitEffectIds[segIdx - 1]</c>）。
    /// 返回 0 表示「该段没有受击特效」，受击方据此直接跳过播特效。
    /// ⚠ 段号 ≤ 0（非 FSM 路径，例如将来有 Buff Tick 直接调过来）或越界时一律返回 0 ——
    ///   宁可这一段不出特效，也绝不能取到相邻段的特效（这正是删掉 attacker.m_CurSkill 反查后必须显式处理的情形）。
    /// </summary>
    public int GetHitEffectIdForSegment(int segIdx)
    {
        List<int> ids = BaseData.HitEffectIds;
        if (ids == null || ids.Count == 0 || segIdx <= 0 || segIdx > ids.Count)
            return 0;

        return ids[segIdx - 1];
    }'''

SKILL_NEW = '''    /// <summary>
    /// ★ 取第 segIdx 段（1-based）命中时，受击方身上要播的特效 id。
    ///
    /// 【★ 2026-09-28 规则变更】受击特效来源由 CSV 列 24（1-based 第 25 列 HitEffectIds）
    ///   改为 **列 14（1-based 第 15 列 HitEffect）**，列 24 停止读取（字段已删）。
    ///   取值规则同步调整为三段式：
    ///     ① 没配受击特效 / 列表无效 / 段号 &lt;= 0   → 0（该段不播）
    ///     ② segIdx &lt;= 特效数                        → ids[segIdx - 1]（按下标一一对应）
    ///     ③ segIdx &gt;  特效数                       → **复用最后一个**
    ///
    /// 【规则 ③ 是本次核心】策划只配 1 个特效而技能打 3 段是常态，
    ///   三段都播同一个；配 2 个打 3 段则第 3 段沿用第 2 个。
    ///   旧规则「越界返回 0」会让「配 1 个打 3 段」变成只有第 1 段有火花、
    ///   后两段光掉血不冒光 —— 看起来像特效丢失，排查成本极高。
    ///   复用末尾是显式的、可预期的行为：配 1 个就等于「所有段都用这个」。
    ///
    /// 【约束】特效数不得大于生效段数（多配的永远没有段可以播 = 配置错误），
    ///   由 <c>SkillData.ValidateHitEffectCount</c> 在加载期一次性 LogError 指名。
    /// </summary>
    public int GetHitEffectIdForSegment(int segIdx)
    {
        // 取值走封装：空列会被 Str2IntList("") 解析成含一个 0 的 List，直接用会把「没配」当「配了 1 个」
        List<int> ids = BaseData.GetHitEffectIdList();
        if (ids == null || ids.Count == 0 || segIdx <= 0)
            return 0;

        // ② 段号落在特效表范围内 → 按下标一一对应
        if (segIdx <= ids.Count)
        {
            int id = ids[segIdx - 1];
            return id > 0 ? id : 0;
        }

        // ③ 段数多于特效数 → 后续段一律复用最后一个（只配 1 个时即「所有段都播它」）
        int last = ids[ids.Count - 1];
        return last > 0 ? last : 0;
    }'''

# ══════════════════ SkillData.cs ══════════════════

# A1: HitEffect（列14）字段注释 —— 从「无读取者的遗留字段」改为「唯一真相源」
SD_A1_OLD = '''    /// <summary>
    /// ⚠【v5 P10 起已无读取者，事实上的遗留字段】CSV 列 14（0-based）。
    /// 它此前唯一的用途是 <c>Skill.ImmedDamage</c> 把它当第三参传给 <c>OnDamaged</c>，
    /// 而那次传参是无效的（OnDamaged 不读该参数，实际去反查 HitEffectIds）。改造后改传按段算好的
    /// 单个 id，本字段遂无人读。保留是为了不动 CSV 列结构（删列要同步全部 proArray 硬编码下标）。
    /// 受击特效请一律走 <see cref="HitEffectIds"/>（列 24）。
    /// </summary>
    public List<int> HitEffect = new List<int>();'''

SD_A1_NEW = '''    /// <summary>
    /// ★★ 受击特效 id 列表 —— **全工程唯一的受击特效真相源**
    /// （CSV 列 <b>14</b>，0-based；即表格里 1-based 的<b>第 15 列</b> "HitEffect"）。
    /// 分号分隔多 int，按段号顺序与伤害段对应：第 1 段命中播第 1 个，第 2 段播第 2 个……。
    ///
    /// 【★ 2026-09-28 改动】此前受击特效走列 24（表格第 25 列，字段 HitEffectIds 已删），
    ///   而本列自 v5 P10 起是「已无读取者的遗留字段」。现统一收敛到本列，列 24 停止读取。
    ///   （CSV 列结构本身未动 —— 删列会让后面列的下标整体前移，属策划手工操作范围。）
    ///
    /// 【取值规则】由 <c>Skill.GetHitEffectIdForSegment(int segIdx)</c> 实现，此处不重复判定。
    /// 【铁定约束】特效个数 &lt;= 生效攻击段数 —— 多配的那些永远没有段可以播，属配置错误，
    ///   加载期由 <c>ValidateHitEffectCount</c> 一次性 LogError 指名。
    /// 【取值必须走 <c>GetHitEffectIdList()</c>】空列会被 <c>CommonHelper.Str2IntList("")</c>
    ///   解析成**含一个 0 的 List**，直接读 Count 会把「没配」误判成「配了 1 个 id=0 的特效」。
    /// </summary>
    public List<int> HitEffect = new List<int>();'''

# A2: 删除 HitEffectIds 字段块，换成取值封装 + 约束校验
SD_A2_OLD = '''    /// <summary>
    /// ★ 受击特效 id 列表（挂在敌人受击点），按段号索引（<c>segIdx - 1</c>）。
    /// CSV 列 <b>24</b>（⚠ 0-based，分号分隔多 int，例 "10002;10001;10002"）。
    ///
    /// ★ v5 P10：取值完全由攻击方 <c>Skill.GetHitEffectIdForSegment(int segIdx)</c> 负责 ——
    ///   它算好后作为 <c>OnDamaged</c> 的第三参传给受击方。本字段**不再被受击方反查**
    ///   （改造前受击方要读 attacker.m_CurSkill.BaseData.HitEffectIds，属跨对象耦合）。
    /// ⚠ 段号越界 / 该位为 0 / 列表为空 → 一律不播受击特效，**没有**回落单值
    ///   <c>HitEffect</c>（列 14）的兜底。宁缺毋滥。
    /// </summary>
    public List<int> HitEffectIds = new List<int>();'''

SD_A2_NEW = '''    /// <summary>
    /// ★ 受击特效「有效 id」列表 —— 与 <c>GetCastEffectIdList()</c> 同构，规则也一致。
    ///
    /// 【为什么必须封装】<c>CommonHelper.Str2IntList("")</c> 对空串返回**含一个 0 的 List**，
    ///   直接读 <c>HitEffect.Count</c> 会把「这一列空着」误判成「配了 1 个 id=0 的特效」。
    ///   本方法把这个脏数据规则吃掉一次，调用方只看得到「有」或「没有」。
    ///
    /// 【缓存】配置对象加载后全程只读，多个 Skill 实例共享同一份 SkillData，可安全缓存。
    /// 【返回 null】整列没配或全是 0 ⇒ 该技能没有受击特效，调用方据此判定不播。
    /// </summary>
    private List<int> _validHitEffectIds;
    private bool _validHitEffectIdsResolved;

    public List<int> GetHitEffectIdList()
    {
        if (_validHitEffectIdsResolved) return _validHitEffectIds;

        if (HitEffect != null)
        {
            for (int i = 0; i < HitEffect.Count; i++)
            {
                if (HitEffect[i] > 0)
                {
                    _validHitEffectIds = HitEffect;
                    _validHitEffectIdsResolved = true;
                    return _validHitEffectIds;
                }
            }
        }

        // 连 false 结果也要缓存，否则每次都白遍历一遍
        _validHitEffectIds = null;
        _validHitEffectIdsResolved = true;
        return null;
    }

    /// <summary>
    /// ★★ 铁定约束校验：受击特效个数 **不得大于** 生效攻击段数（<c>GetSegmentCount()</c>）。
    ///
    /// 【为什么只在生效段数 &gt; 0 时检查】全表 233 个技能 <c>SegCount=0</c>（策划尚未补配），
    ///   它们本来就不结算任何一段，配几个特效都不会播 —— 若一并报警，233 条噪音会把
    ///   真正需要修的那几条彻底埋掉（与「告警不能被噪音淹没」是同一条原则）。
    ///
    /// 【不做自动修正】是裁掉多配的、还是给技能补段，属于策划决策；
    ///   静默截断会让「我明明配了 3 个怎么只播 2 个」无从追查。
    ///
    /// 【违规后果】多配的那些永远取不到（段号最大只到段数），等于配置浪费而非运行时错误，
    ///   所以这里只报一次，不改变取值行为。
    /// </summary>
    private void ValidateHitEffectCount(SkillData data)
    {
#if UNITY_EDITOR
        int segCount = data.GetSegmentCount();
        if (segCount <= 0) return;

        List<int> ids = data.GetHitEffectIdList();
        int n = (ids != null) ? ids.Count : 0;
        if (n <= segCount) return;

        Debug.LogError($"[SkillData] 技能 {data.ID} 违反受击特效约束：第 15 列(HitEffect) 配了 {n} 个受击特效，" +
            $"但生效攻击段数只有 {segCount} —— 第 {segCount + 1} 个及其后的特效永远播不到。" +
            $"请删掉多配的，或给该技能补上对应段数（列 SegCount / SegTimings）。");
#endif
    }'''

# A3: 校验里的取值改走封装
SD_A3_OLD = "        List<int> hitIds = data.HitEffectIds;"
SD_A3_NEW = "        List<int> hitIds = data.GetHitEffectIdList();"

# A4: 文案 列24 → 列14（两处）
SD_A4_OLD = '"受击特效(列24)"'
SD_A4_NEW = '"受击特效(列14/表格第15列)"'

SD_A5_OLD = "出手特效(列22)与受击特效(列24)都用了 id="
SD_A5_NEW = "出手特效(列22)与受击特效(列14/表格第15列)都用了 id="

# A6: 删除列 24 的解析行（删除型：幂等判据为 old 不存在）
SD_A6_OLD = '''                    HitEffectIds = proArray.Length > 24 ? CommonHelper.Str2IntList(proArray[24]) : new List<int>(),
'''

# A7: 列 14 解析加说明注释
SD_A7_OLD = "                    HitEffect = CommonHelper.Str2IntList(proArray[14]),"
SD_A7_NEW = '''                    // ★ 受击特效(列 14 / 表格第 15 列) —— 2026-09-28 起为唯一真相源
                    //   （原列 24 HitEffectIds 已停止读取；CSV 列结构未动，删列需同步后面所有下标）
                    HitEffect = CommonHelper.Str2IntList(proArray[14]),'''

# A8: 加载期挂上约束校验
SD_A8_OLD = '''                // ★ v5 P6:配置防呆 —— 段数(SegCount)与时机点个数(SegTimings)必须对得上
                ValidateSegCount(info);'''
SD_A8_NEW = '''                // ★ v5 P6:配置防呆 —— 段数(SegCount)与时机点个数(SegTimings)必须对得上
                ValidateSegCount(info);

                // ★ 2026-09-28:受击特效铁定约束 —— 特效个数不得大于生效攻击段数
                ValidateHitEffectCount(info);'''

# A9: 旧注释里对 HitEffectIds 的提及（164 行）
SD_A9_OLD = "///   HitEffectIds 索引边界、配置校验），若各写各的 `SegTimings.Length`，"
SD_A9_NEW = "///   受击特效索引边界、配置校验），若各写各的 `SegTimings.Length`，"

BLOCKS = {
    "Skill.cs": [
        (SKILL_OLD, SKILL_NEW),
    ],
    "SkillData.cs": [
        (SD_A1_OLD, SD_A1_NEW),
        (SD_A2_OLD, SD_A2_NEW),
        (SD_A3_OLD, SD_A3_NEW),
        (SD_A4_OLD, SD_A4_NEW),
        (SD_A5_OLD, SD_A5_NEW),
        (SD_A6_OLD, ""),
        (SD_A7_OLD, SD_A7_NEW),
        (SD_A8_OLD, SD_A8_NEW),
        (SD_A9_OLD, SD_A9_NEW),
    ],
}

PATHS = {"Skill.cs": SKILL, "SkillData.cs": SKILLDATA}

rc = 0
for key, blocks in BLOCKS.items():
    path = PATHS[key]
    print("── %s" % key)
    txt = read(path)

    # ① 逐块断言
    todo = []
    for i, (old, new) in enumerate(blocks):
        already = (old not in txt) if new == "" else (new in txt)
        if already:
            print("  · 第 %d 块已应用，跳过（幂等）" % (i + 1))
            continue
        n = txt.count(old)
        if n != 1:
            print("  ✗ 第 %d 块：期望命中 1 次，实际 %d 次" % (i + 1, n))
            print("    old: %r" % old[:100])
            rc = 1
            continue
        todo.append((old, new))

    if rc != 0:
        continue

    # ② 写入
    for old, new in todo:
        txt = txt.replace(old, new, 1)
    write(path, txt)

    # ③ 写回后重读复验
    disk = read(path)
    code = strip_comments(disk)
    ok = True
    for old, new in blocks:
        if new == "":
            if old in code:
                print("  ✗ 删除块仍有残留: %r" % old[:70])
                ok = False
            continue
        if new not in disk:
            print("  ✗ 找不到 new: %r" % new[:70])
            ok = False
    if ok:
        print("  ✓ 复验通过（%d 块）" % len(blocks))

print("退出码=%d" % rc)
sys.exit(rc)
