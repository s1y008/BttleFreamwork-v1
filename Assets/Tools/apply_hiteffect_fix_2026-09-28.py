# -*- coding: utf-8 -*-
"""
修复 2026-09-28：ValidateHitEffectCount 被误插进 SkillData 类（10-412），
而调用点 763 在 SkillDataManager 类（413 起）⇒ CS0103。
本脚本把它搬到 SkillDataManager 类内（ValidateEffectTimings 之前）。

★ 两处文本刻意做成不同（插入版首行注释加「【SkillDataManager 配置防呆】」），
  否则「删除块」与「插入块」文本相同，幂等判据会互相误判导致重复删除/插入。
"""
import io
import sys

TARGET = "E:/Project/BttleFreamwork-v1/Assets/Scripts/BattleFreamwork/ConfigData/SkillData.cs"


def read(p):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        return f.read().replace("\r\n", "\n")


def write(p, s):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        raw = f.read()
    if "\r\n" in raw:
        s = s.replace("\n", "\r\n")
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


BODY = '''    /// <summary>
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
    }
'''

# 插入版：首行注释加前缀，使其与待删除版文本不同（避免幂等互判）
INSERT_BODY = BODY.replace(
    "    /// ★★ 铁定约束校验",
    "    /// ★★ 【SkillDataManager 配置防呆】铁定约束校验",
)

ANCHOR = "    private void ValidateEffectTimings(SkillData data)\n"

# 删除版后面还带一个空行（它原来位于 SkillData 类末尾）
DELETE_OLD = BODY + "\n"
INSERT_NEW = INSERT_BODY + "\n" + ANCHOR

txt = read(TARGET)
changed = False

# ① 删除 SkillData 类内的那份
if DELETE_OLD in txt:
    n = txt.count(DELETE_OLD)
    if n != 1:
        print("✗ 删除块命中 %d 次（期望 1）" % n)
        sys.exit(1)
    txt = txt.replace(DELETE_OLD, "", 1)
    print("· 已从 SkillData 类移除（原位置）")
    changed = True
else:
    print("· 删除块不存在，跳过（幂等）")

# ② 插入 SkillDataManager 类内
if INSERT_NEW in txt:
    print("· 插入块已存在，跳过（幂等）")
else:
    n = txt.count(ANCHOR)
    if n != 1:
        print("✗ 锚点命中 %d 次（期望 1）" % n)
        sys.exit(1)
    txt = txt.replace(ANCHOR, INSERT_NEW, 1)
    print("· 已插入 SkillDataManager 类（ValidateEffectTimings 之前）")
    changed = True

if changed:
    write(TARGET, txt)

# ③ 写回复验
disk = read(TARGET)
ok = True
if DELETE_OLD in disk:
    print("✗ 删除未完成")
    ok = False
if INSERT_NEW not in disk:
    print("✗ 插入未生效")
    ok = False
if disk.count("private void ValidateHitEffectCount(SkillData data)") != 1:
    print("✗ 方法定义应为 1 处，实际 %d"
          % disk.count("private void ValidateHitEffectCount(SkillData data)"))
    ok = False

# 类归属断言：方法定义必须在 SkillDataManager 之后
idx_cls = disk.index("public class SkillDataManager")
idx_m = disk.index("private void ValidateHitEffectCount(SkillData data)")
if idx_m < idx_cls:
    print("✗ 方法仍在 SkillData 类内")
    ok = False
else:
    print("· 类归属正确：位于 SkillDataManager 内")

print("✓ 复验通过" if ok else "✗ 复验失败")
sys.exit(0 if ok else 1)
