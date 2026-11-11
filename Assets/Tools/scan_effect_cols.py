# -*- coding: utf-8 -*-
"""只读扫描：统计 SkillData.csv 中 AttackEffectID(22) / CastEffectIds(24) 两列的数据分布。"""
import io, os, collections

p = r"E:\Project\BttleFreamwork-v1\Assets\Resources\Config\SkillData.csv"
with io.open(p, "r", encoding="utf-8-sig", newline="") as f:
    raw = f.read()

lines = raw.replace("\r\n", "\n").split("\n")
header = lines[0].split(",")
print("HEADER_COLS =", len(header))
for i, h in enumerate(header):
    print("   [%2d] %s" % (i, h))

cnt = collections.Counter()
both = []
only_cast = []
only_atk = []
bad_len = []

for ln in lines[1:]:
    if not ln.strip():
        continue
    cols = ln.split(",")
    if len(cols) != len(header):
        bad_len.append((cols[0] if cols else "?", len(cols)))
        continue
    atk = cols[22].strip()
    cast = cols[24].strip()
    timings = cols[26].strip()
    segcount = cols[27].strip()
    if atk and cast:
        cnt["both"] += 1
        both.append((cols[0], atk, cast, timings, segcount))
    elif cast:
        cnt["only_cast"] += 1
        only_cast.append((cols[0], cast, timings, segcount))
    elif atk:
        cnt["only_atk"] += 1
        only_atk.append((cols[0], atk, timings, segcount))
    else:
        cnt["neither"] += 1

print("\nTOTAL DATA ROWS =", sum(cnt.values()))
print("DISTRIBUTION    =", dict(cnt))
print("\n--- BOTH (两列都有值, 需要取舍) : %d 行 ---" % len(both))
for r in both:
    print("  id=%s | AttackEffectID=%s | CastEffectIds=%s | timings=%s | segCount=%s" % r)
print("\n--- ONLY_CAST (只有 CastEffectIds, 删列会丢数据) : %d 行 ---" % len(only_cast))
for r in only_cast[:20]:
    print("  id=%s | CastEffectIds=%s | timings=%s | segCount=%s" % r)
if len(only_cast) > 20:
    print("  ... 其余 %d 行省略" % (len(only_cast) - 20))
print("\n--- ONLY_ATK (只有 AttackEffectID) : %d 行 ---" % len(only_atk))
for r in only_atk[:20]:
    print("  id=%s | AttackEffectID=%s | timings=%s | segCount=%s" % r)
if len(only_atk) > 20:
    print("  ... 其余 %d 行省略" % (len(only_atk) - 20))
if bad_len:
    print("\n列数异常行:", bad_len[:10])
