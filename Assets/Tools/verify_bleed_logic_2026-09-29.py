# -*- coding: utf-8 -*-
"""
2026-09-29 流血链路离线推演（不依赖 Unity）

忠实复刻下列 C# 逻辑，用改后的真实配置跑一遍，验证「三段都挂 + 可叠加」的结果符合预期：
  · SkillData.EffectTypesBySegment → 每段命中执行哪些效果（2 = AddBuff）
  · AddBuffEffect.Execute          → 遍历 BuffID 调 OnAddBuff
  · CombatUnitController.OnAddBuff → BuffSystem.AddBuff(buffId, casterId)
  · BuffSystemPooled.ApplyBuff     → 叠加/刷新规则（Stackable / MaxStack / Duration）
  · BuffSystemPooled.Update        → 每帧递减 RemainingTime、按 TickInterval 触发 OnBuffTick
  · UnitBuffComponent.OnBuffTickDamage → damage = EffectValue * StackCount → ApplyHit 扣血

★ 目的：在没法进 Play 的情况下，把「配置改对了没有 / 叠加到底能叠几层 / 一共跳多少伤害」
  用可复算的数字说清楚，而不是靠推断。
"""
import json
import os

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Resources", "Config")

skills = json.load(open(os.path.join(BASE, "SkillData.json"), encoding="utf-8-sig"))
buffs = json.load(open(os.path.join(BASE, "BufferData.json"), encoding="utf-8-sig"))

SKILL_ID = 10002
BLEED_ID = 1003

skill = next(r for r in skills if r["ID"] == SKILL_ID)
bleed = next(b for b in buffs if b["Id"] == BLEED_ID)

print("=" * 68)
print("技能 %d「%s」流血链路推演" % (SKILL_ID, skill["Name"]))
print("=" * 68)
print("  EffectTypesBySegment = %s" % skill["EffectTypesBySegment"])
print("  BuffID               = %s" % skill["BuffID"])
print("  SegCount / SegTimings= %s / %s" % (skill["SegCount"], skill["SegTimings"]))
print("  Attack(每段直伤)     = %s" % skill["Attack"])
print()
print("Buff %d「%s」" % (BLEED_ID, bleed["Name"]))
for k in ("Type", "Category", "Duration", "MaxStack", "EffectValue", "TickInterval", "Stackable", "Tags"):
    print("   %-14s = %s" % (k, bleed[k]))
print()

# ── ① 每段是否都会执行 AddBuff ──
seg_addbuff = []
for i, seg in enumerate(skill["EffectTypesBySegment"], 1):
    seg_addbuff.append(2 in seg)
print("① 各段是否含效果 2(AddBuff)：%s" % seg_addbuff)
assert all(seg_addbuff), "并非三段都挂 —— 与用户要求不符"

# ── ② 三段命中后的叠加结果（复刻 ApplyBuff）──
class BuffInst:
    def __init__(self, d):
        self.d = d
        self.stack = 1
        self.remaining = d["Duration"]
        self.next_tick = d["TickInterval"]   # 出池时 NextTickTime = _currentTime + TickInterval

active = {}
for seg in range(3):
    if BLEED_ID in active:
        b = active[BLEED_ID]
        if b.d["Stackable"] and b.stack < b.d["MaxStack"]:
            b.stack += 1
            b.remaining = b.d["Duration"]
            print("② 第%d段命中 → 叠加至 %d 层（时长刷新为 %.1fs）" % (seg + 1, b.stack, b.remaining))
        elif b.d["Duration"] > b.remaining:
            b.remaining = b.d["Duration"]
            print("② 第%d段命中 → 已达上限，仅刷新时长 %.1fs" % (seg + 1, b.remaining))
        else:
            print("② 第%d段命中 → 已达上限且时长未衰减，无变化" % (seg + 1))
    else:
        active[BLEED_ID] = BuffInst(bleed)
        print("② 第%d段命中 → 新增流血，1 层" % (seg + 1))

b = active[BLEED_ID]
print()
print("③ 三段打完：层数 = %d / 上限 %d，剩余时长 = %.1fs，每跳伤害 = %.0f × %d = %.0f"
      % (b.stack, b.d["MaxStack"], b.remaining, b.d["EffectValue"], b.stack,
         b.d["EffectValue"] * b.stack))

# ── ④ 逐帧模拟 Tick（复刻 BuffSystemPooled.Update，dt = 1/60）──
dt = 1.0 / 60.0
t = 0.0
hp = 10000
ticks = []
b.remaining = b.d["Duration"]
b.next_tick = b.d["TickInterval"]
while b.remaining > 0:
    t += dt
    b.remaining -= dt
    if b.remaining <= 0:
        break
    if t >= b.next_tick:
        b.next_tick = t + b.d["TickInterval"]
        dmg = int(b.d["EffectValue"] * b.stack)
        hp -= dmg
        ticks.append((round(t, 2), dmg, hp))

print()
print("④ Tick 明细（dt=1/60 模拟，共 %d 跳）：" % len(ticks))
for tt, dmg, left in ticks:
    print("     t=%.2fs  跳伤 %d  剩余HP %d" % (tt, dmg, left))
total_dot = sum(x[1] for x in ticks)
direct = skill["Attack"] * skill["SegCount"]
print()
print("⑤ 伤害结构：")
print("     直伤  = %d × %d 段 = %d" % (skill["Attack"], skill["SegCount"], direct))
print("     DoT   = %d 跳 × %d = %d" % (len(ticks), int(b.d["EffectValue"] * b.stack), total_dot))
print("     合计  = %d（DoT 占直伤 %.0f%%）" % (direct + total_dot, total_dot * 100.0 / direct))

# ── ⑤ 契约自检 ──
print()
print("=" * 68)
print("契约自检")
print("=" * 68)
checks = [
    (all(seg_addbuff), "三段都挂了效果 2"),
    (bleed["Stackable"] is True, "流血可叠加"),
    (bleed["MaxStack"] >= 3, "上限 >= 3（否则三段挂不满）"),
    (bleed["TickInterval"] > 0, "TickInterval > 0（否则永不跳伤害）"),
    (bleed["EffectValue"] > 0, "EffectValue > 0"),
    (b.stack == 3, "实际叠到 3 层"),
    (len(ticks) > 0, "Tick 真的触发了"),
]
allok = True
for cond, msg in checks:
    print(("  ✔ " if cond else "  ✘ ") + msg)
    allok = allok and cond
print()
print("结论: " + ("OK —— 流血链路自洽" if allok else "FAILED"))
