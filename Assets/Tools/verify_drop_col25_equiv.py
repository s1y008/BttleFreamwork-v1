# -*- coding: utf-8 -*-
"""
2026-09-28 删列等价性验证（离线实锤，替代暂时跑不了的 Play）。

删列前 CSV 27 列，删列后 26 列。用两套下标分别解析备份与当前文件，
逐行逐字段比对 —— 只要有一个字段不等，就说明下标没同步干净 / 列错位。

旧映射（27 列）：...  [23]SegTimings  [24]HitEffectIds  [25]CastEffectTimings  [26]SegCount
新映射（26 列）：...  [23]SegTimings                [24]CastEffectTimings  [25]SegCount
                （HitEffectIds 整列丢弃）
"""
import os

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Resources\Config"
OLD = os.path.join(ROOT, "SkillData.csv.bak.2026-09-28-drop-col25-26cols")   # 27 列
NEW = os.path.join(ROOT, "SkillData.csv")                                    # 26 列

FIELD_COUNT_OLD = 27
FIELD_COUNT_NEW = 26


def read(path):
    with open(path, "rb") as f:
        raw = f.read()
    txt = raw.decode("utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8")
    txt = txt.replace("\r\n", "\n")
    return [l for l in txt.split("\n") if l != ""]


old_lines = read(OLD)
new_lines = read(NEW)

assert len(old_lines) == len(new_lines), "行数不等 %d vs %d" % (len(old_lines), len(new_lines))

# (字段名, 旧下标, 新下标)；新下标为 None 表示该列已删除，不参与比对
FIELDS = [
    ("ID", 0, 0), ("Name", 1, 1), ("Icon", 2, 2), ("SkillType", 3, 3),
    ("SkillThrowType", 4, 4), ("CDTime", 5, 5), ("AttackDist", 6, 6),
    ("SkillHitTypes", 7, 7), ("SkillHitSharpTypes", 8, 8), ("HitNum", 9, 9),
    ("AttackRadius", 10, 10), ("AttackAngle", 11, 11), ("Warning", 12, 12),
    ("SkillEffectTypes", 13, 13),
    ("HitEffect", 14, 14),           # ← 受击特效唯一真相源，删列前后都必须是列 14
    ("Atk", 15, 15), ("Crit", 16, 16), ("BuffID", 17, 17),
    ("BackDis", 18, 18), ("BackTime", 19, 19), ("AriHeight", 20, 20), ("AriTime", 21, 21),
    ("AttackEffectID", 22, 22), ("SegTimings", 23, 23),
    ("HitEffectIds", 24, None),      # ← 已删除列
    ("CastEffectTimings", 25, 24),   # ← 前移
    ("SegCount", 26, 25),            # ← 前移
]


def cols(line):
    return line.split(",")


diffs = []
row = 0
for i in range(len(old_lines)):
    o = cols(old_lines[i])
    n = cols(new_lines[i])
    if len(o) != FIELD_COUNT_OLD:
        diffs.append("第%d行 旧文件列数=%d != 27" % (i + 1, len(o)))
        continue
    if len(n) != FIELD_COUNT_NEW:
        diffs.append("第%d行 新文件列数=%d != 26" % (i + 1, len(n)))
        continue

    if i == 0:  # 表头：只校验删除列与末尾两列的名字
        assert o[24] == "HitEffectIds", "旧表头第25列不是 HitEffectIds: %r" % o[24]
        assert n[24] == "CastEffectTimings", "新表头第25列不是 CastEffectTimings: %r" % n[24]
        assert n[25] == "SegCount", "新表头第26列不是 SegCount: %r" % n[25]
        continue

    row += 1
    for name, oi, ni in FIELDS:
        if ni is None:
            continue
        if o[oi] != n[ni]:
            diffs.append("第%d行 %s: 旧[%d]=%r != 新[%d]=%r" % (i + 1, name, oi, o[oi], ni, n[ni]))

print("比对数据行数 =", row)
print("比对方字段数 =", sum(1 for f in FIELDS if f[2] is not None))

if diffs:
    print("\n=== 发现 %d 处不一致 ===" % len(diffs))
    for d in diffs[:30]:
        print(" ", d)
    raise SystemExit("FAIL：删列未做到行为等价")

print("\n=== 全部一致 ===")
print("OK 删列后所有字段取值与删列前逐条全等 —— 下标前移已同步干净，无错位")

# 顺带确认：受击特效列（列 14）非空，且 ValidateHitEffectCount 约束成立
not_empty = 0
over = []
for i in range(1, len(new_lines)):
    c = cols(new_lines[i])
    hit = [x for x in c[14].split(";") if x.strip() != "" and int(x) > 0]
    if hit:
        not_empty += 1
    seg = int(c[25]) if c[25].strip() else 0
    timings = [x for x in c[23].split(";") if x.strip() != ""]
    eff_seg = min(seg, len(timings)) if (seg > 0 and timings) else 0
    if eff_seg > 0 and len(hit) > eff_seg:
        over.append((i + 1, len(hit), eff_seg))

print("\n配置了受击特效(列14)的技能数 =", not_empty)
print("违反「特效数 <= 生效段数」的行 =", over if over else "无")
