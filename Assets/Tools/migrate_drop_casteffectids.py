# -*- coding: utf-8 -*-
"""
一次性迁移脚本：删除 SkillData.csv 的 CastEffectIds 列(原 index 24)，特效 id 统一走 AttackEffectID(22)。

迁移规则（已与用户确认 —— 以 AttackEffectID 为准）：
  1. AttackEffectID 非空            → 原样保留（策划既有配置优先，10003 属于此类）
  2. AttackEffectID 为空、CastEffectIds 非空 → 用 CastEffectIds 补位（10002 属于此类，否则删列即丢数据）
  3. 两列都空                        → 保持空
删列后各列左移一位：HitEffectIds 25→24 / CastEffectTimings 26→25 / SegCount 27→26 / SegEffectTypes 28→27
总列数 29 → 28。
"""
import io

SRC = r"E:\Project\BttleFreamwork-v1\Assets\Resources\Config\SkillData.csv"
IDX_ATTACK_EFFECT = 22   # AttackEffectID
IDX_CAST_IDS      = 24   # CastEffectIds（本次要删的列）

with io.open(SRC, "r", encoding="utf-8-sig", newline="") as f:
    raw = f.read()

# 保留原始行尾：先探测
crlf = "\r\n" if "\r\n" in raw else "\n"
lines = raw.replace("\r\n", "\n").split("\n")

header = lines[0].split(",")
assert header[IDX_CAST_IDS] == "CastEffectIds", "表头第 24 列不是 CastEffectIds，实际=%s" % header[IDX_CAST_IDS]
assert header[IDX_ATTACK_EFFECT] == "AttackEffectID", "表头第 22 列不是 AttackEffectID，实际=%s" % header[IDX_ATTACK_EFFECT]

out = []
changes = []

# 表头：删列
new_header = header[:IDX_CAST_IDS] + header[IDX_CAST_IDS + 1:]
out.append(",".join(new_header))

for ln in lines[1:]:
    if not ln.strip():
        # 保留末尾空行，原样写出
        out.append(ln)
        continue
    cols = ln.split(",")
    if len(cols) != len(header):
        raise SystemExit("列数异常 id=%s 列数=%d（应为 %d），已中止" % (cols[0], len(cols), len(header)))

    atk = cols[IDX_ATTACK_EFFECT].strip()
    cast = cols[IDX_CAST_IDS].strip()
    sid = cols[0]

    if atk:
        pass                       # ① 策划原配优先，保持不动
    elif cast:
        cols[IDX_ATTACK_EFFECT] = cast   # ② 补位，避免删列丢数据
        changes.append("id=%s  AttackEffectID 空 ← CastEffectIds 补位 = %s" % (sid, cast))
    else:
        pass                       # ③ 两列都空

    if atk and cast:
        changes.append("id=%s  两列都有值 → 以 AttackEffectID 为准 = %s（丢弃 CastEffectIds=%s）" % (sid, atk, cast))
    elif atk and not cast:
        changes.append("id=%s  AttackEffectID 原样保留 = %s" % (sid, atk))

    del cols[IDX_CAST_IDS]
    out.append(",".join(cols))

DST = SRC + ".new"
with io.open(DST, "w", encoding="utf-8-sig", newline="") as f:
    f.write(crlf.join(out))
print("已生成新文件:", DST)

print("行尾 =", repr(crlf))
print("列数 = %d -> %d" % (len(header), len(new_header)))
print("新表头 =", ",".join(new_header))
print("\n--- 数据变更明细 (%d 条) ---" % len(changes))
for c in changes:
    print("  " + c)
