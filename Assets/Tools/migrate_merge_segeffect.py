# -*- coding: utf-8 -*-
"""
把 SegEffectTypes(列27) 合并进 SkillEffectTypes(列13)，并删除列 27。

新语法（列 13 "SkillEffectTypes"）：
    含 |  → 按 | 分段，段内 ; 分效     例 "0|0;5|6"  第1段掉血 / 第2段掉血+浮空 / 第3段只后退
    无 |  → 整串视为「第 1 段」的全部表现，后续段无效果

迁移规则（保证现有行为等价）：
    ① SegEffectTypes 有值              → 直接搬进列 13（它本来就是逐段覆盖值）
    ② 没配但 SegCount>0（多段技能）    → 把原串复制成 SegCount 段 "0;4" → "0;4|0;4|0;4"
    ③ 其余（SegCount=0，不分段）        → 原样保留
"""
import io
import os

SRC = r"E:\Project\BttleFreamwork-v1\Assets\Resources\Config\SkillData.csv"
DST = SRC + ".new"

I_SET = 13        # SkillEffectTypes
I_SEGCNT = 26     # SegCount
I_SEG = 27        # SegEffectTypes（待删除）

raw = io.open(SRC, encoding="utf-8-sig", newline="").read()
lines = raw.replace("\r\n", "\n").split("\n")

out = []
report = []

for idx, line in enumerate(lines):
    # 末尾空行原样保留
    if line == "" and idx == len(lines) - 1:
        continue

    f = line.split(",")

    if idx == 0:
        assert f[I_SEG] == "SegEffectTypes", "表头列 27 不是 SegEffectTypes: %r" % f[I_SEG]
        assert f[I_SET] == "SkillEffectTypes", "表头列 13 不是 SkillEffectTypes: %r" % f[I_SET]
        out.append(",".join(v for i, v in enumerate(f) if i != I_SEG))
        continue

    if len(f) <= I_SEG:
        # 老 CSV 没有列 27，原样保留
        out.append(line)
        continue

    seg = f[I_SEG].strip()
    setv = f[I_SET].strip()
    segcnt = f[I_SEGCNT].strip()

    if seg:
        new13 = seg
        why = "搬移 SegEffectTypes"
    elif segcnt and segcnt != "0":
        new13 = "|".join([setv] * int(segcnt))
        why = "按 SegCount=%s 展开" % segcnt
    else:
        new13 = setv
        why = "原样"

    f[I_SET] = new13
    if why != "原样":
        report.append((f[0], setv, seg, segcnt, new13, why))

    out.append(",".join(v for i, v in enumerate(f) if i != I_SEG))

text = "\r\n".join(out) + "\r\n"

# 先尝试直接覆盖；沙箱若拦截（WinError 5）则退化到写 .new，交由外部通道复制
try:
    io.open(SRC, "w", encoding="utf-8-sig", newline="").write(text)
    print("写出: 已直接覆盖", SRC)
    wrote_direct = True
except Exception as e:
    io.open(DST, "w", encoding="utf-8-sig", newline="").write(text)
    print("直接覆盖失败(%s)，已写出: %s" % (type(e).__name__, DST))
    wrote_direct = False

print("写出:", DST if not wrote_direct else SRC)
print("行数: %d (含表头)" % len(out))
print()
print("=== 变更明细（其余行原样） ===")
for r in report:
    print("  id=%-6s  Set=%-18r Seg=%-12r SegCount=%-3s → %-22r  (%s)" % r)
print()
print("校验: 新表头列数 =", len(out[0].split(",")))
